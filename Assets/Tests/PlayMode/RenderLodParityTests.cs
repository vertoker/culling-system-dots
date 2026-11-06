#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Rendering.Tests
{
    public class RenderLodParityTests
    {
        [UnityTest]
        public IEnumerator EveryMaterialAndLodEntityFollowsTheGroup()
        {
            var source = new GameObject("Independent LOD stand");
            var lodGroupSetup = source.AddComponent<LODGroup>();
            var setup = new LOD[3];
            for (int level = 0; level < 3; level++)
            {
                var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                child.transform.SetParent(source.transform, false);
                Object.Destroy(child.GetComponent<Collider>());
                setup[level] = new LOD(new[] { .6f, .3f, .05f }[level], new Renderer[] { child.GetComponent<Renderer>() });
            }
            lodGroupSetup.SetLODs(setup); lodGroupSetup.RecalculateBounds();
            var lodGroup = source.GetComponent<LODGroup>();
            var lods = lodGroup.GetLODs();
            Assert.That(lods.Length, Is.EqualTo(3));
            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            var entities = new List<Entity>();
            var meshes = new List<Mesh>();
            var materials = new[] { new Material(Shader.Find("Universal Render Pipeline/Unlit")), new Material(Shader.Find("Universal Render Pipeline/Unlit")) };
            materials[0].SetColor("_BaseColor", Color.red); materials[1].SetColor("_BaseColor", Color.green);
            var cameraObject = new GameObject("Original prefab LOD parity camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            var frame = CullingFrameState.Create(1, 1, 1, 1, Allocator.Persistent);
            var volumes = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = 1 } }, Allocator.Persistent);
            NativeArray<RenderGroupBinding> bindings = default;
            EcsRenderVisibilityAdapter adapter = null;
            var positions = new Vector3[3];
            var expected = new Color32[3][];
            try
            {
                foreach (var transform in source.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
                for (int lod = 0; lod < 3; lod++)
                {
                    Assert.That(lods[lod].renderers.Length, Is.EqualTo(1));
                    var renderer = lods[lod].renderers[0];
                    var filter = renderer.GetComponent<MeshFilter>();
                    var mesh = Object.Instantiate(filter.sharedMesh); meshes.Add(mesh);
                    int[] triangles = mesh.triangles;
                    int middle = (triangles.Length / 6) * 3;
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(triangles.Take(middle).ToArray(), 0);
                    mesh.SetTriangles(triangles.Skip(middle).ToArray(), 1);
                    filter.sharedMesh = mesh; renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    // Choose the midpoint of each original prefab's screen-height interval.
                    float upper = lod == 0 ? 1 : lods[lod - 1].screenRelativeTransitionHeight;
                    float height = (upper + lods[lod].screenRelativeTransitionHeight) * 0.5f;
                    float distance = LODGroupExtensions.GetWorldSpaceSize(lodGroup) / (height * LODGroupExtensions.CalculateLODParams(camera).distanceScale);
                    var reference = source.transform.TransformPoint(lodGroup.localReferencePoint);
                    positions[lod] = reference - Vector3.forward * distance;
                }
                for (int lod = 0; lod < 3; lod++)
                {
                    camera.transform.position = positions[lod];
                    for (int i = 0; i < 3; i++) yield return null;
                    expected[lod] = Pixels(target);
                    Assert.That(expected[lod].Any(p => p.r != 0 || p.g != 0 || p.b != 0), Is.True, "Source LOD stand rendered nothing: " + lod);
                }
                source.SetActive(false);
                var root = manager.CreateEntity(typeof(LocalToWorld), typeof(MeshLODGroupComponent)); entities.Add(root);
                manager.SetComponentData(root, new LocalToWorld { Value = source.transform.localToWorldMatrix });
                float size = LODGroupExtensions.GetWorldSpaceSize(lodGroup);
                var distances = new float4(float.PositiveInfinity);
                for (int lod = 0; lod < 3; lod++) distances[lod] = size / lods[lod].screenRelativeTransitionHeight;
                manager.SetComponentData(root, new MeshLODGroupComponent { LocalReferencePoint = lodGroup.localReferencePoint,
                    LODDistances0 = distances, LODDistances1 = new float4(float.PositiveInfinity) });
                var key = new CullingGroupKey(new uint4(1, 0, 0, 0), 0);
                var rows = new List<RenderGroupBinding>();
                for (int lod = 0; lod < 3; lod++)
                {
                    var renderer = lods[lod].renderers[0];
                    var array = new RenderMeshArray(materials, new[] { meshes[lod] });
                    for (ushort submesh = 0; submesh < 2; submesh++)
                    {
                        var entity = manager.CreateEntity(); entities.Add(entity);
                        RenderMeshUtility.AddComponents(entity, manager, new RenderMeshDescription(renderer), array,
                            MaterialMeshInfo.FromRenderMeshArrayIndices(submesh, 0, submesh));
                        manager.SetComponentData(entity, new LocalToWorld { Value = renderer.transform.localToWorldMatrix });
                        manager.AddComponentData(entity, new MeshLODComponent { Group = root, LODMask = 1 << lod });
                        rows.Add(new RenderGroupBinding { Group = key, Main = entity, OriginalShadowMode = ShadowCastingMode.Off,
                            ToggleMode = CullingRenderToggleMode.ShadowcastMode, InitialRendererEnabled = true });
                    }
                }
                Assert.That(rows.Count, Is.EqualTo(6));
                bindings = new NativeArray<RenderGroupBinding>(rows.ToArray(), Allocator.Persistent);
                frame.Groups[0] = new CullingGroupSnapshot { Key = key, StateGeneration = 1, InitialApplied = 1 };
                frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                frame.Passes[0] = new CullingViewPass { ViewIndex = 0, RenderViewId = 1 };
                frame.PassVolumeApplications[0] = 1;
                adapter = new EcsRenderVisibilityAdapter(world); adapter.RegisterCamera(camera, 1);
                for (int lod = 0; lod < 3; lod++)
                {
                    camera.transform.position = positions[lod];
                    frame.PassStates[0] = 1;
                    for (int i = 0; i < 3; i++) { adapter.Publish(in frame, volumes, bindings); yield return null; }
                    var actual = Pixels(target);
                    int differences = expected[lod].Where((pixel, i) => !pixel.Equals(actual[i])).Count();
                    Assert.That(differences, Is.Zero, "Exact source/ECS material and LOD image parity failed: LOD=" + lod);
                    frame.PassStates[0] = 0;
                    for (int i = 0; i < 2; i++) { adapter.Publish(in frame, volumes, bindings); yield return null; }
                    Assert.That(Pixels(target).All(p => p.r == 0 && p.g == 0 && p.b == 0 && p.a == 255), Is.True,
                        "Some material or LOD entity survived group hiding at LOD=" + lod);
                    foreach (var row in rows)
                    {
                        Assert.That(manager.IsEnabled(row.Main), Is.True);
                        Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(row.Main), Is.True);
                    }
                }
                Assert.That(manager.IsEnabled(root), Is.True);
                Assert.That(adapter.LastFault, Is.Null);
                TestContext.Out.WriteLine("All 3 source prefab LOD intervals and all 6 material/LOD render entities matched exactly and hid together; root/entity/MMI remained enabled.");
            }
            finally
            {
                adapter?.Dispose(); manager.CompleteAllTrackedJobs();
                foreach (var entity in entities) if (manager.Exists(entity)) manager.DestroyEntity(entity);
                frame.Dispose(); volumes.Dispose(); if (bindings.IsCreated) bindings.Dispose();
                camera.targetTexture = null; cameraObject.SetActive(false); source.SetActive(false);
                Object.Destroy(cameraObject); Object.Destroy(source); target.Release(); Object.Destroy(target);
                foreach (var mesh in meshes) Object.Destroy(mesh);
                foreach (var material in materials) Object.Destroy(material);
            }
        }

        private static Color32[] Pixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
#endif
