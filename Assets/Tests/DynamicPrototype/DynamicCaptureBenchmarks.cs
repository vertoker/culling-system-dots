using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    public class DynamicCaptureBenchmarks
    {
        [Serializable] private sealed class Report
        {
            public string utc, unity, cpu, gpu, scope;
            public string[] sourceSha256;
            public bool managedAllocationCounterAvailable;
            public List<Case> cases = new List<Case>();
        }
        [Serializable] private sealed class Case
        {
            public int instances, warmup = 20, samples = 120, maxPreparedBytes, pendingAtEnd;
            public bool moving;
            public double captureMedianMs, captureP95Ms, preparationMedianMs, preparationP95Ms, allocatedBytesPerSample;
        }
        [UnityTest, Explicit("Scoped Editor capture profile; does not measure rendered-frame benefit.")]
        public IEnumerator MeasureCaptureAndPreparation()
        {
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName,
                scope = "Editor main-thread capture and logical preparation only; excludes transform writes, registration, rendering, snapshots and GPU upload." };
            report.sourceSha256 = SourceHashes();
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[16384]);
            report.managedAllocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationBefore;
            foreach (int count in new[] { 512, 4096 })
            foreach (bool moving in new[] { false, true })
            {
                var result = new Case { instances = count, moving = moving };
                using (var world = new World("scoped capture profile"))
                {
                    var system = world.GetOrCreateSystemManaged<DynamicOcclusionSystem>();
                    var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                    try
                    {
                        var geometry = system.Registry.RegisterGeometry(mesh); var entities = new Entity[count];
                        for (int i = 0; i < count; i++)
                        {
                            var e = world.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(MaterialMeshInfo)); entities[i] = e;
                            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.Translate(new float3(i % 64 * 2, 0, i / 64 * 2)) });
                            system.Bindings.Bind(e, new OcclusionInstanceDescriptor { Geometry = geometry,
                                State = new OcclusionInstanceState { Roles = OcclusionRole.Occludee, Color = new float4(1), Opaque = true } });
                        }
                        // Drain initial registration independently; this measurement is steady-state capture.
                        while (system.Registry.PendingUpdateCount > 0) system.Registry.ProcessUpdates(new OcclusionUpdateBudget(int.MaxValue));
                        var capture = new double[result.samples]; var preparation = new double[result.samples]; long allocated = 0;
                        var clock = new Stopwatch();
                        for (int frame = -result.warmup; frame < result.samples; frame++)
                        {
                            if (moving)
                                for (int i = 0; i < count; i++)
                                    world.EntityManager.SetComponentData(entities[i], new LocalToWorld { Value = float4x4.Translate(
                                        new float3(i % 64 * 2 + (frame + result.warmup) * 0.01f, 0, i / 64 * 2)) });
                            long before = GC.GetAllocatedBytesForCurrentThread(); clock.Restart(); system.Bindings.Capture();
                            double elapsed = clock.Elapsed.TotalMilliseconds; clock.Restart();
                            var batch = system.Registry.ProcessUpdates(new OcclusionUpdateBudget(int.MaxValue, int.MaxValue));
                            double prepared = clock.Elapsed.TotalMilliseconds; long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                            if (frame >= 0)
                            {
                                capture[frame] = elapsed; preparation[frame] = prepared; allocated += bytes;
                                result.maxPreparedBytes = Math.Max(result.maxPreparedBytes, batch.PreparedBytes);
                            }
                            Assert.That(system.Bindings.LastFault, Is.Null); yield return null;
                        }
                        Array.Sort(capture); Array.Sort(preparation);
                        result.captureMedianMs = capture[result.samples / 2]; result.captureP95Ms = capture[(int)(result.samples * 0.95)];
                        result.preparationMedianMs = preparation[result.samples / 2]; result.preparationP95Ms = preparation[(int)(result.samples * 0.95)];
                        result.allocatedBytesPerSample = report.managedAllocationCounterAvailable ? (double)allocated / result.samples : -1;
                        result.pendingAtEnd = system.Registry.PendingUpdateCount;
                        report.cases.Add(result);
                    }
                    finally { UnityEngine.Object.Destroy(mesh); }
                }
            }
            string root = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(root, "Docs/Validation/DynamicOcclusion-Capture.json"), JsonUtility.ToJson(report, true) + "\n");
        }
        private static string[] SourceHashes()
        {
            var files = new[] { "Runtime/Dynamic/Ecs/DynamicOcclusionBindings.cs", "Runtime/Dynamic/Ecs/DynamicOcclusionSystem.cs",
                "Runtime/Dynamic/Ecs/DynamicOcclusionCaptureSystem.cs", "Runtime/Dynamic/OcclusionGeometry.cs",
                "Runtime/Dynamic/OcclusionWorld.cs", "Runtime/Dynamic/OcclusionWorld.Updates.cs", "Runtime/Dynamic/OcclusionUpdateQueue.cs" };
            using (var hash = SHA256.Create())
                for (int i = 0; i < files.Length; i++)
                {
                    string relative = files[i];
                    files[i] = relative + " " + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(
                        Path.Combine(Application.dataPath, "CullingSystemDOTS", relative)))).Replace("-", "").ToLowerInvariant();
                }
            return files;
        }
    }
}
