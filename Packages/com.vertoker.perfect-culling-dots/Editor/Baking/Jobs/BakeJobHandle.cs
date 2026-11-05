using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace PerfectCullingDOTS.Editor.Baking.Jobs
{
    public sealed class BakeJobHandle : IBakeSession
    {
        private BakeSnapshot snapshot;
        private readonly BakeBackendOptions options;
        private NativeBvh bvh;
        private CwbvhBakeData compatibleBvh;
        private readonly bool sourceCompatible;
        private readonly ushort[] rasterColors;
        private NativeArray<ushort> rasterPalette;
        private NativeArray<NativeRasterPixel> rasterPixels;
        private long TreeBytes => rasterColors != null ? (rasterPalette.IsCreated ? rasterPalette.Length*2L : 0) : compatibleBvh != null ? compatibleBvh.Bytes : bvh == null ? 0 : bvh.Bytes;
        private NativeArray<uint> tileBits, tileNeighbors, cellBits, cellNeighbors;
        private NativeArray<byte> burstFlags;
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
        internal BakeJobHandle(BakeSnapshot snapshot, BakeBackendOptions options,bool sourceCompatible = false,ushort[] rasterColors = null)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.options = options; this.sourceCompatible = sourceCompatible; this.rasterColors = rasterColors == null ? null : (ushort[])rasterColors.Clone(); total = snapshot.Samples.Length;
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
            if (rasterColors != null) NativeRasterCpuReference.Validate(snapshot,rasterColors);
            if (settings.Width <= 0 || settings.Height <= 0 || !math.isfinite(settings.Far) || settings.Far <= 0 ||
                !math.all(math.isfinite(settings.Rotation.value))) throw new ArgumentException("Finite positive ray settings required.");
            if (options.MemoryBudgetBytes < 0 || options.MaxCellsPerBatch < 0) throw new ArgumentOutOfRangeException(nameof(options));
            words = (snapshot.Groups.Length+31)/32; tilesPerFace = checked((settings.Height+7)/8);
            long perCell = checked(6L*tilesPerFace*(words*4L+5 + (rasterColors == null ? 0 : settings.Width*8L*8)) + words*4L+4);
            long snapshotBytes = SnapshotBytes(snapshot), estimate = rasterColors != null ? rasterColors.Length*2L : sourceCompatible ? CwbvhSnapshotBuilder.EstimateNativeBytes(snapshot) : BvhBuilder.EstimateNativeBytes(snapshot);
            long budget = options.MemoryBudgetBytes == 0 ? 64L*1024*1024 : options.MemoryBudgetBytes;
            long available = budget - snapshotBytes - estimate;
            if (available < perCell && total > 0) throw new InvalidOperationException("Bake native memory budget cannot fit snapshot, BVH and one cell tile batch.");
            batchSize = (int)math.min(options.MaxCellsPerBatch == 0 ? 8 : options.MaxCellsPerBatch, math.min(total,math.max(1,available/perCell)));
            if (rasterColors != null) rasterPalette = new NativeArray<ushort>(rasterColors,Allocator.Persistent);
            else if (sourceCompatible) compatibleBvh = CwbvhSnapshotBuilder.Build(snapshot); else bvh = BvhBuilder.Build(snapshot);
            NativeBytes = TreeBytes; PeakNativeBytes = NativeBytes;
            result = new BakeCellResult[total];
        }
        private static long SnapshotBytes(BakeSnapshot s) => (long)s.Vertices.Length*12 + (long)s.Indices.Length*4 +
            (long)s.Geometry.Length*20 + (long)s.Instances.Length*168 + (long)s.Groups.Length*20 + (long)s.Samples.Length*16;
        private void Schedule()
        {
            activeCount = math.min(batchSize,total-processed); int work = checked(activeCount*6*tilesPerFace);
            tileBits = new NativeArray<uint>(checked(work*words),Allocator.Persistent);
            tileNeighbors = new NativeArray<uint>(work,Allocator.Persistent);
            burstFlags = new NativeArray<byte>(work,Allocator.Persistent);
            cellBits = new NativeArray<uint>(checked(activeCount*words),Allocator.Persistent);
            cellNeighbors = new NativeArray<uint>(activeCount,Allocator.Persistent);
            if (rasterColors != null) rasterPixels = new NativeArray<NativeRasterPixel>(checked(work*snapshot.Rays.Width*8),Allocator.Persistent);
            NativeBytes = TreeBytes + (long)(tileBits.Length+tileNeighbors.Length+cellBits.Length+cellNeighbors.Length)*4+burstFlags.Length+(rasterPixels.IsCreated ? rasterPixels.Length*8L : 0);
            PeakNativeBytes = math.max(PeakNativeBytes,NativeBytes);
            if (rasterColors != null)
                dependency = new NativeRasterTileJob {Vertices = snapshot.Vertices,Indices = snapshot.Indices,Geometry = snapshot.Geometry,Instances = snapshot.Instances,
                    Palette = rasterPalette.AsReadOnly(),Samples = snapshot.Samples,Settings = snapshot.Rays,SampleStart = processed,TilesPerFace = tilesPerFace,Words = words,
                    Pixels = rasterPixels,TileBits = tileBits,TileNeighbors = tileNeighbors,BurstFlags = burstFlags}.Schedule(work,1);
            else if (sourceCompatible)
                dependency = new CwbvhRayTraversalJob {Nodes = compatibleBvh.Nodes.AsReadOnly(),BlasTriangles = compatibleBvh.BlasTriangles.AsReadOnly(),
                    TlasInstances = compatibleBvh.TlasInstances.AsReadOnly(),Instances = compatibleBvh.Instances.AsReadOnly(),Samples = snapshot.Samples,
                    Settings = snapshot.Rays,SampleStart = processed,TilesPerFace = tilesPerFace,Words = words,GroupCount = snapshot.Groups.Length,
                    OpaqueRoot = compatibleBvh.OpaqueRoot,TransparentRoot = compatibleBvh.TransparentRoot,TileBits = tileBits,TileNeighbors = tileNeighbors,BurstFlags = burstFlags}.Schedule(work,1);
            else dependency = new BakeRayTraversalJob { Nodes = bvh.Nodes.AsReadOnly(), Order = bvh.Order.AsReadOnly(),
                GeometryRoots = bvh.GeometryRoots.AsReadOnly(), Triangles = bvh.Triangles.AsReadOnly(), Instances = snapshot.Instances,
                Samples = snapshot.Samples, Settings = snapshot.Rays, SampleStart = processed, TilesPerFace = tilesPerFace,
                Words = words, OpaqueRoot = bvh.OpaqueRoot, TransparentRoot = bvh.TransparentRoot,
                TileBits = tileBits, TileNeighbors = tileNeighbors, BurstFlags = burstFlags }.Schedule(work,1);
            // Register immediately, including the partial scheduling failure path.
            snapshot.RegisterReader(dependency); compatibleBvh?.RegisterReader(dependency);
            dependency = new VisibilityReductionJob { TileBits = tileBits, TileNeighbors = tileNeighbors,
                CellBits = cellBits, CellNeighbors = cellNeighbors, Words = words, TilesPerCell = 6*tilesPerFace }.Schedule(activeCount,1,dependency);
            snapshot.RegisterReader(dependency); compatibleBvh?.RegisterReader(dependency);
            JobHandle.ScheduleBatchedJobs();
        }
        private void Collect()
        {
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
            if (burstFlags.IsCreated) burstFlags.Dispose(); if (rasterPixels.IsCreated) rasterPixels.Dispose(); NativeBytes = TreeBytes;
        }
        private void Cleanup()
        {
            try { dependency.Complete(); }
            finally { dependency = default; DisposeBatch(); bvh?.Dispose(); bvh = null; compatibleBvh?.Dispose(); compatibleBvh = null; if (rasterPalette.IsCreated) rasterPalette.Dispose(); NativeBytes = 0; }
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
