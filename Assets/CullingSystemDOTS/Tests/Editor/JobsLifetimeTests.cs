using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Editor.Baking;
using CullingSystemDOTS.Editor.Baking.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace CullingSystemDOTS.Tests
{
    public class JobsLifetimeTests
    {
        private static BakeSnapshot Snapshot()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh {vertices = new[] {new Vector3(-4,-4,0),new Vector3(-4,4,0),new Vector3(4,4,0),new Vector3(4,-4,0)},triangles = new[] {0,1,2,0,2,3}};
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var groups = new BakeGroupSource[40];
                for (int i = 0; i < groups.Length; i++)
                {
                    var go = new GameObject("Binary BVH lifetime renderer "+i); SceneManager.MoveGameObjectToScene(go,scene); go.transform.position = new Vector3((i%2)*.1f,0,1+i%5);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    go.AddComponent<CullingBakeRendererTag>().Surface = i%9 == 0 ? BakeSurfaceOverride.Transparent : BakeSurfaceOverride.Opaque;
                    groups[i] = new BakeGroupSource {Key = new CullingGroupKey(new uint4(91,2,3,4),(ushort)i),Renderers = new Renderer[] {renderer}};
                }
                return MeshSnapshotExtractor.Capture(new BakeSnapshotRequest {Profile = BakeInputProfile.ComputeSoftwareInput,Groups = groups,
                    Rays = new BakeRaySettings {Width = 8,Height = 9,Far = 30,CellSize = new float3(2,1,3),Rotation = quaternion.identity},
                    Samples = new[] {new BakeSampleLocation {FullGridIndex = 2},new BakeSampleLocation {FullGridIndex = 19,Position = new float3(.25f,0,0)},new BakeSampleLocation {FullGridIndex = 74,Position = new float3(0,.1f,0)}}});
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
        [TestCase(1)] [TestCase(2)]
        public void BurstMatchesManagedReferenceAndReleasesEachBoundedBatch(int workers)
        {
            int previous = JobsUtility.JobWorkerCount;
            try
            {
                JobsUtility.JobWorkerCount = workers; using var snapshot = Snapshot(); var expected = ReferenceCpuTraversal.Bake(snapshot);
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    using var session = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions {MemoryBudgetBytes = 1024*1024,MaxCellsPerBatch = 1});
                    var actual = session.Complete(); var handle = (BakeJobHandle)session;
                    Assert.That(handle.RanInBurst, Is.True); Assert.That(handle.CompletedBatchCount, Is.EqualTo(3)); Assert.That(handle.NativeBytes, Is.Zero);
                    Assert.That(handle.PeakNativeBytes, Is.Positive.And.LessThanOrEqualTo(1024*1024)); Assert.That(session.Progress, Is.EqualTo(1));
                    for (int i = 0; i < actual.Length; i++)
                    { Assert.That(actual[i].FullGridIndex, Is.EqualTo(expected[i].FullGridIndex)); CollectionAssert.AreEqual(expected[i].Groups,actual[i].Groups); Assert.That(actual[i].NeighborMask, Is.EqualTo(expected[i].NeighborMask)); }
                }
            }
            finally { JobsUtility.JobWorkerCount = previous; }
        }
        [TestCase(false)] [TestCase(true)]
        public void CancellationJoinsPendingReadersAndCannotPublish(bool running)
        {
            using var snapshot = Snapshot(); var session = new JobsBakeBackend().Begin(snapshot);
            try
            {
                if (running) { session.Tick(); Assert.That(((BakeJobHandle)session).NativeBytes, Is.Positive); }
                session.Cancel(); Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Cancelled)); Assert.That(((BakeJobHandle)session).NativeBytes, Is.Zero);
                Assert.Throws<OperationCanceledException>(()=>session.Complete());
            }
            finally { session.Dispose(); session.Dispose(); }
        }
        [Test]
        public void EarlySnapshotDisposalAndBudgetFaultsReleaseCompatibilityBuffers()
        {
            using (var snapshot = Snapshot())
            using (var session = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions {MemoryBudgetBytes = 1}))
            {
                Assert.Throws<InvalidOperationException>(()=>session.Tick()); Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Faulted)); Assert.That(((BakeJobHandle)session).NativeBytes, Is.Zero);
            }
            var early = Snapshot(); using var active = new JobsBakeBackend().Begin(early); active.Tick(); early.Dispose();
            Assert.Throws<ObjectDisposedException>(()=>active.Complete()); Assert.That(active.Status, Is.EqualTo(BakeSessionStatus.Faulted)); Assert.That(((BakeJobHandle)active).NativeBytes, Is.Zero);
        }
        [UnityTest]
        public IEnumerator EditorTicksAdvanceCompatibilityBatchesWithoutCallerCompletion()
        {
            using var snapshot = Snapshot(); using var session = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions {MaxCellsPerBatch = 1});
            double deadline = EditorApplication.timeSinceStartup+10; float previous = 0;
            while ((session.Status == BakeSessionStatus.Pending || session.Status == BakeSessionStatus.Running) && EditorApplication.timeSinceStartup < deadline)
            { session.Tick(); Assert.That(session.Progress, Is.GreaterThanOrEqualTo(previous)); previous = session.Progress; yield return null; }
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Completed)); Assert.That(((BakeJobHandle)session).RanInBurst, Is.True);
        }

        [Test]
        public void BackgroundPreparationChecksBudgetBeforeBuildingAndJoinsOnCancellation()
        {
            using var snapshot = Snapshot();
            using (var insufficient = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { MemoryBudgetBytes = 1, BackgroundPreparation = true }))
            {
                Assert.Throws<InvalidOperationException>(() => insufficient.Tick());
                Assert.That(insufficient.Status, Is.EqualTo(BakeSessionStatus.Faulted));
                Assert.That(((BakeJobHandle)insufficient).NativeBytes, Is.Zero);
            }
            using var cancelled = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { BackgroundPreparation = true });
            cancelled.Tick(); cancelled.Cancel();
            Assert.That(cancelled.Status, Is.EqualTo(BakeSessionStatus.Cancelled));
            Assert.That(((BakeJobHandle)cancelled).NativeBytes, Is.Zero);
            Assert.Throws<OperationCanceledException>(() => cancelled.Complete());
        }

        [Test]
        public void SnapshotDisposalJoinsBackgroundBuilderBeforeFreeingItsArrays()
        {
            var snapshot = Snapshot();
            using var session = new JobsBakeBackend().Begin(snapshot,new BakeBackendOptions { BackgroundPreparation = true });
            session.Tick(); snapshot.Dispose();
            Assert.Throws<ObjectDisposedException>(() => session.Complete());
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Faulted));
            Assert.That(((BakeJobHandle)session).NativeBytes, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void BackgroundPreparationMatchesTheDirectBackend(bool gpu)
        {
            if (gpu && !CullingSystemDOTS.Editor.Baking.Gpu.ComputeBakeBackend.IsSupported) Assert.Ignore("Compute/readback unavailable.");
            using var snapshot = Snapshot();
            IBakeBackend backend = gpu ? (IBakeBackend)new CullingSystemDOTS.Editor.Baking.Gpu.ComputeBakeBackend() : new JobsBakeBackend();
            using var direct = backend.Begin(snapshot); var expected = direct.Complete();
            using var background = backend.Begin(snapshot,new BakeBackendOptions { BackgroundPreparation = true, MaxCellsPerBatch = 1 });
            var actual = background.Complete();
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.That(actual[i].FullGridIndex, Is.EqualTo(expected[i].FullGridIndex));
                Assert.That(actual[i].NeighborMask, Is.EqualTo(expected[i].NeighborMask));
                CollectionAssert.AreEqual(expected[i].Groups,actual[i].Groups);
            }
        }
    }
}
