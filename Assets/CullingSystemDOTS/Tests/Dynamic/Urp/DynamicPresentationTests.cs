using System.Collections;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Dynamic.Gpu;
using CullingSystemDOTS.Dynamic.Urp;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Urp.Tests
{
    public class DynamicPresentationTests
    {
        private static byte Red(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(1,1,TextureFormat.RGBA32,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(target.width/2,target.height/2,1,1),0,0);image.Apply();return image.GetPixels32()[0].r;}
            finally {RenderTexture.active=previous;Object.Destroy(image);}
        }
        [UnityTest] public IEnumerator ReadyEcsGraphicsTransferRestoreAndLateMutationKeepCurrentPixels()
        {
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            var world=World.DefaultGameObjectInjectionWorld;var manager=world.EntityManager;
            Assert.That(world.GetExistingSystemManaged<DynamicOcclusionSystem>(),Is.Null,"Fixture requires exclusive dynamic-system ownership.");
            var system=world.GetOrCreateSystemManaged<DynamicOcclusionSystem>();var group=world.GetExistingSystemManaged<PresentationSystemGroup>();
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.SetActive(false);
            var stock=new Material(Shader.Find("Universal Render Pipeline/Unlit")){enableInstancing=true};stock.SetColor("_BaseColor",Color.red);
            var indirect=new Material(resources.Indirect);var camera=new GameObject("Dynamic ECS integration view").AddComponent<Camera>();
            var target=new RenderTexture(96,96,24);target.Create();camera.targetTexture=target;camera.cullingMask=1<<31;camera.allowMSAA=false;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.transform.position=new Vector3(0,0,-5);
            var entity=manager.CreateEntity();DynamicOcclusionPresentation presentation=null;
            System.Action<ScriptableRenderContext,System.Collections.Generic.List<Camera>> late=null;
            System.Action<ScriptableRenderContext,Camera> observe=null;
            try
            {
                var mesh=cube.GetComponent<MeshFilter>().sharedMesh;
                RenderMeshUtility.AddComponents(entity,manager,new RenderMeshDescription(ShadowCastingMode.Off,layer:31),new RenderMeshArray(new[]{stock},new[]{mesh}),MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
                manager.SetComponentData(entity,new LocalToWorld{Value=float4x4.identity});
                for(int i=0;i<3;i++)yield return null;
                Assert.That(Red(target),Is.GreaterThan(200),"Ordinary ECS rendering must work before transfer.");
                var geometry=system.Registry.RegisterGeometry(mesh);
                system.Bindings.Bind(entity,new OcclusionInstanceDescriptor {Geometry=geometry,Material=indirect,Layer=31,Shadows=ShadowCastingMode.Off,
                    State=new OcclusionInstanceState {Roles=OcclusionRole.Renderable|OcclusionRole.Occludee,Opaque=true,Color=new float4(1,0,0,1)}});
                var wallMatrix=float4x4.TRS(new float3(0,0,-2),quaternion.identity,new float3(6,6,.2f));
                var wall=system.Registry.RegisterInstance(new OcclusionInstanceDescriptor {Geometry=geometry,Layer=31,
                    State=new OcclusionInstanceState {LocalToWorld=wallMatrix,WorldBounds=new Bounds(new Vector3(0,0,-2),new Vector3(6,6,.2f)),Roles=OcclusionRole.Occluder,Opaque=true}});
                presentation=new DynamicOcclusionPresentation(system,resources);presentation.RegisterView(camera);
                group.AddSystemToUpdateList(system);group.SortSystems();
                for(int i=0;i<6;i++)yield return null;
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.False,"Only ready packets may own stock graphics.");
                Assert.That(Red(target),Is.LessThan(5));Assert.That(manager.IsEnabled(entity),Is.True);
                presentation.Enabled=false;
                for(int i=0;i<3;i++)yield return null;
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.True);Assert.That(Red(target),Is.GreaterThan(200));
                presentation.Enabled=true;
                for(int i=0;i<4;i++)yield return null;
                Assert.That(Red(target),Is.LessThan(5));
                var data=camera.GetUniversalAdditionalCameraData();data.antialiasing=AntialiasingMode.TemporalAntiAliasing;
                for(int i=0;i<3;i++)yield return null;
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.True,"Unsupported temporal rendering must retain stock ownership.");
                data.antialiasing=AntialiasingMode.None;
                var overlay=new GameObject("Owned unsupported overlay").AddComponent<Camera>();overlay.enabled=false;
                overlay.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Overlay;data.cameraStack.Add(overlay);
                try
                {
                    for(int i=0;i<3;i++)yield return null;
                    Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.True,"Camera stacks must retain stock rendering.");
                }
                finally {data.cameraStack.Remove(overlay);Object.Destroy(overlay.gameObject);}
                for(int i=0;i<3;i++)yield return null;
                Assert.That(Red(target),Is.LessThan(5));
                bool changed=false,stockObserved=false,observed=false;int mutationFrame=-1,observedFrame=-1;
                late=(context,views)=>{if(!changed && views.Contains(camera)){system.Registry.RemoveInstance(wall);changed=true;mutationFrame=Time.frameCount;}};
                observe=(context,view)=>{if(changed && !observed && view==camera){stockObserved=manager.IsComponentEnabled<MaterialMeshInfo>(entity);observed=true;observedFrame=Time.frameCount;}};
                RenderPipelineManager.beginContextRendering+=late;RenderPipelineManager.endCameraRendering+=observe;
                for(int i=0;i<10 && !observed;i++)yield return null;
                Assert.That(observed,Is.True);Assert.That(observedFrame,Is.EqualTo(mutationFrame));
                Assert.That(stockObserved,Is.True,"Late registry mutation must restore stock before this camera's culling.");
                Assert.That(Red(target),Is.GreaterThan(200));
                presentation.Dispose();presentation=null;
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.True);
                presentation=new DynamicOcclusionPresentation(system,null);presentation.RegisterView(camera);
                for(int i=0;i<3;i++)yield return null;
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(entity),Is.True,"Missing resources must leave the stock draw available.");
                Assert.That(Red(target),Is.GreaterThan(200));
            }
            finally
            {
                if(late!=null)RenderPipelineManager.beginContextRendering-=late;
                if(observe!=null)RenderPipelineManager.endCameraRendering-=observe;
                presentation?.Dispose();group.RemoveSystemFromUpdateList(system);world.DestroySystemManaged(system);
                manager.CompleteAllTrackedJobs();if(manager.Exists(entity))manager.DestroyEntity(entity);
                camera.targetTexture=null;Object.Destroy(camera.gameObject);target.Release();Object.Destroy(target);
                Object.Destroy(indirect);Object.Destroy(stock);Object.Destroy(cube);
            }
            #else
            throw new System.NotSupportedException("The fixture requires serialized shader references for Player execution.");
            #endif
        }
    }
}
