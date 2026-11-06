using System;
using System.Collections.Generic;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;

namespace CullingSystemDOTS.Runtime
{
    [DisableAutoCreation]
    public partial class CullingApplySystem : SystemBase
    {
        private struct Operation { public CullingGroupKey Key; public bool Visible; }
        private Operation[][] operations = Array.Empty<Operation[]>();
        private ulong[] views = Array.Empty<ulong>();
        private CullingWorldSystem owner;
        private EcsRenderVisibilityAdapter adapter;
        private ulong version, revision;
        private int next;
        protected override void OnUpdate() { }

        // Copies ordered operations; no native ownership is transferred and no final state is applied early.
        public void Prepare(CullingWorldSystem source, NativeArray<RenderVolumeRange> volumes)
            => Prepare(source, volumes, null);

        public void Publish(CullingWorldSystem source, EcsRenderVisibilityAdapter target,
            NativeArray<RenderVolumeRange> volumes, NativeArray<RenderGroupBinding> bindings)
        {
            if (target == null || target.TargetWorld != World) throw new ArgumentException("Adapter must belong to the same World.");
            Prepare(source, volumes, () => {
                var frame = source.AcceptedFrame;
                target.Publish(in frame, volumes, bindings);
            });
            Attach(target);
        }

        private void Prepare(CullingWorldSystem source, NativeArray<RenderVolumeRange> volumes, Action publishGraphics)
        {
            if (source == null || source.World != World) throw new ArgumentException("Receiver replay must belong to the same World.");
            if (next < operations.Length) throw new InvalidOperationException("Consume the previous ordered presentation first.");
            if (source.AcceptedVersion == 0 || source.AcceptedRevision != source.Revision)
                throw new InvalidOperationException("A current accepted evaluation is required.");
            var frame = source.AcceptedFrame;
            if (!volumes.IsCreated || frame.PassVolumeApplications.Length != checked(volumes.Length * frame.Passes.Length))
                throw new ArgumentException("Volume ranges do not match the accepted frame.");
            int end = 0;
            foreach (var range in volumes)
            {
                if (range.GroupOffset != end || range.GroupCount < 0 || range.GroupCount > frame.Groups.Length - end)
                    throw new ArgumentException("Volume ranges must cover the ordered groups exactly.");
                end += range.GroupCount;
            }
            if (end != frame.Groups.Length) throw new ArgumentException("Unmapped receiver groups.");
            var prepared = new Operation[frame.Passes.Length][];
            var preparedViews = new ulong[frame.Passes.Length];
            for (int p = 0; p < frame.Passes.Length; p++)
            {
                var list = new List<Operation>();
                for (int v = 0; v < volumes.Length; v++)
                {
                    if (frame.PassVolumeApplications[p * volumes.Length + v] == 0) continue;
                    var range = volumes[v];
                    for (int g = range.GroupOffset; g < range.GroupOffset + range.GroupCount; g++)
                        list.Add(new Operation { Key = frame.Groups[g].Key, Visible = frame.PassStates[p * frame.Groups.Length + g] != 0 });
                }
                prepared[p] = list.ToArray(); preparedViews[p] = frame.Passes[p].RenderViewId;
            }
            publishGraphics?.Invoke();
            if (frame.Control[0].RestoreApplied)
            {
                source.Registry.RestoreAll();
                for (int g = 0; g < frame.Groups.Length; g++) source.Registry.Apply(frame.Groups[g].Key, true);
            }
            owner = source; version = source.AcceptedVersion; revision = source.Revision;
            operations = prepared; views = preparedViews; next = 0;
        }

        public void ApplyPass(int index, ulong viewId)
        {
            if (index != next || (uint)index >= (uint)operations.Length || views[index] != viewId)
                throw new InvalidOperationException("Receiver pass order differs from the accepted presentation.");
            if (owner.AcceptedVersion != version || owner.Revision != revision)
                throw new InvalidOperationException("Receiver publication became stale before presentation.");
            foreach (var operation in operations[index]) owner.Registry.Apply(operation.Key, operation.Visible);
            next++;
        }

        public void Attach(EcsRenderVisibilityAdapter target)
        {
            if (target != null && target.TargetWorld != World) throw new ArgumentException("Adapter must belong to the same World.");
            if (ReferenceEquals(adapter, target)) return;
            if (adapter != null) adapter.ApplyingPass -= ApplyPass;
            adapter = target;
            if (adapter != null) adapter.ApplyingPass += ApplyPass;
        }

        // Explicit presentation-owner shutdown; no pending operations may leak to a new driver.
        public void CancelPresentation()
        {
            Attach(null); operations = Array.Empty<Operation[]>(); views = Array.Empty<ulong>(); next = 0;
            owner = null;
        }

        protected override void OnDestroy()
        {
            Attach(null); operations = Array.Empty<Operation[]>(); views = Array.Empty<ulong>(); owner = null;
        }
    }
}
