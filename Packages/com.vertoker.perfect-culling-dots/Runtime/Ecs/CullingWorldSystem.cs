using System;
using System.Collections.Generic;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace PerfectCullingDOTS.Runtime
{
    public enum CullingExecutionMode { Sequential, Jobs }
    public enum CullingPublicationStatus { Published, NoPending, WrongTicket, Stale, InvalidData }

    public readonly struct CullingEvaluationTicket
    {
        public readonly ulong WorldSequence;
        public readonly ulong Request;
        public readonly ulong SourceVersion;
        public readonly ulong Revision;
        internal CullingEvaluationTicket(ulong worldSequence, ulong request, ulong sourceVersion, ulong revision)
        { WorldSequence = worldSequence; Request = request; SourceVersion = sourceVersion; Revision = revision; }
    }

    [DisableAutoCreation]
    public partial class CullingWorldSystem : SystemBase
    {
        private ulong request, revision, worldSequence, viewStamp, viewRevision;
        private bool disposed, hasPending;
        private CullingFrameState current, pending;
        private OwnedSnapshot pendingInput;
        private CullingEvaluationTicket pendingTicket;
        private JobHandle pendingHandle;
        private EcsRenderVisibilityAdapter ownedRenderAdapter;
        private readonly Dictionary<ulong, ViewVersion> viewVersions = new Dictionary<ulong, ViewVersion>();
        private struct ViewVersion { public ulong Source, Revision, Effective; }

        public bool HasPendingEvaluation => hasPending;
        public bool HasNativeState => current.Result.IsCreated || pending.Result.IsCreated || pendingInput.Input.Volumes.IsCreated;
        public ulong AcceptedVersion { get; private set; }
        public CullingEvaluationResult LastEvaluationResult { get; private set; }
        public JobHandle PendingDependency => pendingHandle;
        public CullingGroupRegistry Registry { get; private set; }
        public GraphicsShadowCompanions ShadowCompanions { get; private set; }
        internal CullingFrameState AcceptedFrame => current;
        internal ulong Revision => revision;
        internal ulong AcceptedRevision { get; private set; }

        protected override void OnCreate()
        {
            worldSequence = World.SequenceNumber;
            current = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            Registry = new CullingGroupRegistry(World, Invalidate);
            ShadowCompanions = new GraphicsShadowCompanions(World);
        }
        protected override void OnUpdate() { }
        public EcsRenderVisibilityAdapter GetOrCreateRenderingAdapter()
        {
            EnsureAlive();
            return ownedRenderAdapter ?? (ownedRenderAdapter = new EcsRenderVisibilityAdapter(World));
        }

        public CullingEvaluationTicket Schedule(in CullingFrameInput input, CullingExecutionMode mode = CullingExecutionMode.Jobs,
            JobHandle dependency = default)
        {
            EnsureAlive();
            if (hasPending) throw new InvalidOperationException("Complete or cancel the pending evaluation first.");
            if (mode != CullingExecutionMode.Sequential && mode != CullingExecutionMode.Jobs)
                throw new ArgumentOutOfRangeException(nameof(mode));
            var ticket = new CullingEvaluationTicket(worldSequence, checked(++request), input.SnapshotVersion, revision);
            try
            {
                pendingInput = OwnedSnapshot.Capture(in input);
                var snapshot = pendingInput.Input;
                snapshot.SnapshotVersion = ticket.Request;
                for (int i = 0; i < snapshot.Views.Length; i++)
                {
                    var view = snapshot.Views[i];
                    if (!viewVersions.TryGetValue(view.ViewId, out var version) ||
                        version.Source != view.InvalidationVersion || version.Revision != viewRevision)
                    {
                        version = new ViewVersion { Source = view.InvalidationVersion, Revision = viewRevision, Effective = checked(++viewStamp) };
                        viewVersions[view.ViewId] = version;
                    }
                    view.InvalidationVersion = version.Effective;
                    snapshot.Views[i] = view;
                }
                pendingInput.Input = snapshot;
                pending = CullingFrameState.Create(snapshot.Groups.Length, snapshot.Views.Length,
                    snapshot.Volumes.Length, snapshot.Passes.Length, Allocator.Persistent);
                if (mode == CullingExecutionMode.Sequential)
                {
                    dependency.Complete();
                    CullingFrameEvaluator.Run(in snapshot, in current, ref pending);
                }
                else pendingHandle = CullingFrameEvaluator.Schedule(in snapshot, in current, ref pending, dependency);
                pendingTicket = ticket;
                hasPending = true;
                return ticket;
            }
            catch { ReleasePending(); throw; }
        }

        public CullingPublicationStatus CompleteAndPublish(CullingEvaluationTicket ticket)
        {
            EnsureAlive();
            if (!hasPending) return CullingPublicationStatus.NoPending;
            if (ticket.WorldSequence != pendingTicket.WorldSequence || ticket.Request != pendingTicket.Request ||
                ticket.SourceVersion != pendingTicket.SourceVersion || ticket.Revision != pendingTicket.Revision)
                return CullingPublicationStatus.WrongTicket;
            pendingHandle.Complete();
            LastEvaluationResult = pending.Result[0];
            if (ticket.Revision != revision) { ReleasePending(); PruneViewVersions(); return CullingPublicationStatus.Stale; }
            if (LastEvaluationResult.Status != CullingEvaluationStatus.Success)
            { ReleasePending(); PruneViewVersions(); return CullingPublicationStatus.InvalidData; }
            current.Dispose();
            current = pending;
            pending = default;
            AcceptedVersion = ticket.Request;
            AcceptedRevision = ticket.Revision;
            ReleasePending();
            PruneViewVersions();
            return CullingPublicationStatus.Published;
        }

        public void Invalidate() { InvalidatePublication(); viewRevision = checked(viewRevision + 1); }
        // Camera registry changes affect the source's computed hash; do not force unrelated cached views dirty.
        public void InvalidatePublication() { EnsureAlive(); revision = checked(revision + 1); }
        public void CancelPending() { EnsureAlive(); ReleasePending(); PruneViewVersions(); }
        public bool TryGetGroupState(CullingGroupKey key, out bool queued, out bool applied)
        {
            EnsureAlive();
            for (int i = 0; i < current.Groups.Length; i++)
                if (current.Groups[i].Key.Equals(key))
                { queued = current.Queued[i] != 0; applied = current.Applied[i] != 0; return true; }
            queued = applied = false; return false;
        }
        public bool TryGetViewState(ulong viewId, out CullingViewState state)
        {
            EnsureAlive();
            for (int i = 0; i < current.Views.Length; i++)
                if (current.Views[i].ViewId == viewId) { state = current.Views[i]; return true; }
            state = default; return false;
        }

        public bool TryGetPassState(int passIndex, CullingGroupKey key, out bool applied)
        {
            EnsureAlive();
            if ((uint)passIndex < (uint)current.Passes.Length)
                for (int g = 0; g < current.Groups.Length; g++)
                    if (current.Groups[g].Key.Equals(key))
                    { applied = current.PassStates[passIndex * current.Groups.Length + g] != 0; return true; }
            applied = false; return false;
        }

        protected override void OnDestroy()
        {
            try { ReleasePending(); }
            finally
            {
                try { try { ownedRenderAdapter?.Dispose(); Registry?.Dispose(); } finally { ShadowCompanions?.Dispose(); } }
                finally { current.Dispose(); current = default; viewVersions.Clear(); disposed = true; }
            }
        }

        private void EnsureAlive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CullingWorldSystem));
        }

        private void ReleasePending()
        {
            pendingHandle.Complete();
            pending.Dispose(); pending = default;
            pendingInput.Dispose(); pendingInput = default;
            pendingHandle = default; pendingTicket = default; hasPending = false;
        }

        private void PruneViewVersions()
        {
            var retained = new Dictionary<ulong, ViewVersion>();
            for (int i = 0; i < current.Views.Length; i++)
                if (viewVersions.TryGetValue(current.Views[i].ViewId, out var version))
                    retained.Add(current.Views[i].ViewId, version);
            viewVersions.Clear();
            foreach (var item in retained) viewVersions.Add(item.Key, item.Value);
        }

        // Arrays are copied; immutable bake blobs remain borrowed until completion or World destruction.
        private struct OwnedSnapshot : IDisposable
        {
            public CullingFrameInput Input;
            public static OwnedSnapshot Capture(in CullingFrameInput source)
            {
                if (!source.Volumes.IsCreated || !source.Views.IsCreated || !source.Groups.IsCreated ||
                    !source.Portals.IsCreated || !source.Passes.IsCreated)
                    throw new ArgumentException("All snapshot arrays must be created, including empty arrays.");
                var owned = new OwnedSnapshot();
                try
                {
                    owned.Input.Volumes = new NativeArray<CullingVolumeSnapshot>(source.Volumes, Allocator.Persistent);
                    owned.Input.Views = new NativeArray<CullingViewSnapshot>(source.Views, Allocator.Persistent);
                    owned.Input.Groups = new NativeArray<CullingGroupSnapshot>(source.Groups, Allocator.Persistent);
                    owned.Input.Portals = new NativeArray<CullingPortalSnapshot>(source.Portals, Allocator.Persistent);
                    owned.Input.Passes = new NativeArray<CullingViewPass>(source.Passes, Allocator.Persistent);
                    owned.Input.FrameId = source.FrameId; owned.Input.SnapshotVersion = source.SnapshotVersion;
                    owned.Input.RestoreAllVisibility = source.RestoreAllVisibility;
                    return owned;
                }
                catch { owned.Dispose(); throw; }
            }
            public void Dispose()
            {
                if (Input.Volumes.IsCreated) Input.Volumes.Dispose();
                if (Input.Views.IsCreated) Input.Views.Dispose();
                if (Input.Groups.IsCreated) Input.Groups.Dispose();
                if (Input.Portals.IsCreated) Input.Portals.Dispose();
                if (Input.Passes.IsCreated) Input.Passes.Dispose();
                Input = default;
            }
        }
    }
}
