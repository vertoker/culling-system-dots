using System;
using System.Runtime.ExceptionServices;
namespace CullingSystemDOTS.Editor.Baking
{
    // Only execution is retried. Publication is outside this object and can see
    // results only after one complete backend has successfully finished.
    public sealed class ResilientBakeBackend : IBakeBackend
    {
        private readonly IBakeBackend primary,fallback;
        public string Name => primary.Name+" with CPU recovery";
        public ResilientBakeBackend(IBakeBackend primary,IBakeBackend fallback)
        { this.primary = primary ?? throw new ArgumentNullException(nameof(primary)); this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback)); }
        public IBakeSession Begin(BakeSnapshot snapshot,BakeBackendOptions options = default) => new Session(primary,fallback,snapshot,options);
        private sealed class Session : IBakeSession,IBakeSessionDiagnostics
        {
            private IBakeBackend selected;
            private readonly IBakeBackend fallback;
            private BakeSnapshot snapshot;
            private readonly BakeBackendOptions options;
            private IBakeSession active;
            private BakeCellResult[] completed;
            private Exception failure;
            private bool recovering,disposed;
            public BakeSessionStatus Status { get; private set; }
            public float Progress => Status == BakeSessionStatus.Completed ? 1 : active?.Progress ?? 0;
            public string BackendName => (active as IBakeSessionDiagnostics)?.BackendName ?? selected.Name;
            public string FallbackReason { get; private set; }
            public Session(IBakeBackend primary,IBakeBackend fallback,BakeSnapshot snapshot,BakeBackendOptions options)
            {
                selected = primary; this.fallback = fallback; this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
                _ = snapshot.Samples.Length; this.options = options;
            }
            public void Tick() { EnsureAlive(); Advance(false); }
            private void Advance(bool wait)
            {
                if (Status != BakeSessionStatus.Pending && Status != BakeSessionStatus.Running) return;
                while (true)
                {
                    try
                    {
                        _ = snapshot.Samples.Length;
                        if (active == null) active = selected.Begin(snapshot,options);
                        Status = BakeSessionStatus.Running;
                        if (wait) completed = active.Complete(); else active.Tick();
                        if (active.Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
                        if (active.Status == BakeSessionStatus.Faulted)
                        { active.Complete(); throw new InvalidOperationException("Bake backend faulted without an exception."); }
                        if (active.Status == BakeSessionStatus.Completed)
                        { completed = active.Complete(); if (completed == null) throw new InvalidOperationException("Bake backend completed without results."); Status = BakeSessionStatus.Completed; snapshot = null; }
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        Status = BakeSessionStatus.Cancelled; completed = null; snapshot = null; Release(); throw;
                    }
                    catch (Exception error)
                    {
                        completed = null;
                        if (recovering)
                        {
                            failure = error; Status = BakeSessionStatus.Faulted; snapshot = null; Release(); throw;
                        }
                        // If GPU cleanup itself fails, CPU must not run concurrently
                        // with potentially live device resources.
                        try { Release(); }
                        catch (Exception cleanup)
                        { failure = new AggregateException(error,cleanup); Status = BakeSessionStatus.Faulted; snapshot = null; throw failure; }
                        FallbackReason = error.GetType().Name+": "+error.Message;
                        selected = fallback; recovering = true; Status = BakeSessionStatus.Pending;
                    }
                }
            }
            public BakeCellResult[] Complete()
            {
                EnsureAlive();
                while (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running) Advance(true);
                if (Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
                if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
                return completed;
            }
            public void Cancel()
            {
                EnsureAlive(); if (Status != BakeSessionStatus.Pending && Status != BakeSessionStatus.Running) return;
                try { active?.Cancel(); } finally { Status = BakeSessionStatus.Cancelled; completed = null; snapshot = null; Release(); }
            }
            private void Release() { var owned = active; active = null; owned?.Dispose(); }
            private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(ResilientBakeBackend)); }
            public void Dispose()
            {
                if (disposed) return;
                try { Release(); } finally { if (Status == BakeSessionStatus.Pending || Status == BakeSessionStatus.Running) Status = BakeSessionStatus.Cancelled; completed = null; snapshot = null; disposed = true; }
            }
        }
    }
}
