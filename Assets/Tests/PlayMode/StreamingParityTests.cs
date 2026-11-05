using System.Collections;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Runtime;
using PerfectCullingDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PerfectCullingDOTS.Rendering.Tests
{
    public class StreamingParityTests
    {
        public struct Receiver : IComponentData, IEnableableComponent { }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SubSceneReloadPreservesEveryBakedBindingAndStableKey()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var guid = new Unity.Entities.Hash128(UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Tests/Fixtures/Streaming/StreamingVolumes.unity"));
            Assert.That(guid.IsValid, Is.True);
            Entity scene = Entity.Null;
            uint4[] ids = null;
            try
            {
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    scene = SceneSystem.LoadSceneAsync(world.Unmanaged, guid, new SceneSystem.LoadParameters {
                        Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn });
                    for (int wait = 0; wait < 100 && !SceneSystem.IsSceneLoaded(world.Unmanaged, scene); wait++) yield return null;
                    Assert.That(SceneSystem.IsSceneLoaded(world.Unmanaged, scene), Is.True, "SubScene did not finish loading.");
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(CullingVolume)))
                    using (var volumes = query.ToComponentDataArray<CullingVolume>(Allocator.Temp))
                    {
                        Assert.That(volumes.Length, Is.EqualTo(2));
                        var current = new[] { volumes[0].InstanceId, volumes[1].InstanceId };
                        if (ids == null) ids = current; else CollectionAssert.AreEquivalent(ids, current, "Reload changed serialized volume identity.");
                        Assert.That(volumes[0].Bake.Value.GroupCount, Is.EqualTo(1));
                    }
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(CullingGroupDefinition), typeof(CullingBakedRenderBinding)))
                    using (var groups = query.ToEntityArray(Allocator.Temp))
                    {
                        Assert.That(groups.Length, Is.EqualTo(2));
                        foreach (var group in groups)
                        {
                            var bindings = world.EntityManager.GetBuffer<CullingBakedRenderBinding>(group);
                            Assert.That(bindings.Length, Is.EqualTo(2));
                            foreach (var binding in bindings)
                            {
                                Assert.That(world.EntityManager.Exists(binding.Main), Is.True);
                                Assert.That(world.EntityManager.HasComponent<MaterialMeshInfo>(binding.Main), Is.True);
                            }
                        }
                    }
                    SceneSystem.UnloadScene(world.Unmanaged, scene, SceneSystem.UnloadParameters.DestroyMetaEntities); scene = Entity.Null;
                    yield return null;
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(CullingVolume))) Assert.That(query.CalculateEntityCount(), Is.Zero);
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(CullingGroupDefinition))) Assert.That(query.CalculateEntityCount(), Is.Zero);
                }
            }
            finally { if (scene != Entity.Null) SceneSystem.UnloadScene(world.Unmanaged, scene, SceneSystem.UnloadParameters.DestroyMetaEntities); }
        }
