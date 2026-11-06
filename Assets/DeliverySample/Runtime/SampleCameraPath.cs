using System;
using System.Collections;
using System.IO;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
namespace CullingSystemDOTS.Sample
{
    [RequireComponent(typeof(Camera),typeof(CullingCameraAuthoring))]
    public sealed class SampleCameraPath : MonoBehaviour
    {
        public bool AnimateCamera = true;
        private bool smoke;
        private float phase;
        [Serializable] private sealed class Report
        {
            public bool passed; public string unity,device,api,configuration,error,renderMethod,pipeline;
            public int volumes,groups,renderEntities,normalRed,invertedRed,restoredRed,frames;
            public double mainThreadMs,drawCalls,shadowCasters,gcAllocatedBytes,totalUsedBytes,gpuFrameMs = -1;
            public bool mainThreadAvailable,drawCallsAvailable,shadowCastersAvailable,gcAvailable,memoryAvailable;
            public string gpuTimingStatus;
        }
        private void Update()
        {
            if (!AnimateCamera) return;
            phase += Time.unscaledDeltaTime;
            transform.position = new Vector3(Mathf.Lerp(-2,2,Mathf.PingPong(phase*.2f,1)),0,-3);
        }
        private void OnGUI()
        {
            if (smoke) return;
            GUILayout.BeginArea(new Rect(12,12,300,110),GUI.skin.box);
            GUILayout.Label("Culling System DOTS — two rooms");
            if (GUILayout.Button(AnimateCamera ? "Pause camera" : "Move between rooms")) AnimateCamera = !AnimateCamera;
            var culling = GetComponent<CullingCameraAuthoring>();
            if (GUILayout.Button(culling.Invert ? "Show normally visible groups" : "Invert culling")) culling.Invert = !culling.Invert;
            GUILayout.EndArea();
        }
        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs(); int argument = Array.IndexOf(args,"--pvs-smoke-output");
            if (argument < 0 || argument+1 >= args.Length) yield break;
            smoke = true; AnimateCamera = false;
            string output = Path.GetFullPath(args[argument+1]);
            var report = new Report {unity = Application.unityVersion,device = SystemInfo.graphicsDeviceName,api = SystemInfo.graphicsDeviceType.ToString(),configuration = Debug.isDebugBuild ? "Development Player" : "Release Player"};
            World world = null; double deadline = Time.realtimeSinceStartupAsDouble+60;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    using var volumes = world.EntityManager.CreateEntityQuery(typeof(CullingVolume)); report.volumes = volumes.CalculateEntityCount();
                    using var groups = world.EntityManager.CreateEntityQuery(typeof(CullingGroupDefinition)); report.groups = groups.CalculateEntityCount();
                    using var renderers = world.EntityManager.CreateEntityQuery(typeof(MaterialMeshInfo)); report.renderEntities = renderers.CalculateEntityCount();
                    if (report.volumes >= 1 && report.groups == 3 && report.renderEntities >= 4) break;
                }
                yield return null;
            }
            if (report.volumes < 1 || report.groups != 3 || report.renderEntities < 4)
            { report.error = "SubScene content did not load."; SaveAndExit(output,report); yield break; }
            var camera = GetComponent<Camera>(); var cullingCamera = GetComponent<CullingCameraAuthoring>();
            var target = new RenderTexture(128,128,24); target.Create(); var previousTarget = camera.targetTexture; camera.targetTexture = target;
            var renderRequest = new RenderPipeline.StandardRequest {destination = target};
            report.renderMethod = "explicit offscreen SRP StandardRequest";
            report.pipeline = GraphicsSettings.currentRenderPipeline == null ? "none" : GraphicsSettings.currentRenderPipeline.name;
            transform.position = new Vector3(-2,0,-3); cullingCamera.Invert = false;
            try
            {
                for (int i = 0; i < 30; i++) { yield return null; RenderPipeline.SubmitRenderRequest(camera,renderRequest); }
                report.normalRed = ReadRed(target,output+".normal.png");
                cullingCamera.Invert = true; for (int i = 0; i < 3; i++) { yield return null; RenderPipeline.SubmitRenderRequest(camera,renderRequest); }
                report.invertedRed = ReadRed(target,output+".inverted.png");
                cullingCamera.Invert = false; for (int i = 0; i < 3; i++) { yield return null; RenderPipeline.SubmitRenderRequest(camera,renderRequest); }
                report.restoredRed = ReadRed(target,output+".restored.png");
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>();
                report.error = owner == null ? "Culling World system is missing." : owner.GetOrCreateRenderingAdapter().LastFault;
                using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread");
                using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count");
                using var shadows = ProfilerRecorder.StartNew(ProfilerCategory.Render,"Shadow Casters Count");
                using var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
                using var memory = ProfilerRecorder.StartNew(ProfilerCategory.Memory,"Total Used Memory");
                report.mainThreadAvailable = main.Valid; report.drawCallsAvailable = draws.Valid; report.shadowCastersAvailable = shadows.Valid; report.gcAvailable = gc.Valid; report.memoryAvailable = memory.Valid;
                var timing = new FrameTiming[1]; double gpu = 0; int gpuSamples = 0;
                for (int frame = 0; frame < 60; frame++)
                {
                    FrameTimingManager.CaptureFrameTimings(); yield return null;
                    RenderPipeline.SubmitRenderRequest(camera,renderRequest);
                    report.frames++; report.mainThreadMs += main.Valid ? main.LastValue/1000000.0 : 0;
                    report.drawCalls += draws.Valid ? draws.LastValue : 0; report.shadowCasters += shadows.Valid ? shadows.LastValue : 0;
                    report.gcAllocatedBytes += gc.Valid ? gc.LastValue : 0; report.totalUsedBytes += memory.Valid ? memory.LastValue : 0;
                    if (FrameTimingManager.GetLatestTimings(1,timing) > 0 && timing[0].gpuFrameTime > 0) { gpu += timing[0].gpuFrameTime; gpuSamples++; }
                }
                report.mainThreadMs /= report.frames; report.drawCalls /= report.frames; report.shadowCasters /= report.frames; report.gcAllocatedBytes /= report.frames; report.totalUsedBytes /= report.frames;
                report.gpuFrameMs = gpuSamples == 0 ? -1 : gpu/gpuSamples; report.gpuTimingStatus = gpuSamples == 0 ? "unavailable: FrameTimingManager returned no positive GPU times" : "measured";
                report.passed = string.IsNullOrEmpty(report.error) && report.normalRed > 20 && report.invertedRed < report.normalRed/2 && report.restoredRed > report.normalRed/2;
                if (!report.passed && string.IsNullOrEmpty(report.error)) report.error = "Rendered normal/inverted/restored visibility did not match the sample contract.";
            }
            finally { camera.targetTexture = previousTarget; target.Release(); Destroy(target); }
            SaveAndExit(output,report);
        }
        private static int ReadRed(RenderTexture target,string imagePath)
        {
            var previous = RenderTexture.active; var image = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); int red = 0;
                foreach (var color in image.GetPixels32()) if (color.r > 40 && color.r > color.g*1.6f && color.r > color.b*1.6f) red++;
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)); File.WriteAllBytes(imagePath,image.EncodeToPNG()); return red;
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }
        private static void SaveAndExit(string path,Report report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,JsonUtility.ToJson(report,true)+"\n");
            Application.Quit(report.passed ? 0 : 2);
        }
    }
}
