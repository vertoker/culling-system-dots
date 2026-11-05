using System;
using System.Collections.Generic;
using Unity.Mathematics;
using PerfectCullingDOTS.Authoring;

namespace PerfectCullingDOTS.Editor.Baking
{
    public enum SamplingDecision { Continue, Include, Exclude }
    public interface IBakeSamplingProvider : ICullingSamplingProvider { }
    public static class SamplingSnapshotExtractor
    {
        public static BakeSampleLocation[] Capture(IReadOnlyList<float3> fullGrid,
            IReadOnlyList<ICullingSamplingProvider> providers, Func<float3, SamplingDecision> defaultDecision = null)
        {
            if (fullGrid == null || providers == null) throw new ArgumentNullException();
            foreach (var provider in providers)
            {
                if (provider == null) throw new ArgumentException("Sampling providers must be present.");
                provider.Initialize();
            }
            var active = new List<BakeSampleLocation>();
            for (int i = 0; i < fullGrid.Count; i++)
            {
                var position = fullGrid[i];
                if (!math.all(math.isfinite(position))) throw new ArgumentException("Sampling positions must be finite.");
                var decision = defaultDecision == null ? SamplingDecision.Continue : defaultDecision(position);
                if ((uint)decision > 2) throw new ArgumentException("Unknown default sampling decision.");
                bool include = decision != SamplingDecision.Exclude;
                if (decision == SamplingDecision.Continue)
                    foreach (var provider in providers) if (!provider.IsActive(position)) { include = false; break; }
                if (include) active.Add(new BakeSampleLocation { FullGridIndex = i, Position = position });
            }
            return active.ToArray();
        }
    }
}
