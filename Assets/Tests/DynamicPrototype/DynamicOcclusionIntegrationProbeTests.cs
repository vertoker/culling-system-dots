using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    public class DynamicOcclusionIntegrationProbeTests
    {
        [Test, Explicit("G1 capability assertion: no supported stock GPU handoff has been established.")]
        public void StockGpuVisibilityCanReachDrawSubmissionWithoutReadback()
        {
            Assert.That(DynamicOcclusionIntegrationProbe.SupportedStockGpuHandoff, Is.True,
                "No public GPU visibility input/culling callback on installed EntitiesGraphicsSystem. " +
                "BRG indirect commands require owning a separate BRG; stock enable bits are CPU inputs.");
        }

        [UnityTest]
        public IEnumerator ReferenceMaskRemovesRenderedTargetWithoutDisablingSimulation()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            Assert.That(world?.GetExistingSystemManaged<EntitiesGraphicsSystem>(), Is.Not.Null);
            var manager = world.EntityManager;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.red);
            var camera = new GameObject("Owned G1 reference camera").AddComponent<Camera>();
            var target = new RenderTexture(128, 128, 24); target.Create();
            camera.targetTexture = target; camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(0, 0, -5);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var entity = manager.CreateEntity();
            try
            {
                RenderMeshUtility.AddComponents(entity, manager, new RenderMeshDescription(ShadowCastingMode.Off, layer: 31),
                    new RenderMeshArray(new[] { material }, new[] { cube.GetComponent<MeshFilter>().sharedMesh }),
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                manager.SetComponentData(entity, new LocalToWorld { Value = float4x4.identity });
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200), "Ordinary ECS rendering must work first.");
                DynamicOcclusionIntegrationProbe.ApplyReferenceMask(manager, entity, true);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(Center(target).r, Is.LessThan(5), "The CPU mask must actually remove rendering.");
                Assert.That(manager.IsEnabled(entity), Is.True);
                DynamicOcclusionIntegrationProbe.ApplyReferenceMask(manager, entity, false);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(Center(target).r, Is.GreaterThan(200), "Restoration must reveal the same entity.");
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                if (manager.Exists(entity)) manager.DestroyEntity(entity);
                camera.targetTexture = null; Object.Destroy(camera.gameObject);
                target.Release(); Object.Destroy(target); Object.Destroy(material); Object.Destroy(cube);
            }
        }

        [UnityTest]
        public IEnumerator GpuReadbackRejectsChangedViewEpochAndBounds()
        {
            var camera = new GameObject("Owned G1 validity camera").AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true;
            var target = new RenderTexture(128, 128, 24); target.Create(); camera.targetTexture = target;
            camera.transform.position = new Vector3(-1, 0, -5);
            try
            {
                using var gpu = new GpuWallMask(new[] { new Vector4(-1, 0, 4, .1f), new Vector4(1, 0, 4, .1f) });
                gpu.Submit(camera, 1, 1, true);
                uint[] bits = null;
                for (int i = 0; i < 120 && bits == null; i++) { yield return null; bits = gpu.Read(camera, 1, 1); }
                Assert.That(bits, Is.Not.Null, "Readback must complete within 120 Editor frames.");
                Assert.That(bits[0], Is.EqualTo(1), "Cube entirely behind the fixture wall must be classified on the GPU.");
                Assert.That(bits[1], Is.Zero, "Cube beyond the wall edge must remain visible.");
                camera.transform.position += Vector3.right * .01f;
                Assert.That(gpu.Read(camera, 1, 1), Is.Null, "Any view movement rejects old hiding.");
                camera.transform.position -= Vector3.right * .01f;
                // Capture a new result rather than relying on floating-point round-trip equality.
                gpu.Submit(camera, 1, 1, true); bits = null;
                for (int i = 0; i < 120 && bits == null; i++) { yield return null; bits = gpu.Read(camera, 1, 1); }
                Assert.That(bits, Is.Not.Null);
                Assert.That(gpu.Read(camera, 2, 1), Is.Null, "Wall removal invalidates before a new upload/readback.");
                Assert.That(gpu.Read(camera, 1, 2), Is.Null, "Moved/replaced candidates reject the old mask.");
                camera.transform.rotation = Quaternion.Euler(0, 90, 0);
                Assert.That(gpu.Read(camera, 1, 1), Is.Null, "Camera turn cannot reuse a hidden result.");
                camera.transform.position += Vector3.right * 100;
                Assert.That(gpu.Read(camera, 1, 1), Is.Null, "Teleport cannot reuse a hidden result.");
            }
            finally
            {
                camera.targetTexture = null; Object.Destroy(camera.gameObject);
                target.Release(); Object.Destroy(target);
            }
        }

        [UnityTest]
        public IEnumerator WallRemovalAndOppositeViewsMatchOrdinaryRendering()
        {
            using var stand = new IntegrationStand(2, 2);
            stand.Cameras[1].transform.position = new Vector3(-1, 0, 8);
            stand.Cameras[1].transform.rotation = Quaternion.Euler(0, 180, 0);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(Center(stand.Targets[0]).b, Is.GreaterThan(200), "Front camera must see the opaque wall.");
            Assert.That(Center(stand.Targets[1]).r, Is.GreaterThan(200), "Opposite camera must see the target in front of the wall.");
            var reference = new[] { Pixels(stand.Targets[0]), Pixels(stand.Targets[1]) };
            stand.Route = ProbeRoute.StrictAsync;
            for (int i = 0; i < 120 && stand.AcceptedViews == 0; i++) yield return null;
            Assert.That(stand.AcceptedViews, Is.GreaterThan(0), "Idle GPU result must cause actual CPU render masking.");
            for (int i = 0; i < 2; i++) yield return null;
            for (int i = 0; i < 2; i++) CollectionAssert.AreEqual(reference[i], Pixels(stand.Targets[i]), "Same-state view " + i);
            stand.SetWall(false);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Center(stand.Targets[0]).r, Is.GreaterThan(200), "Removed wall must reveal target without waiting for a new hidden result.");
            for (int i = 0; i < 10; i++)
            {
                stand.Cameras[0].transform.position += new Vector3(.01f, 0, 0);
                stand.Cameras[0].transform.rotation = Quaternion.Euler(0, i * .05f, 0);
                yield return null;
            }
            Assert.That(Center(stand.Targets[0]).r, Is.GreaterThan(200), "Motion/turn must retain visible target.");
            stand.Cameras[0].transform.position = new Vector3(1, 0, -8); stand.Cameras[0].transform.rotation = Quaternion.identity;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Center(stand.Targets[0]).r, Is.GreaterThan(200), "Teleport must reveal the other target.");
            foreach (var entity in stand.Entities)
            {
                Assert.That(stand.World.EntityManager.IsEnabled(entity), Is.True);
                Assert.That(stand.World.EntityManager.IsComponentEnabled<MaterialMeshInfo>(entity), Is.True, "Camera masks must restore after rendering.");
            }
        }

        [Test]
        public void FailedGpuAllocationReleasesPreviouslyAllocatedBuffers()
        {
            ComputeBuffer first = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => new GpuWallMask(new[] { new Vector4(-1, 0, 4, .1f) }, (count, stride) => {
                    if (first != null) throw new InvalidOperationException("Owned G1 allocation failure");
                    return first = new ComputeBuffer(count, stride);
                }));
                Assert.That(first.IsValid(), Is.False, "Constructor must release an earlier successful allocation.");
            }
            finally { first?.Dispose(); }
        }

        [UnityTest]
        public IEnumerator FailedStandConstructionReleasesEarlierOwners()
        {
            var world = World.DefaultGameObjectInjectionWorld; var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            var originalEntities = new HashSet<Entity>();
            using (var snapshot = manager.UniversalQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var entity in snapshot) originalEntities.Add(entity);
            var originalObjects = new HashSet<GameObject>(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            var originalTargets = new HashSet<RenderTexture>(Resources.FindObjectsOfTypeAll<RenderTexture>());
            GpuWallMask first = null;
            IntegrationStand failed = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => new IntegrationStand(2, 2, (owner, bounds) => {
                    failed = owner;
                    if (first != null) throw new InvalidOperationException("Owned G1 second view allocation failure");
                    return first = new GpuWallMask(bounds);
                }));
                Assert.Throws<ObjectDisposedException>(() => first.Read(null, 1, 1), "Earlier GPU owner must be disposed.");
                Assert.That(manager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(originalEntities.Count), "Failed setup must remove owned Entities.");
                yield return null;
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (go.name.StartsWith("Owned G1", StringComparison.Ordinal))
                        Assert.That(originalObjects.Contains(go), Is.True, "Failed setup left a GameObject: " + go.name);
                foreach (var target in Resources.FindObjectsOfTypeAll<RenderTexture>())
                    Assert.That(originalTargets.Contains(target), Is.True, "Failed setup left a render target.");
            }
            finally
            {
                // Also clean the deliberately broken RED implementation, without touching pre-existing state.
                failed?.Dispose(); first?.Dispose();
            }
        }

        [Serializable]
        public sealed class Measurement
        {
            public string route;
            public int entities, cameras, requestedEditsPerSecond, observedEdits, frames = 60;
            public bool moving;
            public int views, acceptedViews, failOpenViews, hiddenApplications, broadJobCompletions;
            public int readbacksIssued, readbacksCompleted, readbackErrors, saturatedSubmissions;
            public double beginMsPerView, endMsPerView, blockingWaitMsPerView;
            public double wallFrameMedianMs, wallFrameP95Ms, wallFrameP99Ms, observedEditsPerSecond;
            public double stockWorldRenderedInstancesPerFrame, stockWorldDrawCommandsPerFrame;
        }

        [Serializable]
        public sealed class MeasurementReport
        {
            public string utc, sourceRevision, unity, gpu, api, cpu, operatingSystem;
            public string scope = "G1 Editor diagnostic only: 10 warmup/60 measured frames, 128x128 single-sample targets, fixed closed-form orthographic wall GPU producer, two readback slots per view. No Hi-Z, no Player timings or general geometry correctness claim. Wall toggles exercise global epoch invalidation; requested edit rates use elapsed Editor frame time. Whole frame intervals include Editor/test/render overhead. Stock World draw/instance counters include other views/entities and are not isolated draw savings.";
            public string gpuTiming = "Unavailable: no isolated reliable GPU timing; zero is not implied.";
            public string gcTiming = "Unavailable: not sampled in this integration diagnostic.";
            public string verdict = "No acceptable stock GPU route established. Strict async must reject changed views; blocking route is diagnostic only.";
            public List<Measurement> cases = new List<Measurement>();
        }

        [UnityTest, Explicit("G1 integration measurement, not a product performance threshold.")]
        public IEnumerator MeasureStockReadbackIntegration()
        {
            var report = new MeasurementReport { utc = DateTime.UtcNow.ToString("O"), sourceRevision = SourceRevision(),
                unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
                cpu = SystemInfo.processorType, operatingSystem = SystemInfo.operatingSystem };
            var graphics = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            foreach (int count in new[] { 512, 4096 })
            foreach (int cameraCount in new[] { 1, 4 })
            {
                using var stand = new IntegrationStand(count, cameraCount);
                foreach (bool moving in new[] { false, true })
                foreach (int editRate in new[] { 0, 10, 100 })
                foreach (ProbeRoute route in Enum.GetValues(typeof(ProbeRoute)))
                {
                    stand.Route = route; stand.SetWall(true);
                    foreach (var camera in stand.Cameras) camera.transform.position = new Vector3(0, 0, -8);
                    for (int i = 0; i < 10; i++) yield return null;
                    var measurement = new Measurement { entities = count, cameras = cameraCount, moving = moving,
                        requestedEditsPerSecond = editRate, route = route.ToString() };
                    stand.ResetCounters();
                    int issued = 0, completed = 0, errors = 0, saturated = 0;
                    foreach (var mask in stand.Masks) { issued += mask.Issued; completed += mask.Completed; errors += mask.Errors; saturated += mask.Saturated; }
                    var frameTimes = new List<double>(measurement.frames);
                    double editAccumulator = 0, elapsed = 0;
                    for (int i = 0; i < measurement.frames; i++)
                    {
                        if (moving)
                            for (int c = 0; c < stand.Cameras.Length; c++) stand.Cameras[c].transform.position = new Vector3((i + 1) * .003f + c * .001f, 0, -8);
                        while (editAccumulator >= 1)
                        {
                            stand.SetWall(!stand.WallPresent); measurement.observedEdits++; editAccumulator--;
                        }
                        long start = Stopwatch.GetTimestamp(); yield return null;
                        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                        frameTimes.Add(ms); elapsed += ms / 1000; editAccumulator += editRate * ms / 1000;
                        var stats = graphics.Stats;
                        measurement.stockWorldDrawCommandsPerFrame += stats.DrawCommandCount;
                        measurement.stockWorldRenderedInstancesPerFrame += stats.RenderedInstanceCount;
                    }
                    measurement.views = stand.Views; measurement.acceptedViews = stand.AcceptedViews;
                    measurement.failOpenViews = stand.FailOpenViews; measurement.hiddenApplications = stand.HiddenApplications;
                    measurement.broadJobCompletions = stand.JobCompletions;
                    measurement.beginMsPerView = stand.BeginMs / Math.Max(1, stand.Views);
                    measurement.endMsPerView = stand.EndMs / Math.Max(1, stand.Views);
                    measurement.blockingWaitMsPerView = stand.BlockingWaitMs / Math.Max(1, stand.Views);
                    foreach (var mask in stand.Masks)
                    {
                        measurement.readbacksIssued += mask.Issued; measurement.readbacksCompleted += mask.Completed;
                        measurement.readbackErrors += mask.Errors; measurement.saturatedSubmissions += mask.Saturated;
                    }
                    measurement.readbacksIssued -= issued; measurement.readbacksCompleted -= completed;
                    measurement.readbackErrors -= errors; measurement.saturatedSubmissions -= saturated;
                    frameTimes.Sort(); measurement.wallFrameMedianMs = Percentile(frameTimes, .5);
                    measurement.wallFrameP95Ms = Percentile(frameTimes, .95); measurement.wallFrameP99Ms = Percentile(frameTimes, .99);
                    measurement.observedEditsPerSecond = measurement.observedEdits / Math.Max(elapsed, .0001);
                    measurement.stockWorldDrawCommandsPerFrame /= measurement.frames;
                    measurement.stockWorldRenderedInstancesPerFrame /= measurement.frames;
                    report.cases.Add(measurement);
                    if (route == ProbeRoute.StrictAsync && moving)
                        Assert.That(measurement.acceptedViews, Is.Zero, "Strict changed-view validation must prevent stale hiding.");
                    Assert.That(measurement.readbackErrors, Is.Zero);
                    Assert.That(stand.World.EntityManager.IsEnabled(stand.Entities[0]), Is.True);
                }
            }
            File.WriteAllText("Docs/Validation/DynamicOcclusion-Integration.json", JsonUtility.ToJson(report, true));
            Assert.That(report.cases.Count, Is.EqualTo(72));
        }

        private static double Percentile(List<double> sorted, double p) => sorted[(int)Math.Ceiling((sorted.Count - 1) * p)];

        private static string SourceRevision()
        {
            string head = File.ReadAllText(".git/HEAD").Trim();
            return head.StartsWith("ref: ", StringComparison.Ordinal) ? File.ReadAllText(".git/" + head.Substring(5)).Trim() : head;
        }

        private static Color32[] Pixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        internal static Color32 Center(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0); image.Apply();
                return image.GetPixels32()[0];
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
    }
}
