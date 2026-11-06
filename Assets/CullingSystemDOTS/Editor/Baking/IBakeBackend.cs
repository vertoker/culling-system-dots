using System;

namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeSessionStatus { Pending, Running, Completed, Cancelled, Faulted }
    public struct BakeBackendOptions { public int MemoryBudgetBytes, MaxCellsPerBatch; internal bool BackgroundPreparation; }
    public interface IBakeSession : IDisposable
    {
        BakeSessionStatus Status { get; }
        float Progress { get; }
        void Tick();
        void Cancel();
        BakeCellResult[] Complete();
    }
    public interface IBakeBackend
    {
        string Name { get; }
        IBakeSession Begin(BakeSnapshot snapshot, BakeBackendOptions options = default);
    }
    public sealed class ReferenceCpuBakeBackend : IBakeBackend
    {
        public string Name => "Managed software-compute reference";
        public IBakeSession Begin(BakeSnapshot snapshot, BakeBackendOptions options = default) => new Session(snapshot);
        private sealed class Session : IBakeSession
        {
            private BakeSnapshot snapshot;
            private BakeCellResult[] result;
            private Exception error;
            private bool disposed;
            public BakeSessionStatus Status { get; private set; }
            public float Progress => Status == BakeSessionStatus.Completed ? 1 : 0;
            public Session(BakeSnapshot snapshot)
            {
                this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
                _ = snapshot.Samples.Length;
            }
            public void Tick()
            {
                EnsureAlive();
                if (Status != BakeSessionStatus.Pending) return;
                Status = BakeSessionStatus.Running;
                try { result = ReferenceCpuTraversal.Bake(snapshot); Status = BakeSessionStatus.Completed; }
                catch (Exception failure) { error = failure; Status = BakeSessionStatus.Faulted; throw; }
                finally { snapshot = null; }
            }
            public void Cancel()
            {
                EnsureAlive();
                if (Status == BakeSessionStatus.Pending) { Status = BakeSessionStatus.Cancelled; snapshot = null; }
            }
            public BakeCellResult[] Complete()
            {
                EnsureAlive();
                if (Status == BakeSessionStatus.Pending) Tick();
                if (Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
                if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                return result;
            }
            private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(Session)); }
            public void Dispose()
            {
                if (disposed) return;
                Cancel(); snapshot = null; result = null; disposed = true;
            }
        }
    }
}
