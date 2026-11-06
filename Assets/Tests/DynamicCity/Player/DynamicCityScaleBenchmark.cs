using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Samples.DynamicCity;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.DynamicCity.PlayerChecks
{
    public struct ScaleMotion : IComponentData {public float3 Origin;public float Phase;}
    [DisableAutoCreation]
    public partial class ScaleMotionSystem : SystemBase
    {
        protected override void OnUpdate(){Dependency=new Move{Time=(float)World.Time.ElapsedTime}.ScheduleParallel(Dependency);}
        public void ResetPositions(){EntityManager.CompleteAllTrackedJobs();Dependency.Complete();new Move{Time=0}.Run();}
        [BurstCompile] private partial struct Move : IJobEntity
        {
            public float Time;
            private void Execute(ref LocalToWorld transform,in ScaleMotion motion)
            {
                var p=motion.Origin;p.x+=math.sin(Time*.7f+motion.Phase)*.05f;p.y+=math.cos(Time*.8f+motion.Phase)*.05f;
                transform.Value=float4x4.TRS(p,quaternion.identity,new float3(.8f));
            }
        }
    }

    // Separate from the normal city. Every target is in the reference camera's frustum.
    public sealed class DynamicCityScaleBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Distribution {public double Median=-1,P95=-1,P99=-1;public int Available;}
        [Serializable] private sealed class Result
        {
            public int Index,Targets,MovingTargets,FrustumTargets,ReferenceIds,GpuReferenceIds,LostIds,Warmup,Frames,SimulationFrames,OrdinaryEnabledTargets;
            public int MinReady=int.MaxValue,MaxReady,MaxPending,OverflowFrames,GpuFrames,FallbackFrames,Collections;
            public bool Enabled;public string Layout;
            public long ReferenceTriangles,MaxAccountedBytes,MaxManagedBytes,MaxUploadBytes;
            public uint StateHash;
            public Distribution Total,Main,Render,Gpu,Allocations,DrawCalls,Triangles;
        }
        [Serializable] private sealed class Report
        {
            public string Revision,HarnessRevision,Unity,Api,Gpu,Cpu,Failure,Status;
            public bool Complete;public int Width=2560,Height=1440;
            public List<Result> Results=new List<Result>();
        }
        private readonly Report report=new Report();
        private readonly List<string> errors=new List<string>();
        private readonly List<OcclusionWorld> retired=new List<OcclusionWorld>();
        private DynamicCityBootstrap sample;
        private InitializationSystemGroup initialization;
        private SimulationSystemGroup simulation;
        private PresentationSystemGroup presentation;
        private ScaleMotionSystem motion;
        private NativeArray<Entity> entities;
        private Entity prototype,wall;
        private GeometryHandle geometry;
        private RenderTexture target;
        private string directory,status="Starting scale validation";
        private int count,columns,rows,frame,rendered=-1,gpuFrames,fallbackFrames;
        private bool drive,measuring,completed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void StartIfRequested()
        {
            if(!Environment.GetCommandLineArgs().Contains("--scale-dynamic-city"))return;
            var host=new GameObject("Full-frustum scale benchmark");DontDestroyOnLoad(host);host.AddComponent<DynamicCityScaleBenchmark>();
        }
        private static string Arg(string key,string fallback)
        {var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,key);return at>=0 && at+1<args.Length?args[at+1]:fallback;}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private void Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
            directory=Path.GetFullPath(Arg("--scale-output",Path.Combine(Application.persistentDataPath,"DynamicCity-scale")));Directory.CreateDirectory(directory);
            report.Revision=Arg("--source-revision","unspecified");report.HarnessRevision=Arg("--harness-revision","development");report.Unity=Application.unityVersion;
            report.Api=SystemInfo.graphicsDeviceType.ToString();report.Gpu=SystemInfo.graphicsDeviceName;report.Cpu=SystemInfo.processorType;
            Application.logMessageReceived+=Log;RenderPipelineManager.endContextRendering+=Rendered;StartCoroutine(Driver());
        }
        private void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
        private IEnumerator Driver()
        {
            var run=Run();
            while(true)
            {
                bool next=false;object current=null;
                try{next=run.MoveNext();if(next)current=run.Current;}
                catch(Exception error){report.Failure=error.ToString();break;}
                if(!next){report.Complete=errors.Count==0;if(errors.Count>0)report.Failure=string.Join("\n",errors);break;}
                yield return current;
            }
            Cleanup();completed=true;report.Status=report.Complete?"complete":"failed";Save();
            Application.logMessageReceived-=Log;RenderPipelineManager.endContextRendering-=Rendered;Application.Quit(report.Complete?0:1);
        }
        private IEnumerator Run()
        {
            for(int i=0;i<180;i++){sample=FindAnyObjectByType<DynamicCityBootstrap>();if(sample!=null && sample.Initialized)break;yield return null;}
            Require(sample!=null && sample.Initialized,"The imported sample did not initialize.");
            int index=0,skip=int.Parse(Arg("--scale-skip","0"),CultureInfo.InvariantCulture),limit=int.Parse(Arg("--scale-limit","24"),CultureInfo.InvariantCulture);
            var populations=Arg("--scale-counts","4096,40960,409600").Split(',').Select(int.Parse).ToArray();
            foreach(int population in populations)
            {
                Require(population==4096 || population==40960 || population==409600,"Use one of the three declared scale populations.");
                if(index+8<=skip){index+=8;continue;}
                var setup=Setup(population);while(setup.MoveNext())yield return setup.Current;
                foreach(bool occluded in new[]{false,true})
                {
                    if(index+4<=skip){index+=4;continue;}
                    SetWall(occluded);var proof=new Result{Targets=count,Layout=occluded?"occluded":"open"};
                    var checking=ProveVisibility(proof);while(checking.MoveNext())yield return checking.Current;
                    if(Environment.GetCommandLineArgs().Contains("--scale-proof-only"))
                    {motion.Enabled=true;for(int i=0;i<8;i++)yield return null;motion.Enabled=false;continue;}
                    foreach(bool moving in new[]{false,true})foreach(bool enabled in new[]{false,true})
                    {
                        int current=index++;if(current<skip)continue;if(current>=skip+limit){Cleanup();goto Finished;}
                        var result=new Result{Index=current,Targets=count,MovingTargets=moving?count:0,Enabled=enabled,Layout=proof.Layout,
                            FrustumTargets=proof.FrustumTargets,ReferenceIds=proof.ReferenceIds,GpuReferenceIds=proof.GpuReferenceIds,LostIds=proof.LostIds,ReferenceTriangles=proof.ReferenceTriangles,OrdinaryEnabledTargets=proof.OrdinaryEnabledTargets};
                        var measure=Measure(result);while(measure.MoveNext())yield return measure.Current;
                        report.Results.Add(result);Save();Require(errors.Count==0,string.Join("\n",errors));
                    }
                }
                Cleanup();for(int i=0;i<120 && retired.Any(r=>r.AccountedResourceBytes!=0);i++)yield return null;
                Require(retired.All(r=>r.AccountedResourceBytes==0),"Completed population retained GPU/native payload.");retired.Clear();
            }
            Finished:
            for(int i=0;i<120 && retired.Any(r=>r.AccountedResourceBytes!=0);i++)yield return null;
            Require(retired.All(r=>r.AccountedResourceBytes==0),"Scale shutdown retained GPU/native payload.");
        }
        private IEnumerator Setup(int population)
        {
            Cleanup();count=population;columns=(int)Math.Ceiling(Math.Sqrt(count));rows=(count+columns-1)/columns;
            sample.AgentCount=sample.BuildingCount=0;sample.IncludeRotatedFixture=sample.IncludeInteriorDetails=false;sample.AutomaticEdits=false;sample.ShowHud=false;
            sample.GroundMaterial=null;sample.SetFeatureEnabled(false);
            sample.Settings=new OcclusionSettings{MaxInstances=count+1024,MaxQueuedUpdates=count*2+4096,MaxBlockMemberships=count*4+4096,
                MaxUploadBytesPerFrame=128*1024*1024,MaxResourceBytes=2L*1024*1024*1024};
            sample.Initialize();ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(sample.World);sample.View.GetComponent<DynamicCityCamera>().enabled=false;
            target=new RenderTexture(report.Width,report.Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=1};target.Create();sample.View.targetTexture=target;
            sample.View.orthographic=true;sample.View.orthographicSize=rows*.5f+2;sample.View.aspect=(float)report.Width/report.Height;
            sample.View.transform.position=new Vector3(0,0,-50);sample.View.transform.rotation=Quaternion.identity;sample.View.nearClipPlane=.1f;sample.View.farClipPlane=100;
            sample.View.backgroundColor=Color.black;sample.View.useOcclusionCulling=false;
            initialization=sample.World.GetExistingSystemManaged<InitializationSystemGroup>();simulation=sample.World.GetExistingSystemManaged<SimulationSystemGroup>();presentation=sample.World.GetExistingSystemManaged<PresentationSystemGroup>();
            motion=sample.World.GetOrCreateSystemManaged<ScaleMotionSystem>();simulation.AddSystemToUpdateList(motion);simulation.SortSystems();motion.Enabled=false;
            var manager=sample.World.EntityManager;var meshes=new RenderMeshArray(new[]{sample.StockPalette[0]},new[]{sample.CubeMesh});prototype=manager.CreateEntity();
            RenderMeshUtility.AddComponents(prototype,manager,new RenderMeshDescription(ShadowCastingMode.Off,receiveShadows:false,layer:sample.Layer),meshes,MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
            manager.AddComponent<Prefab>(prototype);manager.AddComponentData(prototype,new ScaleMotion());manager.AddComponentData(prototype,new URPMaterialPropertyBaseColor{Value=new float4(1)});
            geometry=sample.Culling.Registry.RegisterGeometry(sample.CubeMesh);entities=new NativeArray<Entity>(count,Allocator.Persistent);
            for(int start=0;start<count;start+=4096)
            {
                int length=Math.Min(4096,count-start);manager.Instantiate(prototype,entities.GetSubArray(start,length));
                for(int j=0;j<length;j++)
                {
                    int at=start+j;var origin=new float3(at%columns-(columns-1)*.5f,at/columns-(rows-1)*.5f,0);var pose=float4x4.TRS(origin,quaternion.identity,new float3(.8f));
                    var tint=Encode(at+1);manager.SetComponentData(entities[at],new LocalToWorld{Value=pose});manager.SetComponentData(entities[at],new ScaleMotion{Origin=origin,Phase=at*.123456f});
                    manager.SetComponentData(entities[at],new URPMaterialPropertyBaseColor{Value=tint});Bind(entities[at],tint,OcclusionRole.Renderable|OcclusionRole.Occludee);
                }
                status="Scale fixture: preparing "+(start+length)+" / "+count+" objects";report.Status=status;Save();yield return null;
            }
            motion.ResetPositions();frame=0;drive=true;
        }
        private static float4 Encode(int id)
        {var encoded=new Color((id&255)/255f,((id>>8)&255)/255f,((id>>16)&255)/255f,1);var value=QualitySettings.activeColorSpace==ColorSpace.Linear?encoded.linear:encoded;return new float4(value.r,value.g,value.b,1);}
        private void Bind(Entity entity,float4 color,OcclusionRole roles)
        {
            sample.Culling.Bindings.Bind(entity,new OcclusionInstanceDescriptor{Geometry=geometry,Material=sample.IndirectMaterial,Layer=sample.Layer,Shadows=ShadowCastingMode.Off,
                State=new OcclusionInstanceState{Roles=roles,Opaque=true,Color=color}});
        }
        private void SetWall(bool occluded)
        {
            sample.SetFeatureEnabled(false);motion.Enabled=false;motion.ResetPositions();
            var manager=sample.World.EntityManager;
            if(wall!=Entity.Null){sample.Culling.Bindings.Unbind(wall);manager.DestroyEntity(wall);wall=Entity.Null;}
            if(!occluded)return;wall=manager.Instantiate(prototype);manager.RemoveComponent<ScaleMotion>(wall);
            manager.SetComponentData(wall,new LocalToWorld{Value=float4x4.TRS(new float3(-columns*.175f,0,-2),quaternion.identity,new float3(columns*.65f,rows+4,.5f))});
            manager.SetComponentData(wall,new URPMaterialPropertyBaseColor{Value=new float4(1)});Bind(wall,new float4(1),OcclusionRole.Renderable|OcclusionRole.Occludee|OcclusionRole.Occluder);
        }
        private IEnumerator ProveVisibility(Result proof)
        {
            status="Checking actual rendered IDs: "+count+", "+proof.Layout;report.Status=status;Save();motion.Enabled=false;drive=true;
            var planes=GeometryUtility.CalculateFrustumPlanes(sample.View);var manager=sample.World.EntityManager;manager.CompleteDependencyBeforeRO<LocalToWorld>();
            foreach(var entity in entities)if(GeometryUtility.TestPlanesAABB(planes,new Bounds(manager.GetComponentData<LocalToWorld>(entity).Value.c3.xyz,Vector3.one*.8f)))proof.FrustumTargets++;
            Require(proof.FrustumTargets==count,"Some requested targets are outside the reference frustum.");
            using(var triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1))
            {
                sample.SetFeatureEnabled(false);int begin=Time.frameCount+1,stable=0,lastSample=-1;
                for(int i=0;i<600 && (i<12 || stable<3);i++)
                {
                    yield return null;
                    if(rendered<begin || rendered==lastSample)continue;lastSample=rendered;
                    stable=triangles.Valid && triangles.LastValue>=count*12L?stable+1:0;
                }
                Require(rendered>=begin,"The reference camera did not render.");
                proof.ReferenceTriangles=triangles.Valid?triangles.LastValue:-1;
                var reference=ReadIds();proof.ReferenceIds=reference.Count(v=>v);
                foreach(var entity in entities)if(manager.IsComponentEnabled<MaterialMeshInfo>(entity))proof.OrdinaryEnabledTargets++;
                File.WriteAllText(Path.Combine(directory,"reference-proof-"+count+"-"+proof.Layout+".json"),JsonUtility.ToJson(proof,true));
                Require(proof.ReferenceTriangles>=count*12L,"Ordinary triangles="+proof.ReferenceTriangles+", expected at least "+count*12L+", enabled="+proof.OrdinaryEnabledTargets+", visible IDs="+proof.ReferenceIds);
                Require(proof.OrdinaryEnabledTargets==count,"Ordinary reference retained disabled target bindings.");
                if(proof.Layout=="open")Require(proof.ReferenceIds==count,"The open reference did not rasterize every requested target ID.");
                else
                {
                    int expected=0;foreach(var entity in entities)if(manager.GetComponentData<LocalToWorld>(entity).Value.c3.x+.4f>columns*.15f+.01f)expected++;
                    Require(proof.ReferenceIds==expected,"The ordinary occluded reference is missing analytically exposed grid targets: "+proof.ReferenceIds+" / "+expected);
                }
                sample.SetFeatureEnabled(true);begin=Time.frameCount+1;
                for(int i=0;i<120 && (rendered<begin || sample.Presentation.ReadyInstances!=count+(wall==Entity.Null?0:1));i++)yield return null;
                Require(sample.Presentation.ReadyInstances==count+(wall==Entity.Null?0:1),"GPU transfer covered only part of the requested population: "+sample.Presentation.ReadyInstances+" / "+count+"; "+sample.Presentation.LastFault);
                Require(sample.Presentation.ViewFallback(sample.View)==OcclusionFallbackReason.None,"Scale proof fell back to ordinary rendering.");
                var actual=ReadIds();proof.GpuReferenceIds=actual.Count(v=>v);for(int i=0;i<count;i++)if(reference[i] && !actual[i])proof.LostIds++;
                Require(proof.LostIds==0,"GPU scale proof lost reference-visible IDs: "+proof.LostIds);
                File.WriteAllText(Path.Combine(directory,"proof-"+count+"-"+proof.Layout+".json"),JsonUtility.ToJson(proof,true));
            }
        }
        private bool[] ReadIds()
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);var ids=new bool[count];
            try
            {
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetRawTextureData<Color32>();
                foreach(var pixel in pixels){int id=pixel.r|(pixel.g<<8)|(pixel.b<<16);if(id>0 && id<=count)ids[id-1]=true;}return ids;
            }
            finally{RenderTexture.active=previous;Destroy(image);}
        }
        private IEnumerator Measure(Result result)
        {
            drive=false;sample.SetFeatureEnabled(false);motion.Enabled=false;motion.ResetPositions();frame=0;motion.Enabled=result.MovingTargets>0;sample.SetFeatureEnabled(result.Enabled);
            result.Warmup=count>=409600?60:300;result.Frames=count>=409600?300:1800;
            status="SCALE "+count+" | "+result.Layout+" | "+(result.MovingTargets>0?"all moving":"static")+" | "+(result.Enabled?"GPU occlusion ON":"ordinary rendering")+" | case "+result.Index;
            report.Status=status;Save();yield return new WaitForEndOfFrame();drive=true;gpuFrames=fallbackFrames=0;
            var total=new double[result.Frames];var main=new double[result.Frames];var render=new double[result.Frames];var gpu=new double[result.Frames];var gcBytes=new double[result.Frames];var draws=new double[result.Frames];var tris=new double[result.Frames];var timings=new FrameTiming[1];
            using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
            using(var draw=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1))
            using(var triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1))
            {
                bool gcAvailable=false,drawAvailable=false,triAvailable=false;int before=Collections();long previous=System.Diagnostics.Stopwatch.GetTimestamp();
                for(int i=0;i<result.Warmup+result.Frames;i++)
                {
                    measuring=i>=result.Warmup;FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
                    Require(frame==i+1,"Scale simulation/render phase drifted.");long now=System.Diagnostics.Stopwatch.GetTimestamp();double elapsed=(now-previous)*1000.0/System.Diagnostics.Stopwatch.Frequency;previous=now;
                    if(i==5){var probe=new byte[8192];GC.KeepAlive(probe);}gcAvailable|=gc.Valid && gc.LastValue>0;drawAvailable|=draw.Valid && draw.LastValue>0;triAvailable|=triangles.Valid && triangles.LastValue>0;
                    if(i<result.Warmup)continue;int at=i-result.Warmup;total[at]=elapsed;bool available=FrameTimingManager.GetLatestTimings(1,timings)>0;var timing=timings[0];
                    main[at]=available && timing.cpuMainThreadFrameTime>0?timing.cpuMainThreadFrameTime:-1;render[at]=available && timing.cpuRenderThreadFrameTime>0?timing.cpuRenderThreadFrameTime:-1;gpu[at]=available && timing.gpuFrameTime>0?timing.gpuFrameTime:-1;
                    gcBytes[at]=gcAvailable?gc.LastValue:-1;draws[at]=drawAvailable?draw.LastValue:-1;tris[at]=triAvailable?triangles.LastValue:-1;
                    int ready=sample.Presentation.ReadyInstances;result.MinReady=Math.Min(result.MinReady,ready);result.MaxReady=Math.Max(result.MaxReady,ready);
                    if(result.Enabled)Require(ready==count+(wall==Entity.Null?0:1),"A measured GPU frame transferred only part of the population.");
                    result.MaxPending=Math.Max(result.MaxPending,sample.Culling.Registry.PendingUpdateCount);if(sample.Culling.Registry.QueueOverflowed)result.OverflowFrames++;
                    result.MaxAccountedBytes=Math.Max(result.MaxAccountedBytes,sample.Presentation.AccountedResourceBytes);result.MaxManagedBytes=Math.Max(result.MaxManagedBytes,GC.GetTotalMemory(false));result.MaxUploadBytes=Math.Max(result.MaxUploadBytes,sample.Presentation.UploadedBytes);
                }
                result.Collections=Collections()-before;
            }
            measuring=false;drive=false;result.SimulationFrames=frame;result.GpuFrames=gpuFrames;result.FallbackFrames=fallbackFrames;
            var manager=sample.World.EntityManager;manager.CompleteDependencyBeforeRO<LocalToWorld>();uint hash=2166136261;
            foreach(var entity in entities){unchecked{hash=(hash^math.hash(manager.GetComponentData<LocalToWorld>(entity).Value))*16777619;}}result.StateHash=hash;
            if(result.Enabled){var off=report.Results.Last();Require(off.Index==result.Index-1 && off.StateHash==result.StateHash && off.SimulationFrames==result.SimulationFrames,"Scale pair ended in different states.");}
            result.Total=Stats(total);result.Main=Stats(main);result.Render=Stats(render);result.Gpu=Stats(gpu);result.Allocations=Stats(gcBytes);result.DrawCalls=Stats(draws);result.Triangles=Stats(tris);
            var csv=new StringBuilder("frame,total_ms,main_ms,render_ms,gpu_ms,gc_bytes,draw_calls,triangles\n");
            for(int i=0;i<result.Frames;i++){csv.Append(i);foreach(var column in new[]{total,main,render,gpu,gcBytes,draws,tris})csv.Append(',').Append(column[i].ToString("R",CultureInfo.InvariantCulture));csv.Append('\n');}
            File.WriteAllText(Path.Combine(directory,"case-"+result.Index+".csv"),csv.ToString());
        }
        private static int Collections(){int total=0;for(int i=0;i<=GC.MaxGeneration;i++)total+=GC.CollectionCount(i);return total;}
        private static Distribution Stats(double[] values)
        {
            var a=values.Where(v=>v>=0 && !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v=>v).ToArray();var result=new Distribution{Available=a.Length};
            if(a.Length>0){result.Median=a[a.Length/2];result.P95=a[(int)((a.Length-1)*.95)];result.P99=a[(int)((a.Length-1)*.99)];}return result;
        }
        private void Update()
        {
            if(!drive || sample==null || !sample.Initialized)return;
            initialization.Update();sample.World.SetTime(new TimeData(frame/60.0,1f/60));simulation.Update();presentation.Update();frame++;
        }
        private void Rendered(ScriptableRenderContext context,List<Camera> cameras)
        {
            if(sample==null || sample.View==null || !cameras.Contains(sample.View))return;rendered=Time.frameCount;if(!measuring)return;
            if(sample.FeatureEnabled && sample.Presentation.ViewFallback(sample.View)==OcclusionFallbackReason.None && sample.Presentation.ReadyInstances>0)gpuFrames++;else fallbackFrames++;
        }
        private void OnGUI()
        {
            if(target!=null)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),target,ScaleMode.ScaleToFit,false);
            GUI.Box(new Rect(8,8,Screen.width-16,58),status+"\nDevelopment scale test; this is separate from the normal interactive city.");
        }
        private void Cleanup()
        {
            drive=measuring=false;
            if(sample!=null && sample.Initialized){if(sample.View!=null)sample.View.targetTexture=null;retired.Add(sample.Culling.Registry);sample.Shutdown();}
            if(entities.IsCreated)entities.Dispose();prototype=wall=Entity.Null;
            if(target!=null){target.Release();Destroy(target);target=null;}
        }
        private void Save()=>File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));
        private void OnApplicationQuit(){if(!completed && directory!=null){report.Failure="Player quit before scale validation completed.";Save();}}
    }
}
