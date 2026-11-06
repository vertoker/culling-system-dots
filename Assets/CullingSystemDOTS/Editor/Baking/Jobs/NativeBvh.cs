using System;
using Unity.Collections;
using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    internal struct NativeBvhNode
    {
        public float3 Min, Max;
        public int Left, Right, Start, Count;
    }
    // Session owns these buffers. No writer exists after construction; jobs receive read-only views.
    internal sealed class NativeBvh : IDisposable
    {
        internal NativeArray<NativeBvhNode> Nodes;
        internal NativeArray<int> Order, GeometryRoots;
        internal NativeArray<BakeTrianglePlanes> Triangles;
        internal int OpaqueRoot = -1, TransparentRoot = -1;
        internal long Bytes => (long)Nodes.Length * 40 + (long)(Order.Length + GeometryRoots.Length) * 4 + (long)Triangles.Length * 48;
        public void Dispose()
        {
            if (Nodes.IsCreated) Nodes.Dispose(); if (Order.IsCreated) Order.Dispose();
            if (GeometryRoots.IsCreated) GeometryRoots.Dispose(); if (Triangles.IsCreated) Triangles.Dispose();
        }
    }
}
