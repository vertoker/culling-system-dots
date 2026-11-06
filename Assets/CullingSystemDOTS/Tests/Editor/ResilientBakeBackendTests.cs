using System;
using NUnit.Framework;
using CullingSystemDOTS.Editor.Baking;
using CullingSystemDOTS.Editor.Baking.Jobs;
namespace CullingSystemDOTS.Tests
{
    public class ResilientBakeBackendTests
    {
        private sealed class FailingGpu : IBakeBackend
        {
            internal bool Disposed;
            private readonly string stage;
            internal FailingGpu(string stage) { this.stage = stage; }
            public string Name => "GPU test transport";
            public IBakeSession Begin(BakeSnapshot snapshot,BakeBackendOptions options = default)
            { if (stage == "begin") throw new InvalidOperationException("device lost at startup"); return new Session(this,stage); }
            private sealed class Session : IBakeSession
            {
                private readonly FailingGpu owner; private readonly string stage; private int ticks;
                public BakeSessionStatus Status { get; private set; }
                public float Progress => ticks == 0 ? 0 : .66f;
                public Session(FailingGpu owner,string stage) { this.owner = owner; this.stage = stage; }
                public void Tick()
                {
                    if (stage == "cancel") { Status = BakeSessionStatus.Cancelled; return; }
                    if (++ticks == 1) { Status = BakeSessionStatus.Running; return; }
                    Status = stage == "complete" ? BakeSessionStatus.Completed : BakeSessionStatus.Faulted;
                    if (stage == "tick") throw new InvalidOperationException("device lost after partial progress");
                }
                public BakeCellResult[] Complete()
                {
                    while (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running) Tick();
                    if (Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
                    throw new InvalidOperationException("readback failed after partial progress");
                }
                public void Cancel() { Status = BakeSessionStatus.Cancelled; }
                public void Dispose() { owner.Disposed = true; }
            }
        }
        private sealed class CpuAfterGpuRelease : IBakeBackend
        {
            private readonly FailingGpu gpu; internal int Begins;
            public string Name => "CPU Jobs/Burst";
            public CpuAfterGpuRelease(FailingGpu gpu) { this.gpu = gpu; }
            public IBakeSession Begin(BakeSnapshot snapshot,BakeBackendOptions options = default)
            {
                if (!gpu.Disposed) throw new InvalidOperationException("GPU still owns resources when CPU starts");
                Begins++; return new JobsBakeBackend().Begin(snapshot,options);
            }
        }
        private static IBakeBackend Resilient(IBakeBackend primary,IBakeBackend fallback)
        {
            var type = typeof(IBakeBackend).Assembly.GetType("CullingSystemDOTS.Editor.Baking.ResilientBakeBackend");
            Assert.That(type, Is.Not.Null, "Whole-bake CPU recovery has not been implemented.");
            return (IBakeBackend)Activator.CreateInstance(type,primary,fallback);
        }
        [TestCase("begin",false)] [TestCase("tick",false)] [TestCase("complete",false)] [TestCase("status",false)]
        [TestCase("begin",true)] [TestCase("tick",true)] [TestCase("complete",true)] [TestCase("status",true)]
        public void GpuFailureDiscardsPartialProgressAndRebakesAllCellsOnRealCpu(string stage,bool useTicks)
        {
            using var snapshot = ComputeBakeBackendTests.Snapshot(); var primary = new FailingGpu(stage);
            // A begin failure has no created GPU session to dispose.
            if (stage == "begin") primary.Disposed = true;
            var cpu = new CpuAfterGpuRelease(primary); using var session = Resilient(primary,cpu).Begin(snapshot,new BakeBackendOptions {MaxCellsPerBatch = 1});
            if (stage != "begin") { session.Tick(); Assert.That(session.Progress, Is.EqualTo(.66f)); }
            if (useTicks)
            {
                double deadline = UnityEditor.EditorApplication.timeSinceStartup+10;
                while (session.Status != BakeSessionStatus.Completed && UnityEditor.EditorApplication.timeSinceStartup < deadline) session.Tick();
            }
            var result = session.Complete(); Assert.That(cpu.Begins, Is.EqualTo(1)); Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Completed));
            Assert.That(result.Length, Is.EqualTo(3)); Assert.That(Array.ConvertAll(result,c=>c.FullGridIndex), Is.EqualTo(new[] {2,19,74}));
            foreach (var cell in result) CollectionAssert.AreEqual(new ushort[] {0,1,32},cell.Groups);
            var name = (string)session.GetType().GetProperty("BackendName").GetValue(session);
            Assert.That(name, Is.EqualTo(cpu.Name)); Assert.That((string)session.GetType().GetProperty("FallbackReason").GetValue(session), Is.Not.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void CancellationDoesNotTriggerCpuRestart(bool afterProgress)
        {
            using var snapshot = ComputeBakeBackendTests.Snapshot(); var primary = new FailingGpu("tick"); var cpu = new CpuAfterGpuRelease(primary);
            using var session = Resilient(primary,cpu).Begin(snapshot); if (afterProgress) session.Tick(); session.Cancel();
            Assert.Throws<OperationCanceledException>(()=>session.Complete()); Assert.That(cpu.Begins, Is.Zero);
        }
        [Test]
        public void PrimaryCancellationIsNotTreatedAsGpuFailure()
        {
            using var snapshot = ComputeBakeBackendTests.Snapshot(); var primary = new FailingGpu("cancel"); var cpu = new CpuAfterGpuRelease(primary);
            using var session = Resilient(primary,cpu).Begin(snapshot); Assert.Throws<OperationCanceledException>(()=>session.Complete());
            Assert.That(cpu.Begins, Is.Zero); Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Cancelled));
        }
        [Test]
        public void CpuFailureRemainsFaultedAndCannotReturnGpuOutput()
        {
            using var snapshot = ComputeBakeBackendTests.Snapshot(); var primary = new FailingGpu("tick");
            using var session = Resilient(primary,new JobsBakeBackend()).Begin(snapshot,new BakeBackendOptions {MemoryBudgetBytes = 1});
            Assert.Throws<InvalidOperationException>(()=>session.Complete()); Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Faulted));
            Assert.Throws<InvalidOperationException>(()=>session.Complete()); Assert.That(primary.Disposed, Is.True);
        }
        [Test]
        public void RealGpuUploadBudgetFailureRecoversWithinTheSameBudgetOnCpu()
        {
            using var snapshot = ComputeBakeBackendTests.Snapshot();
            using var session = new ResilientBakeBackend(new CullingSystemDOTS.Editor.Baking.Gpu.ComputeBakeBackend(),new JobsBakeBackend())
                .Begin(snapshot,new BakeBackendOptions {MemoryBudgetBytes = 3000,MaxCellsPerBatch = 1});
            var cells = session.Complete(); Assert.That(cells.Length, Is.EqualTo(3));
            foreach (var cell in cells) CollectionAssert.AreEqual(new ushort[] {0,1,32},cell.Groups);
            Assert.That(((IBakeSessionDiagnostics)session).BackendName, Does.StartWith("CPU Jobs/Burst"));
            Assert.That(((IBakeSessionDiagnostics)session).FallbackReason, Does.Contain("budget"));
        }
    }
}
