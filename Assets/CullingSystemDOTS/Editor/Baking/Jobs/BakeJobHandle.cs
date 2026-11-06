using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    public sealed class BakeJobHandle : IBakeSession
    {
        private BakeSnapshot snapshot;
        private readonly BakeBackendOptions options;
        private NativeBvh bvh;
        private long TreeBytes => bvh == null ? 0 : bvh.Bytes;
        private NativeArray<uint> tileBits, tileNeighbors, cellBits, cellNeighbors;
        private NativeArray<byte> burstFlags, tileErrors;
        private JobHandle dependency;
        private BakeCellResult[] result;
        private Exception failure;
        private int total, processed, batchSize, activeCount, words, tilesPerFace;
        private bool disposed;
        private bool anyExecuted, allExecutedInBurst = true;
        public BakeSessionStatus Status { get; private set; }
        public float Progress => Status == BakeSessionStatus.Completed ? 1 : total == 0 ? 0 : (float)processed/total;
        public bool RanInBurst => anyExecuted && allExecutedInBurst;
        public long NativeBytes { get; private set; }
        public long PeakNativeBytes { get; private set; }
        public int CompletedBatchCount { get; private set; }
        internal BakeJobHandle(BakeSnapshot snapshot, BakeBackendOptions options)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.options = options; total = snapshot.Samples.Length;
        }
        public void Tick()
        {
            EnsureAlive();
            if (Status == BakeSessionStatus.Completed || Status == BakeSessionStatus.Cancelled || Status == BakeSessionStatus.Faulted) return;
            try
            {
                if (Status == BakeSessionStatus.Pending)
                {
                    Initialize(); Status = BakeSessionStatus.Running;
                    if (total == 0) Finish(); else Schedule();
                }
                else if (dependency.IsCompleted)
                {
                    dependency.Complete(); dependency = default;
                    Collect(); DisposeBatch();
                    if (processed == total) Finish(); else Schedule();
                }
            }
            catch (Exception error)
            {
                failure = error; Status = BakeSessionStatus.Faulted;
                try { Cleanup(); } finally { snapshot = null; result = null; }
                throw;
            }
        }
        private void Initialize()
        {
            BakeNumericValidation.Validate(snapshot);
            var settings = snapshot.Rays;
            if (settings.Width <= 0 || settings.Height <= 0 || !math.isfinite(settings.Far) || settings.Far <= 0 ||
                !math.all(math.isfinite(settings.Rotation.value))) throw new ArgumentException("Finite positive ray settings required.");
            if (options.MemoryBudgetBytes < 0 || options.MaxCellsPerBatch < 0) throw new ArgumentOutOfRangeException(nameof(options));
            words = (snapshot.Groups.Length+31)/32; tilesPerFace = checked((settings.Height+7)/8);
            long perCell = checked(6L*tilesPerFace*(words*4L+6) + words*4L+4);
            long snapshotBytes = SnapshotBytes(snapshot), estimate = BvhBuilder.EstimateNativeBytes(snapshot);
            long budget = options.MemoryBudgetBytes == 0 ? 64L*1024*1024 : options.MemoryBudgetBytes;
            long available = budget - snapshotBytes - estimate;
            if (available < perCell && total > 0) throw new InvalidOperationException("Bake native memory budget cannot fit snapshot, BVH and one cell tile batch.");
            batchSize = (int)math.min(options.MaxCellsPerBatch == 0 ? 8 : options.MaxCellsPerBatch, math.min(total,math.max(1,available/perCell)));
            bvh = BvhBuilder.Build(snapshot);
            NativeBytes = TreeBytes; PeakNativeBytes = NativeBytes;
            result = new BakeCellResult[total];
        }
        private static long SnapshotBytes(BakeSnapshot s) => (long)s.Vertices.Length*12 + (long)s.Indices.Length*4 +
            (long)s.Geometry.Length*16 + (long)s.Instances.Length*168 + (long)s.Groups.Length*20 + (long)s.Samples.Length*16;
        private void Schedule()
        {
            activeCount = math.min(batchSize,total-processed); int work = checked(activeCount*6*tilesPerFace);
            tileBits = new NativeArray<uint>(checked(work*words),Allocator.Persistent);
            tileNeighbors = new NativeArray<uint>(work,Allocator.Persistent);
            burstFlags = new NativeArray<byte>(work,Allocator.Persistent);
            tileErrors = new NativeArray<byte>(work,Allocator.Persistent);
            cellBits = new NativeArray<uint>(checked(activeCount*words),Allocator.Persistent);
            cellNeighbors = new NativeArray<uint>(activeCount,Allocator.Persistent);
            NativeBytes = TreeBytes + (long)(tileBits.Length+tileNeighbors.Length+cellBits.Length+cellNeighbors.Length)*4+burstFlags.Length+(tileErrors.IsCreated ? tileErrors.Length : 0);
            PeakNativeBytes = math.max(PeakNativeBytes,NativeBytes);
            dependency = new BakeRayTraversalJob { Nodes = bvh.Nodes.AsReadOnly(), Order = bvh.Order.AsReadOnly(),
                GeometryRoots = bvh.GeometryRoots.AsReadOnly(), Triangles = bvh.Triangles.AsReadOnly(), Instances = snapshot.Instances,
                Samples = snapshot.Samples, Settings = snapshot.Rays, SampleStart = processed, TilesPerFace = tilesPerFace,
                Words = words, OpaqueRoot = bvh.OpaqueRoot, TransparentRoot = bvh.TransparentRoot,
                TileBits = tileBits, TileNeighbors = tileNeighbors, BurstFlags = burstFlags, TileErrors = tileErrors }.Schedule(work,1);
            // Register immediately, including the partial scheduling failure path.
            snapshot.RegisterReader(dependency);
            dependency = new VisibilityReductionJob { TileBits = tileBits, TileNeighbors = tileNeighbors,
                CellBits = cellBits, CellNeighbors = cellNeighbors, Words = words, TilesPerCell = 6*tilesPerFace }.Schedule(activeCount,1,dependency);
            snapshot.RegisterReader(dependency);
            JobHandle.ScheduleBatchedJobs();
        }
        private void Collect()
        {
            if (tileErrors.IsCreated)
                for (int i = 0; i < tileErrors.Length; i++)
                    if (tileErrors[i] != 0) throw new InvalidOperationException("CPU BVH traversal stack exhausted; unpublished bake discarded.");
            for (int i = 0; i < burstFlags.Length; i++) { anyExecuted = true; allExecutedInBurst &= burstFlags[i] != 0; }
            for (int cell = 0; cell < activeCount; cell++)
            {
                var groups = new List<ushort>();
                for (int g = 0; g < snapshot.Groups.Length; g++) if ((cellBits[cell*words+(g>>5)] & (1u<<(g&31))) != 0) groups.Add((ushort)g);
                result[processed+cell] = new BakeCellResult { FullGridIndex = snapshot.Samples[processed+cell].FullGridIndex,
                    Groups = groups.ToArray(), NeighborMask = cellNeighbors[cell] };
            }
            processed += activeCount; CompletedBatchCount++;
        }
        private void Finish() { Cleanup(); snapshot = null; Status = BakeSessionStatus.Completed; }
        public BakeCellResult[] Complete()
        {
            EnsureAlive();
            while (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running)
            {
                // Tick handles completion failures and disposes all buffers.
                if (Status == BakeSessionStatus.Running) JobHandle.ScheduleBatchedJobs();
                Tick();
                if (Status == BakeSessionStatus.Running)
                {
                    try { dependency.Complete(); }
                    catch (Exception error) { failure = error; Status = BakeSessionStatus.Faulted; try { Cleanup(); } finally { snapshot = null; result = null; } throw; }
                }
            }
            if (Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
        public void Cancel()
        {
            EnsureAlive();
            if (Status != BakeSessionStatus.Pending && Status != BakeSessionStatus.Running) return;
            try { Cleanup(); } finally { result = null; snapshot = null; Status = BakeSessionStatus.Cancelled; }
        }
        private void DisposeBatch()
        {
            if (tileBits.IsCreated) tileBits.Dispose(); if (tileNeighbors.IsCreated) tileNeighbors.Dispose();
            if (cellBits.IsCreated) cellBits.Dispose(); if (cellNeighbors.IsCreated) cellNeighbors.Dispose();
            if (burstFlags.IsCreated) burstFlags.Dispose(); if (tileErrors.IsCreated) tileErrors.Dispose(); NativeBytes = TreeBytes;
        }
        private void Cleanup()
        {
            try { dependency.Complete(); }
            finally { dependency = default; DisposeBatch(); bvh?.Dispose(); bvh = null; NativeBytes = 0; }
        }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(BakeJobHandle)); }
        public void Dispose()
        {
            if (disposed) return;
            try { Cleanup(); } finally { if (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running) Status = BakeSessionStatus.Cancelled;
                snapshot = null; result = null; disposed = true; }
        }
    }
}
