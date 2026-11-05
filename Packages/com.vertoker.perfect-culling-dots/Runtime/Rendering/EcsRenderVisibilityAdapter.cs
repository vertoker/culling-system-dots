using System;
using System.Collections.Generic;
using PerfectCullingDOTS.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace PerfectCullingDOTS.Rendering
{
    public sealed class EcsRenderVisibilityAdapter : IDisposable
    {
        public string LastFault { get; private set; }
        public World TargetWorld => world;
        public bool IsDisposed => disposed;
        public event Action<BatchCullingViewType, Camera> CullingCaptured;
        public event Action<int, ulong> ApplyingPass;
        private readonly World world;
        private readonly EntitiesGraphicsSystem graphics;
        private readonly Func<BatchCullingContext, EntitiesGraphicsCullingFilter> capture;
        private readonly Dictionary<Camera, ulong> cameras = new Dictionary<Camera, ulong>();
        private readonly Stack<Scope> scopes = new Stack<Scope>();
        private Dictionary<Entity, bool> physical = new Dictionary<Entity, bool>();
        private HashSet<BindingIdentity> registered = new HashSet<BindingIdentity>();
        private PreparedPass[] prepared = Array.Empty<PreparedPass>();
        private Entity[] latest = Array.Empty<Entity>();
        private int nextPass;
        private bool disposed;

        private struct PreparedPass { public ulong ViewId; public Entity[] Excluded; }
        private struct Scope { public Camera Camera; public Entity[] Excluded; }
        private readonly struct BindingIdentity : IEquatable<BindingIdentity>
        {
            private readonly CullingGroupKey group;
            private readonly Entity main, shadow;
            private readonly ulong generation;
            private readonly int settings;
            public BindingIdentity(in RenderGroupBinding binding, ulong generation)
            {
                group = binding.Group; main = binding.Main; shadow = binding.ShadowOnly; this.generation = generation;
                settings = (int)binding.ToggleMode | ((int)binding.OriginalShadowMode << 2) |
                    (binding.InitialRendererEnabled ? 16 : 0) | (binding.InitialForceRenderingOff ? 32 : 0);
            }
            public bool Equals(BindingIdentity other) => group.Equals(other.group) && main == other.main && shadow == other.shadow && generation == other.generation && settings == other.settings;
            public override bool Equals(object obj) => obj is BindingIdentity other && Equals(other);
            public override int GetHashCode() => unchecked((((group.GetHashCode() * 397 ^ main.GetHashCode()) * 397 ^ shadow.GetHashCode()) * 397 ^ generation.GetHashCode()) * 397 ^ settings);
        }

        public EcsRenderVisibilityAdapter(World world)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live world is required.");
            this.world = world;
            graphics = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (graphics == null) throw new ArgumentException("World has no active Entities Graphics system.");
            if (graphics.CullingFilter != null) throw new InvalidOperationException("This world's culling filter already has an owner.");
            capture = Capture;
            graphics.CullingFilter = capture;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        public void RegisterCamera(Camera camera, ulong renderViewId)
        {
            CheckAlive();
            if (camera == null || renderViewId == 0) throw new ArgumentException("A live camera and nonzero stable render-view ID are required.");
            foreach (var entry in cameras)
                if (entry.Key != camera && entry.Value == renderViewId) throw new ArgumentException("Duplicate render-view ID.");
            cameras[camera] = renderViewId;
        }

        public void UnregisterCamera(Camera camera)
        {
            CheckAlive(); cameras.Remove(camera);
        }

        /// <summary>
        /// Call on the main thread after completing evaluation and before rendering begins.
        /// Copies all publication data; later native input disposal is safe. Owns no entities.
        /// Caller creates/removes graphics-only shadow companions and handles binding removal.
        /// </summary>
        public void Publish(in CullingFrameState frame, NativeArray<RenderVolumeRange> volumes,
            NativeArray<RenderGroupBinding> bindings)
        {
            CheckAlive();
            if (scopes.Count != 0) throw new InvalidOperationException("Publish before camera rendering, not inside a camera scope.");
            if (nextPass < prepared.Length) throw new InvalidOperationException("The previous ordered publication was not fully consumed.");
            if (!frame.Result.IsCreated || frame.Result[0].Status != CullingEvaluationStatus.Success)
                throw new ArgumentException("Only a completed successful Core result can be published.");
            if (!volumes.IsCreated || !bindings.IsCreated || !frame.StartApplied.IsCreated || frame.StartApplied.Length != frame.Groups.Length || frame.PassStates.Length != checked(frame.Groups.Length * frame.Passes.Length) ||
                frame.PassVolumeApplications.Length != checked(volumes.Length * frame.Passes.Length))
                throw new ArgumentException("Render publication shapes do not match the Core result.");
            int end = 0;
            for (int v = 0; v < volumes.Length; v++)
            {
                var range = volumes[v];
                if (range.GroupOffset != end || range.GroupCount < 0 || range.GroupCount > frame.Groups.Length - end)
                    throw new ArgumentException("Volume ranges must cover ordered Core groups exactly.");
                end += range.GroupCount;
            }
            if (end != frame.Groups.Length) throw new ArgumentException("Unmapped Core groups.");
            var rows = new Dictionary<CullingGroupKey, List<RenderGroupBinding>>();
            var keys = new HashSet<CullingGroupKey>();
            var groupIndices = new Dictionary<CullingGroupKey, int>();
            for (int g = 0; g < frame.Groups.Length; g++) { keys.Add(frame.Groups[g].Key); groupIndices.Add(frame.Groups[g].Key, g); }
            var canonical = new Dictionary<Entity, RenderGroupBinding>();
            var nextPhysical = new Dictionary<Entity, bool>();
            var nextRegistered = new HashSet<BindingIdentity>();
            CullingRenderToggleMode? mode = null;
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (!keys.Contains(binding.Group) || binding.Main == Entity.Null || !world.EntityManager.Exists(binding.Main) ||
                    (uint)binding.OriginalShadowMode > 3 || (uint)binding.ToggleMode > 2)
                    throw new ArgumentException("Binding must address a current Core key and a live full graphics Entity.");
                if (mode.HasValue && mode.Value != binding.ToggleMode)
                    throw new ArgumentException("The source toggle mode is global; mixed modes within a publication are unsupported.");
                mode = binding.ToggleMode;
                if (binding.ToggleMode == CullingRenderToggleMode.ShadowcastMode && binding.OriginalShadowMode == ShadowCastingMode.On &&
                    (binding.ShadowOnly == Entity.Null || !world.EntityManager.Exists(binding.ShadowOnly)))
                    throw new ArgumentException("Original On renderers require a precreated graphics-only ShadowsOnly companion.");
                if (binding.Main == binding.ShadowOnly) throw new ArgumentException("Main and shadow companion must be distinct entities.");
                if (canonical.TryGetValue(binding.Main, out var other) &&
                    (other.ShadowOnly != binding.ShadowOnly || other.ToggleMode != binding.ToggleMode ||
                     other.OriginalShadowMode != binding.OriginalShadowMode || other.InitialRendererEnabled != binding.InitialRendererEnabled ||
                     other.InitialForceRenderingOff != binding.InitialForceRenderingOff))
                    throw new ArgumentException("Overlapping bindings must describe the same physical renderer and companion.");
                canonical[binding.Main] = binding;
                if (!rows.TryGetValue(binding.Group, out var list)) rows.Add(binding.Group, list = new List<RenderGroupBinding>());
                list.Add(binding);
                if (!nextPhysical.ContainsKey(binding.Main))
                    nextPhysical[binding.Main] = physical.TryGetValue(binding.Main, out bool shown)
                        ? shown : binding.InitialRendererEnabled && !binding.InitialForceRenderingOff;
                if (binding.ShadowOnly != Entity.Null && !nextPhysical.ContainsKey(binding.ShadowOnly))
                    nextPhysical[binding.ShadowOnly] = physical.TryGetValue(binding.ShadowOnly, out bool shadowShown) && shadowShown;
                int groupIndex = groupIndices[binding.Group];
                var identity = new BindingIdentity(in binding, frame.Groups[groupIndex].StateGeneration);
                nextRegistered.Add(identity);
                if (!registered.Contains(identity))
                {
                    var inherited = RenderGroupState.Evaluate(frame.StartApplied[groupIndex] != 0, in binding);
                    nextPhysical[binding.Main] = inherited.Main;
                    if (binding.ShadowOnly != Entity.Null) nextPhysical[binding.ShadowOnly] = inherited.ShadowOnly;
                }
            }
            // Build off to the side. A malformed/rejected publication cannot replace the last accepted one.
            if (frame.Control[0].RestoreApplied)
                for (int g = 0; g < frame.Groups.Length; g++) Apply(frame.Groups[g].Key, true, rows, nextPhysical);
            var initial = Excluded(nextPhysical);
            var nextPrepared = new PreparedPass[frame.Passes.Length];
            for (int p = 0; p < frame.Passes.Length; p++)
            {
                for (int v = 0; v < volumes.Length; v++)
                {
                    if (frame.PassVolumeApplications[p * volumes.Length + v] == 0) continue;
                    var range = volumes[v];
                    for (int g = range.GroupOffset; g < range.GroupOffset + range.GroupCount; g++)
                        Apply(frame.Groups[g].Key, frame.PassStates[p * frame.Groups.Length + g] != 0, rows, nextPhysical);
                }
                nextPrepared[p] = new PreparedPass { ViewId = frame.Passes[p].RenderViewId, Excluded = Excluded(nextPhysical) };
            }
            physical = nextPhysical;
            registered = nextRegistered;
            prepared = nextPrepared;
            nextPass = 0;
            latest = initial;
        }

        private static void Apply(CullingGroupKey group, bool visible,
            Dictionary<CullingGroupKey, List<RenderGroupBinding>> rows, Dictionary<Entity, bool> state)
        {
            if (!rows.TryGetValue(group, out var bindings)) return;
            foreach (var binding in bindings)
            {
                var value = RenderGroupState.Evaluate(visible, in binding);
                state[binding.Main] = value.Main;
                if (binding.ShadowOnly != Entity.Null) state[binding.ShadowOnly] = value.ShadowOnly;
            }
        }

        private static Entity[] Excluded(Dictionary<Entity, bool> state)
        {
            var excluded = new List<Entity>();
            foreach (var pair in state) if (!pair.Value) excluded.Add(pair.Key);
            return excluded.ToArray();
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (cameras.TryGetValue(camera, out ulong viewId) && nextPass < prepared.Length)
            {
                if (prepared[nextPass].ViewId == viewId)
                {
                    ApplyingPass?.Invoke(nextPass, viewId);
                    latest = prepared[nextPass++].Excluded;
                }
                else Fault("Actual camera order differs from the prepared Core pass order.");
            }
            scopes.Push(new Scope { Camera = camera, Excluded = latest });
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (scopes.Count == 0 || scopes.Peek().Camera != camera)
            {
                Fault("Unbalanced render camera scopes."); scopes.Clear(); return;
            }
            scopes.Pop();
        }

        private EntitiesGraphicsCullingFilter Capture(BatchCullingContext context)
        {
            var owner = scopes.Count == 0 ? default : scopes.Peek();
            if (owner.Camera == null && (context.viewType == BatchCullingViewType.Light || context.viewType == BatchCullingViewType.Camera))
                Fault("Camera/light culling arrived outside a proven render camera scope.");
            var entities = owner.Excluded ?? latest;
            var excluded = new NativeParallelHashSet<Entity>(entities.Length, Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++) excluded.Add(entities[i]);
            try { CullingCaptured?.Invoke(context.viewType, owner.Camera); }
            catch { excluded.Dispose(); throw; }
            return new EntitiesGraphicsCullingFilter { ExcludedEntities = excluded };
        }

        private void Fault(string message) { if (LastFault == null) LastFault = message; }
        private void CheckAlive() { if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(EcsRenderVisibilityAdapter)); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            if (graphics.CullingFilter == capture) graphics.CullingFilter = null;
            scopes.Clear(); cameras.Clear(); physical.Clear(); registered.Clear(); prepared = Array.Empty<PreparedPass>(); latest = Array.Empty<Entity>();
        }
    }
}
