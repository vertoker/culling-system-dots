using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

namespace PerfectCullingDOTS.Rendering
{
    public struct CullingShadowCompanion : IComponentData { public Entity Main; }

    // One owner per World. Create before presentation, never inside a camera callback.
    public sealed class GraphicsShadowCompanions : IDisposable
    {
        private readonly World world;
        private readonly Dictionary<Entity, Entity> companions = new Dictionary<Entity, Entity>();
        private bool disposed;
        public GraphicsShadowCompanions(World world)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.");
            this.world = world;
        }
        public int Count => companions.Count;
        public Entity GetOrCreate(Entity main)
        {
            CheckAlive();
            var manager = world.EntityManager;
            if (!manager.Exists(main) || !manager.HasComponent<MaterialMeshInfo>(main) ||
                !manager.HasComponent<LocalToWorld>(main) || manager.HasComponent<Prefab>(main) ||
                manager.HasComponent<CullingShadowCompanion>(main))
                throw new ArgumentException("A live instantiated main graphics entity is required.");
            if (companions.TryGetValue(main, out var existing) && manager.Exists(existing)) return existing;
            Entity companion = Entity.Null;
            using var sources = new NativeArray<Entity>(new[] { main }, Allocator.Temp);
            using var outputs = new NativeArray<Entity>(1, Allocator.Temp);
            try
            {
                // This pinned 6.6 API clones ONLY the supplied set and ignores LinkedEntityGroup.
                // No simulation system runs between this copy and removal of non-graphics components.
                #pragma warning disable CS0618
                manager.Instantiate(sources, outputs);
                #pragma warning restore CS0618
                companion = outputs[0];
                using (var types = manager.GetComponentTypes(companion, Allocator.Temp))
                    foreach (var component in types)
                    {
                        var type = component.GetManagedType();
                        if (type == typeof(Entity) || type == typeof(LocalToWorld) || type == typeof(Disabled) ||
                            type == typeof(SceneTag) || type == typeof(SceneSection) ||
                            type.Assembly == typeof(MaterialMeshInfo).Assembly || type.Namespace == "Unity.Deformations" ||
                            Attribute.IsDefined(type, typeof(MaterialPropertyAttribute))) continue;
                        manager.RemoveComponent(companion, component);
                    }
                manager.AddComponentData(companion, new CullingShadowCompanion { Main = main });
                manager.AddComponentData(companion, LocalTransform.Identity);
                manager.AddComponentData(companion, new Parent { Value = main });
                var filter = manager.GetSharedComponent<RenderFilterSettings>(companion);
                filter.ShadowCastingMode = ShadowCastingMode.ShadowsOnly;
                manager.SetSharedComponent(companion, filter);
                companions[main] = companion;
                return companion;
            }
            catch
            {
                if (companion != Entity.Null && manager.Exists(companion)) manager.DestroyEntity(companion);
                throw;
            }
        }
        public void Prune()
        {
            CheckAlive();
            var removed = new List<Entity>();
            foreach (var pair in companions)
                if (!world.EntityManager.Exists(pair.Key) || !world.EntityManager.Exists(pair.Value))
                {
                    if (world.EntityManager.Exists(pair.Value)) world.EntityManager.DestroyEntity(pair.Value);
                    removed.Add(pair.Key);
                }
            foreach (var key in removed) companions.Remove(key);
        }
        public void RetainOnly(ISet<Entity> mains)
        {
            CheckAlive();
            var removed = new List<Entity>();
            foreach (var pair in companions)
                if (!mains.Contains(pair.Key))
                {
                    if (world.EntityManager.Exists(pair.Value)) world.EntityManager.DestroyEntity(pair.Value);
                    removed.Add(pair.Key);
                }
            foreach (var key in removed) companions.Remove(key);
            Prune();
        }
        private void CheckAlive()
        {
            if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(GraphicsShadowCompanions));
        }
        public void Dispose()
        {
            if (disposed) return;
            if (world.IsCreated)
                foreach (var pair in companions)
                    if (world.EntityManager.Exists(pair.Value)) world.EntityManager.DestroyEntity(pair.Value);
            companions.Clear(); disposed = true;
        }
    }
}
