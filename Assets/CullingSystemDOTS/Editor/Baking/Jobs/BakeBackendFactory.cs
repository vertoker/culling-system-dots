using System;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    public static class BakeBackendFactory
    {
        public static IBakeBackend Create(BakeInputProfile profile,BakeExecutionMode mode)
        {
            if ((uint)mode > (uint)BakeExecutionMode.Cpu) throw new ArgumentOutOfRangeException(nameof(mode));
            if (profile != BakeInputProfile.ComputeSoftwareInput) throw new ArgumentOutOfRangeException(nameof(profile));
            var cpu = new JobsBakeBackend();
            return mode == BakeExecutionMode.Cpu ? cpu : new ResilientBakeBackend(new Gpu.ComputeBakeBackend(),cpu);
        }
        // Retained for direct callers of the original numeric CPU diagnostics.
        public static IBakeBackend Create(BakeInputProfile profile)
        {
            switch (profile)
            {
                case BakeInputProfile.ComputeSoftwareInput: return new JobsBakeBackend();
                default: throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }
        public static string AlgorithmId(BakeInputProfile profile)
        {
            switch (profile)
            {
                case BakeInputProfile.ComputeSoftwareInput: return "BinaryBvhRay/2";
                default: throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }
    }
}
