using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Editor.Baking
{
    public struct BakeRay { public float3 Origin, Direction; public float Near, Far; }
    public struct BakeTrianglePlanes { public float4 A, B, C; }
    public sealed class BakeCellResult
    {
        public int FullGridIndex;
        public ushort[] Groups;
        public uint NeighborMask;
    }
    public static class ReferenceCpuTraversal
    {
        public static BakeCellResult[] Bake(BakeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var settings = snapshot.Rays;
            if (!math.isfinite(settings.Far) || settings.Far <= 0 || !math.all(math.isfinite(settings.Rotation.value)))
                throw new ArgumentException("Finite bake ray settings are required.");
            var geometry = snapshot.Geometry; var indices = snapshot.Indices; var vertices = snapshot.Vertices;
            var instances = snapshot.Instances; var samples = snapshot.Samples;
            var triangles = new BakeTrianglePlanes[indices.Length / 3];
            for (int g = 0; g < geometry.Length; g++)
            {
                var range = geometry[g];
                for (int i = 0; i < range.IndexCount; i += 3)
                    triangles[(range.IndexOffset + i) / 3] = PrecomputeTriangle(
                        vertices[range.VertexOffset + indices[range.IndexOffset + i]],
                        vertices[range.VertexOffset + indices[range.IndexOffset + i + 1]],
                        vertices[range.VertexOffset + indices[range.IndexOffset + i + 2]]);
            }
            var output = new BakeCellResult[samples.Length];
            for (int cell = 0; cell < samples.Length; cell++)
            {
                var visible = new bool[snapshot.Groups.Length]; uint neighbors = 0;
                for (int face = 0; face < 6; face++) for (int y = 0; y < settings.Height; y++) for (int x = 0; x < settings.Width; x++)
                {
                    var ray = GenerateRay(in settings, samples[cell].Position, face, x, y);
                    float maximum = ray.Far; int opaque = -1;
                    for (int n = 0; n < instances.Length; n++)
                    {
                        var instance = instances[n];
                        if (instance.Transparent != 0 || !MeetsBounds(in ray, instance.BoundsMin, instance.BoundsMax, maximum)) continue;
                        var local = Local(in ray, in instance); var range = geometry[instance.Geometry];
                        for (int i = 0; i < range.IndexCount; i += 3)
                        {
                            var plane = triangles[(range.IndexOffset + i) / 3];
                            float hit = Intersect(in local, in plane, maximum, instance.DoubleSided != 0);
                            if (hit < maximum) { maximum = hit; opaque = instance.GroupIndex; }
                        }
                    }
                    if (opaque >= 0) visible[opaque] = true;
                    neighbors |= NeighborVisibility(ray.Direction, maximum, in settings);
                    for (int n = 0; n < instances.Length; n++)
                    {
                        var instance = instances[n];
                        if (instance.Transparent == 0 || !MeetsBounds(in ray, instance.BoundsMin, instance.BoundsMax, maximum)) continue;
                        var local = Local(in ray, in instance); var range = geometry[instance.Geometry];
                        for (int i = 0; i < range.IndexCount; i += 3)
                        {
                            var plane = triangles[(range.IndexOffset + i) / 3];
                            float hit = Intersect(in local, in plane, maximum, instance.DoubleSided != 0);
                            if (hit < maximum && instance.GroupIndex >= 0) { visible[instance.GroupIndex] = true; break; }
                        }
                    }
                }
                var groups = new List<ushort>(); for (int g = 0; g < visible.Length; g++) if (visible[g]) groups.Add((ushort)g);
                output[cell] = new BakeCellResult { FullGridIndex = samples[cell].FullGridIndex, Groups = groups.ToArray(), NeighborMask = neighbors };
            }
            return output;
        }

        internal static BakeRay Local(in BakeRay ray, in BakeInstance instance) => new BakeRay {
            Origin = math.transform(instance.WorldToLocal, ray.Origin), Direction = math.mul(new float3x3(instance.WorldToLocal), ray.Direction), Near = ray.Near, Far = ray.Far };
        internal static bool MeetsBounds(in BakeRay ray, float3 min, float3 max, float maximum)
        {
            float enter = ray.Near, exit = maximum;
            for (int axis = 0; axis < 3; axis++)
            {
                float direction = ray.Direction[axis], origin = ray.Origin[axis];
                if (math.abs(direction) < 1e-30f) { if (origin < min[axis] || origin > max[axis]) return false; continue; }
                float a = (min[axis] - origin) / direction, b = (max[axis] - origin) / direction;
                enter = math.max(enter, math.min(a, b)); exit = math.min(exit, math.max(a, b));
                if (enter > exit) return false;
            }
            return enter <= exit;
        }
        internal static uint NeighborVisibility(float3 direction, float distance, in BakeRaySettings settings)
        {
            if (!math.all(settings.CellSize > 0)) return 0;
            uint mask = 0; var q = settings.Rotation.value;
            float radius = math.cmax(settings.CellSize) * .25f;
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                var p = new float3(x, y, z); var t = 2f * math.cross(q.xyz, p);
                p = (p + q.w * t + math.cross(q.xyz, t)) * settings.CellSize;
                float b = math.dot(p, direction), c = math.dot(p, p) - radius * radius, h = b * b - c;
                if (h < 0) continue;
                float hit = -b - math.sqrt(h);
                if (hit >= 0 && distance > hit) mask |= 1u << ((x + 1) + 3 * ((y + 1) + 3 * (z + 1)));
            }
            return mask;
        }
        public static BakeRay GenerateRay(in BakeRaySettings settings, float3 origin, int face, int x, int y)
        {
            if (settings.Width <= 0 || settings.Height <= 0 || (uint)face >= 6 || (uint)x >= (uint)settings.Width || (uint)y >= (uint)settings.Height)
                throw new ArgumentOutOfRangeException();
            float u = 2f * ((x + .5f) * (1f / settings.Width)) - 1f;
            float v = 2f * ((y + .5f) * (1f / settings.Height)) - 1f;
            float3 direction;
            switch (face)
            {
                case 0: direction = new float3(1, v, -u); break;
                case 1: direction = new float3(-1, v, u); break;
                case 2: direction = new float3(u, 1, -v); break;
                case 3: direction = new float3(u, -1, v); break;
                case 4: direction = new float3(u, v, 1); break;
                default: direction = new float3(-u, v, -1); break;
            }
            direction = math.normalize(direction);
            float maximum = settings.Far / math.cmax(math.abs(direction));
            var q = settings.Rotation.value;
            var t = 2f * math.cross(q.xyz, direction);
            direction = direction + q.w * t + math.cross(q.xyz, t);
            return new BakeRay { Origin = origin, Direction = direction, Near = .0001f, Far = maximum };
        }
        // Store original vertices. Intersection uses the scalar triple products of
        // the ray and triangle edges; no imported plane encoding or cache format.
        public static BakeTrianglePlanes PrecomputeTriangle(float3 a, float3 b, float3 c) =>
            new BakeTrianglePlanes { A = new float4(a, 0), B = new float4(b, 0), C = new float4(c, 0) };

        public static float Intersect(in BakeRay ray, in BakeTrianglePlanes triangle, float maximum, bool doubleSided)
        {
            var edge1 = triangle.B.xyz - triangle.A.xyz;
            var edge2 = triangle.C.xyz - triangle.A.xyz;
            var cross = math.cross(ray.Direction, edge2);
            float determinant = math.dot(edge1, cross);
            float scale = math.sqrt(math.lengthsq(edge1) * math.lengthsq(edge2) * math.lengthsq(ray.Direction));
            float epsilon = scale * 1e-7f;
            if (doubleSided ? math.abs(determinant) <= epsilon : determinant <= epsilon)
                return float.PositiveInfinity;
            var offset = ray.Origin - triangle.A.xyz;
            float inverse = 1f / determinant;
            float u = math.dot(offset, cross) * inverse;
            if (u < 0 || u > 1) return float.PositiveInfinity;
            var perpendicular = math.cross(offset, edge1);
            float v = math.dot(ray.Direction, perpendicular) * inverse;
            if (v < 0 || u + v > 1) return float.PositiveInfinity;
            float distance = math.dot(edge2, perpendicular) * inverse;
            return distance >= ray.Near && distance < maximum ? distance : float.PositiveInfinity;
        }
    }
}
