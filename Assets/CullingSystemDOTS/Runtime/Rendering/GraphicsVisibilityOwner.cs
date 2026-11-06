using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Entities;

namespace CullingSystemDOTS.Rendering
{
    /// <summary>Exclusive, full-Entity graphics claims. Claims never disable simulation.</summary>
    public sealed class GraphicsVisibilityOwner : IDisposable
    {
        private static readonly ConditionalWeakTable<World, Dictionary<Entity, GraphicsVisibilityOwner>> Claims =
            new ConditionalWeakTable<World, Dictionary<Entity, GraphicsVisibilityOwner>>();
        private readonly World world;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<Entity, GraphicsVisibilityOwner> claims;
        private HashSet<Entity> retained = new HashSet<Entity>();
        private bool disposed;
        public GraphicsVisibilityOwner(World world)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.", nameof(world));
            this.world = world;
            claims = Claims.GetValue(world, _ => new Dictionary<Entity, GraphicsVisibilityOwner>());
        }
        public int Count => retained.Count;
        public void EnsureAvailable(IEnumerable<Entity> entities)
        {
            CheckAlive();
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            foreach (var entity in entities) EnsureAvailable(entity);
        }
        private void EnsureAvailable(Entity entity)
        {
            if (entity == Entity.Null || !world.EntityManager.Exists(entity)) throw new ArgumentException("A live full Entity is required.");
            if (claims.TryGetValue(entity, out var owner) && owner != this)
                throw new InvalidOperationException("This graphics Entity already belongs to another visibility owner.");
        }
        public void Acquire(Entity entity)
        {
            CheckAlive(); EnsureAvailable(entity); claims[entity] = this; retained.Add(entity);
        }
        /// <summary>Validates the entire batch before changing any claim. Rejection preserves previous ownership.</summary>
        public void RetainOnly(IEnumerable<Entity> entities)
        {
            CheckAlive();
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            var next = new HashSet<Entity>(entities); EnsureAvailable(next);
            foreach (var entity in retained) if (!next.Contains(entity)) claims.Remove(entity);
            foreach (var entity in next) claims[entity] = this;
            retained = next;
        }
        public void Release(Entity entity)
        {
            CheckThread();
            if (retained.Remove(entity) && claims.TryGetValue(entity, out var owner) && owner == this) claims.Remove(entity);
        }
        public void Dispose()
        {
            CheckThread(); if (disposed) return;
            foreach (var entity in retained) if (claims.TryGetValue(entity, out var owner) && owner == this) claims.Remove(entity);
            retained.Clear(); disposed = true;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Graphics claims belong to their creating thread.");
        }
        private void CheckAlive()
        {
            CheckThread(); if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(GraphicsVisibilityOwner));
        }
    }
}
