using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Samples.DynamicCity;
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
    // Development-only built-Player harness. No assertion readback enters a measured frame.
    public sealed class DynamicCityPlayerBenchmark : MonoBehaviour
    {
        [Serializable] private sealed class Distribution
        {public double Median=-1,P95=-1,P99=-1;public int Available;}
        [Serializable] private sealed class Result
        {
            public int Index,Targets,Views,EditsPerSecond,Warmup=300,Frames=1800,GpuFrames,FallbackFrames,AppliedEdits;
            public string Layout;
            public bool Enabled;
            public Distribution Total,Main,Render,Gpu,Allocations,DrawCalls,Triangles;
            public long MaxAccountedBytes,MaxManagedBytes,MaxUploadBytes;
            public int Collections;
            public int SimulationFrames,MinReadyInstances=int.MaxValue,MaxReadyInstances,MaxPendingUpdates,QueueOverflowFrames;
            public uint CameraTrace,CommandTrace,AgentState,ConstructionState;
        }
        [Serializable] private sealed class Report
        {
            public string Revision,Unity,Api,Gpu,Cpu,Failure,Mode;
            public bool Complete,FrameTimingEnabled;
            public int Seed=12345,Width=1280,Height=720,StressReloads,StressEdits;
            public double StressSeconds;
            public int StressGpuFrames,StressFallbackFrames;
            public List<Result> Results=new List<Result>();
        }
        private sealed class Part {internal Entity Entity;internal float4x4 Pose;}
        private readonly Report report=new Report();
        private readonly List<Camera> cameras=new List<Camera>();
        private readonly List<RenderTexture> targets=new List<RenderTexture>();
        private readonly List<Part> openParts=new List<Part>();
        private readonly List<string> errors=new List<string>();
        private readonly List<CullingSystemDOTS.Dynamic.OcclusionWorld> retired=new List<CullingSystemDOTS.Dynamic.OcclusionWorld>();
        private static readonly int[] CameraBlocks={0,3,12,15};
        private DynamicCityBootstrap sample;
        private GeometryHandle openGeometry;
        private RenderMeshArray openMeshArray;
        private InitializationSystemGroup initialization;
        private SimulationSystemGroup simulation;
        private PresentationSystemGroup presentation;
        private bool drive,measuring;
        private int frame,gpuFrames,fallbackFrames,appliedEdits;
        private int views,editRate;
        private int editAccumulator;
        private uint random,cameraTrace,commandTrace;
        private string directory;
        private bool complete;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void StartIfRequested()
        {
            if(!Environment.GetCommandLineArgs().Contains("--benchmark-dynamic-city"))return;
            var host=new GameObject("Dynamic City benchmark");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<DynamicCityPlayerBenchmark>();
        }
        private static string Arg(string name,string fallback)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:fallback;}
        private void Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
            directory=Path.GetFullPath(Arg("--benchmark-output",Path.Combine(Application.persistentDataPath,"DynamicCity-benchmark")));Directory.CreateDirectory(directory);
            report.Revision=Arg("--source-revision","unspecified");report.Mode=Arg("--benchmark-mode","matrix");report.Unity=Application.unityVersion;
            report.Api=SystemInfo.graphicsDeviceType.ToString();report.Gpu=SystemInfo.graphicsDeviceName;report.Cpu=SystemInfo.processorType;
            report.Width=Screen.width;report.Height=Screen.height;
            report.FrameTimingEnabled=FrameTimingManager.IsFeatureEnabled();Application.logMessageReceived+=Log;
            RenderPipelineManager.endContextRendering+=Rendered;StartCoroutine(Drive());
        }
        private void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
        private IEnumerator Drive()
        {
            var run=Run();
            while(true)
            {
                object current=null;bool next=false;
                try{next=run.MoveNext();if(next)current=run.Current;}
                catch(Exception exception){report.Failure=exception.ToString();break;}
                if(!next){report.Complete=errors.Count==0;if(errors.Count>0)report.Failure=string.Join("\n",errors);break;}
                yield return current;
            }
            Cleanup();complete=true;Save();Application.logMessageReceived-=Log;RenderPipelineManager.endContextRendering-=Rendered;Application.Quit(report.Complete?0:1);
        }
        private IEnumerator Run()
        {
            for(int i=0;i<180;i++){sample=UnityEngine.Object.FindFirstObjectByType<DynamicCityBootstrap>();if(sample!=null && sample.Initialized)break;yield return null;}
            Require(sample!=null && sample.Initialized,"Sample initialization failed.");
            if(report.Mode=="stress")
            {
                Configure(4096,4,false,true,100);var clock=System.Diagnostics.Stopwatch.StartNew();double nextReload=60,nextSave=30;int reload=0;
                double seconds=double.Parse(Arg("--stress-seconds","1800"),CultureInfo.InvariantCulture);
                while(clock.Elapsed.TotalSeconds<seconds)
                {
                    yield return new WaitForEndOfFrame();
                    if(frame%600==0)sample.SetFeatureEnabled(!sample.FeatureEnabled);
                    if(frame%300==0)for(int j=0;j<1000;j++)Edit();
                    if(clock.Elapsed.TotalSeconds>=nextReload)
                    {Cleanup();for(int i=0;i<8;i++)yield return null;Configure(4096,4,false,true,100);report.StressReloads=++reload;nextReload+=60;}
                    if(clock.Elapsed.TotalSeconds>=nextSave){report.StressSeconds=clock.Elapsed.TotalSeconds;Save();nextSave+=30;}
                    Require(errors.Count==0,string.Join("\n",errors));
                }
                report.StressSeconds=clock.Elapsed.TotalSeconds;Cleanup();
                for(int i=0;i<120 && retired.Any(r=>r.AccountedResourceBytes!=0);i++)yield return null;
                Require(retired.All(r=>r.AccountedResourceBytes==0),"Stress retained retired World resources.");yield break;
            }
            int skip=int.Parse(Arg("--benchmark-skip","0")),limit=int.Parse(Arg("--benchmark-limit","72")),index=0;
            foreach(int count in new[]{512,4096,16384})foreach(int viewCount in new[]{1,4})foreach(bool open in new[]{false,true})foreach(int rate in new[]{0,10,100})foreach(bool enabled in new[]{false,true})
            {
                int current=index++;if(current<skip || current>=skip+limit)continue;
                Configure(count,viewCount,open,enabled,rate);
                // Configure may resume either before or after Update when retirement yields.
                // Start every case after a rendered boundary, before its first driven Update.
                drive=false;yield return new WaitForEndOfFrame();drive=true;
                var result=new Result{Index=current,Targets=count,Views=viewCount,Layout=open?"open":"occluded",Enabled=enabled,EditsPerSecond=rate};
                const int measured=1800;var total=new double[measured];var main=new double[measured];var render=new double[measured];var gpu=new double[measured];
                var allocated=new double[measured];var draws=new double[measured];var triangles=new double[measured];var timings=new FrameTiming[1];
                using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
                using(var draw=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1))
                using(var tri=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1))
                {
                    int collections=0;for(int generation=0;generation<=GC.MaxGeneration;generation++)collections+=GC.CollectionCount(generation);
                    long previous=System.Diagnostics.Stopwatch.GetTimestamp();gpuFrames=fallbackFrames=0;bool gcAvailable=false,drawAvailable=false,triAvailable=false;
                    for(int i=0;i<300+measured;i++)
                    {
                        measuring=i>=300;FrameTimingManager.CaptureFrameTimings();yield return new WaitForEndOfFrame();
                        Require(frame==i+1,"Benchmark simulation/render phase drifted.");
                        long now=System.Diagnostics.Stopwatch.GetTimestamp();double elapsed=(now-previous)*1000.0/System.Diagnostics.Stopwatch.Frequency;previous=now;
                        if(i==10){var probe=new byte[4096];GC.KeepAlive(probe);}
                        gcAvailable|=gc.Valid && gc.LastValue>0;drawAvailable|=draw.Valid && draw.LastValue>0;triAvailable|=tri.Valid && tri.LastValue>0;
                        if(i<300)continue;int at=i-300;total[at]=elapsed;
                        bool available=FrameTimingManager.GetLatestTimings(1,timings)>0;var timing=timings[0];
                        main[at]=available && timing.cpuMainThreadFrameTime>0?timing.cpuMainThreadFrameTime:-1;
                        render[at]=available && timing.cpuRenderThreadFrameTime>0?timing.cpuRenderThreadFrameTime:-1;
                        gpu[at]=available && timing.gpuFrameTime>0?timing.gpuFrameTime:-1;
                        allocated[at]=gcAvailable?gc.LastValue:-1;draws[at]=drawAvailable?draw.LastValue:-1;triangles[at]=triAvailable?tri.LastValue:-1;
                        result.MaxAccountedBytes=Math.Max(result.MaxAccountedBytes,sample.Presentation.AccountedResourceBytes);
                        result.MaxManagedBytes=Math.Max(result.MaxManagedBytes,GC.GetTotalMemory(false));result.MaxUploadBytes=Math.Max(result.MaxUploadBytes,sample.Presentation.UploadedBytes);
                        result.MinReadyInstances=Math.Min(result.MinReadyInstances,sample.Presentation.ReadyInstances);result.MaxReadyInstances=Math.Max(result.MaxReadyInstances,sample.Presentation.ReadyInstances);
                        result.MaxPendingUpdates=Math.Max(result.MaxPendingUpdates,sample.Culling.Registry.PendingUpdateCount);if(sample.Culling.Registry.QueueOverflowed)result.QueueOverflowFrames++;
                    }
                    for(int generation=0;generation<=GC.MaxGeneration;generation++)result.Collections+=GC.CollectionCount(generation);
                    result.Collections-=collections;
                }
                measuring=false;result.GpuFrames=gpuFrames;result.FallbackFrames=fallbackFrames;result.AppliedEdits=appliedEdits;
                result.SimulationFrames=frame;result.CameraTrace=cameraTrace;result.CommandTrace=commandTrace;CaptureState(result);
                Require(frame==2100 && appliedEdits==2100*rate/60,"Benchmark input count drifted.");
                if(enabled && report.Results.Count>0 && report.Results[report.Results.Count-1].Index==current-1)
                {
                    var reference=report.Results[report.Results.Count-1];
                    Require(reference.SimulationFrames==result.SimulationFrames && reference.AppliedEdits==result.AppliedEdits && reference.CameraTrace==result.CameraTrace &&
                        reference.CommandTrace==result.CommandTrace && reference.AgentState==result.AgentState && reference.ConstructionState==result.ConstructionState,"Paired benchmark states differ.");
                }
                result.Total=Summarize(total);result.Main=Summarize(main);result.Render=Summarize(render);result.Gpu=Summarize(gpu);
                result.Allocations=Summarize(allocated);result.DrawCalls=Summarize(draws);result.Triangles=Summarize(triangles);
                WriteCsv(current,total,main,render,gpu,allocated,draws,triangles);report.Results.Add(result);Save();Cleanup();
                for(int i=0;i<120 && retired.Any(r=>r.AccountedResourceBytes!=0);i++)yield return null;
                Require(retired.All(r=>r.AccountedResourceBytes==0),"A completed case retained World resources.");retired.Clear();
                Require(errors.Count==0,string.Join("\n",errors));
            }
        }
        private void Configure(int count,int viewCount,bool open,bool enabled,int edits)
        {
            Cleanup();sample.AgentCount=count;sample.BuildingCount=open?0:16;sample.IncludeRotatedFixture=!open;sample.AutomaticEdits=false;sample.ShowHud=false;sample.AgentsPaused=false;
            sample.Initialize();sample.AutomaticEdits=false;sample.PauseSimulation(false);sample.View.GetComponent<DynamicCityCamera>().enabled=false;
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(sample.World);
            initialization=sample.World.GetExistingSystemManaged<InitializationSystemGroup>();simulation=sample.World.GetExistingSystemManaged<SimulationSystemGroup>();presentation=sample.World.GetExistingSystemManaged<PresentationSystemGroup>();
            views=viewCount;editRate=edits;frame=0;editAccumulator=0;appliedEdits=0;random=12345;cameraTrace=commandTrace=2166136261;
            var manager=sample.World.EntityManager;
            for(int i=0;i<sample.Agents.Length;i++){var agent=manager.GetComponentData<DynamicCityAgent>(sample.Agents[i]);agent.Home=DynamicCityBootstrap.Home(i%16);manager.SetComponentData(sample.Agents[i],agent);}
            if(open)
            {
                openGeometry=sample.Culling.Registry.RegisterGeometry(sample.CubeMesh);openMeshArray=new RenderMeshArray(new[]{sample.StockPalette[0]},new[]{sample.CubeMesh});
                for(int i=0;i<128;i++){var part=new Part{Pose=float4x4.TRS(new float3((i%16-7.5f)*7,0,(i/16-3.5f)*14),quaternion.identity,new float3(.3f,.1f,.3f))};part.Entity=Spawn(part.Pose);openParts.Add(part);}
            }
            cameras.Add(sample.View);
            for(int i=1;i<views;i++)
            {
                var camera=new GameObject("Benchmark view "+i).AddComponent<Camera>();camera.transform.SetParent(sample.transform,false);camera.CopyFrom(sample.View);camera.allowMSAA=false;camera.targetTexture=new RenderTexture(1280,720,24);camera.targetTexture.Create();
                targets.Add(camera.targetTexture);cameras.Add(camera);sample.Presentation.RegisterView(camera);
            }
            sample.SetFeatureEnabled(enabled);drive=true;
        }
        private Entity Spawn(float4x4 pose)
        {
            var manager=sample.World.EntityManager;var entity=manager.CreateEntity();
            RenderMeshUtility.AddComponents(entity,manager,new RenderMeshDescription(ShadowCastingMode.On,receiveShadows:false,layer:sample.Layer),openMeshArray,MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
            manager.SetComponentData(entity,new LocalToWorld{Value=pose});
            sample.Culling.Bindings.Bind(entity,new OcclusionInstanceDescriptor{Geometry=openGeometry,Material=sample.IndirectMaterial,Layer=sample.Layer,Shadows=ShadowCastingMode.On,
                State=new OcclusionInstanceState{Roles=OcclusionRole.Renderable|OcclusionRole.Occludee,Opaque=true,Color=sample.StockPalette[0].GetVector("_BaseColor")}});return entity;
        }
        private void Update()
        {
            if(!drive || sample==null || !sample.Initialized)return;
            for(int i=0;i<cameras.Count;i++)
            {
                float t=frame/60f;int block=CameraBlocks[i];var home=(Vector3)DynamicCityBootstrap.Home(block);
                cameras[i].transform.position=home+new Vector3(-1.5f+Mathf.Sin(t*.31f)*3,1.7f+Mathf.Sin(t*.17f)*.4f,-10+Mathf.Sin(t*.4f)*8);
                cameras[i].transform.LookAt(home+new Vector3(0,1.7f,0));
                cameraTrace=Mix(cameraTrace,math.hash((float4x4)cameras[i].transform.localToWorldMatrix));
            }
            editAccumulator+=editRate;while(editAccumulator>=60){Edit();editAccumulator-=60;}
            initialization.Update();sample.World.SetTime(new TimeData(frame/60.0,1f/60));simulation.Update();presentation.Update();frame++;
        }
        private void Edit()
        {
            random^=random<<13;random^=random>>17;random^=random<<5;
            commandTrace=Mix(commandTrace,random);
            if(openParts.Count>0)
            {
                var part=openParts[(int)(random%(uint)openParts.Count)];
                if(part.Entity!=Entity.Null){sample.Culling.Bindings.Unbind(part.Entity);sample.World.EntityManager.DestroyEntity(part.Entity);part.Entity=Entity.Null;}else part.Entity=Spawn(part.Pose);
            }
            else
            {
                var piece=sample.Construction.Pieces[(int)(random%(uint)sample.Construction.Pieces.Count)];
                sample.Construction.Apply(new ConstructionCommand{PieceId=piece.Id,Operation=piece.Present?ConstructionOperation.Remove:ConstructionOperation.Place,Tick=sample.Construction.LastTick+1});
            }
            appliedEdits++;report.StressEdits++;
        }
        private static uint Mix(uint hash,uint value){unchecked{return (hash^value)*16777619;}}
        private void CaptureState(Result result)
        {
            // Assertions and full-population inspection are outside measured frames.
            var manager=sample.World.EntityManager;manager.CompleteDependencyBeforeRO<LocalToWorld>();manager.CompleteDependencyBeforeRO<DynamicCityAgent>();
            uint agents=2166136261,pieces=2166136261;
            foreach(var entity in sample.Agents)
            {
                var agent=manager.GetComponentData<DynamicCityAgent>(entity);Require(agent.Tick==(uint)frame,"Agent simulation count drifted.");
                agents=Mix(agents,math.hash(manager.GetComponentData<LocalToWorld>(entity).Value));agents=Mix(agents,agent.Tick);
            }
            foreach(var piece in sample.Construction.Pieces){pieces=Mix(pieces,(uint)piece.Id);pieces=Mix(pieces,piece.Present?1u:0u);}
            foreach(var part in openParts)pieces=Mix(pieces,part.Entity==Entity.Null?0u:1u);
            result.AgentState=agents;result.ConstructionState=pieces;
        }
        private void Rendered(ScriptableRenderContext context,List<Camera> rendered)
        {
            if((!measuring && report.Mode!="stress") || sample==null || sample.View==null || !rendered.Contains(sample.View))return;
            bool gpu=sample.FeatureEnabled && sample.Presentation.ReadyInstances>0 && sample.Presentation.ViewFallback(sample.View)==OcclusionFallbackReason.None;
            if(gpu)gpuFrames++;else fallbackFrames++;
            if(report.Mode=="stress"){if(gpu)report.StressGpuFrames++;else report.StressFallbackFrames++;}
        }
        private void Cleanup()
        {
            drive=measuring=false;
            for(int i=1;i<cameras.Count;i++)if(cameras[i]!=null){cameras[i].targetTexture=null;cameras[i].gameObject.SetActive(false);UnityEngine.Object.Destroy(cameras[i].gameObject);}
            cameras.Clear();foreach(var target in targets){target.Release();UnityEngine.Object.Destroy(target);}targets.Clear();openParts.Clear();
            if(sample!=null && sample.Initialized){retired.Add(sample.Culling.Registry);sample.Shutdown();}
        }
        private static Distribution Summarize(double[] values)
        {
            var sorted=values.Where(x=>x>=0 && !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x=>x).ToArray();var result=new Distribution{Available=sorted.Length};
            if(sorted.Length>0){result.Median=sorted[sorted.Length/2];result.P95=sorted[(int)Math.Floor((sorted.Length-1)*.95)];result.P99=sorted[(int)Math.Floor((sorted.Length-1)*.99)];}return result;
        }
        private void WriteCsv(int index,params double[][] columns)
        {
            var text=new StringBuilder("frame,total_ms,main_ms,render_ms,gpu_ms,gc_bytes,draw_calls,triangles\n");
            for(int i=0;i<columns[0].Length;i++){text.Append(i);foreach(var column in columns)text.Append(',').Append(column[i].ToString("R",CultureInfo.InvariantCulture));text.Append('\n');}
            File.WriteAllText(Path.Combine(directory,"case-"+index+".csv"),text.ToString());
        }
        private void Save()=>File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        private void OnApplicationQuit(){if(!complete){report.Failure="Player quit before benchmark completion.";Save();}}
    }
}
