using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Editor;
using CullingSystemDOTS.Runtime;
using CullingSystemDOTS.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Tests
{
    public class CullingAuthoringTests
    {
        public static string CreateStreamingFixture()
        {
            const string folder = "Assets/Tests/Fixtures/Streaming";
            const string path = folder + "/StreamingVolumes.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) throw new InvalidOperationException("Fixture already exists.");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Tests/Fixtures", "Streaming");
            var id = new uint4(91, 2, 3, 4);
            using var imported = LegacyBakeImporter.Import(new LegacyBakeSource { VolumeId = id, DeclaredGroupCount = 1,
                Layout = new CullingBakeLayout { CellCount = new float3(1), CellSize = new float3(2), BakeOrientation = quaternion.identity },
                Cells = new[] { new LegacyVisibilityCell { Compressed = new byte[] { 0 }, EncodedLength = 1 } }
            }, new[] { new CullingGroupKey(id, 0) });
            if (!imported.Success) throw new InvalidOperationException(imported.Diagnostic);
            AssetDatabase.CreateAsset(imported.Asset, folder + "/StreamingBake.asset");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Streaming material" };
            AssetDatabase.CreateAsset(material, folder + "/StreamingMaterial.mat");
            var mesh = new Mesh { name = "Streaming two submeshes" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 0, 2, 3 }, 1); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, folder + "/StreamingMesh.asset");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                for (int v = 0; v < 2; v++)
                {
                    var go = new GameObject("Streaming volume " + v); go.transform.position = new Vector3(v * 3, 0, 0);
                    var volume = go.AddComponent<CullingVolumeAuthoring>(); volume.Bake = imported.Asset; volume.Size = new Vector3(2, 2, 2); volume.Layers = 1u << v;
                    var groupObject = new GameObject("Streaming group 0"); groupObject.transform.parent = go.transform;
                    groupObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = groupObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { material, material };
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var group = groupObject.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.Renderers = new Renderer[] { renderer };
                }
                if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save owned streaming fixture.");
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
            return AssetDatabase.AssetPathToGUID(path);
        }

        [Test]
        public void BakingKeepsEveryMaterialEntityAndBothInstancesOfTheSameBake()
        {
            var id = new uint4(1, 2, 3, 4);
            using var imported = LegacyBakeImporter.Import(new LegacyBakeSource { VolumeId = id, DeclaredGroupCount = 1,
                Layout = new CullingBakeLayout { CellCount = new float3(1), CellSize = new float3(2), BakeOrientation = quaternion.identity },
                Cells = new[] { new LegacyVisibilityCell { Compressed = new byte[] { 0 }, EncodedLength = 1 } }
            }, new[] { new CullingGroupKey(id, 0) });
            Assert.That(imported.Success, Is.True, imported.Diagnostic);
            var root = new GameObject("Baking binding fixture");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var mesh = new Mesh { name = "Two submesh fixture" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 0, 2, 3 }, 1); mesh.RecalculateBounds();
            using var store = new BlobAssetStore(128);
            using var world = new World("Authoring fixture", WorldFlags.Conversion);
            try
            {
                for (int v = 0; v < 2; v++)
                {
                    var go = new GameObject("Volume " + v); go.transform.parent = root.transform;
                    var volume = go.AddComponent<CullingVolumeAuthoring>(); volume.Bake = imported.Asset; volume.Size = new Vector3(2, 2, 2); volume.Layers = 1u << v;
                    var renderers = new Renderer[3];
                    var lods = new LOD[3]; var thresholds = new[] { .6f, .3f, .008268547f };
                    for (int l = 0; l < renderers.Length; l++)
                    {
                        var lodObject = new GameObject("LOD " + l); lodObject.transform.parent = go.transform;
                        lodObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = lodObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { material, material };
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderers[l] = renderer; lods[l] = new LOD(thresholds[l], new Renderer[] { renderer });
                    }
                    var lodGroup = go.AddComponent<LODGroup>(); lodGroup.SetLODs(lods); lodGroup.RecalculateBounds();
                    var group = go.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = 0; group.Renderers = renderers;
                }
                Bake(world, root, store);
                using var volumes = world.EntityManager.CreateEntityQuery(typeof(CullingVolume)).ToComponentDataArray<CullingVolume>(Allocator.Temp);
                Assert.That(volumes.Length, Is.EqualTo(2));
                Assert.That(volumes[0].InstanceId, Is.Not.EqualTo(volumes[1].InstanceId));
                Assert.That(volumes[0].Bake, Is.EqualTo(volumes[1].Bake), "BlobAssetStore should deduplicate the shared immutable bake.");
                Assert.That(volumes[0].Bake.Value.Groups[0], Is.EqualTo(new CullingGroupKey(id, 0)), "Asset mapping remains canonical.");
                using var groups = world.EntityManager.CreateEntityQuery(typeof(CullingGroupDefinition), typeof(CullingBakedRenderBinding)).ToEntityArray(Allocator.Temp);
                Assert.That(groups.Length, Is.EqualTo(2));
                foreach (var group in groups)
                {
                    Assert.That(world.EntityManager.GetComponentData<CullingGroupDefinition>(group).GroupIndex, Is.Zero);
                    var bindings = world.EntityManager.GetBuffer<CullingBakedRenderBinding>(group);
                    Assert.That(bindings.Length, Is.EqualTo(6), "All three LODs and both material entities from the stock renderer baker must be bound.");
                    Assert.That(bindings[0].Main, Is.Not.EqualTo(bindings[1].Main));
                    foreach (var binding in bindings)
                    {
                        Assert.That(world.EntityManager.HasComponent<MaterialMeshInfo>(binding.Main), Is.True);
                        Assert.That(world.EntityManager.HasComponent<MeshLODComponent>(binding.Main), Is.True);
                    }
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); Object.DestroyImmediate(imported.Asset); }
        }

        [Test]
        public void PortalAuthoringBakesAnExplicitVolumeLinkAndStableRegistrationOrder()
        {
            var id = new uint4(41,2,3,4);
            using var imported = LegacyBakeImporter.Import(new LegacyBakeSource { VolumeId = id, DeclaredGroupCount = 1,
                Layout = new CullingBakeLayout { CellCount = new float3(1), CellSize = new float3(2), BakeOrientation = quaternion.identity },
                Cells = new[] { new LegacyVisibilityCell { EncodedLength = 0 } }
            }, new[] {new CullingGroupKey(id,0)});
            Assert.That(imported.Success, Is.True,imported.Diagnostic);
            var root = new GameObject("portal authoring fixture");
            using var store = new BlobAssetStore(128); using var world = new World("Portal baking",WorldFlags.Conversion);
            try
            {
                var volume = root.AddComponent<CullingVolumeAuthoring>(); volume.Bake = imported.Asset;
                var group = root.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.UseImportedVertexCount = true; group.ImportedVertexCount = 42;
                var portalObject = new GameObject("portal"); portalObject.transform.SetParent(root.transform,false); portalObject.transform.localPosition = new Vector3(3,4,5);
                var portal = portalObject.AddComponent<CullingPortalAuthoring>(); portal.Volume = volume; portal.RegistrationOrder = 17;
                Bake(world,root,store);
                using var query = world.EntityManager.CreateEntityQuery(typeof(CullingPortalLink),typeof(Unity.Transforms.LocalTransform));
                Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
                var entity = query.GetSingletonEntity(); var link = world.EntityManager.GetComponentData<CullingPortalLink>(entity);
                Assert.That(link.RegistrationOrder, Is.EqualTo(17)); Assert.That(world.EntityManager.HasComponent<CullingVolume>(link.Volume), Is.True);
                Assert.That(world.EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(entity).Position, Is.EqualTo(new float3(3,4,5)));
                using var groupQuery = world.EntityManager.CreateEntityQuery(typeof(CullingGroupDefinition));
                Assert.That(groupQuery.GetSingleton<CullingGroupDefinition>().VertexCount, Is.EqualTo(42),"Imported source statistics must survive entity baking.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(imported.Asset); }
        }

        // Test-only entry into Unity's exact baking pipeline. No private reflection is used by the package runtime/bakers.
        private static void Bake(World world, GameObject root, BlobAssetStore store)
        {
            var assembly = typeof(Baker<>).Assembly;
            var settingsType = assembly.GetType("Unity.Entities.BakingSettings", true);
            var settings = Activator.CreateInstance(settingsType);
            settingsType.GetProperty("BlobAssetStore").SetValue(settings, store);
            var utility = assembly.GetType("Unity.Entities.BakingUtility", true);
            utility.GetMethod("BakeGameObjects", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { world, new[] { root }, settings });
        }
    }
}
