using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using PerfectCullingDOTS.Editor.Baking.Jobs;
namespace PerfectCullingDOTS.Editor.Baking
{
    public sealed class CollectedBakeInput : IDisposable
    {
        public BakeSnapshot Snapshot;
        public CullingBakeLayout Layout;
        public CullingGroupKey[] Mapping;
        public uint4 VolumeId;
        public BakeMergeMetadata Metadata;
        public string Fingerprint;
        public void Dispose() { Snapshot?.Dispose(); Snapshot = null; }
    }
    public static class SceneBakeCollector
    {
        public static bool IsBakeCurrent(CullingVolumeAuthoring volume, BakeSettingsAsset settings)
        {
            if (volume == null || volume.Bake == null || string.IsNullOrEmpty(volume.Bake.InputFingerprint)) return false;
            using var current = Capture(volume,settings); return current.Fingerprint == volume.Bake.InputFingerprint;
        }
        public static CollectedBakeInput Capture(CullingVolumeAuthoring volume, BakeSettingsAsset settings)
        {
            if (volume == null || settings == null) throw new ArgumentNullException();
            if ((uint)settings.InputProfile > 1 || (uint)settings.ExecutionMode > 2 || settings.Resolution <= 0 || !math.isfinite(settings.Far) || settings.Far <= 0 ||
                !math.all(math.isfinite((float3)settings.CellSize)) || !math.all((float3)settings.CellSize > 0) ||
                !math.all(math.isfinite((float3)volume.Size)) || !math.all((float3)volume.Size > 0) || settings.MergeAxes == null)
                throw new ArgumentException("Finite positive volume, cell, ray settings and merge axes are required.");
            var sceneRoots = new List<GameObject>(volume.gameObject.scene.GetRootGameObjects());
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene != volume.gameObject.scene) sceneRoots.AddRange(scene.GetRootGameObjects());
            }
            var roots = sceneRoots.ToArray();
            var groups = roots.SelectMany(r=>r.GetComponentsInChildren<CullingGroupAuthoring>(true)).Where(g=>g.Volume == volume).OrderBy(g=>g.GroupIndex).ToArray();
            if (groups.Length > ushort.MaxValue) throw new ArgumentException("At most 65535 original groups are supported.");
            for (int i = 0; i < groups.Length; i++) if (groups[i].GroupIndex != i) throw new ArgumentException("Group indices must be unique and contiguous, including empty groups.");
            var objectId = GlobalObjectId.GetGlobalObjectIdSlow(volume);
            string identity = objectId.targetObjectId == 0 ? volume.GetEntityId().ToString() : objectId.ToString();
            uint4 id = volume.Bake == null ? new Unity.Entities.Hash128(UnityEngine.Hash128.Compute(identity).ToString()).Value : volume.Bake.VolumeId;
            var sources = new BakeGroupSource[groups.Length]; var mapping = new CullingGroupKey[groups.Length];
            bool Included(Renderer renderer) => renderer != null && (renderer.GetComponent<CullingBakeRendererTag>() == null || !renderer.GetComponent<CullingBakeRendererTag>().ExcludeFromBake);
            for (int i = 0; i < groups.Length; i++)
            {
                mapping[i] = new CullingGroupKey(id,(ushort)i);
                sources[i] = new BakeGroupSource { Key = mapping[i], Renderers = (groups[i].Renderers ?? Array.Empty<Renderer>()).Where(Included).ToArray() };
            }
            var inputs = volume.GetComponent<CullingBakeInputsAuthoring>();
            var explicitProviders = inputs == null ? Array.Empty<MonoBehaviour>() : inputs.SamplingProviders ?? Array.Empty<MonoBehaviour>();
            var providers = new List<ICullingSamplingProvider>();
            foreach (var provider in volume.GetComponentsInChildren<MonoBehaviour>().Concat(explicitProviders))
            {
                if (provider is ICullingSamplingProvider sampling && !providers.Contains(sampling)) providers.Add(sampling);
                else if (Array.IndexOf(explicitProviders,provider) >= 0 && !(provider is ICullingSamplingProvider))
                    throw new ArgumentException("Explicit sampling providers must implement ICullingSamplingProvider.");
            }
            // Source FindObjectsByType includes disabled components on active GameObjects.
            var regions = roots.SelectMany(r=>r.GetComponentsInChildren<CullingSamplingRegionAuthoring>())
                .Where(r=>r.gameObject.activeInHierarchy).OrderBy(r=>r.GetEntityId()).ToArray();
            bool RegionContains(CullingSamplingRegionAuthoring region, Vector3 position)
            {
                if (region.RestrictToVolumes != null && region.RestrictToVolumes.Length > 0 && Array.IndexOf(region.RestrictToVolumes,volume) < 0) return false;
                Vector3 local = Quaternion.Inverse(region.transform.rotation)*(region.transform.position-position);
                Vector3 extent = region.Size*.5f;
                local.x *= 1f/extent.x; local.y *= 1f/extent.y; local.z *= 1f/extent.z;
                return local.x <= 1 && local.x >= -1 && local.y <= 1 && local.y >= -1 && local.z <= 1 && local.z >= -1;
            }
            SamplingDecision Decision(float3 position)
            {
                foreach (var region in regions) if (region.Mode == CullingSamplingRegionMode.AlwaysInclude && RegionContains(region,position)) return SamplingDecision.Include;
                foreach (var region in regions) if (region.Mode == CullingSamplingRegionMode.Exclude && RegionContains(region,position)) return SamplingDecision.Exclude;
                return SamplingDecision.Continue;
            }
            Vector3 rawGrid = new Vector3(volume.Size.x/settings.CellSize.x,volume.Size.y/settings.CellSize.y,volume.Size.z/settings.CellSize.z);
            int3 rounded = new int3(Mathf.RoundToInt(rawGrid.x),Mathf.RoundToInt(rawGrid.y),Mathf.RoundToInt(rawGrid.z));
            long total = (long)Mathf.CeilToInt(rawGrid.x)*Mathf.CeilToInt(rawGrid.y)*Mathf.CeilToInt(rawGrid.z);
            // Preserve source center arithmetic, but diagnose source grids whose serialized/storage shapes disagree.
            if ((int)rawGrid.x < 1 || (int)rawGrid.y < 1 || (int)rawGrid.z < 1 || total > int.MaxValue ||
                (long)rounded.x*rounded.y*rounded.z != total) throw new ArgumentException("Source rounded bake dimensions and sampling storage disagree, or an axis has no complete cell.");
            var fullGrid = new float3[(int)total]; Vector3 half = rawGrid*.5f;
            for (int i = 0; i < fullGrid.Length; i++)
            {
                int x = i%(int)rawGrid.x, y = (i/(int)rawGrid.x)%(int)rawGrid.y, z = i/((int)rawGrid.x*(int)rawGrid.y);
                Vector3 local = settings.CellSize/2 + new Vector3(x*settings.CellSize.x,y*settings.CellSize.y,z*settings.CellSize.z) -
                    new Vector3(half.x*settings.CellSize.x,half.y*settings.CellSize.y,half.z*settings.CellSize.z);
                fullGrid[i] = volume.transform.position+volume.transform.rotation*local;
            }
            var rotation = volume.transform.rotation;
            // Source CullAdditionalOccluders uses a world-axis-aligned box, regardless of
            // volume rotation, and its HashSet removes duplicate renderer references.
            var additionalBounds = new Bounds(volume.transform.position,volume.Size);
            var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest { Profile = settings.InputProfile, Groups = sources,
                AdditionalOccluders = inputs == null ? Array.Empty<Renderer>() : (inputs.AdditionalOccluders ?? Array.Empty<Renderer>())
                    .Where(Included).Where(r=>additionalBounds.Intersects(r.bounds)).Distinct().ToArray(),
                Samples = SamplingSnapshotExtractor.Capture(fullGrid,providers,Decision),
                LodGroups = roots.SelectMany(r=>r.GetComponentsInChildren<LODGroup>()).Where(r=>r.gameObject.activeInHierarchy)
                    .OrderBy(r=>r.GetEntityId()).ToArray(), RenderTransparency = settings.RenderTransparency,
                Rays = new BakeRaySettings { Width = settings.Resolution, Height = settings.Resolution, Far = settings.Far,
                    Rotation = new quaternion(rotation.x,rotation.y,rotation.z,rotation.w), CellSize = settings.CellSize } });
            var result = new CollectedBakeInput { Snapshot = snapshot, Mapping = mapping, VolumeId = id,
                Metadata = settings.InputProfile == BakeInputProfile.NativeVulkanInput ? BakeMergeMetadata.NativeDefault : BakeMergeMetadata.ComputeNeighbors,
                Layout = new CullingBakeLayout { CellCount = rounded, CellSize = settings.CellSize,
                    BakeOrientation = new quaternion(rotation.x,rotation.y,rotation.z,rotation.w) } };
            try { result.Fingerprint = Fingerprint(result,settings,volume.Size); return result; }
            catch { result.Dispose(); throw; }
        }
        private static string Fingerprint(CollectedBakeInput input, BakeSettingsAsset settings, Vector3 size)
        {
            using var hash = SHA256.Create(); using var sink = new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write);
            using var writer = new BinaryWriter(sink);
            void F3(float3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            void F4(float4 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w); }
            writer.Write(BakeBackendFactory.AlgorithmId(settings.InputProfile));
            writer.Write((int)settings.ExecutionMode);
            writer.Write((int)settings.InputProfile); F3(size); F3(settings.CellSize); writer.Write(settings.Resolution);
            writer.Write(settings.Far); writer.Write(settings.RenderTransparency); writer.Write(settings.MemoryBudgetMiB); writer.Write(settings.MaxCellsPerBatch);
            writer.Write(settings.MergeAxes.Length); foreach (var axis in settings.MergeAxes) { writer.Write(axis.x); writer.Write(axis.y); writer.Write(axis.z); }
            var s = input.Snapshot; F4(s.Rays.Rotation.value);
            writer.Write(s.Groups.Length);
            foreach (var key in s.Groups) { writer.Write(key.VolumeId.x); writer.Write(key.VolumeId.y); writer.Write(key.VolumeId.z); writer.Write(key.VolumeId.w); writer.Write(key.GroupIndex); }
            writer.Write(s.Vertices.Length); foreach (var vertex in s.Vertices) F3(vertex);
            writer.Write(s.Indices.Length); foreach (int index in s.Indices) writer.Write(index);
            writer.Write(s.Geometry.Length); foreach (var g in s.Geometry) { writer.Write(g.VertexOffset); writer.Write(g.VertexCount); writer.Write(g.IndexOffset); writer.Write(g.IndexCount); writer.Write(g.SourceBinCount); }
            writer.Write(s.Instances.Length);
            foreach (var instance in s.Instances)
            {
                writer.Write(instance.Geometry); writer.Write(instance.GroupIndex); writer.Write(instance.Transparent); writer.Write(instance.DoubleSided);
                F4(instance.LocalToWorld.c0); F4(instance.LocalToWorld.c1); F4(instance.LocalToWorld.c2); F4(instance.LocalToWorld.c3);
                F4(instance.WorldToLocal.c0); F4(instance.WorldToLocal.c1); F4(instance.WorldToLocal.c2); F4(instance.WorldToLocal.c3); F3(instance.BoundsMin); F3(instance.BoundsMax);
            }
            writer.Write(s.Samples.Length); foreach (var sample in s.Samples) { writer.Write(sample.FullGridIndex); F3(sample.Position); }
            writer.Flush(); sink.FlushFinalBlock(); return Convert.ToBase64String(hash.Hash);
        }
    }
}
