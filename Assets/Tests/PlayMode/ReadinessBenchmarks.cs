using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Tests
{
    public class ReadinessBenchmarks
    {
        [Serializable] public sealed class Case
        {
            public int cameras, entities = 3000, frames = 30, beginCalls, endCalls;
            public bool adapter;
            public double publicationMsPerFrame, beginMaskMsPerCamera, endRestoreMsPerCamera, wallMsPerFrame;
            public long mainThreadAllocatedBytesPerFrame, sampledPeakUnityAllocatedBytes;
            public string allocationStatus;
        }
        [Serializable] public sealed class Report
        {
            public string unity, gpu, api, utc;
            public string scope = "Editor PlayMode synthetic render entities, 5 warmup/30 sampled frames per case, one/four offscreen cameras. Callback timing brackets actual adapter handlers; includes reader synchronization and mask mutation. Wall frame time and allocations include Editor/test runner/render loop, not isolated adapter CPU time.";
            public string gpuTiming = "Unavailable: no reliable GPU timing captured; zero is not implied.";
            public List<Case> cases = new List<Case>();
        }

        [UnityTest, Explicit("On-demand readiness measurements; not a functional performance threshold.")]
        public IEnumerator MeasureCameraSynchronizationWithThreeThousandRenderEntities()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            Assert.That(world?.GetExistingSystemManaged<EntitiesGraphicsSystem>(), Is.Not.Null);
            var manager = world.EntityManager;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var entities = new NativeArray<Entity>(3000,Allocator.Persistent);
            var cameras = new Camera[4]; var targets = new RenderTexture[4];
            var report = new Report { unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                api = SystemInfo.graphicsDeviceType.ToString(), utc = DateTime.UtcNow.ToString("O") };
            try
            {
                var prototype = manager.CreateEntity();
                RenderMeshUtility.AddComponents(prototype,manager,new RenderMeshDescription(ShadowCastingMode.Off,layer:31),
                    new RenderMeshArray(new[] {material},new[] {mesh}),MaterialMeshInfo.FromRenderMeshArrayIndices(0,0));
                manager.Instantiate(prototype,entities); manager.DestroyEntity(prototype);
                for (int i = 0; i < entities.Length; i++) manager.SetComponentData(entities[i],new LocalToWorld {
                    Value = float4x4.TRS(new float3(i%60-30,i/60-25,4),quaternion.identity,new float3(.3f)) });
                for (int i = 0; i < 4; i++)
                {
                    cameras[i] = new GameObject("Owned readiness camera " + i).AddComponent<Camera>();
                    cameras[i].depth = 100+i; cameras[i].orthographic = true; cameras[i].orthographicSize = 32;
                    cameras[i].cullingMask = 1<<31; cameras[i].clearFlags = CameraClearFlags.SolidColor; cameras[i].backgroundColor = Color.black;
                    targets[i] = new RenderTexture(128,128,16); targets[i].Create(); cameras[i].targetTexture = targets[i];
                }
                foreach (int count in new[] {1,4}) foreach (bool enabled in new[] {false,true})
                {
                    var sample = new Case { cameras = count, adapter = enabled }; report.cases.Add(sample);
                    for (int i = 0; i < 4; i++) cameras[i].enabled = i<count;
                    var frame = CullingFrameState.Create(entities.Length,count,1,count,Allocator.Persistent);
                    var volumes = new NativeArray<RenderVolumeRange>(1,Allocator.Persistent);
                    var bindings = new NativeArray<RenderGroupBinding>(entities.Length,Allocator.Persistent);
                    volumes[0] = new RenderVolumeRange { GroupCount = entities.Length };
                    frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var key = new CullingGroupKey(new uint4(900,2,3,4),(ushort)i);
                        frame.Groups[i] = new CullingGroupSnapshot { Key = key, StateGeneration = 1 };
                        frame.StartApplied[i] = 1; frame.StartAppliedValid[i] = 1;
                        bindings[i] = new RenderGroupBinding { Group = key, Main = entities[i], OriginalShadowMode = ShadowCastingMode.Off,
                            ToggleMode = CullingRenderToggleMode.ShadowcastMode, InitialRendererEnabled = true };
                        for (int p = 0; p < count; p++) frame.PassStates[p*entities.Length+i] = (byte)((i+p)%2);
                    }
                    for (int p = 0; p < count; p++)
                    { frame.Passes[p] = new CullingViewPass { ViewIndex = p, RenderViewId = (ulong)p+1 }; frame.PassVolumeApplications[p] = 1; }
                    bool measuring = false; long beginAt = 0,endAt = 0,beginTicks = 0,endTicks = 0;
                    var owned = new HashSet<Camera>(cameras);
                    Action<ScriptableRenderContext,Camera> beforeBegin = (_,camera) => { if (owned.Contains(camera)) beginAt = Stopwatch.GetTimestamp(); };
                    Action<ScriptableRenderContext,Camera> afterBegin = (_,camera) => { if (measuring && owned.Contains(camera)) { beginTicks += Stopwatch.GetTimestamp()-beginAt; sample.beginCalls++; } };
                    Action<ScriptableRenderContext,Camera> beforeEnd = (_,camera) => { if (owned.Contains(camera)) endAt = Stopwatch.GetTimestamp(); };
                    Action<ScriptableRenderContext,Camera> afterEnd = (_,camera) => { if (measuring && owned.Contains(camera)) { endTicks += Stopwatch.GetTimestamp()-endAt; sample.endCalls++; } };
                    EcsRenderVisibilityAdapter adapter = null;
                    RenderPipelineManager.beginCameraRendering += beforeBegin; RenderPipelineManager.endCameraRendering += beforeEnd;
                    try
                    {
                        if (enabled)
                        {
                            adapter = new EcsRenderVisibilityAdapter(world);
                            for (int i = 0; i < count; i++) adapter.RegisterCamera(cameras[i],(ulong)i+1);
                        }
                        RenderPipelineManager.beginCameraRendering += afterBegin; RenderPipelineManager.endCameraRendering += afterEnd;
                        for (int i = 0; i < 5; i++) { adapter?.Publish(in frame,volumes,bindings); yield return null; }
                        measuring = true; long allocated = GC.GetAllocatedBytesForCurrentThread(); var wall = Stopwatch.StartNew();
                        for (int i = 0; i < sample.frames; i++)
                        {
                            long start = Stopwatch.GetTimestamp(); adapter?.Publish(in frame,volumes,bindings);
                            sample.publicationMsPerFrame += (Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                            yield return null;
                            sample.sampledPeakUnityAllocatedBytes = Math.Max(sample.sampledPeakUnityAllocatedBytes,UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                        }
                        measuring = false; wall.Stop();
                        long allocationDelta = GC.GetAllocatedBytesForCurrentThread()-allocated;
                        sample.mainThreadAllocatedBytesPerFrame = allocationDelta > 0 ? allocationDelta/sample.frames : -1;
                        sample.allocationStatus = allocationDelta > 0 ? "Whole main-thread loop samples, including Editor/test overhead."
                            : "Unavailable: Mono returned no allocation delta; this is not a zero-allocation result.";
                        sample.wallMsPerFrame = wall.Elapsed.TotalMilliseconds/sample.frames; sample.publicationMsPerFrame /= sample.frames;
                        sample.beginMaskMsPerCamera = beginTicks*1000.0/Stopwatch.Frequency/Math.Max(1,sample.beginCalls);
                        sample.endRestoreMsPerCamera = endTicks*1000.0/Stopwatch.Frequency/Math.Max(1,sample.endCalls);
                        Assert.That(sample.beginCalls, Is.GreaterThan(0)); Assert.That(sample.endCalls, Is.EqualTo(sample.beginCalls));
                        if (adapter != null) Assert.That(adapter.LastFault, Is.Null);
                        Assert.That(manager.IsEnabled(entities[0]), Is.True);
                    }
                    finally
                    {
                        RenderPipelineManager.beginCameraRendering -= beforeBegin; RenderPipelineManager.beginCameraRendering -= afterBegin;
                        RenderPipelineManager.endCameraRendering -= beforeEnd; RenderPipelineManager.endCameraRendering -= afterEnd;
                        adapter?.Dispose(); bindings.Dispose(); volumes.Dispose(); frame.Dispose();
                    }
                }
                string path = Path.GetFullPath("Docs/Validation/Readiness-Cameras.json"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                for (int i = 0; i < entities.Length; i++) if (manager.Exists(entities[i])) manager.DestroyEntity(entities[i]); entities.Dispose();
                for (int i = 0; i < 4; i++)
                { if (cameras[i] != null) { cameras[i].targetTexture = null; Object.Destroy(cameras[i].gameObject); } if (targets[i] != null) { targets[i].Release(); Object.Destroy(targets[i]); } }
                Object.Destroy(material); Object.Destroy(cube);
            }
        }
    }
}
