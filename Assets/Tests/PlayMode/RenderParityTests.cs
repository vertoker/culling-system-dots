using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Rendering;

namespace PerfectCullingDOTS.Rendering.Tests
{
    public class RenderParityTests
    {
        [UnityTest]
        public IEnumerator TwoCamerasUseDifferentMasksInBothOrders()
        {
            foreach (bool reverse in new[] { false, true })
            {
                var world = World.DefaultGameObjectInjectionWorld;
                var manager = world.EntityManager;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
                var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", Color.red);
                var cameras = new[] { new GameObject("PVS hidden camera").AddComponent<Camera>(), new GameObject("PVS visible camera").AddComponent<Camera>() };
                var targets = new[] { new RenderTexture(256, 256, 24), new RenderTexture(256, 256, 24) };
                for (int i = 0; i < 2; i++)
                {
                    targets[i].Create(); cameras[i].targetTexture = targets[i];
                    cameras[i].clearFlags = CameraClearFlags.SolidColor; cameras[i].backgroundColor = Color.black;
                    cameras[i].cullingMask = 1 << 31; cameras[i].transform.position = new Vector3(0, 0, -5);
                    cameras[i].depth = reverse ? 1 - i : i;
                }
                var entity = manager.CreateEntity();
                var frame = CullingFrameState.Create(1, 2, 1, 2, Allocator.Persistent);
                var volumes = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = 1 } }, Allocator.Persistent);
                var bindings = new NativeArray<RenderGroupBinding>(1, Allocator.Persistent);
                EcsRenderVisibilityAdapter adapter = null;
                try
                {
                    RenderMeshUtility.AddComponents(entity, manager, new RenderMeshDescription(ShadowCastingMode.Off, layer: 31),
                        new RenderMeshArray(new[] { material }, new[] { mesh }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                    manager.SetComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                    var key = new CullingGroupKey(new uint4(1, 0, 0, 0), 0);
                    frame.Groups[0] = new CullingGroupSnapshot { Key = key, StateGeneration = 1, InitialApplied = 1 };
                    frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                    for (int p = 0; p < 2; p++)
                    {
                        int camera = reverse ? 1 - p : p;
                        frame.Passes[p] = new CullingViewPass { ViewIndex = camera, RenderViewId = (ulong)camera + 1 };
                        frame.PassStates[p] = (byte)camera;
                        frame.PassVolumeApplications[p] = 1;
                    }
                    bindings[0] = new RenderGroupBinding { Group = key, Main = entity,
                        OriginalShadowMode = ShadowCastingMode.Off, ToggleMode = CullingRenderToggleMode.ShadowcastMode,
                        InitialRendererEnabled = true };
                    for (int i = 0; i < 3; i++) yield return null;
                    adapter = new EcsRenderVisibilityAdapter(world);
                    adapter.RegisterCamera(cameras[0], 1); adapter.RegisterCamera(cameras[1], 2);
                    for (int i = 0; i < 3; i++) { adapter.Publish(in frame, volumes, bindings); yield return null; }
                    Assert.That(Center(targets[0]), Is.EqualTo(new Color32(0, 0, 0, 255)), "Hidden camera, reverse=" + reverse);
                    Assert.That(Center(targets[1]).r, Is.GreaterThan(200), "Visible camera, reverse=" + reverse);
                    Assert.That(adapter.LastFault, Is.Null);
                    Assert.That(manager.IsEnabled(entity), Is.True);
                    Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity), Is.True);
                }
                finally
                {
                    adapter?.Dispose(); manager.CompleteAllTrackedJobs();
                    if (manager.Exists(entity)) manager.DestroyEntity(entity);
                    for (int i = 0; i < 2; i++) { cameras[i].targetTexture = null; Object.Destroy(cameras[i].gameObject); targets[i].Release(); Object.Destroy(targets[i]); }
                    Object.Destroy(material); Object.Destroy(cube);
                    bindings.Dispose(); volumes.Dispose(); frame.Dispose();
                }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ViewFilterHidesTheDrawWithoutDisablingItsEntity()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            Assert.That(world, Is.Not.Null);
            var graphics = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            Assert.That(graphics, Is.Not.Null);
            var manager = world.EntityManager;
            var previousFilter = graphics.CullingFilter;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.SetActive(false);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.red);
            var cameraObject = new GameObject("PVS view filter test camera");
            var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(0, 0, -5);
            var entity = manager.CreateEntity();
            int calls = 0;
            try
            {
                RenderMeshUtility.AddComponents(entity, manager,
                    new RenderMeshDescription(ShadowCastingMode.Off, layer: 31),
                    new RenderMeshArray(new[] { material }, new[] { mesh }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                manager.SetComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200), "Stand must actually render the ECS cube before filtering.");
                graphics.CullingFilter = context => {
                    calls++;
                    var excluded = new NativeParallelHashSet<Entity>(1, Allocator.TempJob);
                    excluded.Add(entity);
                    return new EntitiesGraphicsCullingFilter { ExcludedEntities = excluded };
                };
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(Center(target), Is.EqualTo(new Color32(0, 0, 0, 255)), "Excluded graphics entity still drew into its camera.");
                Assert.That(calls, Is.GreaterThan(0));
                Assert.That(manager.Exists(entity), Is.True);
                Assert.That(manager.IsEnabled(entity), Is.True);
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity), Is.True);
            }
            finally
            {
                graphics.CullingFilter = previousFilter;
                manager.CompleteAllTrackedJobs();
                if (manager.Exists(entity)) manager.DestroyEntity(entity);
                camera.targetTexture = null;
                Object.Destroy(cameraObject); Object.Destroy(cube); Object.Destroy(material);
                target.Release(); Object.Destroy(target);
            }
        }

        private static Color32 Center(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0);
                image.Apply();
                return image.GetPixels32()[0];
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
