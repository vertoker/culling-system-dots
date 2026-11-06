using System;
using System.Collections.Generic;
using System.Linq;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Core;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Editor.Baking
{
    public static class MeshSnapshotExtractor
    {
        private struct Entry { public Renderer Renderer; public int Group, Source;  }
        public static BakeSnapshot Capture(BakeSnapshotRequest request)
        {
            BakeSnapshot result = null;
            foreach (float ignored in CaptureSteps(request,value => result = value)) { }
            return result;
        }
        internal static IEnumerable<float> CaptureSteps(BakeSnapshotRequest request, Action<BakeSnapshot> done)
        {
            if (request == null || request.Groups == null || request.Samples == null || request.Profile != BakeInputProfile.ComputeSoftwareInput)
                throw new ArgumentException("A complete snapshot request is required.");
            if (request.Groups.Length > ushort.MaxValue) throw new ArgumentException("Too many bake groups.");
            var entries = new List<Entry>(); var mapping = new Dictionary<Renderer, int>();
            var groupKeys = new CullingGroupKey[request.Groups.Length];
            for (int g = 0; g < request.Groups.Length; g++)
            {
                var group = request.Groups[g];
                if (group == null || group.Key.GroupIndex != g || math.all(group.Key.VolumeId == 0) ||
                    (g > 0 && !group.Key.VolumeId.Equals(groupKeys[0].VolumeId)))
                    throw new ArgumentException("Groups must preserve explicit contiguous original indices in one volume.");
                groupKeys[g] = group.Key;
                foreach (var renderer in group.Renderers ?? Array.Empty<Renderer>())
                {
                    if ((entries.Count & 63) == 0) yield return 0;
                    if (renderer == null || mapping.ContainsKey(renderer)) continue;
                    mapping.Add(renderer, g); entries.Add(new Entry { Renderer = renderer, Group = g, Source = entries.Count });
                }
            }
            foreach (var renderer in request.AdditionalOccluders ?? Array.Empty<Renderer>())
            {
                if (renderer == null) continue;
                if (!mapping.TryGetValue(renderer, out int group)) { group = -1; mapping.Add(renderer, group); }
                entries.Add(new Entry { Renderer = renderer, Group = group, Source = entries.Count });
            }
            var keep = new HashSet<Renderer>(); var other = new HashSet<Renderer>();
            foreach (var lod in request.LodGroups ?? UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.InstanceID))
            {
                var levels = lod.GetLODs(); int selected = 0;
                for (int l = 0; l < levels.Length; l++) if (levels[l].renderers.Length != 0) { selected = l; break; }
                for (int l = 0; l < levels.Length; l++) foreach (var renderer in levels[l].renderers)
                    if (l == selected) keep.Add(renderer); else other.Add(renderer);
            }
            entries.RemoveAll(e => other.Contains(e.Renderer) && !keep.Contains(e.Renderer));
            var vertices = new List<float3>(); var indices = new List<int>(); var geometry = new List<BakeGeometry>();
            var instances = new List<BakeInstance>(); var baked = new Dictionary<Renderer, Mesh>();
            var cachedVertices = new Dictionary<Mesh, int>();
            var cachedGeometry = new Dictionary<(Mesh, int), int>();
            try
            {
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    yield return (float)entryIndex/Math.Max(1,entries.Count);
                    var renderer = entry.Renderer; Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (!baked.TryGetValue(renderer, out mesh))
                        {
                            mesh = new Mesh(); baked.Add(renderer, mesh); skinned.BakeMesh(mesh);
                        }
                    }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var materials = renderer.sharedMaterials;
                    int count = Math.Min(materials.Length, mesh.subMeshCount);
                    Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                    Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
                    var policy = Policy(request, renderer);
                    for (int sub = 0; sub < count; sub++)
                    {
                        var cacheKey = (mesh, sub);
                        if (!cachedGeometry.TryGetValue(cacheKey, out int index))
                        {
                            if (mesh.GetTopology(sub) != MeshTopology.Triangles) throw new ArgumentException("Bake geometry requires triangle topology.");
                            var triangleIndices = mesh.GetTriangles(sub);
                            var sourceVertices = mesh.vertices;
                            var range = new BakeGeometry { IndexOffset = indices.Count, IndexCount = triangleIndices.Length };
                            if (!cachedVertices.TryGetValue(mesh, out int start))
                            { start = vertices.Count; cachedVertices.Add(mesh, start); vertices.AddRange(sourceVertices.Select(v => (float3)v)); }
                            range.VertexOffset = start; range.VertexCount = sourceVertices.Length;
                            indices.AddRange(triangleIndices); index = geometry.Count; geometry.Add(range); cachedGeometry.Add(cacheKey, index);
                        }
                        float3 min = new float3(float.PositiveInfinity), max = new float3(float.NegativeInfinity);
                        var item = geometry[index];
                        // Validation hashes all source vertices/indices/transforms again. Bounds are
                        // derived data: reuse them only in this non-tracing comparison snapshot.
                        bool reuseBounds = request.ValidationBounds != null && instances.Count < request.ValidationBounds.Length;
                        if (reuseBounds) { min = request.ValidationBounds[instances.Count].BoundsMin; max = request.ValidationBounds[instances.Count].BoundsMax; }
                        for (int i = 0; !reuseBounds && i < item.IndexCount; i++)
                        {
                            if ((i & 4095) == 0) yield return (float)entryIndex/Math.Max(1,entries.Count);
                            float3 point = matrix.MultiplyPoint3x4(vertices[item.VertexOffset + indices[item.IndexOffset + i]]);
                            min = math.min(min, point); max = math.max(max, point);
                        }
                        if (item.IndexCount == 0) min = max = matrix.MultiplyPoint3x4(Vector3.zero);
                        instances.Add(new BakeInstance { Geometry = index, GroupIndex = entry.Group, SourceRenderer = entry.Source,
                            LocalToWorld = matrix, WorldToLocal = inverse, BoundsMin = min, BoundsMax = max,
                            Transparent = MaterialClassification.IsTransparent(materials[sub], request.RenderTransparency, policy.Surface) ? (byte)1 : (byte)0,
                            DoubleSided = policy.DoubleSided ? (byte)1 : (byte)0 });
                    }
                }
                done(new BakeSnapshot(vertices.ToArray(), indices.ToArray(), geometry.ToArray(), instances.ToArray(), groupKeys, request.Samples, request));
            }
            finally { foreach (var mesh in baked.Values) UnityEngine.Object.DestroyImmediate(mesh); }
        }
        private static BakeRendererPolicy Policy(BakeSnapshotRequest request, Renderer renderer)
        {
            if (request.Policy != null) return request.Policy(renderer);
            var tag = renderer.GetComponent<CullingBakeRendererTag>();
            return tag == null ? default : new BakeRendererPolicy { Surface = tag.Surface, DoubleSided = tag.DoubleSided };
        }
    }
}
