using System.Collections;
using System.Collections.Generic;
using CullingSystemDOTS.Dynamic.Gpu;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Rendering.Tests
{
    public class DynamicIndirectRendererTests
    {
        private static Color32 Pixel(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(1,1,TextureFormat.RGBA32,false);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(target.width/2,target.height/2,1,1),0,0);image.Apply();return image.GetPixels32()[0];}
            finally {RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
        }
        private static byte Red(RenderTexture target)=>Pixel(target).r;
        [UnityTest] public IEnumerator CurrentGeometryProducesOppositeViewsAndMsaaFailsOpenThenDemolitionRevealsTarget()
        {
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.SetActive(false);var material=new Material(resources.Indirect);
            var cameras=new Camera[3];var targets=new RenderTexture[3];Mesh oneSided=null;GameObject stockPlane=null;Material stockMaterial=null;
            var registry=new OcclusionWorld(new OcclusionSettings {MaxResourceBytes=1024*1024});OcclusionIndirectRenderer renderer=null;
            System.Action<ScriptableRenderContext,List<Camera>> prepare=null;ulong frame=0;
            try
            {
                var geometry=registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                var wallMatrix=float4x4.TRS(new float3(0,0,-2),quaternion.identity,new float3(6,6,.2f));
                var wallDescriptor=new OcclusionInstanceDescriptor {Geometry=geometry,Layer=30,
                    State=new OcclusionInstanceState {LocalToWorld=wallMatrix,WorldBounds=new Bounds(new Vector3(0,0,-2),new Vector3(6,6,.2f)),
                        Roles=OcclusionRole.Occluder,Opaque=true}};
                var wall=registry.RegisterInstance(wallDescriptor);
                registry.RegisterInstance(new OcclusionInstanceDescriptor {Geometry=geometry,Material=material,Layer=31,
                    State=new OcclusionInstanceState {LocalToWorld=float4x4.identity,WorldBounds=new Bounds(Vector3.zero,Vector3.one),Color=new float4(1,0,0,1),
                        Roles=OcclusionRole.Renderable|OcclusionRole.Occludee,Opaque=true}});
                for(int i=0;i<3;i++)
                {
                    cameras[i]=new GameObject("Indirect renderer integration view "+i).AddComponent<Camera>();
                    cameras[i].transform.position=new Vector3(0,0,i==1?5:-5);cameras[i].transform.LookAt(Vector3.zero);
                    cameras[i].orthographic=true;cameras[i].orthographicSize=2.5f;cameras[i].cullingMask=(1<<30)|(1<<31);
                    cameras[i].clearFlags=CameraClearFlags.SolidColor;cameras[i].backgroundColor=Color.black;cameras[i].allowMSAA=i==2;
                    targets[i]=new RenderTexture(65,49,24){antiAliasing=i==2?4:1};targets[i].Create();cameras[i].targetTexture=targets[i];
                }
                renderer=new OcclusionIndirectRenderer(registry,resources);foreach(var camera in cameras)renderer.RegisterView(camera);
                prepare=(context,views)=>
                {
                    if(!views.Contains(cameras[0]))return;
                    registry.ProcessUpdates(new OcclusionUpdateBudget(int.MaxValue,int.MaxValue));renderer.PrepareFrame(++frame);
                };
                RenderPipelineManager.beginContextRendering+=prepare;
                for(int i=0;i<8;i++)yield return null;
                Assert.That(Red(targets[1]),Is.GreaterThan(200),"The opposite view consumes actual current GPU color data.");
                Assert.That(Red(targets[0]),Is.LessThan(5),"Current occluder depth must suppress the target draw.");
                Assert.That(Red(targets[2]),Is.GreaterThan(200),"MSAA cannot consume a single-sample occlusion proof.");
                Assert.That(renderer.ViewFallback(cameras[2]),Is.EqualTo(OcclusionFallbackReason.UnsupportedView));
                Assert.That(renderer.SlotCount,Is.LessThanOrEqualTo(2));
                Assert.That(renderer.PacketCreations,Is.LessThanOrEqualTo(renderer.SlotCount),"Matching frame payloads must reuse buffers.");
                Assert.That(renderer.ViewCreations,Is.LessThanOrEqualTo(renderer.SlotCount*2),"Stable views must reuse depth and compaction resources.");
                registry.RemoveInstance(wall);
                var shadowOnly=wallDescriptor;shadowOnly.Material=material;shadowOnly.Shadows=ShadowCastingMode.ShadowsOnly;
                shadowOnly.State.Roles=OcclusionRole.Renderable|OcclusionRole.Occluder|OcclusionRole.Occludee;
                var shadowWall=registry.RegisterInstance(shadowOnly);
                for(int i=0;i<3;i++)yield return null;
                Assert.That(Red(targets[0]),Is.GreaterThan(200),"A shadow-only wall has no opaque camera coverage and must not hide the target.");
                registry.RemoveInstance(shadowWall);wall=registry.RegisterInstance(wallDescriptor);
                for(int i=0;i<3;i++)yield return null;
                cameras[0].cullingMask=1<<31;
                for(int i=0;i<3;i++)yield return null;
                Assert.That(Red(targets[0]),Is.GreaterThan(200),"A camera-excluded layer cannot contribute occluder depth.");
                cameras[0].cullingMask=(1<<30)|(1<<31);
                for(int i=0;i<3;i++)yield return null;
                Assert.That(Red(targets[0]),Is.LessThan(5));
                registry.RemoveInstance(wall);
                oneSided=new Mesh{vertices=new[]{new Vector3(-3,-3,0),new Vector3(3,-3,0),new Vector3(-3,3,0),new Vector3(3,3,0)},
                    normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back},triangles=new[]{0,2,1,1,2,3}};
                stockMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));stockMaterial.SetColor("_BaseColor",Color.blue);
                stockPlane=new GameObject("Owned ordinary one-sided reference");stockPlane.layer=31;stockPlane.transform.position=new Vector3(0,0,-2);
                stockPlane.AddComponent<MeshFilter>().sharedMesh=oneSided;var stockRenderer=stockPlane.AddComponent<MeshRenderer>();stockRenderer.sharedMaterial=stockMaterial;stockRenderer.shadowCastingMode=ShadowCastingMode.Off;
                for(int i=0;i<3;i++)yield return null;byte ordinaryFront=Pixel(targets[0]).b;stockRenderer.enabled=false;
                var planeGeometry=registry.RegisterGeometry(oneSided);var plane=wallDescriptor;plane.Geometry=planeGeometry;
                plane.State.LocalToWorld=float4x4.Translate(new float3(0,0,-2));plane.State.WorldBounds=new Bounds(new Vector3(0,0,-2),new Vector3(6,6,0));
                var planeInstance=registry.RegisterInstance(plane);
                for(int i=0;i<4;i++)yield return null;
                byte gpuFront=Red(targets[0]);
                oneSided.triangles=new[]{0,1,2,1,3,2};registry.ReplaceGeometry(planeGeometry,oneSided);
                stockRenderer.enabled=true;for(int i=0;i<3;i++)yield return null;byte ordinaryBack=Pixel(targets[0]).b;stockRenderer.enabled=false;
                for(int i=0;i<4;i++)yield return null;
                Debug.Log("One-sided winding diagnostic: ordinary front blue="+ordinaryFront+", GPU front red="+gpuFront+", ordinary back blue="+ordinaryBack+", GPU back red="+Red(targets[0]));
                Assert.That(ordinaryFront,Is.GreaterThan(200),"The independent ordinary renderer must confirm the fixture front side.");
                Assert.That(ordinaryBack,Is.LessThan(5),"The independent ordinary renderer must reject the fixture back side.");
                Assert.That(gpuFront,Is.LessThan(5),"A forward one-sided surface must actually occlude.");
                Assert.That(Red(targets[0]),Is.GreaterThan(200),"Back-facing one-sided geometry has no opaque coverage.");
                registry.RemoveInstance(planeInstance);registry.RemoveGeometry(planeGeometry);wall=registry.RegisterInstance(wallDescriptor);
                for(int i=0;i<3;i++)yield return null;
                var large=new RenderTexture(2048,1024,24);large.Create();
                try
                {
                    cameras[0].targetTexture=large;
                    for(int i=0;i<3;i++)yield return null;
                    Assert.That(Red(large),Is.GreaterThan(200),"A resized view exceeding the depth budget must use immutable full-draw arguments.");
                    Assert.That(renderer.ViewFallback(cameras[0]),Is.EqualTo(OcclusionFallbackReason.ResourceBudget));
                }
                finally {cameras[0].targetTexture=targets[0];large.Release();UnityEngine.Object.Destroy(large);}
                registry.RemoveInstance(wall);
                for(int i=0;i<4;i++)yield return null;
                Assert.That(Red(targets[0]),Is.GreaterThan(200),"Demolition must not retain the old hidden result.");
                RenderPipelineManager.beginContextRendering-=prepare;prepare=null;
                OcclusionGpuRetirement.DrainForApplicationQuit();registry.Dispose();
                Assert.That(registry.AccountedResourceBytes,Is.Zero,"Application shutdown must retire the active publication before MonoBehaviour destruction.");
                renderer.Dispose();renderer=null;
                for(int i=0;i<4;i++)yield return null;
                OcclusionGpuRetirement.DrainForShutdown();Assert.That(registry.AccountedResourceBytes,Is.Zero,"Teardown must release retained frame/view payloads.");
            }
            finally
            {
                if(prepare!=null)RenderPipelineManager.beginContextRendering-=prepare;
                renderer?.Dispose();registry.Dispose();OcclusionGpuRetirement.DrainForShutdown();
                for(int i=0;i<3;i++) {if(cameras[i]!=null){cameras[i].targetTexture=null;UnityEngine.Object.Destroy(cameras[i].gameObject);}if(targets[i]!=null){targets[i].Release();UnityEngine.Object.Destroy(targets[i]);}}
                UnityEngine.Object.Destroy(stockPlane);UnityEngine.Object.Destroy(stockMaterial);UnityEngine.Object.Destroy(oneSided);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(cube);
            }
            #else
            throw new System.NotSupportedException("This Editor fixture requires serialized Player shader references.");
            #endif
        }
    }
}
