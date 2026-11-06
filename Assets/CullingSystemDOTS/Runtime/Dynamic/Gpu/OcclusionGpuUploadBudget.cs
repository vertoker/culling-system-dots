using System;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionGpuUploadBudget
    {
        internal int Limit { get; }
        internal int Used { get; private set; }
        internal int Remaining => Limit - Used;
        private ulong currentFrame;
        internal ulong Frame => currentFrame;
        internal OcclusionGpuUploadBudget(int limit) { if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit)); Limit = limit; }
        internal void BeginFrame(ulong frame)
        {
            if (frame == 0 || frame < currentFrame) throw new ArgumentException("Upload frame tokens must be nonzero and monotonic.", nameof(frame));
            if (frame == currentFrame) return;
            currentFrame = frame; Used = 0;
        }
        internal int TakeAligned(int requested, int alignment)
        {
            if (currentFrame == 0) throw new InvalidOperationException("Begin the shared upload frame before writing GPU data.");
            if (requested < 0 || alignment <= 0) throw new ArgumentOutOfRangeException(nameof(requested));
            int bytes = Math.Min(requested, Remaining) / alignment * alignment; Used += bytes; return bytes;
        }
    }
}
