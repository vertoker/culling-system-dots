using System.Collections;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Mathematics;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Samples.DynamicCity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.DynamicCity.Tests
{
    public class DynamicCityImageTests
    {
        private static Color32[] Capture(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image.GetPixels32();}
            finally{RenderTexture.active=previous;Object.Destroy(image);}
        }
        private static bool Same(Color32 a,Color32 b)=>Mathf.Abs(a.r-b.r)<=1 && Mathf.Abs(a.g-b.g)<=1 && Mathf.Abs(a.b-b.b)<=1;
        private static int InteriorErrors(Color32[] reference,Color32[] actual,int width,int height)
        {
            int errors=0;
            for(int y=1;y<height-1;y++)for(int x=1;x<width-1;x++)
            {
                int i=y*width+x;if(Same(reference[i],actual[i]))continue;
                bool interior=true;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)interior&=Same(reference[i],reference[i+dy*width+dx]);
                if(interior)errors++;
            }
            return errors;
        }
        [UnityTest] public IEnumerator FrozenConstructionAndMovingCameraMatchOrdinaryColorAndShadows()
        {
            #if UNITY_EDITOR
            var scenePath=UnityEditor.AssetDatabase.FindAssets("DynamicCity t:Scene").Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Single(p=>p.EndsWith("/DynamicCity.unity"));
            var lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None);var enabled=lights.Select(l=>l.enabled).ToArray();foreach(var light in lights)light.enabled=false;
            Scene scene=default;RenderTexture target=null,outsideTarget=null;Camera outside=null;DynamicCityBootstrap sample=null;
            try
            {
                scene=UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath,new LoadSceneParameters(LoadSceneMode.Additive));
                for(int i=0;i<5;i++)yield return null;
                sample=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DynamicCityBootstrap>()).Single();
                Assert.That(sample.Initialized,Is.True,sample.Fault);
                Assert.That(sample.AutomaticEdits,Is.False,"The imported scene must start as a stable city.");
                sample.ShowHud=false;sample.AutomaticEdits=false;sample.PauseSimulation(true);
                sample.View.GetComponent<DynamicCityCamera>().Tour=false;
                target=new RenderTexture(160,120,24);target.Create();sample.View.targetTexture=target;
                outsideTarget=new RenderTexture(160,120,24);outsideTarget.Create();outside=new GameObject("Owned simultaneous outside view").AddComponent<Camera>();
                outside.CopyFrom(sample.View);outside.targetTexture=outsideTarget;sample.Presentation.RegisterView(outside);
                sample.SetFeatureEnabled(false);
                int ids=0;var manager=sample.World.EntityManager;
                using(var query=manager.CreateEntityQuery(typeof(DynamicOcclusionBinding)))
                using(var entities=query.ToEntityArray(Allocator.Temp))
                    foreach(var entity in entities)
                    {
                        int id=++ids;var encoded=new Color((id&255)/255f,((id>>8)&255)/255f,((id>>16)&255)/255f,1);
                        var tint=QualitySettings.activeColorSpace==ColorSpace.Linear?encoded.linear:encoded;
                        float4 value=new float4(tint.r,tint.g,tint.b,1);
                        manager.AddComponentData(entity,new URPMaterialPropertyBaseColor {Value=value});
                        var binding=manager.GetComponentData<DynamicOcclusionBinding>(entity);binding.Color=value;manager.SetComponentData(entity,binding);
                    }
                var home=(Vector3)DynamicCityBootstrap.Home(0);var door=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Door);
                var wall=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Wall);var floor=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Floor);
                outside.transform.position=home+new Vector3(15,5,15);outside.transform.LookAt(home+new Vector3(0,2,0));
                for(int scenario=0;scenario<10;scenario++)
                {
                    if(scenario==1)sample.Construction.Apply(new ConstructionCommand{PieceId=door.Id,Operation=ConstructionOperation.OpenDoor,Tick=sample.Construction.LastTick+1});
                    if(scenario==2)sample.Construction.Apply(new ConstructionCommand{PieceId=wall.Id,Operation=ConstructionOperation.Remove,Tick=sample.Construction.LastTick+1});
                    if(scenario==3)sample.Construction.Apply(new ConstructionCommand{PieceId=floor.Id,Operation=ConstructionOperation.Remove,Tick=sample.Construction.LastTick+1});
                    if(scenario==7){sample.PauseSimulation(false);sample.Advance(.75f);sample.PauseSimulation(true);}
                    sample.View.transform.position=home+(scenario<4?new Vector3(-1.5f,1.7f,-10):scenario==4?new Vector3(-1.5f,1.7f,-5.9f):scenario==5?new Vector3(2,1.7f,2):new Vector3(15,5,15));
                    sample.View.transform.LookAt(home+new Vector3(-1.5f,1.7f,0));
                    if(scenario>=8)
                    {
                        sample.Construction.Apply(new ConstructionCommand{PieceId=wall.Id,Operation=ConstructionOperation.Place,Tick=sample.Construction.LastTick+1});
                        sample.View.transform.position=wall.Bounds.center+(scenario==8?Vector3.zero:Vector3.forward*.14f);
                        sample.View.transform.LookAt(home+new Vector3(0,1.7f,0));
                        if(scenario==8)Assert.That(wall.Bounds.Contains(sample.View.transform.position),Is.True,"The regression camera must be inside an existing wall, not a removed piece.");
                    }
                    sample.SetFeatureEnabled(false);for(int i=0;i<3;i++)yield return null;
                    var reference=Capture(target);
                    var outsideReference=Capture(outsideTarget);
                    var visibleIds=reference.Select(p=>(int)p.r|((int)p.g<<8)|((int)p.b<<16)).Where(id=>id>0 && id<=ids).Distinct().ToArray();
                    Assert.That(visibleIds.Length,Is.GreaterThan(1),"The ordinary path must render per-object IDs for this oracle.");
                    sample.SetFeatureEnabled(true);for(int i=0;i<4;i++)yield return null;
                    Assert.That(sample.Presentation.ReadyInstances,Is.GreaterThan(0),sample.Presentation.LastFault);
                    Assert.That(sample.Presentation.ViewFallback(sample.View),Is.EqualTo(CullingSystemDOTS.Dynamic.OcclusionFallbackReason.None),"The oracle must exercise real current-view GPU occlusion.");
                    var actual=Capture(target);
                    var outsideActual=Capture(outsideTarget);
                    var actualIds=actual.Select(p=>(int)p.r|((int)p.g<<8)|((int)p.b<<16)).ToHashSet();
                    foreach(int id in visibleIds)Assert.That(actualIds.Contains(id),Is.True,"Entire reference-visible ID lost: "+id+", scenario="+scenario);
                    Assert.That(InteriorErrors(reference,actual,target.width,target.height),Is.Zero,"Lost reference interior, scenario="+scenario);
                    Assert.That(sample.Presentation.ViewFallback(outside),Is.EqualTo(CullingSystemDOTS.Dynamic.OcclusionFallbackReason.None));
                    Assert.That(InteriorErrors(outsideReference,outsideActual,outsideTarget.width,outsideTarget.height),Is.Zero,"Outside camera changed when the other camera entered geometry, scenario="+scenario);
                    var outsideIds=outsideReference.Select(p=>(int)p.r|((int)p.g<<8)|((int)p.b<<16)).Where(id=>id>0 && id<=ids).Distinct();
                    var outsideActualIds=outsideActual.Select(p=>(int)p.r|((int)p.g<<8)|((int)p.b<<16)).ToHashSet();
                    foreach(int id in outsideIds)Assert.That(outsideActualIds.Contains(id),Is.True,"Outside reference-visible ID lost: "+id+", scenario="+scenario);
                }
            }
            finally
            {
                if(sample!=null){if(sample.View!=null)sample.View.targetTexture=null;sample.Shutdown();}
                if(outside!=null){outside.targetTexture=null;Object.Destroy(outside.gameObject);}
                if(outsideTarget!=null){outsideTarget.Release();Object.Destroy(outsideTarget);}
                if(target!=null){target.Release();Object.Destroy(target);}
                if(scene.IsValid() && scene.isLoaded)SceneManager.UnloadSceneAsync(scene);
                for(int i=0;i<lights.Length;i++)if(lights[i]!=null)lights[i].enabled=enabled[i];
            }
            #else
            throw new System.NotSupportedException("Use the sample Player oracle host for built-Player validation.");
            #endif
        }
    }
}
