using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    internal static class BvhBuilder
    {
        private struct Primitive { public float3 Min, Max; public int Id; }
        internal static long EstimateNativeBytes(BakeSnapshot snapshot) =>
            (long)(snapshot.Indices.Length / 3) * (80 + 4 + 48) + (long)snapshot.Instances.Length * 84 + (long)snapshot.Geometry.Length * 4;
        internal static NativeBvh Build(BakeSnapshot snapshot, CancellationToken cancellation = default)
        {
            var nodes = new List<NativeBvhNode>(); var order = new List<int>();
            var roots = new int[snapshot.Geometry.Length]; var planes = new BakeTrianglePlanes[snapshot.Indices.Length / 3];
            var vertices = snapshot.Vertices; var indices = snapshot.Indices;
            for (int g = 0; g < roots.Length; g++)
            {
                cancellation.ThrowIfCancellationRequested();
                var geometry = snapshot.Geometry[g]; var primitives = new List<Primitive>(geometry.IndexCount / 3);
                for (int i = 0; i < geometry.IndexCount; i += 3)
                {
                    if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    int offset = geometry.IndexOffset + i, id = offset / 3;
                    var a = vertices[geometry.VertexOffset + indices[offset]];
                    var b = vertices[geometry.VertexOffset + indices[offset + 1]];
                    var c = vertices[geometry.VertexOffset + indices[offset + 2]];
                    planes[id] = ReferenceCpuTraversal.PrecomputeTriangle(a,b,c);
                    primitives.Add(new Primitive { Id = id, Min = math.min(a, math.min(b,c)), Max = math.max(a, math.max(b,c)) });
                }
                roots[g] = Tree(primitives.ToArray(), nodes, order, cancellation);
            }
            var opaque = new List<Primitive>(); var transparent = new List<Primitive>();
            for (int i = 0; i < snapshot.Instances.Length; i++)
            {
                var instance = snapshot.Instances[i];
                var p = new Primitive { Id = i, Min = instance.BoundsMin, Max = instance.BoundsMax };
                (instance.Transparent == 0 ? opaque : transparent).Add(p);
            }
            int opaqueRoot = Tree(opaque.ToArray(), nodes, order, cancellation), transparentRoot = Tree(transparent.ToArray(), nodes, order, cancellation);
            var result = new NativeBvh { OpaqueRoot = opaqueRoot, TransparentRoot = transparentRoot };
            try
            {
                result.Nodes = new NativeArray<NativeBvhNode>(nodes.ToArray(), Allocator.Persistent);
                result.Order = new NativeArray<int>(order.ToArray(), Allocator.Persistent);
                result.GeometryRoots = new NativeArray<int>(roots, Allocator.Persistent);
                result.Triangles = new NativeArray<BakeTrianglePlanes>(planes, Allocator.Persistent);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private static int Tree(Primitive[] primitives, List<NativeBvhNode> nodes, List<int> order, CancellationToken cancellation) =>
            primitives.Length == 0 ? -1 : Split(primitives, 0, primitives.Length, nodes, order, cancellation);
        private static int Split(Primitive[] primitives, int start, int count, List<NativeBvhNode> nodes, List<int> order, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            float3 min = new float3(float.PositiveInfinity), max = new float3(float.NegativeInfinity);
            float3 centroidMin = min, centroidMax = max;
            for (int i = start; i < start + count; i++)
            {
                if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                var p = primitives[i]; min = math.min(min,p.Min); max = math.max(max,p.Max);
                var centroid = (p.Min + p.Max) * .5f; centroidMin = math.min(centroidMin, centroid); centroidMax = math.max(centroidMax,centroid);
            }
            // Conservative acceleration bounds; final instance bounds and plane intersection stay exact.
            var padding = math.max(new float3(1e-5f), math.max(math.abs(min), math.abs(max)) * 1e-6f);
            int index = nodes.Count; nodes.Add(default);
            var node = new NativeBvhNode { Min = min - padding, Max = max + padding, Left = -1, Right = -1 };
            if (count <= 4)
            {
                node.Start = order.Count; node.Count = count;
                for (int i = start; i < start + count; i++) order.Add(primitives[i].Id);
            }
            else
            {
                var extent = centroidMax - centroidMin; int axis = extent.y > extent.x ? 1 : 0; if (extent.z > extent[axis]) axis = 2;
                int sortAxis = axis;
                Array.Sort(primitives, start, count, Comparer<Primitive>.Create((a,b) => {
                    int compare = ((a.Min[sortAxis]+a.Max[sortAxis])*.5f).CompareTo((b.Min[sortAxis]+b.Max[sortAxis])*.5f);
                    return compare != 0 ? compare : a.Id.CompareTo(b.Id); }));
                int half = count / 2;
                node.Left = Split(primitives,start,half,nodes,order,cancellation); node.Right = Split(primitives,start+half,count-half,nodes,order,cancellation);
            }
            nodes[index] = node; return index;
        }
    }
}
