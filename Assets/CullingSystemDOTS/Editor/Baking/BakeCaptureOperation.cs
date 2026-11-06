using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CullingSystemDOTS.Editor.Baking
{
    public sealed class BakeCaptureOperation : IDisposable
    {
        private IEnumerator<float> steps;
        private CollectedBakeInput result;
        public bool IsCompleted { get; private set; }
        public float Progress { get; private set; }

        internal BakeCaptureOperation(Func<Action<CollectedBakeInput>, IEnumerable<float>> factory)
        { steps = factory(value => result = value).GetEnumerator(); }

        public void Tick(double budgetMilliseconds = 4)
        {
            if (steps == null) throw new ObjectDisposedException(nameof(BakeCaptureOperation));
            if (IsCompleted) return;
            var watch = Stopwatch.StartNew();
            try
            {
                do
                {
                    if (!steps.MoveNext()) { IsCompleted = true; Progress = 1; return; }
                    Progress = steps.Current;
                } while (watch.Elapsed.TotalMilliseconds < budgetMilliseconds);
            }
            catch { Dispose(); throw; }
        }

        public CollectedBakeInput Complete()
        { while (!IsCompleted) Tick(); return TakeResult(); }

        public CollectedBakeInput TakeResult()
        {
            if (!IsCompleted || result == null) throw new InvalidOperationException("Capture has not completed.");
            var owned = result; result = null; return owned;
        }

        public void Dispose()
        {
            try { steps?.Dispose(); }
            finally { steps = null; result?.Dispose(); result = null; }
        }
    }
}
