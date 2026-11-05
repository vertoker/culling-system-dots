using System;
using System.Collections.Generic;
using System.Linq;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Rendering;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace PerfectCullingDOTS.Runtime
{
    public sealed class CullingViewRegistration
    {
        public uint Layers = uint.MaxValue;
        public int NeighborRadius;
        public bool Invert;
        public ulong InvalidationVersion;
        public void SetDirty() { InvalidationVersion = checked(InvalidationVersion + 1); }
    }

    public sealed class CullingCapturedFrame : IDisposable
    {
        public CullingFrameInput Input;
        public NativeArray<RenderVolumeRange> Volumes;
        public NativeArray<RenderGroupBinding> Bindings;
        internal HashSet<Entity> ShadowMains = new HashSet<Entity>();
        public void Dispose()
        {
            if (Input.Volumes.IsCreated) Input.Volumes.Dispose(); if (Input.Views.IsCreated) Input.Views.Dispose();
            if (Input.Groups.IsCreated) Input.Groups.Dispose(); if (Input.Portals.IsCreated) Input.Portals.Dispose();
            if (Input.Passes.IsCreated) Input.Passes.Dispose(); if (Volumes.IsCreated) Volumes.Dispose();
            if (Bindings.IsCreated) Bindings.Dispose(); Input = default; Volumes = default; Bindings = default; ShadowMains.Clear();
        }
    }

    [DisableAutoCreation]
    public partial class CullingViewCaptureSystem : SystemBase
    {
        public CullingRenderToggleMode ToggleMode = CullingRenderToggleMode.ShadowcastMode;
        private CullingWorldSystem owner;
        private readonly List<View> views = new List<View>();
        private readonly Dictionary<Entity, VolumeRecord> volumes = new Dictionary<Entity, VolumeRecord>();
        private readonly Dictionary<Entity, GroupRecord> groups = new Dictionary<Entity, GroupRecord>();
        private readonly Dictionary<Entity, ulong> portalOrder = new Dictionary<Entity, ulong>();
        private ulong registration, generation, version;
        private bool restore;
        private CullingRenderToggleMode previousMode;
        private sealed class View { public Camera Camera; public ulong Id; public CullingViewRegistration Settings; public bool PreviousInvert; }
        private sealed class VolumeRecord { public ulong Order; public CullingVolume Data; public int GroupCount; }
        private sealed class GroupRecord { public CullingGroupDefinition Data; public CullingBakedRenderBinding[] Bindings; public ulong Generation; public CullingGroupKey Key; public bool HasKey; }
        protected override void OnCreate() { owner = World.GetOrCreateSystemManaged<CullingWorldSystem>(); }
        protected override void OnUpdate() { }
        public CullingViewRegistration RegisterView(Camera camera, ulong viewId)
        {
            if (camera == null || viewId == 0 || views.Any(v => v.Id == viewId || v.Camera == camera))
                throw new ArgumentException("A live camera and unique nonzero view identity are required.");
            var settings = new CullingViewRegistration();
            views.Add(new View { Camera = camera, Id = viewId, Settings = settings });
            owner.InvalidatePublication(); return settings;
        }
        public void UnregisterView(ulong viewId)
        {
            int index = views.FindIndex(v => v.Id == viewId);
            if (index < 0) return;
            views.RemoveAt(index); restore = true; owner.Registry.RestoreAll(); owner.InvalidatePublication();
        }

        // Main-thread boundary; borrowed scene blobs must stay alive until the scheduled evaluation completes.
        public CullingCapturedFrame Capture(int frameId, params ulong[] renderOrder)
        {
            foreach (var view in views.Where(v => v.Camera == null || !v.Camera.gameObject.activeInHierarchy).ToArray()) UnregisterView(view.Id);
            bool changed = previousMode != ToggleMode; previousMode = ToggleMode;
            var frame = new CullingCapturedFrame();
            try
            {
                using var volumeQuery = EntityManager.CreateEntityQuery(typeof(CullingVolume), typeof(LocalToWorld), typeof(LocalTransform));
                using var currentVolumes = volumeQuery.ToEntityArray(Allocator.Temp);
                var liveVolumes = new HashSet<Entity>(currentVolumes.ToArray());
                var freshVolumes = new HashSet<Entity>();
                foreach (var old in volumes.Keys.Where(e => !liveVolumes.Contains(e)).ToArray())
                {
                    var record = volumes[old];
                    for (int g = 0; g < record.GroupCount; g++) owner.Registry.ClearGroup(new CullingGroupKey(record.Data.InstanceId, (ushort)g));
                    volumes.Remove(old); changed = true;
                }
                foreach (var entity in currentVolumes)
                {
                    var data = EntityManager.GetComponentData<CullingVolume>(entity);
                    if (!data.Bake.IsCreated || math.all(data.InstanceId == 0)) throw new ArgumentException("A live baked volume and nonzero instance identity are required.");
                    if (!volumes.TryGetValue(entity, out var record))
                    { volumes.Add(entity, record = new VolumeRecord { Order = checked(++registration) }); freshVolumes.Add(entity); changed = true; }
                    if (record.GroupCount > 0)
                        for (int g = 0; g < record.GroupCount; g++)
                            if (!record.Data.InstanceId.Equals(data.InstanceId) || g >= data.Bake.Value.GroupCount)
                                owner.Registry.ClearGroup(new CullingGroupKey(record.Data.InstanceId, (ushort)g));
                    if (!record.Data.Equals(data)) changed = true;
                    record.Data = data; record.GroupCount = data.Bake.Value.GroupCount;
                }
                var ordered = volumes.Keys.OrderBy(e => volumes[e].Data.RegistrationOrder).ThenBy(e => volumes[e].Order).ToArray();
                var definitions = new Dictionary<Entity, Entity[]>();
                foreach (var entity in ordered) definitions.Add(entity, new Entity[volumes[entity].GroupCount]);
                using var groupQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc {
                    All = new[] { ComponentType.ReadOnly<CullingGroupDefinition>(), ComponentType.ReadOnly<CullingBakedRenderBinding>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities });
                using var currentGroups = groupQuery.ToEntityArray(Allocator.Temp);
                var liveGroups = new HashSet<Entity>();
                foreach (var entity in currentGroups)
                {
                    var definition = EntityManager.GetComponentData<CullingGroupDefinition>(entity);
                    if (!definitions.TryGetValue(definition.Volume, out var slots)) continue;
                    if (definition.GroupIndex >= slots.Length || slots[definition.GroupIndex] != Entity.Null)
                        throw new ArgumentException("Group definitions must preserve unique original bake indices.");
                    slots[definition.GroupIndex] = entity; liveGroups.Add(entity);
                    var bindings = EntityManager.GetBuffer<CullingBakedRenderBinding>(entity, true).ToNativeArray(Allocator.Temp);
                    try
                    {
                        var rows = bindings.ToArray();
                        if (!groups.TryGetValue(entity, out var record))
                        { groups.Add(entity, record = new GroupRecord { Generation = checked(++generation) }); changed = true; }
                        if (!record.Data.Equals(definition))
                        { record.Generation = checked(++generation); changed = true; }
                        if (record.Bindings == null || !record.Bindings.SequenceEqual(rows)) changed = true;
                        record.Data = definition; record.Bindings = rows;
                    }
                    finally { bindings.Dispose(); }
                }
                foreach (var old in groups.Keys.Where(e => !liveGroups.Contains(e)).ToArray())
                {
                    if (groups[old].HasKey) owner.Registry.ClearGroup(groups[old].Key);
                    groups.Remove(old); changed = true;
                }
                var portalSnapshots = new List<CullingPortalSnapshot>();
                using var portalQuery = EntityManager.CreateEntityQuery(typeof(CullingPortalLink), typeof(LocalToWorld));
                using var portalEntities = portalQuery.ToEntityArray(Allocator.Temp);
                var livePortals = new HashSet<Entity>(portalEntities.ToArray());
                foreach (var old in portalOrder.Keys.Where(e => !livePortals.Contains(e)).ToArray()) { portalOrder.Remove(old); changed = true; }
                foreach (var portal in portalEntities)
                    if (!portalOrder.ContainsKey(portal)) { portalOrder.Add(portal, checked(++registration)); changed = true; }
                var volumeSnapshots = new List<CullingVolumeSnapshot>();
                var groupSnapshots = new List<CullingGroupSnapshot>();
                var renderBindings = new List<RenderGroupBinding>();
                var ranges = new List<RenderVolumeRange>();
                foreach (var entity in ordered)
                {
                    var record = volumes[entity]; var data = record.Data; var layout = data.Bake.Value.BakedGrid;
                    int offset = groupSnapshots.Count, portalOffset = portalSnapshots.Count;
                    foreach (var portal in portalEntities.Where(p => EntityManager.GetComponentData<CullingPortalLink>(p).Volume == entity)
                        .OrderBy(p => EntityManager.GetComponentData<CullingPortalLink>(p).RegistrationOrder).ThenBy(p => portalOrder[p]))
                        portalSnapshots.Add(new CullingPortalSnapshot { WorldPosition = EntityManager.GetComponentData<LocalToWorld>(portal).Position });
                    var rotation = Rotation(entity);
                    var inverse = Quaternion.Inverse(rotation);
                    var snapshot = new CullingVolumeSnapshot { Bake = data.Bake, InstanceId = data.InstanceId,
                        Layers = data.Layers, OutOfBounds = data.OutOfBounds, EmptyCell = data.EmptyCell, SearchNonEmpty = data.SearchNonEmpty,
                        GroupOffset = offset, PortalOffset = portalOffset, PortalCount = portalSnapshots.Count - portalOffset,
                        Grid = new CullingGridSnapshot { Origin = EntityManager.GetComponentData<LocalToWorld>(entity).Position,
                            Size = data.Size, CellCount = layout.CellCount, CellSize = layout.CellSize, BakeOrientation = layout.BakeOrientation,
                            InverseBakeOrientation = layout.InverseBakeOrientation, InverseCurrentOrientation = new quaternion(inverse.x, inverse.y, inverse.z, inverse.w) } };
                    for (int g = 0; g < record.GroupCount; g++)
                    {
                        var key = new CullingGroupKey(data.InstanceId, (ushort)g);
                        var definitionEntity = definitions[entity][g];
                        var state = new CullingGroupSnapshot { Key = key, InitialApplied = 1, StateGeneration = record.Order };
                        if (!freshVolumes.Contains(entity) && owner.TryGetGroupState(key, out bool queued, out bool applied))
                        { state.InitialQueued = queued ? (byte)1 : (byte)0; state.InitialApplied = applied ? (byte)1 : (byte)0; }
                        if (definitionEntity != Entity.Null)
                        {
                            var group = groups[definitionEntity];
                            if (group.HasKey && !group.Key.Equals(key)) owner.Registry.ClearGroup(group.Key);
                            group.Key = key; group.HasKey = true;
                            state.StateGeneration = group.Generation; state.VertexCount = group.Data.VertexCount;
                            foreach (var binding in group.Bindings)
                            {
                                if (!EntityManager.Exists(binding.Main)) continue;
                                var shadow = binding.ShadowOnly;
                                if (ToggleMode == CullingRenderToggleMode.RendererComponent && EntityManager.HasComponent<MaterialMeshInfo>(binding.Main))
                                    EntityManager.SetComponentEnabled<MaterialMeshInfo>(binding.Main, true);
                                if (ToggleMode == CullingRenderToggleMode.ShadowcastMode && binding.OriginalShadowMode == ShadowCastingMode.On && shadow == Entity.Null)
                                { shadow = owner.ShadowCompanions.GetOrCreate(binding.Main); frame.ShadowMains.Add(binding.Main); }
                                renderBindings.Add(new RenderGroupBinding { Group = key, Main = binding.Main, ShadowOnly = shadow,
                                    OriginalShadowMode = binding.OriginalShadowMode, ToggleMode = ToggleMode,
                                    InitialRendererEnabled = binding.InitialRendererEnabled != 0, InitialForceRenderingOff = binding.InitialForceRenderingOff != 0 });
                            }
                        }
                        groupSnapshots.Add(state); snapshot.TotalVertexCount = checked(snapshot.TotalVertexCount + state.VertexCount);
                    }
                    volumeSnapshots.Add(snapshot); ranges.Add(new RenderVolumeRange { GroupOffset = offset, GroupCount = record.GroupCount });
                }
                if (changed) owner.Invalidate();
                var viewSnapshots = new List<CullingViewSnapshot>();
                foreach (var view in views)
                {
                    if (view.Settings.Invert != view.PreviousInvert) { view.Settings.SetDirty(); view.PreviousInvert = view.Settings.Invert; }
                    viewSnapshots.Add(new CullingViewSnapshot { ViewId = view.Id, Position = view.Camera.transform.position,
                        Layers = view.Settings.Layers, NeighborRadius = view.Settings.NeighborRadius, Invert = view.Settings.Invert,
                        InvalidationVersion = view.Settings.InvalidationVersion });
                }
                if (renderOrder == null || renderOrder.Length == 0)
                    renderOrder = views.Where(v => v.Camera.enabled).OrderBy(v => v.Camera.depth).Select(v => v.Id).ToArray();
                frame.Input.Volumes = new NativeArray<CullingVolumeSnapshot>(volumeSnapshots.ToArray(), Allocator.Persistent);
                frame.Input.Groups = new NativeArray<CullingGroupSnapshot>(groupSnapshots.ToArray(), Allocator.Persistent);
                frame.Input.Views = new NativeArray<CullingViewSnapshot>(viewSnapshots.ToArray(), Allocator.Persistent);
                frame.Input.Portals = new NativeArray<CullingPortalSnapshot>(portalSnapshots.ToArray(), Allocator.Persistent);
                frame.Input.Passes = new NativeArray<CullingViewPass>(renderOrder.Select(id => new CullingViewPass {
                    RenderViewId = id, ViewIndex = viewSnapshots.FindIndex(v => v.ViewId == id) }).ToArray(), Allocator.Persistent);
                frame.Input.FrameId = frameId; frame.Input.SnapshotVersion = checked(++version); frame.Input.RestoreAllVisibility = restore;
                frame.Volumes = new NativeArray<RenderVolumeRange>(ranges.ToArray(), Allocator.Persistent);
                frame.Bindings = new NativeArray<RenderGroupBinding>(renderBindings.ToArray(), Allocator.Persistent);
                return frame;
            }
            catch { frame.Dispose(); throw; }
        }

        // Completes before returning: scene streaming cannot unload a borrowed blob across this boundary.
        public CullingPublicationStatus EvaluateAndPublish(int frameId, EcsRenderVisibilityAdapter adapter,
            CullingExecutionMode mode = CullingExecutionMode.Jobs, params ulong[] renderOrder)
        {
            using var frame = Capture(frameId, renderOrder);
            var ticket = owner.Schedule(in frame.Input, mode);
            var status = owner.CompleteAndPublish(ticket);
            if (status != CullingPublicationStatus.Published) return status;
            if (adapter != null)
            {
                foreach (var view in views) adapter.RegisterCamera(view.Camera, view.Id);
                World.GetOrCreateSystemManaged<CullingApplySystem>().Publish(owner, adapter, frame.Volumes, frame.Bindings);
            }
            else
            {
                var replay = World.GetOrCreateSystemManaged<CullingApplySystem>(); replay.Prepare(owner, frame.Volumes);
                for (int p = 0; p < frame.Input.Passes.Length; p++) replay.ApplyPass(p, frame.Input.Passes[p].RenderViewId);
            }
            restore = false;
            owner.ShadowCompanions.RetainOnly(frame.ShadowMains);
            return status;
        }

        public CullingPublicationStatus EvaluateAndRender(int frameId, CullingExecutionMode mode = CullingExecutionMode.Jobs,
            params ulong[] renderOrder) => EvaluateAndPublish(frameId, owner.GetOrCreateRenderingAdapter(), mode, renderOrder);

        private Quaternion Rotation(Entity entity)
        {
            var chain = new List<Quaternion>();
            for (int depth = 0; ; depth++)
            {
                if (depth > 1024) throw new ArgumentException("Cyclic or excessively deep transform hierarchy.");
                if (!EntityManager.HasComponent<LocalTransform>(entity)) throw new ArgumentException("Volume transform parents require LocalTransform rotation data.");
                var q = EntityManager.GetComponentData<LocalTransform>(entity).Rotation.value;
                chain.Add(new Quaternion(q.x, q.y, q.z, q.w));
                if (!EntityManager.HasComponent<Parent>(entity)) break;
                entity = EntityManager.GetComponentData<Parent>(entity).Value;
            }
            var rotation = chain[chain.Count - 1];
            for (int i = chain.Count - 2; i >= 0; i--) rotation = rotation * chain[i];
            return rotation;
        }

        protected override void OnDestroy() { views.Clear(); volumes.Clear(); groups.Clear(); portalOrder.Clear(); }
    }
}
