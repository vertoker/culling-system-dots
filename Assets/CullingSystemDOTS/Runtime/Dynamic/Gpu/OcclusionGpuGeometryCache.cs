using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionGpuGeometryCache : IDisposable
    {
        private readonly OcclusionWorld registry;
        private readonly Dictionary<GeometryHandle,OcclusionGpuMesh> meshes = new Dictionary<GeometryHandle,OcclusionGpuMesh>();
        private readonly Dictionary<GeometryHandle,uint> desired = new Dictionary<GeometryHandle,uint>();
        private readonly List<GeometryHandle> order = new List<GeometryHandle>(), removed = new List<GeometryHandle>();
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private ulong frame;
        private int creations, cursor;
        private bool disposed;
        internal int CachedCount => meshes.Count;
        internal int PendingCount { get; private set; }
        internal string LastFault { get; private set; }
        internal OcclusionGpuGeometryCache(OcclusionWorld registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _ = registry.ResourceBudget;
        }
        internal int Process(OcclusionSnapshotLease snapshot, OcclusionGpuUploadBudget budget)
        {
            Check(snapshot);
            if (budget == null || budget.Frame == 0 || budget.Limit > registry.UploadByteLimit)
                throw new ArgumentException("Begin a shared frame budget within the registry upload limit.", nameof(budget));
            if (budget.Frame < frame) throw new ArgumentException("Cache upload frame tokens cannot move backwards.", nameof(budget));
            if (budget.Frame != frame) { frame = budget.Frame; creations = 0; LastFault = null; }
            if (snapshot.Revision != registry.Revision) { LastFault = "Snapshot no longer represents the current registry."; return 0; }
            desired.Clear(); order.Clear(); removed.Clear();
            foreach (var instance in snapshot.Instances)
                if (instance.Geometry.IsValid && !desired.ContainsKey(instance.Geometry))
                { desired.Add(instance.Geometry,snapshot.GetGeometry(instance.Geometry).Revision); order.Add(instance.Geometry); }
            foreach (var pair in meshes)
                if (!desired.TryGetValue(pair.Key,out uint revision) || revision != pair.Value.Revision) removed.Add(pair.Key);
            foreach (var handle in removed) { meshes[handle].Dispose(); meshes.Remove(handle); }
            int written = 0, count = order.Count;
            if (count > 0)
            {
                int start = cursor % count;
                for (int i = 0; i < count; i++)
                {
                    int index = (start + i) % count; var handle = order[index];
                    try
                    {
                        if (!meshes.TryGetValue(handle,out var mesh))
                        {
                            if (creations >= registry.GeometryCreationLimit || budget.Remaining < 4) continue;
                            creations++; mesh = new OcclusionGpuMesh(registry,snapshot,handle); meshes.Add(handle,mesh);
                        }
                        if (mesh.Ready) continue;
                        int bytes = mesh.Upload(budget); written += bytes;
                        if (bytes > 0) cursor = (index + 1) % count;
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                        exception is OverflowException || exception is NotSupportedException || exception is UnityException)
                    {
                        LastFault = exception.Message;
                        if (meshes.TryGetValue(handle,out var failed)) { failed.Dispose(); meshes.Remove(handle); }
                    }
                }
            }
            int ready = 0;
            foreach (var pair in desired) if (meshes.TryGetValue(pair.Key,out var mesh) && mesh.Ready) ready++;
            PendingCount = desired.Count - ready; return written;
        }
        internal OcclusionGpuMeshSet Acquire(OcclusionSnapshotLease snapshot)
        {
            Check(snapshot); var result = new OcclusionGpuMeshSet();
            if (snapshot.Revision != registry.Revision) return result;
            try
            {
                foreach (var instance in snapshot.Instances)
                {
                    var handle = instance.Geometry;
                    if (!handle.IsValid || result.Contains(handle) || !meshes.TryGetValue(handle,out var mesh) || !mesh.Ready ||
                        mesh.Revision != snapshot.GetGeometry(handle).Revision) continue;
                    result.Add(handle,mesh.Revision,mesh.AcquireReader());
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose()
        {
            CheckThread(); if (disposed) return;
            foreach (var mesh in meshes.Values) mesh.Dispose();
            meshes.Clear(); desired.Clear(); order.Clear(); removed.Clear(); PendingCount = 0; disposed = true;
        }
        private void Check(OcclusionSnapshotLease snapshot)
        {
            CheckThread(); if (disposed) throw new ObjectDisposedException(nameof(OcclusionGpuGeometryCache));
            _ = registry.ResourceBudget;
            if (snapshot == null || snapshot.WorldIdentity != registry.Identity) throw new ArgumentException("A matching registry snapshot is required.", nameof(snapshot));
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Geometry caches belong to their creating thread.");
        }
    }
    internal sealed class OcclusionGpuMeshSet : IDisposable
    {
        private readonly struct Binding
        {
            internal readonly uint Revision;
            internal readonly OcclusionGpuMeshLease Reader;
            internal Binding(uint revision, OcclusionGpuMeshLease reader) { Revision = revision; Reader = reader; }
        }
        private readonly Dictionary<GeometryHandle,Binding> readers = new Dictionary<GeometryHandle,Binding>();
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private bool disposed;
        internal int Count => readers.Count;
        internal bool Contains(GeometryHandle handle) => readers.ContainsKey(handle);
        internal void Add(GeometryHandle handle, uint revision, OcclusionGpuMeshLease reader) => readers.Add(handle,new Binding(revision,reader));
        internal bool TryGet(GeometryHandle handle, uint revision, out Mesh mesh)
        {
            CheckThread(); if (disposed) throw new ObjectDisposedException(nameof(OcclusionGpuMeshSet));
            if (readers.TryGetValue(handle,out var binding) && binding.Revision == revision) { mesh = binding.Reader.Mesh; return true; }
            mesh = null; return false;
        }
        public void Dispose()
        {
            CheckThread(); if (disposed) return;
            foreach (var binding in readers.Values) binding.Reader.Dispose(); readers.Clear(); disposed = true;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("Mesh reader sets belong to their creating thread.");
        }
    }
}
