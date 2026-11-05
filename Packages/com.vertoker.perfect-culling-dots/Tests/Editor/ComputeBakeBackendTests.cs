using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Editor.Baking.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace PerfectCullingDOTS.Tests
{
    public class ComputeBakeBackendTests
    {
        internal static IBakeBackend Backend()
        {
            var type = typeof(IBakeBackend).Assembly.GetType("PerfectCullingDOTS.Editor.Baking.Gpu.ComputeBakeBackend");
            Assert.That(type, Is.Not.Null, "Unity compute backend has not been implemented.");
            return (IBakeBackend)Activator.CreateInstance(type);
        }
        internal static BakeSnapshot Snapshot()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh {vertices = new[] {new Vector3(-1,-2,0),new Vector3(-1,2,0),new Vector3(1,2,0),new Vector3(1,-2,0)},triangles = new[] {0,1,2,0,2,3}};
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var groups = Enumerable.Range(0,40).Select(i=>new BakeGroupSource {Key = new CullingGroupKey(new uint4(18,2,3,4),(ushort)i)}).ToArray();
                foreach (int i in new[] {0,1,2,32})
                {
                    var go = new GameObject("analytic group "+i); SceneManager.MoveGameObjectToScene(go,scene);
                    go.transform.position = i == 0 ? new Vector3(-1,0,1) : i == 32 ? new Vector3(1,0,1) : new Vector3(0,0,i == 1 ? 4 : 6);
                    if (i == 1 || i == 2) go.transform.localScale = new Vector3(20,20,1);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    go.AddComponent<PerfectCullingDOTS.Authoring.CullingBakeRendererTag>().Surface = i == 1 ? PerfectCullingDOTS.Authoring.BakeSurfaceOverride.Opaque : PerfectCullingDOTS.Authoring.BakeSurfaceOverride.Transparent;
                    groups[i].Renderers = new Renderer[] {renderer};
                }
                return MeshSnapshotExtractor.Capture(new BakeSnapshotRequest {Profile = BakeInputProfile.ComputeSoftwareInput,Groups = groups,
                    Rays = new BakeRaySettings {Width = 9,Height = 11,Far = 30,CellSize = new float3(2,1,3),Rotation = quaternion.identity},
                    Samples = new[] {new BakeSampleLocation {FullGridIndex = 2},new BakeSampleLocation {FullGridIndex = 19,Position = new float3(.1f,0,0)},new BakeSampleLocation {FullGridIndex = 74,Position = new float3(-.1f,0,0)}}});
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
        [Test]
        public void OddSizedGpuBatchesPreserveTransparentOcclusionAndBitsAcrossWords()
        {
            using var snapshot = Snapshot(); using var cpu = new SourceComputeJobsBackend().Begin(snapshot); var expected = cpu.Complete();
            foreach (var cell in expected) CollectionAssert.AreEqual(new ushort[] {0,1,32},cell.Groups);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                using var gpu = Backend().Begin(snapshot,new BakeBackendOptions {MaxCellsPerBatch = 1,MemoryBudgetBytes = 1024*1024});
                var actual = gpu.Complete(); Assert.That(gpu.Status, Is.EqualTo(BakeSessionStatus.Completed)); Assert.That(gpu.Progress, Is.EqualTo(1));
                Assert.That(actual.Select(c=>c.FullGridIndex), Is.EqualTo(new[] {2,19,74}));
                for (int i = 0; i < actual.Length; i++) { CollectionAssert.AreEqual(new ushort[] {0,1,32},actual[i].Groups); Assert.That(actual[i].NeighborMask, Is.EqualTo(expected[i].NeighborMask)); }
                Assert.That(gpu.GetType().GetProperty("NativeBytes").GetValue(gpu), Is.EqualTo(0L));
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void CancelledGpuSessionCannotReturnPartialResults(bool running)
        {
            using var snapshot = Snapshot(); using var gpu = Backend().Begin(snapshot,new BakeBackendOptions {MaxCellsPerBatch = 1});
            if (running) gpu.Tick(); gpu.Cancel(); Assert.That(gpu.Status, Is.EqualTo(BakeSessionStatus.Cancelled));
            Assert.Throws<OperationCanceledException>(()=>gpu.Complete());
            Assert.That(gpu.GetType().GetProperty("NativeBytes").GetValue(gpu), Is.EqualTo(0L));
        }
        [Test]
        public void ImpossibleBudgetAndEarlySnapshotDisposalFaultWithoutOutput()
        {
            using (var snapshot = Snapshot()) using (var gpu = Backend().Begin(snapshot,new BakeBackendOptions {MemoryBudgetBytes = 1}))
            { Assert.Throws<InvalidOperationException>(()=>gpu.Tick()); Assert.That(gpu.Status, Is.EqualTo(BakeSessionStatus.Faulted)); }
            var early = Snapshot(); using var active = Backend().Begin(early); active.Tick(); early.Dispose();
            Assert.Throws<ObjectDisposedException>(()=>active.Complete()); Assert.That(active.Status, Is.EqualTo(BakeSessionStatus.Faulted));
            Assert.That(active.GetType().GetProperty("NativeBytes").GetValue(active), Is.EqualTo(0L));
        }
        [UnityTest]
        public IEnumerator EditorTicksCompleteGpuReadbacks()
        {
            using var snapshot = Snapshot(); using var gpu = Backend().Begin(snapshot,new BakeBackendOptions {MaxCellsPerBatch = 1});
            double deadline = EditorApplication.timeSinceStartup+20;
            while ((gpu.Status == BakeSessionStatus.Pending || gpu.Status == BakeSessionStatus.Running) && EditorApplication.timeSinceStartup < deadline)
            { gpu.Tick(); yield return null; }
            Assert.That(gpu.Status, Is.EqualTo(BakeSessionStatus.Completed)); Assert.That(gpu.Complete().Length, Is.EqualTo(3));
        }
        [TestCase("front",true)] [TestCase("back",false)] [TestCase("double-back",true)]
        [TestCase("additional",false)] [TestCase("near",false)] [TestCase("beyond-far",false)]
        [TestCase("within-far",true)] [TestCase("empty",false)] [TestCase("transparent-only",true)]
        public void AnalyticVisibilityIsCorrectIndependentlyOfTheSourceBaker(string scenario,bool visible)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            bool back = scenario == "back" || scenario == "double-back";
            var mesh = new Mesh {vertices = new[] {new Vector3(-2,-2,0),new Vector3(-2,2,0),new Vector3(2,2,0),new Vector3(2,-2,0)},triangles = back ? new[] {0,2,1,0,3,2} : new[] {0,1,2,0,2,3}};
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var target = new GameObject("analytic visibility"); SceneManager.MoveGameObjectToScene(target,scene);
                target.transform.position = new Vector3(0,0,scenario == "near" ? .00005f : scenario == "beyond-far" ? 21 : scenario == "within-far" ? 19 : 2);
                // Every point must lie inside the near radius. A large nearby
                // plane still has valid hits in the four side cubemap faces.
                if (scenario == "near") target.transform.localScale = Vector3.one*.00001f;
                target.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = target.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                var tag = target.AddComponent<PerfectCullingDOTS.Authoring.CullingBakeRendererTag>(); tag.DoubleSided = scenario == "double-back";
                tag.Surface = scenario == "transparent-only" ? PerfectCullingDOTS.Authoring.BakeSurfaceOverride.Transparent : PerfectCullingDOTS.Authoring.BakeSurfaceOverride.Opaque;
                var additional = Array.Empty<Renderer>();
                if (scenario == "additional")
                {
                    var wall = new GameObject("additional opaque wall"); SceneManager.MoveGameObjectToScene(wall,scene); wall.transform.position = Vector3.forward;
                    wall.AddComponent<MeshFilter>().sharedMesh = mesh; var wallRenderer = wall.AddComponent<MeshRenderer>(); wallRenderer.sharedMaterial = material; additional = new Renderer[] {wallRenderer};
                }
                using var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest {Profile = BakeInputProfile.ComputeSoftwareInput,
                    Groups = new[] {new BakeGroupSource {Key = new CullingGroupKey(new uint4(19,2,3,4),0),Renderers = scenario == "empty" ? Array.Empty<Renderer>() : new Renderer[] {renderer}}},
                    AdditionalOccluders = additional,Samples = new[] {new BakeSampleLocation {FullGridIndex = 7}},
                    Rays = new BakeRaySettings {Width = 9,Height = 11,Far = 20,Rotation = quaternion.identity,CellSize = new float3(1)}});
                var expected = visible ? new ushort[] {0} : Array.Empty<ushort>();
                foreach (var backend in new IBakeBackend[] {new SourceComputeJobsBackend(),Backend()})
                {
                    using var session = backend.Begin(snapshot); var cells = session.Complete(); Assert.That(cells.Length, Is.EqualTo(1),backend.Name);
                    Assert.That(cells[0].FullGridIndex, Is.EqualTo(7)); CollectionAssert.AreEqual(expected,cells[0].Groups,backend.Name+" "+scenario);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
    }
}
