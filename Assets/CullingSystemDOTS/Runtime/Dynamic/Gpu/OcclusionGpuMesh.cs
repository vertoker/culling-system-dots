using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionGpuMesh : IDisposable, IOcclusionGpuRetirement
    {
        private struct Vertex { internal float3 Position, Normal; }
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private readonly OcclusionMemoryBudget budget;
        private OcclusionGeometry source;
        private NativeArray<Vertex> vertices;
        private Mesh mesh;
        private GraphicsBuffer vertexBuffer, indexBuffer;
        private GraphicsFence writeFence;
        private long bytes, scratchBytes;
        private int vertexOffset, indexOffset, readers;
        private readonly int vertexCount, indexCount;
        private bool retired, hasWriteFence;
        internal readonly GeometryHandle Handle;
        internal readonly uint Revision;
        internal bool Ready => !retired && vertexOffset == vertexCount && indexOffset == indexCount;
        internal bool Released { get; private set; }
        internal OcclusionGpuMesh(OcclusionWorld registry, OcclusionSnapshotLease snapshot, GeometryHandle handle)
        {
            if (registry == null || snapshot == null || snapshot.WorldIdentity != registry.Identity) throw new ArgumentException("A matching live registry and snapshot are required.");
            budget = registry.ResourceBudget; source = snapshot.RetainGeometry(handle); Handle = handle; Revision = source.Revision;
            try
            {
                vertexCount = source.Vertices.Length; indexCount = source.Indices.Length;
                long vertexBytes = checked((long)vertexCount * 24), indexBytes = checked((long)indexCount * 4);
                if (vertexBytes > int.MaxValue || indexBytes > int.MaxValue) throw new ArgumentException("A GPU stream exceeds its addressable payload.");
                scratchBytes = vertexBytes;
                long temporary = checked((long)vertexCount * 12 + (long)source.SubMeshCount * UnsafeUtility.SizeOf<SubMeshDescriptor>());
                long required = checked((vertexBytes + indexBytes) * 2 + scratchBytes + temporary);
                budget.Reserve(required); bytes = required;
                vertices = new NativeArray<Vertex>(vertexCount, Allocator.Persistent);
                var normals = source.Mesh.normals;
                if (normals.Length != vertexCount) throw new ArgumentException("GPU geometry needs one finite normal per vertex.");
                for (int i = 0; i < vertexCount; i++)
                {
                    float3 normal = normals[i];
                    if (!math.all(math.isfinite(normal))) throw new ArgumentException("GPU geometry contains a nonfinite normal.");
                    vertices[i] = new Vertex { Position = source.Vertices[i], Normal = normal };
                }
                mesh = new Mesh { name = "Streamed runtime occlusion geometry", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw; mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.SetVertexBufferParams(vertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3));
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                var submeshes = new SubMeshDescriptor[source.SubMeshCount]; int offset = 0;
                for (int i = 0; i < submeshes.Length; i++)
                {
                    int count = checked((int)source.Mesh.GetIndexCount(i));
                    submeshes[i] = new SubMeshDescriptor(offset, count, MeshTopology.Triangles) {
                        bounds = source.LocalBounds, firstVertex = 0, vertexCount = vertexCount, baseVertex = 0 };
                    offset = checked(offset + count);
                }
                if (offset != indexCount) throw new ArgumentException("Submesh ranges do not match the flattened registered indices.");
                // Fix every CPU metadata field BEFORE acquiring GPU buffers. Later CPU Mesh data
                // mutation could recreate these buffers and overwrite streamed data with placeholders.
                mesh.SetSubMeshes(submeshes, MeshUpdateFlags.DontRecalculateBounds); mesh.bounds = source.LocalBounds;
                vertexBuffer = mesh.GetVertexBuffer(0); indexBuffer = mesh.GetIndexBuffer();
                if (vertexBuffer == null || indexBuffer == null || !vertexBuffer.IsValid() || !indexBuffer.IsValid() ||
                    vertexBuffer.stride != 24 || indexBuffer.stride != 4) throw new NotSupportedException("Unexpected Mesh GPU stream layout.");
                budget.Release(temporary); bytes -= temporary;
                FenceWrites();
            }
            catch { Release(); throw; }
        }
        internal int Upload(OcclusionGpuUploadBudget upload)
        {
            CheckThread(); if (retired || Released) throw new ObjectDisposedException(nameof(OcclusionGpuMesh));
            if (upload == null) throw new ArgumentNullException(nameof(upload));
            int written = 0;
            try
            {
                if (vertexOffset < vertexCount)
                {
                    int amount = upload.TakeAligned(checked((vertexCount - vertexOffset) * 24), 24);
                    if (amount > 0)
                    {
                        written += amount; int count = amount / 24;
                        vertexBuffer.SetData(vertices, vertexOffset, vertexOffset, count); vertexOffset += count;
                    }
                }
                if (vertexOffset == vertexCount && indexOffset < indexCount)
                {
                    int amount = upload.TakeAligned(checked((indexCount - indexOffset) * 4), 4);
                    if (amount > 0)
                    {
                        written += amount; int count = amount / 4;
                        indexBuffer.SetData(source.Indices, indexOffset, indexOffset, count); indexOffset += count;
                    }
                }
                if (Ready && source != null)
                {
                    source.Dispose(); source = null;
                    vertices.Dispose(); budget.Release(scratchBytes); bytes -= scratchBytes; scratchBytes = 0;
                }
                return written;
            }
            finally { if (written > 0) FenceWrites(); }
        }
        internal OcclusionGpuMeshLease AcquireReader()
        {
            CheckThread(); if (!Ready || Released) throw new InvalidOperationException("Mesh upload is incomplete or retired.");
            readers = checked(readers + 1); return new OcclusionGpuMeshLease(this);
        }
        internal Mesh GetLeasedMesh()
        {
            CheckThread(); if (Released || readers == 0) throw new ObjectDisposedException(nameof(OcclusionGpuMesh)); return mesh;
        }
        internal void ReleaseReader() { CheckThread(); readers--; Poll(); }
        internal void Poll()
        {
            CheckThread(); if (!Released && retired && readers == 0 && (!hasWriteFence || writeFence.passed)) Release();
        }
        public void Dispose()
        {
            CheckThread(); retired = true; Poll();
            if (!Released) OcclusionGpuRetirement.Register(this);
        }
        bool IOcclusionGpuRetirement.PollRetirement() { Poll(); return Released; }
        void IOcclusionGpuRetirement.SealForShutdown() { }
        private void FenceWrites()
        {
            writeFence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations); hasWriteFence = true;
        }
        private void Release()
        {
            if (Released) return; Released = true;
            vertexBuffer?.Dispose(); indexBuffer?.Dispose();
            if (mesh != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
            if (vertices.IsCreated) vertices.Dispose(); source?.Dispose(); source = null;
            budget?.Release(bytes); bytes = 0;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("GPU geometry belongs to its creating thread.");
        }
    }
    internal sealed class OcclusionGpuMeshLease : IDisposable
    {
        private OcclusionGpuMesh owner;
        internal OcclusionGpuMeshLease(OcclusionGpuMesh owner) { this.owner = owner; }
        internal Mesh Mesh => owner != null ? owner.GetLeasedMesh() : throw new ObjectDisposedException(nameof(OcclusionGpuMeshLease));
        public void Dispose() { var previous = owner; previous?.ReleaseReader(); owner = null; }
    }
}
