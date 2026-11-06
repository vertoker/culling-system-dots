using System;
using System.Collections.Generic;
using System.Threading;
using CullingSystemDOTS.Rendering;
using CullingSystemDOTS.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CullingSystemDOTS.Dynamic.Ecs
{
    /// <summary>Main-thread capture and lifecycle of explicitly registered full Entities.</summary>
    public sealed class DynamicOcclusionBindings : IDisposable
    {
        private sealed class Binding
        {
            internal InstanceHandle Handle;
            internal GeometryHandle Geometry;
            internal OcclusionInstanceState LastState;
            internal bool Baseline, GraphicsOwned, CaptureValid = true;
        }
        private readonly World world;
        private readonly OcclusionWorld registry;
        private readonly GraphicsVisibilityOwner ownership;
        private readonly DynamicOcclusionCaptureSystem captureSystem;
        private readonly Dictionary<Entity, Binding> bindings = new Dictionary<Entity, Binding>();
        private readonly List<Entity> removed = new List<Entity>();
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private bool disposed;
        public DynamicOcclusionBindings(World world, OcclusionWorld registry)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.", nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.world = world; captureSystem = world.GetOrCreateSystemManaged<DynamicOcclusionCaptureSystem>();
            ownership = new GraphicsVisibilityOwner(world);
        }
        public int Count => bindings.Count;
        public string LastFault { get; private set; }
        public InstanceHandle Bind(Entity entity, in OcclusionInstanceDescriptor descriptor)
        {
            CheckAlive(); var manager = world.EntityManager;
            if (!manager.Exists(entity) || !manager.HasComponent<LocalToWorld>(entity) || !manager.HasComponent<MaterialMeshInfo>(entity) ||
                manager.HasComponent<Prefab>(entity) || manager.HasComponent<DynamicOcclusionBinding>(entity) || bindings.ContainsKey(entity))
                throw new ArgumentException("An unbound live graphics Entity with LocalToWorld is required.", nameof(entity));
            // Authored legacy bindings reserve their graphics even before their first publication.
            using (var query = manager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<CullingBakedRenderBinding>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab }))
            using (var owners = query.ToEntityArray(Allocator.Temp))
                foreach (var owner in owners)
                    foreach (var legacy in manager.GetBuffer<CullingBakedRenderBinding>(owner, true))
                        if (legacy.Main == entity || legacy.ShadowOnly == entity)
                            throw new InvalidOperationException("This Entity belongs to an authored legacy culling binding.");
            ownership.Acquire(entity);
            var handle = default(InstanceHandle);
            try
            {
                var options = new DynamicOcclusionBinding { Roles = descriptor.State.Roles, Color = descriptor.State.Color,
                    Opaque = descriptor.State.Opaque ? (byte)1 : (byte)0 };
                var next = descriptor;
                bool baseline = manager.IsComponentEnabled<MaterialMeshInfo>(entity);
                next.State = CaptureState(entity, descriptor.Geometry, options, baseline && manager.IsEnabled(entity));
                handle = registry.RegisterInstance(next); options.Handle = handle;
                manager.AddComponentData(entity, options);
                bindings.Add(entity, new Binding { Handle = handle, Geometry = descriptor.Geometry, LastState = next.State, Baseline = baseline });
                return handle;
            }
            catch
            {
                if (handle.IsValid) registry.RemoveInstance(handle);
                ownership.Release(entity); throw;
            }
        }
        public void Unbind(Entity entity)
        {
            CheckAlive(); Remove(entity);
        }
        private void Remove(Entity entity)
        {
            if (!bindings.TryGetValue(entity, out var binding)) return;
            Restore(entity, binding);
            if (world.IsCreated && world.EntityManager.Exists(entity) && world.EntityManager.HasComponent<DynamicOcclusionBinding>(entity) &&
                world.EntityManager.GetComponentData<DynamicOcclusionBinding>(entity).Handle.Equals(binding.Handle))
                world.EntityManager.RemoveComponent<DynamicOcclusionBinding>(entity);
            registry.RemoveInstance(binding.Handle); ownership.Release(entity); bindings.Remove(entity);
        }
        public void Capture()
        {
            CheckAlive(); LastFault = null; removed.Clear();
            if (bindings.Count == 0) return;
            long bytes = (long)bindings.Count * UnsafeUtility.SizeOf<OcclusionCaptureRow>();
            try { registry.ResourceBudget.Reserve(bytes); }
            catch (InvalidOperationException exception)
            {
                foreach (var pair in bindings) { registry.WithdrawInstance(pair.Value.Handle); pair.Value.CaptureValid = false; Restore(pair.Key, pair.Value); }
                LastFault = exception.Message; return;
            }
            var rows = default(NativeArray<OcclusionCaptureRow>);
            try
            {
                rows = new NativeArray<OcclusionCaptureRow>(bindings.Count, Allocator.TempJob); int index = 0;
                foreach (var pair in bindings)
                    rows[index++] = new OcclusionCaptureRow { Entity = pair.Key, Handle = pair.Value.Handle,
                        LocalBounds = registry.GetGeometryBounds(pair.Value.Geometry), State = pair.Value.LastState,
                        Baseline = pair.Value.Baseline, Owned = pair.Value.GraphicsOwned, Valid = pair.Value.CaptureValid };
                captureSystem.Capture(rows);
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i]; var entity = row.Entity; var binding = bindings[entity]; binding.Baseline = row.Baseline;
                    if (row.Status == 3) { removed.Add(entity); continue; }
                    if (row.Restore) Restore(entity, binding);
                    if (row.Status == 2)
                    {
                        registry.WithdrawInstance(binding.Handle); binding.CaptureValid = false;
                        LastFault = "ECS capture requires finite affine transforms and valid opaque roles."; continue;
                    }
                    if (row.Status == 0) continue;
                    try { registry.UpdateInstance(binding.Handle, row.State); binding.LastState = row.State; binding.CaptureValid = true; }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
                    {
                        registry.WithdrawInstance(binding.Handle); binding.CaptureValid = false;
                        Restore(entity, binding); LastFault = exception.Message;
                    }
                }
            }
            finally { if (rows.IsCreated) rows.Dispose(); registry.ResourceBudget.Release(bytes); }
            foreach (var entity in removed) Remove(entity);
        }
        private OcclusionInstanceState CaptureState(Entity entity, GeometryHandle geometry, DynamicOcclusionBinding options, bool active)
        {
            var matrix = world.EntityManager.GetComponentData<LocalToWorld>(entity).Value;
            if (!geometry.IsValid) throw new ArgumentException("ECS capture requires registered local geometry bounds.");
            return new OcclusionInstanceState { LocalToWorld = matrix, WorldBounds = OcclusionWorld.TransformBounds(registry.GetGeometryBounds(geometry), matrix),
                Color = options.Color, Roles = active ? options.Roles : OcclusionRole.None, Opaque = options.Opaque != 0 };
        }
        internal void SetGraphicsOwned(Entity entity, bool owned)
        {
            CheckAlive();
            if (!bindings.TryGetValue(entity, out var binding)) throw new ArgumentException("Entity is not bound.", nameof(entity));
            if (!owned) { Restore(entity, binding); return; }
            var manager = world.EntityManager;
            if (!binding.CaptureValid || !manager.Exists(entity) || !manager.IsEnabled(entity) ||
                !manager.HasComponent<DynamicOcclusionBinding>(entity) || !manager.IsComponentEnabled<DynamicOcclusionBinding>(entity))
                throw new InvalidOperationException("Unavailable bindings must retain stock graphics.");
            if (!binding.GraphicsOwned)
            {
                manager.CompleteAllTrackedJobs(); binding.Baseline = manager.IsComponentEnabled<MaterialMeshInfo>(entity);
                manager.SetComponentEnabled<MaterialMeshInfo>(entity, false); binding.GraphicsOwned = true;
            }
        }
        private void Restore(Entity entity, Binding binding)
        {
            if (!binding.GraphicsOwned) return;
            if (world.IsCreated && world.EntityManager.Exists(entity) && world.EntityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                world.EntityManager.CompleteAllTrackedJobs();
                world.EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, binding.Baseline);
            }
            binding.GraphicsOwned = false;
        }
        public void Dispose()
        {
            CheckThread(); if (disposed) return;
            removed.Clear(); removed.AddRange(bindings.Keys);
            foreach (var entity in removed) Remove(entity);
            ownership.Dispose(); disposed = true;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Bindings belong to their creating thread.");
        }
        private void CheckAlive()
        {
            CheckThread(); if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(DynamicOcclusionBindings));
        }
    }
}
