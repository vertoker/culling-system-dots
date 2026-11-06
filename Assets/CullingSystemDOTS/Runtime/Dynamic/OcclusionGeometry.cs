using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Dynamic
{
    // One registry reference plus explicit snapshot references; never expose the owned mutable Mesh.
    internal sealed class OcclusionGeometry : IDisposable
    {
        internal readonly Mesh Mesh;
        internal readonly Bounds LocalBounds;
        internal readonly int SubMeshCount;
        internal NativeArray<float3> Vertices;
        internal NativeArray<int> Indices;
        internal readonly uint Revision;
        private readonly OcclusionMemoryBudget budget;
        private long bytes;
        private int references = 1;

        internal OcclusionGeometry(Mesh source, uint revision, OcclusionMemoryBudget budget)
        {
            if (source == null || !source.isReadable || source.vertexCount < 3 || source.subMeshCount == 0)
                throw new ArgumentException("A readable nonempty triangle Mesh is required.", nameof(source));
            this.budget = budget; Revision = revision;
            var vertices = source.vertices;
            foreach (var vertex in vertices)
                if (!math.all(math.isfinite((float3)vertex))) throw new ArgumentException("Geometry contains nonfinite vertices.", nameof(source));
            var indices = new List<int>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new ArgumentException("Only triangle submeshes are supported.", nameof(source));
                var part = source.GetIndices(submesh, true);
                if (part.Length % 3 != 0) throw new ArgumentException("Incomplete triangle indices.", nameof(source));
                foreach (int index in part)
                    if ((uint)index >= vertices.Length) throw new ArgumentException("Vertex index is outside the geometry.", nameof(source));
                indices.AddRange(part);
            }
            if (indices.Count == 0) throw new ArgumentException("The Mesh has no triangles.", nameof(source));
            long nativeBytes = checked((long)vertices.Length * 12 + (long)indices.Count * 4);
            if (nativeBytes > int.MaxValue) throw new ArgumentException("Geometry exceeds one addressable update payload.", nameof(source));
            long reserved = checked(nativeBytes + MeshPayload(source) * 2 + (long)vertices.Length * 24);
            budget.Reserve(reserved); bytes = reserved;
            try
            {
                Mesh = Object.Instantiate(source); Mesh.name = "Owned runtime occlusion geometry";
                Mesh.hideFlags = HideFlags.HideAndDontSave;
                Mesh.RecalculateBounds();
                LocalBounds = Mesh.bounds; SubMeshCount = Mesh.subMeshCount;
                if (Mesh.normals.Length != vertices.Length) Mesh.RecalculateNormals();
                long actual = checked(nativeBytes + MeshPayload(Mesh) * 2);
                if (actual > bytes) budget.Reserve(actual - bytes); else budget.Release(bytes - actual);
                bytes = actual;
                Vertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
                for (int i = 0; i < vertices.Length; i++) Vertices[i] = vertices[i];
                Indices = new NativeArray<int>(indices.ToArray(), Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }

        private static long MeshPayload(Mesh mesh)
        {
            long bytes = 0, indices = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
                bytes = checked(bytes + (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream));
            for (int i = 0; i < mesh.subMeshCount; i++)
                indices = Math.Max(indices, (long)mesh.GetIndexStart(i) + mesh.GetIndexCount(i));
            return checked(bytes + indices * (mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4));
        }

        internal void Retain()
        {
            if (references == 0) throw new ObjectDisposedException(nameof(OcclusionGeometry));
            references = checked(references + 1);
        }
        public void Dispose()
        {
            if (references == 0 || --references != 0) return;
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Indices.IsCreated) Indices.Dispose();
            if (Mesh != null)
            {
                if (Application.isPlaying) Object.Destroy(Mesh); else Object.DestroyImmediate(Mesh);
            }
            budget.Release(bytes); bytes = 0;
        }
    }
}
