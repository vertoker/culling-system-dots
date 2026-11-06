using System;
using System.Threading;
using System.Threading.Tasks;

namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    internal sealed class BvhBuildOperation : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Task<NativeBvh> task;
        private bool taken;
        private bool disposed;
        internal bool IsCompleted => task.IsCompleted;

        internal BvhBuildOperation(BakeSnapshot snapshot, BakeBackendOptions options, bool gpu = false)
        {
            if (options.MemoryBudgetBytes < 0 || options.MaxCellsPerBatch < 0) throw new ArgumentOutOfRangeException(nameof(options));
            long budget = options.MemoryBudgetBytes == 0 ? 64L*1024*1024 : options.MemoryBudgetBytes;
            long snapshotBytes = (long)snapshot.Vertices.Length*12 + (long)snapshot.Indices.Length*4 +
                (long)snapshot.Geometry.Length*16 + (long)snapshot.Instances.Length*168 + (long)snapshot.Groups.Length*20 + (long)snapshot.Samples.Length*16;
            long estimate = BvhBuilder.EstimateNativeBytes(snapshot), words = (snapshot.Groups.Length+31)/32;
            long perCell = gpu ? 32+(words+2)*12 : checked(6L*((snapshot.Rays.Height+7)/8)*(words*4+6)+words*4+4);
            long available = budget-snapshotBytes-estimate*(gpu ? 3 : 1)-(gpu ? (long)snapshot.Instances.Length*112+128 : 0);
            if (available < perCell && snapshot.Samples.Length > 0) throw new InvalidOperationException("Bake payload budget cannot fit preparation and one cell.");
            task = Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                BakeNumericValidation.Validate(snapshot);
                return BvhBuilder.Build(snapshot, cancellation.Token);
            });
            snapshot.RegisterReader(task);
        }

        internal NativeBvh TakeResult()
        {
            var result = task.GetAwaiter().GetResult();
            taken = true;
            return result;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            NativeBvh result = null;
            try { result = task.GetAwaiter().GetResult(); }
            catch (Exception) { /* The result path observes worker failures; cleanup still joins it. */ }
            finally { cancellation.Dispose(); }
            if (!taken) result?.Dispose();
        }
    }
}
