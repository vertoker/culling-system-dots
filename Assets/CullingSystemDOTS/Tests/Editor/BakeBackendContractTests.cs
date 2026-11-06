using System;
using NUnit.Framework;
using CullingSystemDOTS.Editor.Baking;
using Unity.Mathematics;

namespace CullingSystemDOTS.Tests
{
    public class BakeBackendContractTests
    {
        [Test]
        public void CancelBeforeReferenceExecutionPublishesNoResultAndKeepsSnapshotOwnedByCaller()
        {
            using var snapshot = MeshSnapshotExtractor.Capture(Request());
            using var session = new ReferenceCpuBakeBackend().Begin(snapshot);
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Pending));
            session.Cancel();
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Cancelled));
            Assert.Throws<OperationCanceledException>(() => session.Complete());
            Assert.That(snapshot.Samples.Length, Is.EqualTo(1));
        }
        [Test]
        public void CompletedReferenceSessionHasMonotonicProgressAndDoesNotOwnSnapshot()
        {
            using var snapshot = MeshSnapshotExtractor.Capture(Request());
            var session = new ReferenceCpuBakeBackend().Begin(snapshot);
            Assert.That(session.Progress, Is.Zero); session.Tick();
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Completed));
            Assert.That(session.Progress, Is.EqualTo(1));
            var result = session.Complete(); Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].FullGridIndex, Is.EqualTo(17));
            session.Dispose(); session.Dispose();
            Assert.That(snapshot.Samples.Length, Is.EqualTo(1));
        }
        private static BakeSnapshotRequest Request() => new BakeSnapshotRequest {
            Profile = BakeInputProfile.ComputeSoftwareInput,
            Samples = new[] { new BakeSampleLocation { FullGridIndex = 17, Position = new float3(0) } } };
    }
}
