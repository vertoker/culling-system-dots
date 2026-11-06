using System;

namespace CullingSystemDOTS.Dynamic
{
    [Serializable]
    public sealed class OcclusionSettings
    {
        public float BlockSize = 16;
        public int MaxInstances = 65536, MaxGeometries = 4096, MaxSnapshotLeases = 64;
        public int MaxUploadBytesPerFrame = 256 * 1024, MaxInFlightSubmissions = 2;
        public int MaxQueuedUpdates = 4096, MaxBlocksPerInstance = 4096, MaxActiveBlocks = 65536, MaxBlockMemberships = 1048576;
        public long MaxResourceBytes = 128L * 1024 * 1024;
        public void Validate()
        {
            if (float.IsNaN(BlockSize) || float.IsInfinity(BlockSize) || BlockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(BlockSize));
            if (MaxInstances <= 0) throw new ArgumentOutOfRangeException(nameof(MaxInstances));
            if (MaxGeometries <= 0) throw new ArgumentOutOfRangeException(nameof(MaxGeometries));
            if (MaxSnapshotLeases <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSnapshotLeases));
            if (MaxUploadBytesPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(MaxUploadBytesPerFrame));
            if (MaxQueuedUpdates <= 0 || MaxBlocksPerInstance <= 0 || MaxActiveBlocks <= 0 || MaxBlockMemberships <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxQueuedUpdates), "Queue and membership limits must be positive.");
            if (MaxInFlightSubmissions <= 0 || MaxInFlightSubmissions > 8)
                throw new ArgumentOutOfRangeException(nameof(MaxInFlightSubmissions));
            if (MaxResourceBytes < 1024) throw new ArgumentOutOfRangeException(nameof(MaxResourceBytes));
        }
        internal OcclusionSettings Copy() => (OcclusionSettings)MemberwiseClone();
    }

    internal sealed class OcclusionMemoryBudget
    {
        internal readonly long Limit;
        internal long Used { get; private set; }
        internal OcclusionMemoryBudget(long limit) { Limit = limit; }
        internal void Reserve(long bytes)
        {
            if (bytes < 0 || bytes > Limit - Used) throw new InvalidOperationException("The accounted occlusion resource budget is exhausted.");
            Used += bytes;
        }
        internal void Release(long bytes) { Used -= bytes; }
    }
}
