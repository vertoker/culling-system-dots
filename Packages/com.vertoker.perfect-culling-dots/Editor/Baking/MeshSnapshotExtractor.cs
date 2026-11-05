using System;
using System.Collections.Generic;
using System.Linq;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Core;
using Unity.Mathematics;
using UnityEngine;

namespace PerfectCullingDOTS.Editor.Baking
{
    public static class MeshSnapshotExtractor
    {
        private struct Entry { public Renderer Renderer; public int Group, Source; public bool ReverseAll; }
        public static BakeSnapshot Capture(BakeSnapshotRequest request)
        {
            if (request == null || request.Groups == null || request.Samples == null || (uint)request.Profile > 1)
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
                    if (renderer == null || mapping.ContainsKey(renderer)) continue;
                    // Source compute constructor filters missing/empty MeshFilter before SoftwareRT.
                    if (request.Profile == BakeInputProfile.ComputeSoftwareInput)
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0 || filter.sharedMesh.subMeshCount == 0) continue;
                    }
                    mapping.Add(renderer, g); entries.Add(new Entry { Renderer = renderer, Group = g, Source = entries.Count });
                }
                if (request.Profile == BakeInputProfile.NativeVulkanInput)
                    foreach (var renderer in group.Renderers ?? Array.Empty<Renderer>())
                        if (CanReverse(request, renderer))
                        {
                            entries.Add(new Entry { Renderer = renderer, Group = g, Source = entries.Count, ReverseAll = true });
                            break; // Exact SceneColor behavior: one back copy per group.
                        }
            }
            foreach (var renderer in request.AdditionalOccluders ?? Array.Empty<Renderer>())
            {
                if (renderer == null) continue;
                if (!mapping.TryGetValue(renderer, out int group)) { group = -1; mapping.Add(renderer, group); }
                entries.Add(new Entry { Renderer = renderer, Group = group, Source = entries.Count });
            }
            if (request.Profile == BakeInputProfile.NativeVulkanInput)
                foreach (var renderer in request.AdditionalOccluders ?? Array.Empty<Renderer>())
                    if (CanReverse(request, renderer))
                        entries.Add(new Entry { Renderer = renderer, Group = -1, Source = entries.Count, ReverseAll = true });
            var keep = new HashSet<Renderer>(); var other = new HashSet<Renderer>();
            foreach (var lod in request.LodGroups ?? UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.InstanceID))
            {
                var levels = lod.GetLODs(); int selected = 0;
                for (int l = 0; l < levels.Length; l++) if (levels[l].renderers.Length != 0) { selected = l; break; }
                for (int l = 0; l < levels.Length; l++) foreach (var renderer in levels[l].renderers)
                    if (l == selected) keep.Add(renderer); else other.Add(renderer);
            }
            // Native back copies are new renderers, absent from the original LOD arrays.
            entries.RemoveAll(e => !e.ReverseAll && other.Contains(e.Renderer) && !keep.Contains(e.Renderer));
            var vertices = new List<float3>(); var indices = new List<int>(); var geometry = new List<BakeGeometry>();
            var instances = new List<BakeInstance>(); var baked = new Dictionary<Renderer, Mesh>();
            var cachedVertices = new Dictionary<Mesh, int>();
            var cachedGeometry = new Dictionary<(Mesh, int, int, int, int, bool), int>();
            try
            {
                foreach (var entry in entries)
                {
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
                    int count = Math.Min(materials.Length, entry.ReverseAll ? 1 : mesh.subMeshCount);
                    var scale = renderer.transform.lossyScale;
                    var sign = request.Profile == BakeInputProfile.NativeVulkanInput
                        ? new int3((int)Mathf.Sign(scale.x), (int)Mathf.Sign(scale.y), (int)Mathf.Sign(scale.z)) : new int3(1);
                    Matrix4x4 matrix = request.Profile == BakeInputProfile.NativeVulkanInput
                        ? Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)))
                        : renderer.transform.localToWorldMatrix;
                    Matrix4x4 inverse = request.Profile == BakeInputProfile.NativeVulkanInput ? matrix.inverse : renderer.transform.worldToLocalMatrix;
                    var policy = Policy(request, renderer);
                    for (int sub = 0; sub < count; sub++)
                    {
                        var cacheKey = (mesh, sub, sign.x, sign.y, sign.z, entry.ReverseAll);
                        if (!cachedGeometry.TryGetValue(cacheKey, out int index))
                        {
                            if (!entry.ReverseAll && mesh.GetTopology(sub) != MeshTopology.Triangles) throw new ArgumentException("Bake geometry requires triangle topology.");
                            var triangleIndices = entry.ReverseAll ? mesh.triangles.Reverse().ToArray() : mesh.GetTriangles(sub);
                            var sourceVertices = mesh.vertices;
                            var range = new BakeGeometry { IndexOffset = indices.Count, IndexCount = triangleIndices.Length };
                            if (request.Profile == BakeInputProfile.NativeVulkanInput)
                            {
                                range.VertexOffset = vertices.Count; var compact = new Dictionary<int, int>();
                                for (int i = 0; i < triangleIndices.Length; i++)
                                {
                                    int source = triangleIndices[i];
                                    if (!compact.TryGetValue(source, out int local))
                                    { local = compact.Count; compact.Add(source, local); vertices.Add((float3)sourceVertices[source] * sign); }
                                    triangleIndices[i] = local;
                                }
                                for (int i = 0; i < triangleIndices.Length; i += 3)
                                {
                                    if (sign.x < 0) (triangleIndices[i], triangleIndices[i + 2]) = (triangleIndices[i + 2], triangleIndices[i]);
                                    if (sign.y < 0) (triangleIndices[i], triangleIndices[i + 1]) = (triangleIndices[i + 1], triangleIndices[i]);
                                    if (sign.z < 0) (triangleIndices[i + 1], triangleIndices[i + 2]) = (triangleIndices[i + 2], triangleIndices[i + 1]);
                                }
                                range.VertexCount = compact.Count;
                            }
                            else
                            {
                                if (!cachedVertices.TryGetValue(mesh, out int start))
                                { start = vertices.Count; cachedVertices.Add(mesh, start); vertices.AddRange(sourceVertices.Select(v => (float3)v)); }
                                range.VertexOffset = start; range.VertexCount = sourceVertices.Length;
                            }
                            indices.AddRange(triangleIndices); index = geometry.Count; geometry.Add(range); cachedGeometry.Add(cacheKey, index);
                        }
                        float3 min = new float3(float.PositiveInfinity), max = new float3(float.NegativeInfinity);
                        var item = geometry[index];
                        for (int i = 0; i < item.IndexCount; i++)
                        {
                            float3 point = matrix.MultiplyPoint3x4(vertices[item.VertexOffset + indices[item.IndexOffset + i]]);
                            min = math.min(min, point); max = math.max(max, point);
                        }
                        if (item.IndexCount == 0) min = max = matrix.MultiplyPoint3x4(Vector3.zero);
                        instances.Add(new BakeInstance { Geometry = index, GroupIndex = entry.Group, SourceRenderer = entry.Source,
                            LocalToWorld = matrix, WorldToLocal = inverse, BoundsMin = min, BoundsMax = max,
                            Transparent = MaterialClassification.IsTransparent(materials[sub], request.RenderTransparency, policy.Surface) ? (byte)1 : (byte)0,
                            DoubleSided = request.Profile == BakeInputProfile.ComputeSoftwareInput && policy.DoubleSided ? (byte)1 : (byte)0 });
                    }
                }
                return new BakeSnapshot(vertices.ToArray(), indices.ToArray(), geometry.ToArray(), instances.ToArray(), groupKeys, request.Samples, request);
            }
            finally { foreach (var mesh in baked.Values) UnityEngine.Object.DestroyImmediate(mesh); }
        }
        private static BakeRendererPolicy Policy(BakeSnapshotRequest request, Renderer renderer)
        {
            if (request.Policy != null) return request.Policy(renderer);
            var tag = renderer.GetComponent<CullingBakeRendererTag>();
            return tag == null ? default : new BakeRendererPolicy { Surface = tag.Surface, DoubleSided = tag.DoubleSided };
        }
        private static bool CanReverse(BakeSnapshotRequest request, Renderer renderer)
        {
            if (renderer == null || !Policy(request, renderer).DoubleSided) return false;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            if (!(renderer is MeshRenderer))
                throw new NotSupportedException("Legacy native double-sided preprocessing requires a MeshRenderer.");
            return true;
        }
    }
}
