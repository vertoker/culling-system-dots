using System;
using NUnit.Framework;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Editor.Baking.Jobs;
using Unity.Mathematics;
namespace PerfectCullingDOTS.Tests
{
    public class BakeLifetimeTests
    {
        [Test]
        public void CancellationBeforeAndDuringBatchPublishesNoPartialResult()
        {
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(false,true);
            using (var pending = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot))
            {
                pending.Cancel(); Assert.That(pending.NativeBytes, Is.Zero);
                Assert.Throws<OperationCanceledException>(() => pending.Complete());
            }
            using var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { MaxCellsPerBatch = 1 });
            running.Tick(); Assert.That(running.Status, Is.EqualTo(BakeSessionStatus.Running));
            Assert.That(running.NativeBytes, Is.GreaterThan(0)); running.Cancel();
            Assert.That(running.Status, Is.EqualTo(BakeSessionStatus.Cancelled)); Assert.That(running.NativeBytes, Is.Zero);
            Assert.Throws<OperationCanceledException>(() => running.Complete());
            Assert.That(snapshot.Samples.Length, Is.EqualTo(3));
        }
        [Test]
        public void ExceptionReleasesNativeMemoryAndRetainsOriginalFailure()
        {
            using var fixture = new BakeJobsFixture(); var snapshot = fixture.Capture(false,false);
            using var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { MaxCellsPerBatch = 1 });
            running.Tick(); snapshot.Dispose(); // Reader registration completes jobs before owner releases data.
            Assert.Throws<ObjectDisposedException>(() => running.Complete());
            Assert.That(running.Status, Is.EqualTo(BakeSessionStatus.Faulted)); Assert.That(running.NativeBytes, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => running.Complete());
        }
        [Test]
        public void BudgetFailureHappensBeforeAllocationAndRetainsBorrowedSnapshot()
        {
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(false,false);
            using var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { MemoryBudgetBytes = 1 });
            Assert.Throws<InvalidOperationException>(() => running.Tick());
            Assert.That(running.Status, Is.EqualTo(BakeSessionStatus.Faulted)); Assert.That(running.NativeBytes, Is.Zero);
            Assert.That(snapshot.Samples.Length, Is.EqualTo(3));
        }
        [Test]
        public void RepeatedBakeReleasesAllOwnedBuffersAndRunsInBurst()
        {
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(true,true);
            var expected = ReferenceCpuTraversal.Bake(snapshot);
            for (int n = 0; n < 5; n++)
            {
                var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { MaxCellsPerBatch = 1, MemoryBudgetBytes = 32*1024 });
                JobsBakeTests.AssertResults(expected,running.Complete());
                Assert.That(running.RanInBurst, Is.True, "BurstDiscard runtime marker");
                Assert.That(running.CompletedBatchCount, Is.EqualTo(3));
                Assert.That(running.NativeBytes, Is.Zero); Assert.That(running.PeakNativeBytes, Is.InRange(1,32*1024));
                running.Dispose(); running.Dispose();
                Assert.That(running.NativeBytes, Is.Zero);
            }
        }
        [Test]
        public void EmptyBakeFitsWithoutAWorkBatchAndReportsCompleted()
        {
            using var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest());
            using var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot);
            Assert.That(running.Complete(), Is.Empty); Assert.That(running.Progress, Is.EqualTo(1));
            Assert.That(running.NativeBytes, Is.Zero); Assert.That(running.CompletedBatchCount, Is.Zero);
        }
        [Test]
        public void NoWorkDoesNotClaimExecutionInBurst()
        {
            using var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest());
            using var running = (BakeJobHandle)new JobsBakeBackend().Begin(snapshot);
            Assert.That(running.RanInBurst, Is.False);
            running.Complete(); Assert.That(running.RanInBurst, Is.False);
        }
    }
}
