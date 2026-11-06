using System;
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
    public class DynamicIndirectColorTests
    {
        private sealed class Data : IDisposable
        {
            internal GraphicsBuffer Models, FullIds, Groups, FullArgs;
            internal readonly GraphicsBuffer[] Visible=new GraphicsBuffer[2], Args=new GraphicsBuffer[2], Ids=new GraphicsBuffer[2];
            internal Material Material;
            internal Data(Mesh mesh,OcclusionShaderResources resources,OcclusionGpuInstanceData[] instances=null)
            {
                try
                {
                    Material=new Material(resources.Indirect);
                    Models=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,80);
                    Models.SetData(instances??new[] { new OcclusionGpuInstanceData { LocalToWorld=float4x4.Translate(new float3(-1,0,0)),Color=new float4(1,0,0,1) },
                        new OcclusionGpuInstanceData { LocalToWorld=float4x4.Translate(new float3(1,0,0)),Color=new float4(0,1,0,1) } });
                    FullIds=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4); FullIds.SetData(new uint[] {0,1});
                    Groups=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4); Groups.SetData(new uint[] {0,1});
                    var args=new GraphicsBuffer.IndirectDrawIndexedArgs[2];
                    for (int i=0;i<2;i++) args[i]=new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance=mesh.GetIndexCount(0),instanceCount=1,
                        startIndex=mesh.GetIndexStart(0),baseVertexIndex=mesh.GetBaseVertex(0),startInstance=(uint)i };
                    FullArgs=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Raw,2,GraphicsBuffer.IndirectDrawIndexedArgs.size); FullArgs.SetData(args);
                    for (int i=0;i<2;i++)
                    {
                        Visible[i]=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4);
                        Args[i]=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Raw,2,GraphicsBuffer.IndirectDrawIndexedArgs.size);
                        Args[i].SetData(args); Ids[i]=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4); Ids[i].SetData(new uint[] {0,1});
                    }
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                Models?.Dispose(); FullIds?.Dispose(); Groups?.Dispose(); FullArgs?.Dispose();
                foreach (var buffer in Visible) buffer?.Dispose(); foreach (var buffer in Args) buffer?.Dispose(); foreach (var buffer in Ids) buffer?.Dispose();
                if (Material!=null) UnityEngine.Object.Destroy(Material);
            }
        }
        private sealed class Stand : IDisposable
        {
            internal readonly uint[][] Masks={new uint[] {1,0},new uint[] {0,1}};
            internal bool CastShadows;
            private readonly Camera[] cameras;
            private readonly Mesh mesh;
            private readonly OcclusionShaderResources resources;
            private readonly OcclusionWorld registry=new OcclusionWorld();
            private readonly OcclusionSnapshotLease snapshot;
            private readonly Data data;
            private OcclusionGpuSubmission retired;
            private bool disposed, awaitingContext;
            internal bool Released => retired!=null && retired.Released;
            internal Stand(Mesh mesh,OcclusionShaderResources resources,Camera[] cameras,OcclusionGpuInstanceData[] instances=null)
            {
                this.mesh=mesh; this.resources=resources; this.cameras=cameras;
                snapshot=registry.AcquireSnapshot();
                try { data=new Data(mesh,resources,instances); }
                catch { snapshot.Dispose(); registry.Dispose(); throw; }
                RenderPipelineManager.beginCameraRendering+=Begin; RenderPipelineManager.endContextRendering+=End;
            }
            private void Begin(ScriptableRenderContext context,Camera camera)
            {
                int view=Array.IndexOf(cameras,camera); if (view<0 || disposed) return;
                data.Visible[view].SetData(Masks[view]);
                using (var command=new CommandBuffer())
                { OcclusionIndirectCommands.Compact(command,resources.IndirectArguments,data.Visible[view],data.FullIds,data.Groups,data.Args[view],data.Ids[view],2,2); Graphics.ExecuteCommandBuffer(command); }
                for (int group=0;group<2;group++)
                {
                    OcclusionIndirectCommands.Draw(camera,mesh,data.Material,new Bounds(Vector3.zero,Vector3.one*5),31,data.Models,data.Ids[view],data.Args[view],group);
                    if (CastShadows) OcclusionIndirectCommands.Draw(camera,mesh,data.Material,new Bounds(Vector3.zero,Vector3.one*5),31,data.Models,data.FullIds,data.FullArgs,group,ShadowCastingMode.ShadowsOnly);
                }
                awaitingContext=true;
            }
            private void End(ScriptableRenderContext context,List<Camera> views)
            { awaitingContext=false; if (disposed) Retire(); }
            public void Dispose()
            {
                if (disposed) return; disposed=true; RenderPipelineManager.beginCameraRendering-=Begin;
                if (!awaitingContext) Retire();
            }
            private void Retire()
            {
                if (retired!=null) return; RenderPipelineManager.endContextRendering-=End;
                retired=new OcclusionGpuSubmission(snapshot,data); retired.SealAfterLastConsumer(); retired.Dispose(); registry.Dispose();
            }
        }
        private static Color32 Pixel(RenderTexture target,int x)
        {
            var previous=RenderTexture.active; var image=new Texture2D(1,1,TextureFormat.RGBA32,false);
            try { RenderTexture.active=target; image.ReadPixels(new Rect(x,target.height/2,1,1),0,0); image.Apply(); return image.GetPixels32()[0]; }
            finally { RenderTexture.active=previous; UnityEngine.Object.Destroy(image); }
        }
        private static Color32[] Pixels(RenderTexture target)
        {
            var previous=RenderTexture.active; var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try { RenderTexture.active=target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); return image.GetPixels32(); }
            finally { RenderTexture.active=previous; UnityEngine.Object.Destroy(image); }
        }
        private static int Differences(Color32[] first,Color32[] second)
        { int count=0; for(int i=0;i<first.Length;i++) if(!first[i].Equals(second[i])) count++; return count; }
        [UnityTest] public IEnumerator CameraHiddenIndirectModelsKeepTheirOrdinaryShadowInBothViewOrders()
        {
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            var lights=new Dictionary<Light,bool>(); var previousSun=RenderSettings.sun; var ambientMode=RenderSettings.ambientMode; var ambient=RenderSettings.ambientLight;
            var objects=new List<GameObject>(); var materials=new List<Material>(); var targets=new RenderTexture[2]; var cameras=new Camera[2]; Stand stand=null;
            try
            {
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) { lights.Add(light,light.enabled); light.enabled=false; }
                RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black;
                var receiver=GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(receiver); receiver.layer=31;
                receiver.transform.position=new Vector3(0,-.1f,0); receiver.transform.localScale=new Vector3(8,.2f,8);
                var receiverMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")); materials.Add(receiverMaterial); receiverMaterial.SetColor("_BaseColor",Color.white);
                receiver.GetComponent<MeshRenderer>().sharedMaterial=receiverMaterial;
                var caster=GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(caster); caster.layer=31; caster.transform.position=new Vector3(0,1,0);
                var casterMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")); materials.Add(casterMaterial); caster.GetComponent<MeshRenderer>().sharedMaterial=casterMaterial;
                var renderer=caster.GetComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                var sunObject=new GameObject("Indirect shadow contract sun"); objects.Add(sunObject);
                var sun=sunObject.AddComponent<Light>(); sun.type=LightType.Directional; sun.shadows=LightShadows.Hard; sun.intensity=1.2f; sun.cullingMask=1<<31;
                sun.transform.rotation=Quaternion.Euler(45,-30,0); RenderSettings.sun=sun;
                for(int i=0;i<2;i++)
                {
                    var cameraObject=new GameObject("Indirect shadow contract view "+i); objects.Add(cameraObject); cameras[i]=cameraObject.AddComponent<Camera>();
                    cameras[i].transform.position=new Vector3(0,3,i==0?-6:6); cameras[i].transform.LookAt(new Vector3(0,.5f,0)); cameras[i].cullingMask=1<<31;
                    cameras[i].clearFlags=CameraClearFlags.SolidColor; cameras[i].backgroundColor=Color.black; cameras[i].allowMSAA=false;
                    targets[i]=new RenderTexture(128,128,24); targets[i].Create(); cameras[i].targetTexture=targets[i];
                }
                for(int i=0;i<4;i++) yield return null;
                var ordinary=new[] {Pixels(targets[0]),Pixels(targets[1])}; renderer.forceRenderingOff=true;
                for(int i=0;i<3;i++) yield return null;
                Assert.That(Differences(ordinary[0],Pixels(targets[0])),Is.GreaterThan(0),"Ordinary fixture must show a real shadow.");
                var record=new OcclusionGpuInstanceData { LocalToWorld=caster.transform.localToWorldMatrix,Color=new float4(1) };
                stand=new Stand(caster.GetComponent<MeshFilter>().sharedMesh,resources,cameras,new[] {record,record}) {CastShadows=true};
                stand.Masks[0][0]=stand.Masks[0][1]=stand.Masks[1][0]=stand.Masks[1][1]=0;
                foreach(bool reverse in new[] {false,true})
                {
                    cameras[0].depth=reverse?101:100; cameras[1].depth=reverse?100:101;
                    for(int i=0;i<4;i++) yield return null;
                    for(int i=0;i<2;i++) Assert.That(Differences(ordinary[i],Pixels(targets[i])),Is.Zero,"Independent full-shadow arguments, view="+i+", reverse="+reverse);
                }
            }
            finally
            {
                stand?.Dispose(); OcclusionGpuRetirement.DrainForShutdown();
                for(int i=0;i<2;i++) { if(cameras[i]!=null)cameras[i].targetTexture=null; if(targets[i]!=null){targets[i].Release();UnityEngine.Object.Destroy(targets[i]);} }
                foreach(var item in objects) if(item!=null){item.SetActive(false);UnityEngine.Object.Destroy(item);} foreach(var item in materials) UnityEngine.Object.Destroy(item);
                foreach(var pair in lights)if(pair.Key!=null)pair.Key.enabled=pair.Value;
                RenderSettings.sun=previousSun; RenderSettings.ambientMode=ambientMode; RenderSettings.ambientLight=ambient;
            }
            #else
            throw new NotSupportedException("This Editor fixture requires a serialized Player host for Player runs.");
            #endif
        }
        [UnityTest] public IEnumerator CompactedGroupOffsetsAndModelColorsStayIndependentAcrossViews()
        {
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            Assert.That(resources.Indirect,Is.Not.Null); Assert.That(resources.IndirectArguments,Is.Not.Null);
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var cameras=new Camera[2]; var targets=new RenderTexture[2]; Stand stand=null;
            try
            {
                for (int i=0;i<2;i++)
                {
                    cameras[i]=new GameObject("Indirect color contract view "+i).AddComponent<Camera>();
                    cameras[i].transform.position=new Vector3(0,0,-5); cameras[i].orthographic=true; cameras[i].orthographicSize=1.5f;
                    cameras[i].cullingMask=1<<31; cameras[i].depth=100+i; cameras[i].clearFlags=CameraClearFlags.SolidColor; cameras[i].backgroundColor=Color.black;
                    cameras[i].allowMSAA=false; targets[i]=new RenderTexture(96,48,24); targets[i].Create(); cameras[i].targetTexture=targets[i];
                }
                stand=new Stand(cube.GetComponent<MeshFilter>().sharedMesh,resources,cameras);
                for (int i=0;i<4;i++) yield return null;
                Assert.That(Pixel(targets[0],32).r,Is.GreaterThan(200)); Assert.That(Pixel(targets[0],64).g,Is.LessThan(5));
                Assert.That(Pixel(targets[1],32).r,Is.LessThan(5)); Assert.That(Pixel(targets[1],64).g,Is.GreaterThan(200));
                stand.Masks[0][0]=0; stand.Masks[0][1]=1; stand.Masks[1][0]=1; stand.Masks[1][1]=0;
                for (int i=0;i<3;i++) yield return null;
                Assert.That(Pixel(targets[0],32).r,Is.LessThan(5)); Assert.That(Pixel(targets[0],64).g,Is.GreaterThan(200));
                Assert.That(Pixel(targets[1],32).r,Is.GreaterThan(200)); Assert.That(Pixel(targets[1],64).g,Is.LessThan(5));
                stand.Dispose(); for (int i=0;i<120 && !stand.Released;i++) yield return null;
                Assert.That(stand.Released,Is.True);
            }
            finally
            {
                stand?.Dispose(); OcclusionGpuRetirement.DrainForShutdown();
                for (int i=0;i<2;i++) { if (cameras[i]!=null) { cameras[i].targetTexture=null; UnityEngine.Object.Destroy(cameras[i].gameObject); }
                    if (targets[i]!=null) { targets[i].Release(); UnityEngine.Object.Destroy(targets[i]); } }
                UnityEngine.Object.Destroy(cube);
            }
            #else
            throw new NotSupportedException("This Editor fixture needs a serialized Player host for Player runs.");
            #endif
        }
    }
}
