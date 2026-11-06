using System.Collections;
using NUnit.Framework;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Core;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;
namespace CullingSystemDOTS.Rendering.Tests
{
    public class AutomaticCameraTests
    {
        [UnityTest]
        public IEnumerator AutomaticCameraHidesInvertsAndRestoresActualGraphics()
        {
            var world = World.DefaultGameObjectInjectionWorld; var manager = world.EntityManager;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor",Color.red);
            var go = new GameObject("automatic rendering camera"); var camera = go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-5); camera.cullingMask = 1<<31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(64,64,24); target.Create(); camera.targetTexture = target;
            var bake = CreateBake();
            var volume = manager.CreateEntity(typeof(CullingVolume),typeof(LocalTransform),typeof(LocalToWorld));
            manager.SetComponentData(volume,new CullingVolume { Bake = bake,InstanceId = new uint4(178,0,0,0),Size = new float3(20),Layers = uint.MaxValue });
            manager.SetComponentData(volume,LocalTransform.Identity); manager.SetComponentData(volume,new LocalToWorld { Value = float4x4.identity });
            var renderer = manager.CreateEntity();
            var group = manager.CreateEntity(typeof(CullingGroupDefinition));
            manager.SetComponentData(group,new CullingGroupDefinition { Volume = volume,GroupIndex = 0,StateGeneration = 1 });
            try
            {
                RenderMeshUtility.AddComponents(renderer,manager,new RenderMeshDescription(ShadowCastingMode.Off,layer:31),
                    new RenderMeshArray(new[] {material},new[] {cube.GetComponent<MeshFilter>().sharedMesh}),MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
                manager.SetComponentData(renderer,new LocalToWorld { Value = float4x4.identity });
                manager.AddBuffer<CullingBakedRenderBinding>(group).Add(new CullingBakedRenderBinding { Main = renderer,OriginalShadowMode = ShadowCastingMode.Off,InitialRendererEnabled = 1 });
                for (int i = 0; i < 3; i++) yield return null;
                var authoring = go.AddComponent<CullingCameraAuthoring>(); authoring.Invert = true;
                yield return null; yield return null;
                Assert.That(Center(target), Is.EqualTo(new Color32(0,0,0,255)));
                authoring.Invert = false; yield return null; yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200));
                authoring.Invert = true; yield return null;
                authoring.enabled = false; yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200),"Disabling the last culling camera releases the graphics filter.");
                authoring.enabled = true; authoring.Invert = false; yield return null; yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200));
                Assert.That(world.GetOrCreateSystemManaged<CullingWorldSystem>().GetOrCreateRenderingAdapter().LastFault, Is.Null.Or.Empty);
                Assert.That(manager.IsEnabled(renderer), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
                var apply = world.GetExistingSystemManaged<CullingApplySystem>(); if (apply != null) world.DestroySystemManaged(apply);
                var capture = world.GetExistingSystemManaged<CullingViewCaptureSystem>(); if (capture != null) world.DestroySystemManaged(capture);
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>(); if (owner != null) world.DestroySystemManaged(owner);
                manager.CompleteAllTrackedJobs(); manager.DestroyEntity(group); manager.DestroyEntity(volume); manager.DestroyEntity(renderer); bake.Dispose();
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(material); Object.DestroyImmediate(cube);
            }
        }
        private static BlobAssetReference<CullingVolumeBlob> CreateBake()
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
            root.FormatVersion = CullingVolumeBlob.CurrentFormatVersion; root.VolumeId = new uint4(177,0,0,0); root.GroupCount = 1;
            root.BakedGrid = new CullingBakeLayout { CellCount = new float3(1),CellSize = new float3(20),BakeOrientation = quaternion.identity };
            builder.Allocate(ref root.Groups,1)[0] = new CullingGroupKey(root.VolumeId,0);
            builder.Allocate(ref root.Cells,1)[0] = new CullingCellRange { ByteLength = 1,EncodedLength = 1 };
            builder.Allocate(ref root.CompressedBytes,1)[0] = 0;
            return builder.CreateBlobAssetReference<CullingVolumeBlob>(Allocator.Persistent);
        }
        private static Color32 Center(RenderTexture target)
        {
            var previous = RenderTexture.active; var image = new Texture2D(1,1,TextureFormat.RGBA32,false);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(32,32,1,1),0,0); image.Apply(); return image.GetPixels32()[0]; }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        [UnityTest]
        public IEnumerator SrpContextIncludesUnknownCamerasAndConsumesEveryPreparedPass()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var unknown = new GameObject("first unregistered render camera").AddComponent<Camera>(); unknown.depth = -100;
            var camera = new GameObject("automatic PVS camera").AddComponent<Camera>(); camera.depth = 100;
            var targets = new[] { new RenderTexture(16,16,24),new RenderTexture(16,16,24) };
            targets[0].Create(); targets[1].Create(); unknown.targetTexture = targets[0]; camera.targetTexture = targets[1];
            var authoring = camera.gameObject.AddComponent<CullingCameraAuthoring>();
            try
            {
                yield return null;
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>(); Assert.That(owner, Is.Not.Null);
                var adapter = owner.GetOrCreateRenderingAdapter();
                int passes = 0; adapter.ApplyingPass += (_,__)=>passes++;
                yield return null;
                Assert.That(adapter.LastFault, Is.Null.Or.Empty);
                Assert.That(passes, Is.GreaterThanOrEqualTo(2), "Unknown cameras must consume their own no-view pass.");
                authoring.enabled = false;
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(unknown.gameObject);
                var apply = world.GetExistingSystemManaged<CullingApplySystem>(); if (apply != null) world.DestroySystemManaged(apply);
                var capture = world.GetExistingSystemManaged<CullingViewCaptureSystem>(); if (capture != null) world.DestroySystemManaged(capture);
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>(); if (owner != null) world.DestroySystemManaged(owner);
                foreach (var target in targets) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
    }
}
