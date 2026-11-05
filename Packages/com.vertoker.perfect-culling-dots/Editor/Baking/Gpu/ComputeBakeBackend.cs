using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace PerfectCullingDOTS.Editor.Baking.Gpu
{
    public sealed class ComputeBakeBackend : IBakeBackend
    {
        internal const string ShaderPath = "Packages/com.vertoker.perfect-culling-dots/Editor/Baking/Gpu/Shaders/CwbvhBake.compute";
        public string Name => "GPU compute (CWBVH)";
        public static bool IsSupported => SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback;
        public IBakeSession Begin(BakeSnapshot snapshot,BakeBackendOptions options = default) => new ComputeBakeSession(snapshot,options);
    }
    // Main-thread owner. The readback callback copies data while Unity's temporary
    // request view is valid; callers need not tick every Editor frame.
    public sealed class ComputeBakeSession : IBakeSession
    {
        // Every member is a 16-byte vector or matrix column: the structured
        // buffer layout does not depend on platform float3/scalar padding.
        private struct GpuInstance { public float4x4 WorldToLocal; public uint4 BlasRecord; }
        private BakeSnapshot snapshot;
        private readonly BakeBackendOptions options;
        private ComputeShader shader;
        private ComputeBuffer nodes,triangles,tlas,instances,samples,output;
        private AsyncGPUReadbackRequest request;
        private bool pending,readbackReady,readbackError,disposed;
        private uint[] readback;
        private BakeCellResult[] result;
        private Exception failure;
        private int total,processed,batchSize,active,words,kernel,generation;
        private long treeBytes;
        public BakeSessionStatus Status { get; private set; }
        public float Progress => Status == BakeSessionStatus.Completed ? 1 : total == 0 ? 0 : (float)processed/total;
        public long NativeBytes { get; private set; }
        public long PeakNativeBytes { get; private set; }
        public int CompletedBatchCount { get; private set; }
        internal ComputeBakeSession(BakeSnapshot snapshot,BakeBackendOptions options)
        { this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); this.options = options; total = snapshot.Samples.Length; }
        public void Tick()
        {
            EnsureAlive();
            if (Status != BakeSessionStatus.Pending && Status != BakeSessionStatus.Running) return;
            try
            {
                // Also detects disposal between batches or while a GPU request runs.
                _ = snapshot.Samples.Length;
                if (Status == BakeSessionStatus.Pending)
                {
                    Initialize(); Status = BakeSessionStatus.Running;
                    if (total == 0) Finish(); else Dispatch();
                }
                else if (readbackReady)
                {
                    pending = false;
                    if (readbackError) throw new InvalidOperationException("GPU bake readback failed.");
                    Collect(); ReleaseBatch();
                    if (processed == total) Finish(); else Dispatch();
                }
            }
            catch (Exception error)
            { failure = error; Status = BakeSessionStatus.Faulted; try { Cleanup(); } finally { result = null; snapshot = null; } throw; }
        }
        private void Initialize()
        {
            BakeNumericValidation.Validate(snapshot);
            if (snapshot.Profile != BakeInputProfile.ComputeSoftwareInput) throw new ArgumentException("GPU tracing requires the compute snapshot profile.");
            var rays = snapshot.Rays;
            if (rays.Width <= 0 || rays.Height <= 0 || rays.Width > 65535*8 || rays.Height > 65535*8 ||
                !math.isfinite(rays.Far) || rays.Far <= 0 || !math.all(math.isfinite(rays.Rotation.value))) throw new ArgumentException("Finite supported bake ray settings are required.");
            if (options.MemoryBudgetBytes < 0 || options.MaxCellsPerBatch < 0) throw new ArgumentOutOfRangeException(nameof(options));
            if (!ComputeBakeBackend.IsSupported) throw new NotSupportedException("Compute shaders and asynchronous GPU readback are required.");
            long budget = options.MemoryBudgetBytes == 0 ? 64L*1024*1024 : options.MemoryBudgetBytes;
            long snapshotBytes = (long)snapshot.Vertices.Length*12+(long)snapshot.Indices.Length*4+(long)snapshot.Geometry.Length*20+
                (long)snapshot.Instances.Length*168+(long)snapshot.Groups.Length*20+(long)snapshot.Samples.Length*16;
            long estimate = CwbvhSnapshotBuilder.EstimateNativeBytes(snapshot);
            words = checked((snapshot.Groups.Length+31)/32+2);
            // Peak includes temporary numeric BVH plus GPU copy, sample upload,
            // output buffer and readback copy. Driver overhead is outside this bound.
            long perCell = checked(32L+words*12L);
            long available = budget-snapshotBytes-estimate*2-(long)snapshot.Instances.Length*96-128;
            if (available < perCell && total > 0) throw new InvalidOperationException("GPU memory budget cannot fit snapshot, BVH upload and one cell.");
            batchSize = (int)math.min(options.MaxCellsPerBatch == 0 ? 8 : options.MaxCellsPerBatch,math.min(10922,math.max(1,available/perCell)));
            var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeBakeBackend.ShaderPath);
            if (asset == null) throw new InvalidOperationException("The Unity compute bake shader is missing.");
            shader = Object.Instantiate(asset); kernel = shader.FindKernel("CSMain");
            if (!shader.IsSupported(kernel)) throw new NotSupportedException("The bake compute kernel is unsupported on this graphics device.");
            using (var data = CwbvhSnapshotBuilder.Build(snapshot))
            {
                nodes = Upload(data.Nodes,16); triangles = Upload(data.BlasTriangles,16); tlas = Upload(data.TlasInstances,16);
                using var gpuInstances = new NativeArray<GpuInstance>(data.Instances.Length,Allocator.Temp);
                var writableInstances = gpuInstances;
                for (int i = 0; i < writableInstances.Length; i++) writableInstances[i] = new GpuInstance {WorldToLocal = data.Instances[i].WorldToLocal,BlasRecord = new uint4((uint)data.Instances[i].BlasRoot,0,0,0)};
                instances = Upload(gpuInstances,80);
                treeBytes = (long)nodes.count*16+(long)triangles.count*16+(long)tlas.count*16+(long)instances.count*80;
                NativeBytes = treeBytes; PeakNativeBytes = treeBytes+data.Bytes+(long)gpuInstances.Length*80;
                shader.SetInt("OpaqueTlasRootNode",data.OpaqueRoot);
                shader.SetInt("TransparentTlasRootNodeOffset",data.TransparentRoot < 0 ? 0 : data.TransparentRoot-data.OpaqueRoot);
                shader.SetInt("InstanceCount",data.Instances.Length);
            }
            shader.SetBuffer(kernel,"Blas",nodes); shader.SetBuffer(kernel,"BlasData",triangles); shader.SetBuffer(kernel,"TlasData",tlas); shader.SetBuffer(kernel,"Instances",instances);
            shader.SetInt("GroupCount",snapshot.Groups.Length); shader.SetInt("WordsPerCell",words);
            shader.SetVector("ScreenParams",new Vector4(rays.Width,rays.Height,.0001f,rays.Far));
            shader.SetVector("Rotation",new Vector4(rays.Rotation.value.x,rays.Rotation.value.y,rays.Rotation.value.z,rays.Rotation.value.w));
            shader.SetVector("CellSize",new Vector4(rays.CellSize.x,rays.CellSize.y,rays.CellSize.z,0));
            result = new BakeCellResult[total];
        }
        private static ComputeBuffer Upload<T>(NativeArray<T> data,int stride) where T : struct
        {
            var buffer = new ComputeBuffer(math.max(1,data.Length),stride);
            try { if (data.Length > 0) buffer.SetData(data); else buffer.SetData(new T[1]); return buffer; }
            catch { buffer.Dispose(); throw; }
        }
        private void Dispatch()
        {
            active = math.min(batchSize,total-processed);
            var positions = new Vector4[active]; for (int i = 0; i < active; i++) { var p = snapshot.Samples[processed+i].Position; positions[i] = new Vector4(p.x,p.y,p.z,0); }
            samples = new ComputeBuffer(active,16); samples.SetData(positions);
            output = new ComputeBuffer(checked(active*words),4); output.SetData(new uint[active*words]);
            NativeBytes = treeBytes+active*(32L+words*12L); PeakNativeBytes = Math.Max(PeakNativeBytes,NativeBytes);
            shader.SetBuffer(kernel,"CamPos",samples); shader.SetBuffer(kernel,"VisibleIndices",output); shader.SetInt("ActiveCells",active);
            shader.Dispatch(kernel,(snapshot.Rays.Width+7)/8,(snapshot.Rays.Height+7)/8,active*6);
            readbackReady = readbackError = false; pending = true; int token = ++generation;
            request = AsyncGPUReadback.Request(output,r=>
            {
                if (token != generation) return;
                try { readbackError = r.hasError; if (!readbackError) readback = r.GetData<uint>().ToArray(); }
                catch { readbackError = true; }
                readbackReady = true;
            });
        }
        private void Collect()
        {
            if (readback == null || readback.Length != active*words) throw new InvalidOperationException("GPU bake returned an incomplete batch.");
            for (int i = 0; i < active; i++)
            {
                if (readback[i*words+1] != 0) throw new InvalidOperationException("GPU CWBVH traversal exceeded its stack capacity; partial visibility discarded.");
                var visible = new List<ushort>();
                for (int g = 0; g < snapshot.Groups.Length; g++) if ((readback[i*words+2+(g>>5)]&(1u<<(g&31))) != 0) visible.Add((ushort)g);
                result[processed+i] = new BakeCellResult {FullGridIndex = snapshot.Samples[processed+i].FullGridIndex,Groups = visible.ToArray(),NeighborMask = readback[i*words]};
            }
            processed += active; CompletedBatchCount++;
        }
        private void Finish() { Cleanup(); snapshot = null; Status = BakeSessionStatus.Completed; }
        public BakeCellResult[] Complete()
        {
            EnsureAlive();
            while (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running)
            { Tick(); if (Status == BakeSessionStatus.Running && pending && !readbackReady) request.WaitForCompletion(); }
            if (Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
        public void Cancel()
        {
            EnsureAlive(); if (Status != BakeSessionStatus.Pending && Status != BakeSessionStatus.Running) return;
            try { Cleanup(); } finally { result = null; snapshot = null; Status = BakeSessionStatus.Cancelled; }
        }
        private void ReleaseBatch()
        { samples?.Dispose(); samples = null; output?.Dispose(); output = null; readback = null; readbackReady = false; NativeBytes = treeBytes; }
        private void Cleanup()
        {
            ++generation;
            try { if (pending) request.WaitForCompletion(); }
            finally
            {
                pending = false; ReleaseBatch(); nodes?.Dispose(); nodes = null; triangles?.Dispose(); triangles = null; tlas?.Dispose(); tlas = null; instances?.Dispose(); instances = null;
                if (shader != null) Object.DestroyImmediate(shader); shader = null; treeBytes = NativeBytes = 0;
            }
        }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(ComputeBakeSession)); }
        public void Dispose()
        {
            if (disposed) return;
            try { Cleanup(); } finally { if (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running) Status = BakeSessionStatus.Cancelled; snapshot = null; result = null; disposed = true; }
        }
    }
}
