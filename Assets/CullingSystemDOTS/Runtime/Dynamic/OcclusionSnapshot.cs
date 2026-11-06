using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic
{
    public readonly struct OcclusionGeometryView
    {
        public readonly GeometryHandle Handle;
        public readonly uint Revision;
        public readonly bool Available;
        public readonly NativeArray<float3>.ReadOnly Vertices;
        public readonly NativeArray<int>.ReadOnly Indices;
        internal OcclusionGeometryView(GeometryHandle handle, uint revision, bool available, NativeArray<float3>.ReadOnly vertices, NativeArray<int>.ReadOnly indices)
        { Handle = handle; Revision = revision; Available = available; Vertices = vertices; Indices = indices; }
    }

    public sealed class OcclusionSnapshotLease : IDisposable
    {
        public ulong WorldIdentity { get; }
        public ulong Revision { get; }
        public ulong OccluderEpoch { get; }
        private NativeArray<OcclusionInstanceSnapshot> instances;
        private readonly Material[] materials;
        private readonly Dictionary<GeometryHandle, OcclusionGeometry> geometries;
        private readonly HashSet<GeometryHandle> unavailable;
        private readonly OcclusionMemoryBudget budget;
        private readonly Action<OcclusionSnapshotLease> released;
        private readonly long bytes;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private JobHandle readers;
        private bool disposed;
        internal OcclusionSnapshotLease(ulong world, ulong revision, ulong epoch, NativeArray<OcclusionInstanceSnapshot> instances,
            Material[] materials, Dictionary<GeometryHandle, OcclusionGeometry> geometries, HashSet<GeometryHandle> unavailable,
            OcclusionMemoryBudget budget, long bytes, Action<OcclusionSnapshotLease> released)
        {
            WorldIdentity = world; Revision = revision; OccluderEpoch = epoch;
            this.instances = instances; this.materials = materials; this.geometries = geometries; this.unavailable = unavailable;
            this.budget = budget; this.bytes = bytes; this.released = released;
        }

        /// <summary>Read-only numeric data; register scheduled readers and retain the lease until GPU readers finish.</summary>
        public NativeArray<OcclusionInstanceSnapshot>.ReadOnly Instances { get { CheckAlive(); return instances.AsReadOnly(); } }
        public int GeometryCount { get { CheckAlive(); return geometries.Count; } }
        public OcclusionGeometryView GetGeometry(GeometryHandle handle)
        {
            CheckAlive();
            if (!geometries.TryGetValue(handle, out var geometry)) throw new ArgumentException("Geometry does not belong to this snapshot.", nameof(handle));
            return new OcclusionGeometryView(handle, geometry.Revision, !unavailable.Contains(handle), geometry.Vertices.AsReadOnly(), geometry.Indices.AsReadOnly());
        }
        internal Mesh GetMesh(GeometryHandle handle) { CheckAlive(); return geometries[handle].Mesh; }
        internal Material GetMaterial(int instance) { CheckAlive(); return materials[instance]; }
        internal OcclusionGeometry RetainGeometry(GeometryHandle handle)
        {
            CheckAlive();
            if (!geometries.TryGetValue(handle, out var geometry)) throw new ArgumentException("Geometry does not belong to this snapshot.", nameof(handle));
            geometry.Retain(); return geometry;
        }
        public void RegisterReader(JobHandle dependency) { CheckAlive(); readers = JobHandle.CombineDependencies(readers, dependency); }
        internal void CompleteReaders() { CheckAlive(); readers.Complete(); readers = default; }
        private void CheckAlive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(OcclusionSnapshotLease));
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Manage snapshot leases on their owning thread; Jobs read native data only.");
        }
        public void Dispose()
        {
            if (disposed) return;
            CompleteReaders(); disposed = true;
            instances.Dispose();
            foreach (var geometry in geometries.Values) geometry.Dispose();
            geometries.Clear();
            budget.Release(bytes); released(this);
        }
    }
}
