using System;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Core;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;

namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeInputProfile { ComputeSoftwareInput = 1 }
    public struct BakeRendererPolicy { public BakeSurfaceOverride Surface; public bool DoubleSided; }
    public struct BakeRaySettings { public int Width, Height; public float Far; public quaternion Rotation; public float3 CellSize; }
    public struct BakeGeometry { public int VertexOffset, VertexCount, IndexOffset, IndexCount; }
    public struct BakeInstance
    {
        public int Geometry, GroupIndex, SourceRenderer;
        public float4x4 LocalToWorld, WorldToLocal;
        public float3 BoundsMin, BoundsMax;
        public byte Transparent, DoubleSided;
    }
    public struct BakeSampleLocation { public int FullGridIndex; public float3 Position; }
    public sealed class BakeGroupSource { public CullingGroupKey Key; public Renderer[] Renderers = Array.Empty<Renderer>(); }
    public sealed class BakeSnapshotRequest
    {
        public BakeInputProfile Profile = BakeInputProfile.ComputeSoftwareInput;
        public BakeGroupSource[] Groups = Array.Empty<BakeGroupSource>();
        public Renderer[] AdditionalOccluders = Array.Empty<Renderer>();
        public LODGroup[] LodGroups;
        public Func<Renderer, BakeRendererPolicy> Policy;
        public BakeSampleLocation[] Samples = Array.Empty<BakeSampleLocation>();
        public bool RenderTransparency = true;
        public BakeRaySettings Rays = new BakeRaySettings { Width = 8, Height = 8, Far = 1000,
            Rotation = quaternion.identity, CellSize = new float3(1) };
    }
    public sealed class BakeSnapshot : IDisposable
    {
        private NativeArray<float3> vertices;
        private NativeArray<int> indices;
        private NativeArray<BakeGeometry> geometry;
        private NativeArray<BakeInstance> instances;
        private NativeArray<CullingGroupKey> groups;
        private NativeArray<BakeSampleLocation> samples;
        private JobHandle readers;
        private bool disposed;
        public BakeInputProfile Profile { get; }
        public BakeRaySettings Rays { get; }
        public NativeArray<float3>.ReadOnly Vertices { get { EnsureAlive(); return vertices.AsReadOnly(); } }
        public NativeArray<int>.ReadOnly Indices { get { EnsureAlive(); return indices.AsReadOnly(); } }
        public NativeArray<BakeGeometry>.ReadOnly Geometry { get { EnsureAlive(); return geometry.AsReadOnly(); } }
        public NativeArray<BakeInstance>.ReadOnly Instances { get { EnsureAlive(); return instances.AsReadOnly(); } }
        public NativeArray<CullingGroupKey>.ReadOnly Groups { get { EnsureAlive(); return groups.AsReadOnly(); } }
        public NativeArray<BakeSampleLocation>.ReadOnly Samples { get { EnsureAlive(); return samples.AsReadOnly(); } }
        internal BakeSnapshot(float3[] vertices, int[] indices, BakeGeometry[] geometry, BakeInstance[] instances,
            CullingGroupKey[] groups, BakeSampleLocation[] samples, BakeSnapshotRequest request)
        {
            Profile = request.Profile; Rays = request.Rays;
            try
            {
                this.vertices = new NativeArray<float3>(vertices, Allocator.Persistent);
                this.indices = new NativeArray<int>(indices, Allocator.Persistent);
                this.geometry = new NativeArray<BakeGeometry>(geometry, Allocator.Persistent);
                this.instances = new NativeArray<BakeInstance>(instances, Allocator.Persistent);
                this.groups = new NativeArray<CullingGroupKey>(groups, Allocator.Persistent);
                this.samples = new NativeArray<BakeSampleLocation>(samples, Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }
        public void RegisterReader(JobHandle dependency) { EnsureAlive(); readers = JobHandle.CombineDependencies(readers, dependency); }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(BakeSnapshot)); }
        public void Dispose()
        {
            if (disposed) return;
            readers.Complete();
            if (vertices.IsCreated) vertices.Dispose(); if (indices.IsCreated) indices.Dispose();
            if (geometry.IsCreated) geometry.Dispose(); if (instances.IsCreated) instances.Dispose();
            if (groups.IsCreated) groups.Dispose(); if (samples.IsCreated) samples.Dispose(); disposed = true;
        }
    }
}
