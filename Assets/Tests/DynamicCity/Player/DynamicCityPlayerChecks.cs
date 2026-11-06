using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Samples.DynamicCity;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.DynamicCity.PlayerChecks
{
    public sealed class DynamicCityPlayerChecks : MonoBehaviour
    {
        [Serializable] private sealed class Case
        {public int Scenario,ReferenceIds,LostIds,InteriorErrors,ChangedPixels;public string View;}
        [Serializable] private sealed class Report
        {
            public string Revision,Unity,Api,Gpu,Cpu,Failure;
            public bool Passed;
            public int InitialAgents,InitialPieces,InitialInteriorDetails,Reloads;
            public int GpuFrames,FallbackFrames;
            public List<Case> Cases=new List<Case>();
        }
        private readonly Report report=new Report();
        private string output;
        private bool completed;
        private readonly List<string> errors=new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void StartIfRequested()
        {
            if(!Environment.GetCommandLineArgs().Contains("--validate-dynamic-city"))return;
            var host=new GameObject("Dynamic City Player validation");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<DynamicCityPlayerChecks>();
        }
        private static string Argument(string name,string fallback)
        {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length?args[index+1]:fallback;}
        private void Start()
        {
            Application.runInBackground=true;output=Path.GetFullPath(Argument("--validation-output",Path.Combine(Application.persistentDataPath,"DynamicCity-validation.json")));
            Directory.CreateDirectory(Path.GetDirectoryName(output));report.Revision=Argument("--source-revision","unspecified");
            report.Unity=Application.unityVersion;report.Api=SystemInfo.graphicsDeviceType.ToString();report.Gpu=SystemInfo.graphicsDeviceName;report.Cpu=SystemInfo.processorType;
            Application.logMessageReceived+=Log;StartCoroutine(Drive());
        }
        private void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
        private IEnumerator Drive()
        {
            var run=Validate();
            while(true)
            {
                object current=null;bool next=false;
                try{next=run.MoveNext();if(next)current=run.Current;}
                catch(Exception exception){report.Failure=exception.ToString();break;}
                if(!next){report.Passed=errors.Count==0;if(!report.Passed)report.Failure=string.Join("\n",errors);break;}
                yield return current;
            }
            completed=true;File.WriteAllText(output,JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;
            Application.Quit(report.Passed?0:1);
        }
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        private IEnumerator Validate()
        {
            DynamicCityBootstrap sample=null;
            for(int i=0;i<120;i++){sample=UnityEngine.Object.FindFirstObjectByType<DynamicCityBootstrap>();if(sample!=null && sample.Initialized)break;yield return null;}
            Require(sample!=null && sample.Initialized,"Sample initialization failed: "+sample?.Fault);
            report.InitialAgents=sample.Agents.Length;report.InitialPieces=sample.Construction.Pieces.Count;
            report.InitialInteriorDetails=sample.InteriorDetails.Length;
            Require(report.InitialAgents==512 && report.InitialPieces>600,"The shipped scene must construct the full default workload.");
            Require(!sample.AutomaticEdits && !sample.View.GetComponent<DynamicCityCamera>().Tour && report.InitialInteriorDetails>0,"The normal demo must start with stable buildings, interior details and a stationary camera.");
            sample.ShowHud=false;sample.AutomaticEdits=false;sample.View.GetComponent<DynamicCityCamera>().Tour=false;
            sample.PauseSimulation(true);var target=new RenderTexture(320,240,24);target.Create();sample.View.targetTexture=target;
            int renderedFrame=-1;bool renderedGpu=false;Camera outside=null;RenderTexture outsideTarget=null;
            Action<ScriptableRenderContext,List<Camera>> observed=(context,views)=>
            {
                if(sample.View==null || !views.Contains(sample.View))return;
                renderedFrame=Time.frameCount;renderedGpu=sample.Presentation.ReadyInstances>0 && sample.Presentation.ViewFallback(sample.View)==OcclusionFallbackReason.None;
                if(renderedGpu)report.GpuFrames++;else report.FallbackFrames++;
            };
            RenderPipelineManager.endContextRendering+=observed;
            try
            {
                int first=Time.frameCount;
                for(int i=0;i<120 && (renderedFrame<first || !renderedGpu);i++)yield return null;
                Require(renderedFrame>=first && renderedGpu,sample.Presentation.LastFault??"No rendered GPU frame.");
                Capture(target,"normal-city.png");
                var home=(Vector3)DynamicCityBootstrap.Home(0);var controller=sample.View.GetComponent<DynamicCityCamera>();
                for(int floorIndex=0;floorIndex<2;floorIndex++)
                {
                    controller.SetPose(home+new Vector3(2,1.7f+floorIndex*3.25f,-3.5f),home+new Vector3(4,1.4f+floorIndex*3.25f,3));
                    int begin=Time.frameCount+1;for(int i=0;i<120 && (renderedFrame<begin || !renderedGpu);i++)yield return null;
                    Require(renderedFrame>=begin && renderedGpu,"Workshop preview did not render.");Capture(target,floorIndex==0?"ground-workshop.png":"upper-workshop.png");
                }
                outsideTarget=new RenderTexture(320,240,24);outsideTarget.Create();outside=new GameObject("Owned Player outside reference").AddComponent<Camera>();
                outside.CopyFrom(sample.View);outside.targetTexture=outsideTarget;outside.transform.position=home+new Vector3(15,5,15);outside.transform.LookAt(home+new Vector3(0,2,0));sample.Presentation.RegisterView(outside);
                int ids=0;var manager=sample.World.EntityManager;
                using(var query=manager.CreateEntityQuery(typeof(DynamicOcclusionBinding)))
                using(var entities=query.ToEntityArray(Allocator.Temp))foreach(var entity in entities)
                {
                    int id=++ids;var encoded=new Color((id&255)/255f,((id>>8)&255)/255f,((id>>16)&255)/255f,1);
                    var color=QualitySettings.activeColorSpace==ColorSpace.Linear?encoded.linear:encoded;
                    float4 value=new float4(color.r,color.g,color.b,1);manager.AddComponentData(entity,new URPMaterialPropertyBaseColor{Value=value});
                    var binding=manager.GetComponentData<DynamicOcclusionBinding>(entity);binding.Color=value;manager.SetComponentData(entity,binding);
                }
                var door=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Door);
                var wall=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Wall);var floor=sample.Construction.Pieces.First(p=>p.Kind==PieceKind.Floor);
                for(int scenario=0;scenario<10;scenario++)
                {
                    if(scenario==1)Edit(sample,door,ConstructionOperation.OpenDoor);
                    if(scenario==2)Edit(sample,wall,ConstructionOperation.Remove);
                    if(scenario==3)Edit(sample,floor,ConstructionOperation.Remove);
                    if(scenario==7){sample.PauseSimulation(false);sample.Advance(.75f);sample.PauseSimulation(true);}
                    sample.View.transform.position=home+(scenario<4?new Vector3(-1.5f,1.7f,-10):scenario==4?new Vector3(-1.5f,1.7f,-5.9f):scenario==5?new Vector3(2,1.7f,2):new Vector3(15,5,15));
                    sample.View.transform.LookAt(home+new Vector3(-1.5f,1.7f,0));
                    if(scenario>=8)
                    {
                        Edit(sample,wall,ConstructionOperation.Place);sample.View.transform.position=wall.Bounds.center+(scenario==8?Vector3.zero:Vector3.forward*.14f);
                        sample.View.transform.LookAt(home+new Vector3(0,1.7f,0));
                        if(scenario==8)Require(wall.Bounds.Contains(sample.View.transform.position),"Inside-wall case did not enter a real wall.");
                    }
                    sample.SetFeatureEnabled(false);for(int i=0;i<4;i++)yield return null;var reference=Capture(target,"reference-"+scenario+"-primary.png");
                    var outsideReference=Capture(outsideTarget,"reference-"+scenario+"-outside.png");
                    sample.SetFeatureEnabled(true);int begin=Time.frameCount;
                    for(int i=0;i<120 && (renderedFrame<begin || !renderedGpu);i++)yield return null;
                    var actual=Capture(target,"culled-"+scenario+"-primary.png");var outsideActual=Capture(outsideTarget,"culled-"+scenario+"-outside.png");
                    Require(renderedFrame>=begin && renderedGpu,
                        "The Player must use real GPU occlusion. scenario="+scenario+", ready="+sample.Presentation.ReadyInstances+", view="+sample.Presentation.ViewFallback(sample.View)+
                        ", fault="+sample.Presentation.LastFault+", frame="+Time.frameCount+", size="+sample.View.pixelWidth+"x"+sample.View.pixelHeight+
                        ", scaled="+sample.View.scaledPixelWidth+"x"+sample.View.scaledPixelHeight+", equalProjection="+sample.View.projectionMatrix.Equals(sample.View.nonJitteredProjectionMatrix));
                    Require(sample.Presentation.ViewFallback(outside)==OcclusionFallbackReason.None,"Outside view did not exercise GPU occlusion.");
                    Record(scenario,"primary",reference,actual,target.width,target.height,ids);Record(scenario,"outside",outsideReference,outsideActual,outsideTarget.width,outsideTarget.height,ids);
                }
                sample.Presentation.UnregisterView(outside);outside.enabled=false;outside.targetTexture=null;UnityEngine.Object.Destroy(outside.gameObject);outside=null;
                var agent=sample.Agents[0];uint tick=manager.GetComponentData<DynamicCityAgent>(agent).Tick;
                sample.SetFeatureEnabled(false);sample.PauseSimulation(false);sample.Advance(.25f);
                Require(manager.GetComponentData<DynamicCityAgent>(agent).Tick>tick && manager.IsEnabled(agent),"Rendering disabled simulation.");
                for(int reload=0;reload<3;reload++)
                {
                    sample.View.targetTexture=null;sample.Shutdown();for(int i=0;i<8;i++)yield return null;
                    sample.Initialize();sample.AutomaticEdits=false;sample.PauseSimulation(true);sample.View.GetComponent<DynamicCityCamera>().Tour=false;
                    sample.View.targetTexture=target;sample.SetFeatureEnabled(true);int restart=Time.frameCount;
                    for(int i=0;i<120 && (renderedFrame<restart || !renderedGpu);i++)yield return null;
                    Require(renderedFrame>=restart && renderedGpu && sample.World.IsCreated,"World replacement lost the renderer.");report.Reloads++;
                }
            }
            finally
            {
                RenderPipelineManager.endContextRendering-=observed;if(sample!=null && sample.View!=null)sample.View.targetTexture=null;
                if(outside!=null){outside.targetTexture=null;UnityEngine.Object.Destroy(outside.gameObject);}
                if(outsideTarget!=null){outsideTarget.Release();UnityEngine.Object.Destroy(outsideTarget);}target.Release();UnityEngine.Object.Destroy(target);
            }
            var finalRegistry=sample.Culling.Registry;sample.Shutdown();for(int i=0;i<12;i++)yield return null;
            Require(finalRegistry.AccountedResourceBytes==0,"World teardown retained accounted payloads.");
            Require(errors.Count==0,string.Join("\n",errors));
        }
        private static int Id(Color32 pixel)=>(int)pixel.r|((int)pixel.g<<8)|((int)pixel.b<<16);
        private void Record(int scenario,string view,Color32[] reference,Color32[] actual,int width,int height,int ids)
        {
            var expectedIds=reference.Select(Id).Where(id=>id>0 && id<=ids).Distinct().ToArray();var actualIds=actual.Select(Id).ToHashSet();
            var result=new Case{Scenario=scenario,View=view,ReferenceIds=expectedIds.Length,LostIds=expectedIds.Count(id=>!actualIds.Contains(id))};
            for(int y=1;y<height-1;y++)for(int x=1;x<width-1;x++)
            {
                int index=y*width+x;if(Same(reference[index],actual[index]))continue;result.ChangedPixels++;
                bool interior=true;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)interior&=Same(reference[index],reference[index+dy*width+dx]);
                if(interior)result.InteriorErrors++;
            }
            report.Cases.Add(result);Require(result.ReferenceIds>1 && result.LostIds==0 && result.InteriorErrors==0,"Image/ID oracle failed in scenario "+scenario+", view="+view);
        }
        private static bool Same(Color32 a,Color32 b)=>Math.Abs(a.r-b.r)<=1 && Math.Abs(a.g-b.g)<=1 && Math.Abs(a.b-b.b)<=1;
        private static void Edit(DynamicCityBootstrap sample,DynamicCityPiece piece,ConstructionOperation operation)
        {Require(sample.Construction.Apply(new ConstructionCommand{PieceId=piece.Id,Operation=operation,Tick=sample.Construction.LastTick+1}),"Construction command rejected.");}
        private Color32[] Capture(RenderTexture target,string name)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),name),image.EncodeToPNG());return image.GetPixels32();}
            finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
        }
        private void OnApplicationQuit(){if(!completed && output!=null){report.Failure="Player quit before validation completed.";File.WriteAllText(output,JsonUtility.ToJson(report,true));}}
    }
}
