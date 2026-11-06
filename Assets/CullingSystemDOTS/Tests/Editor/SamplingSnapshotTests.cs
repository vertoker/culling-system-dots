using NUnit.Framework;
using CullingSystemDOTS.Editor.Baking;
using Unity.Mathematics;

namespace CullingSystemDOTS.Tests
{
    public class SamplingSnapshotTests
    {
        [Test]
        public void ExcludedCellsKeepFullGridIndexAndDefaultIncludeOverridesProviders()
        {
            var first = new Provider(1); var second = new Provider(2);
            var positions = new[] { new float3(0), new float3(1), new float3(2), new float3(3) };
            var result = SamplingSnapshotExtractor.Capture(positions, new IBakeSamplingProvider[] { first, second },
                p => p.x == 2 ? SamplingDecision.Include : p.x == 3 ? SamplingDecision.Exclude : SamplingDecision.Continue);
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].FullGridIndex, Is.Zero); Assert.That(result[1].FullGridIndex, Is.EqualTo(2));
            Assert.That(first.Initialized, Is.EqualTo(1)); Assert.That(second.Initialized, Is.EqualTo(1));
            Assert.That(first.Queried, Is.EqualTo(2)); Assert.That(second.Queried, Is.EqualTo(1));
            positions[0] = new float3(99); Assert.That(result[0].Position, Is.EqualTo(new float3(0)));
        }
        private sealed class Provider : IBakeSamplingProvider
        {
            private readonly int excluded; public int Initialized, Queried;
            public Provider(int excluded) { this.excluded = excluded; }
            public void Initialize() { Initialized++; }
            public bool IsActive(float3 position) { Queried++; return position.x != excluded; }
        }
    }
}