#endif

        [UnityTest]
        public IEnumerator EcsReceiversAndGraphicsUseTheSameOrderedCameraPasses()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            var owner = world.GetOrCreateSystemManaged<CullingWorldSystem>();
            var replay = world.GetOrCreateSystemManaged<CullingApplySystem>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.red);
            var cameras = new[] { new GameObject("Ordered visible camera").AddComponent<Camera>(), new GameObject("Ordered inverted camera").AddComponent<Camera>() };
            var targets = new[] { new RenderTexture(64, 64, 24), new RenderTexture(64, 64, 24) };
            var entity = manager.CreateEntity(typeof(Receiver));
            var bake = CreateBake();
            var key = new CullingGroupKey(new uint4(93, 0, 0, 0), 0);
            var input = new CullingFrameInput {
                Volumes = new NativeArray<CullingVolumeSnapshot>(new[] { new CullingVolumeSnapshot { Bake = bake, Layers = uint.MaxValue,
                    Grid = new CullingGridSnapshot { Size = new float3(20), CellSize = new float3(20), CellCount = new float3(1),
                        BakeOrientation = quaternion.identity, InverseBakeOrientation = quaternion.identity, InverseCurrentOrientation = quaternion.identity } } }, Allocator.Persistent),
                Groups = new NativeArray<CullingGroupSnapshot>(new[] { new CullingGroupSnapshot { Key = key, InitialApplied = 1, StateGeneration = 1 } }, Allocator.Persistent),
                Views = new NativeArray<CullingViewSnapshot>(new[] {
                    new CullingViewSnapshot { ViewId = 1, Position = new float3(0, 0, -5), Layers = uint.MaxValue },
                    new CullingViewSnapshot { ViewId = 2, Position = new float3(0, 0, -5), Layers = uint.MaxValue, Invert = true }
                }, Allocator.Persistent),
                Passes = new NativeArray<CullingViewPass>(new[] { new CullingViewPass { RenderViewId = 1, ViewIndex = 0 }, new CullingViewPass { RenderViewId = 2, ViewIndex = 1 } }, Allocator.Persistent),
                Portals = new NativeArray<CullingPortalSnapshot>(0, Allocator.Persistent)
            };
            using var ranges = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = 1 } }, Allocator.Persistent);
            using var bindings = new NativeArray<RenderGroupBinding>(new[] { new RenderGroupBinding { Group = key, Main = entity,
                OriginalShadowMode = ShadowCastingMode.Off, ToggleMode = CullingRenderToggleMode.ShadowcastMode, InitialRendererEnabled = true } }, Allocator.Persistent);
            EcsRenderVisibilityAdapter adapter = null;
            CullingBindingHandle receiver = default;
            int observed = 0;
            try
            {
                RenderMeshUtility.AddComponents(entity, manager, new RenderMeshDescription(ShadowCastingMode.Off, layer: 31),
                    new RenderMeshArray(new[] { material }, new[] { mesh }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                manager.SetComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                receiver = owner.Registry.RegisterEntity<Receiver>(key, entity);
                for (int i = 0; i < 2; i++)
                {
                    targets[i].Create(); cameras[i].targetTexture = targets[i]; cameras[i].depth = i;
                    cameras[i].clearFlags = CameraClearFlags.SolidColor; cameras[i].backgroundColor = Color.black;
                    cameras[i].cullingMask = 1 << 31; cameras[i].transform.position = new Vector3(0, 0, -5);
                }
                for (int i = 0; i < 3; i++) yield return null;
                adapter = new EcsRenderVisibilityAdapter(world);
                adapter.RegisterCamera(cameras[0], 1); adapter.RegisterCamera(cameras[1], 2);
                // Subscribe after replay attaches, so this observes the state that graphics culling sees.
                for (int frame = 0; frame < 3; frame++)
                {
                    var view = input.Views[0]; view.InvalidationVersion++; input.Views[0] = view;
                    view = input.Views[1]; view.InvalidationVersion++; input.Views[1] = view;
                    input.FrameId++;
                    var ticket = owner.Schedule(in input);
                    Assert.That(owner.CompleteAndPublish(ticket), Is.EqualTo(CullingPublicationStatus.Published));
                    replay.Publish(owner, adapter, ranges, bindings);
                    if (frame == 0) adapter.ApplyingPass += (pass, id) => {
                        Assert.That(manager.IsComponentEnabled<Receiver>(entity), Is.EqualTo(pass == 0)); observed++;
                    };
                    yield return null;
                    Assert.That(Center(targets[0]).r, Is.GreaterThan(200));
                    Assert.That(Center(targets[1]), Is.EqualTo(new Color32(0, 0, 0, 255)));
                }
                Assert.That(observed, Is.EqualTo(6)); Assert.That(adapter.LastFault, Is.Null);
                Assert.That(manager.IsEnabled(entity), Is.True);
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity), Is.True);
            }
            finally
            {
                replay.Attach(null); adapter?.Dispose(); owner.CancelPending(); owner.Registry.Remove(receiver);
                manager.CompleteAllTrackedJobs(); if (manager.Exists(entity)) manager.DestroyEntity(entity);
                input.Volumes.Dispose(); input.Groups.Dispose(); input.Views.Dispose(); input.Passes.Dispose(); input.Portals.Dispose(); bake.Dispose();
                for (int i = 0; i < 2; i++) { cameras[i].targetTexture = null; Object.Destroy(cameras[i].gameObject); targets[i].Release(); Object.Destroy(targets[i]); }
                Object.Destroy(material); Object.Destroy(cube);
            }
        }

        private static BlobAssetReference<CullingVolumeBlob> CreateBake()
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
            root.FormatVersion = CullingVolumeBlob.CurrentFormatVersion; root.VolumeId = new uint4(93, 0, 0, 0); root.GroupCount = 1;
            root.BakedGrid = new CullingBakeLayout { CellCount = new float3(1), CellSize = new float3(20), BakeOrientation = quaternion.identity };
            builder.Allocate(ref root.Groups, 1)[0] = new CullingGroupKey(root.VolumeId, 0);
            builder.Allocate(ref root.Cells, 1)[0] = new CullingCellRange { ByteLength = 1, EncodedLength = 1 };
            builder.Allocate(ref root.CompressedBytes, 1)[0] = 0;
            return builder.CreateBlobAssetReference<CullingVolumeBlob>(Allocator.Persistent);
        }

        private static Color32 Center(RenderTexture target)
        {
            var previous = RenderTexture.active; var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(32, 32, 1, 1), 0, 0); image.Apply(); return image.GetPixels32()[0]; }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
