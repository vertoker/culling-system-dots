using System;
using System.Collections.Generic;
using PerfectCullingDOTS.Core;
using Unity.Entities;
using UnityEngine;

namespace PerfectCullingDOTS.Runtime
{
    public readonly struct CullingBindingHandle
    {
        internal readonly ulong World, Id;
        internal CullingBindingHandle(ulong world, ulong id) { World = world; Id = id; }
    }

    // Main-thread boundary for explicitly opted-in receivers. Owned by one CullingWorldSystem.
    public sealed class CullingGroupRegistry : IDisposable
    {
        private readonly World world;
        private readonly Action changed;
        private readonly Dictionary<ulong, Binding> bindings = new Dictionary<ulong, Binding>();
        private readonly Dictionary<CullingGroupKey, bool> states = new Dictionary<CullingGroupKey, bool>();
        private ulong nextId;
        private bool disposed;
        private struct Binding
        {
            public CullingGroupKey Group;
            public Entity Entity;
            public ComponentType Enableable;
            public bool HasEnableable;
            public Behaviour Behaviour;
        }

        internal CullingGroupRegistry(World world, Action changed) { this.world = world; this.changed = changed; }
        public int Count => bindings.Count;
        public CullingBindingHandle RegisterStateEntity(CullingGroupKey group, Entity entity)
            => RegisterEntity(group, entity, default, false);
        public CullingBindingHandle RegisterEntity<T>(CullingGroupKey group, Entity entity)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            // Graphics masks belong to the per-view adapter, never to this simulation receiver bridge.
            if (typeof(T).Assembly == typeof(Unity.Rendering.MaterialMeshInfo).Assembly)
                throw new ArgumentException("Graphics enableable components must use render bindings.");
            return RegisterEntity(group, entity, ComponentType.ReadWrite<T>(), true);
        }
        private CullingBindingHandle RegisterEntity(CullingGroupKey group, Entity entity, ComponentType type, bool enabled)
        {
            CheckAlive();
            var manager = world.EntityManager;
            if (!manager.Exists(entity) || (enabled && !manager.HasComponent(entity, type)))
                throw new ArgumentException("Receiver must be a live full Entity with the explicitly selected component.");
            if (!manager.HasComponent<CullingGroupState>(entity)) manager.AddComponent<CullingGroupState>(entity);
            return Add(new Binding { Group = group, Entity = entity, Enableable = type, HasEnableable = enabled });
        }
        public CullingBindingHandle RegisterBehaviour(CullingGroupKey group, Behaviour behaviour)
        {
            CheckAlive();
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
            return Add(new Binding { Group = group, Behaviour = behaviour });
        }
        private CullingBindingHandle Add(Binding binding)
        {
            ulong id = checked(++nextId);
            if (states.TryGetValue(binding.Group, out bool visible)) ApplyBinding(in binding, visible);
            else if (binding.Entity != Entity.Null)
            {
                var manager = world.EntityManager;
                bool initial = !binding.HasEnableable || manager.IsComponentEnabled(binding.Entity, binding.Enableable);
                manager.SetComponentData(binding.Entity, new CullingGroupState { Group = binding.Group, Visible = initial ? (byte)1 : (byte)0 });
            }
            bindings.Add(id, binding);
            changed();
            return new CullingBindingHandle(world.SequenceNumber, id);
        }
        public bool Remove(CullingBindingHandle handle)
        {
            CheckAlive();
            if (handle.World != world.SequenceNumber || !bindings.TryGetValue(handle.Id, out var binding)) return false;
            ApplyBinding(in binding, true);
            bindings.Remove(handle.Id); changed(); return true;
        }
        public void ClearGroup(CullingGroupKey group)
        {
            CheckAlive();
            var removed = new List<ulong>();
            foreach (var pair in bindings)
                if (pair.Value.Group.Equals(group))
                { var binding = pair.Value; ApplyBinding(in binding, true); removed.Add(pair.Key); }
            foreach (var id in removed) bindings.Remove(id);
            changed();
        }
        public void ForgetGroup(CullingGroupKey group)
        {
            ClearGroup(group);
            states.Remove(group);
        }
        public void Apply(CullingGroupKey group, bool visible)
        {
            CheckAlive(); states[group] = visible;
            foreach (var pair in bindings)
            {
                var binding = pair.Value;
                if (binding.Group.Equals(group)) ApplyBinding(in binding, visible);
            }
        }
        public void RestoreAll()
        {
            CheckAlive();
            foreach (var pair in bindings) { var binding = pair.Value; ApplyBinding(in binding, true); states[binding.Group] = true; }
            var keys = new List<CullingGroupKey>(states.Keys);
            foreach (var key in keys) states[key] = true;
        }
        private void ApplyBinding(in Binding binding, bool visible)
        {
            if (binding.Behaviour != null) binding.Behaviour.enabled = visible;
            var manager = world.EntityManager;
            if (binding.Entity == Entity.Null || !manager.Exists(binding.Entity)) return;
            if (manager.HasComponent<CullingGroupState>(binding.Entity))
                manager.SetComponentData(binding.Entity, new CullingGroupState { Group = binding.Group, Visible = visible ? (byte)1 : (byte)0 });
            if (binding.HasEnableable && manager.HasComponent(binding.Entity, binding.Enableable))
                manager.SetComponentEnabled(binding.Entity, binding.Enableable, visible);
        }
        private void CheckAlive()
        {
            if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(CullingGroupRegistry));
        }
        public void Dispose()
        {
            if (disposed) return;
            if (world.IsCreated) RestoreAll();
            bindings.Clear(); states.Clear(); disposed = true;
        }
    }
}
