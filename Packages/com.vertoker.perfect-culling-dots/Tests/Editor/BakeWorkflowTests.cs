using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Editor.Baking;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace PerfectCullingDOTS.Tests
{
    public class BakeWorkflowTests
    {
        private Scene scene;
        private CullingVolumeAuthoring volume;
        private BakeSettingsAsset settings;
        private Mesh mesh;
        private Material material;
        private string output;
        [SetUp] public void SetUp()
        {
            output = "Assets/PvsWorkflow_"+Guid.NewGuid().ToString("N")+".asset";
            scene = EditorSceneManager.NewPreviewScene(); var root = new GameObject("New PVS volume"); SceneManager.MoveGameObjectToScene(root,scene);
            volume = root.AddComponent<CullingVolumeAuthoring>(); volume.Size = new Vector3(4,2,2);
            settings = ScriptableObject.CreateInstance<BakeSettingsAsset>(); settings.CellSize = new Vector3(2,2,2); settings.Resolution = 8; settings.Far = 20; settings.MaxCellsPerBatch = 1;
            mesh = new Mesh { vertices = new[] { new Vector3(-8,-8,0),new Vector3(-8,8,0),new Vector3(8,8,0),new Vector3(8,-8,0) },triangles = new[] {0,1,2,0,2,3} };
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var go = new GameObject("Group 0"); go.transform.SetParent(root.transform,false); go.transform.localPosition = Vector3.forward;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            var group = go.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = 0; group.Renderers = new Renderer[] {renderer};
        }
        [TearDown] public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); AssetDatabase.DeleteAsset(output);
        }
        private void SelectMode(BakeExecutionMode mode)
        {
            var field = typeof(BakeSettingsAsset).GetField("ExecutionMode");
            Assert.That(field, Is.Not.Null,"Bake execution mode has not been integrated."); field.SetValue(settings,mode);
        }
        [TestCase(BakeExecutionMode.Auto)] [TestCase(BakeExecutionMode.Cpu)] [TestCase(BakeExecutionMode.Gpu)]
        public void SelectedBackendPublishesTheSameRuntimeCellsAndKeepsItsEffectiveName(BakeExecutionMode mode)
        {
            SelectMode(mode);
            bool hostDirty = SceneManager.GetActiveScene().isDirty;
            using var controller = new BakeController(volume,settings); controller.Start();
            string expectedBackend = mode == BakeExecutionMode.Cpu || !PerfectCullingDOTS.Editor.Baking.Gpu.ComputeBakeBackend.IsSupported ? "CPU Jobs/Burst" : "GPU compute";
            Assert.That(controller.BackendName, Does.StartWith(expectedBackend)); controller.Complete();
            Assert.That(controller.BackendName, Does.StartWith(expectedBackend),"Effective backend must survive session disposal.");
            Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Ready)); Assert.That(controller.Progress, Is.EqualTo(1));
            var asset = controller.Publish(output); Assert.That(volume.Bake, Is.SameAs(asset));
            Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Published));
            Resources.UnloadAsset(asset); asset = AssetDatabase.LoadAssetAtPath<PerfectCullingDOTS.Runtime.CullingBakeAsset>(output);
            volume.Bake = asset;
            Assert.That(asset.InputFingerprint, Is.Not.Null.And.Not.Empty);
            Assert.That(SceneBakeCollector.IsBakeCurrent(volume,settings), Is.True);
            settings.Far = 10; Assert.That(SceneBakeCollector.IsBakeCurrent(volume,settings), Is.False); settings.Far = 20;
            Assert.That(asset.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
            using (blob)
            {
                Assert.That(blob.Value.Cells.Length, Is.EqualTo(2)); using var decoded = new NativeList<ushort>(Allocator.Temp); var groups = decoded;
                for (int i = 0; i < 2; i++) { groups.Clear(); PerfectCullingDOTS.Core.LegacyCellDecoder.DecodeCell(ref blob.Value,i,ref groups); CollectionAssert.AreEqual(new ushort[] {0},groups.AsArray().ToArray()); }
            }
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.EqualTo(hostDirty));
        }
        [Test]
        public void ModeChangeInvalidatesReadyBakeAndPreservesPreviouslyPublishedAsset()
        {
            SelectMode(BakeExecutionMode.Cpu);
            using (var initial = new BakeController(volume,settings)) { initial.Start(); initial.Complete(); initial.Publish(output); }
            var previous = volume.Bake; byte[] bytes = File.ReadAllBytes(output);
            using var next = new BakeController(volume,settings); next.Start(); next.Complete(); SelectMode(BakeExecutionMode.Auto);
            Assert.Throws<InvalidOperationException>(()=>next.Publish(output));
            Assert.That(next.Status, Is.EqualTo(BakeControllerStatus.Invalidated)); Assert.That(volume.Bake, Is.SameAs(previous)); CollectionAssert.AreEqual(bytes,File.ReadAllBytes(output));
        }
        [Test]
        public void FailedExecutionCannotReplaceExistingBake()
        {
            SelectMode(BakeExecutionMode.Cpu);
            using (var initial = new BakeController(volume,settings)) { initial.Start(); initial.Complete(); initial.Publish(output); }
            var previous = volume.Bake; byte[] bytes = File.ReadAllBytes(output); settings.MemoryBudgetMiB = 0;
            using var next = new BakeController(volume,settings); Assert.Throws<ArgumentException>(()=>next.Start());
            Assert.That(next.Status, Is.EqualTo(BakeControllerStatus.Faulted)); Assert.Throws<InvalidOperationException>(()=>next.Publish(output));
            Assert.That(volume.Bake, Is.SameAs(previous)); CollectionAssert.AreEqual(bytes,File.ReadAllBytes(output));
        }
        [Test]
        public void DefaultComputeWorkflowKeepsTheSourceCompressedTraversalTie()
        {
            var second = new GameObject("Coincident original group 1"); second.transform.SetParent(volume.transform,false); second.transform.localPosition = Vector3.forward;
            second.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = second.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            var group = second.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = 1; group.Renderers = new Renderer[] {renderer};
            using var controller = new BakeController(volume,settings); controller.Start(); controller.Complete(); var asset = controller.Publish(output);
            Assert.That(asset.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
            using (blob) using (var decoded = new NativeList<ushort>(Allocator.Temp))
            {
                var groups = decoded;
                for (int i = 0; i < 2; i++) { groups.Clear(); PerfectCullingDOTS.Core.LegacyCellDecoder.DecodeCell(ref blob.Value,i,ref groups); CollectionAssert.AreEqual(new ushort[] {1},groups.AsArray().ToArray()); }
            }
        }
        [Test]
        public void SettingsChangeInvalidatesBakeBeforePublication()
        {
            using var controller = new BakeController(volume,settings); controller.Start(); controller.Complete(); settings.Far = 10;
            Assert.Throws<InvalidOperationException>(()=>controller.Publish(output));
            Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Invalidated)); Assert.That(File.Exists(output), Is.False); Assert.That(volume.Bake, Is.Null);
        }
        [Test]
        public void GeometryChangeInvalidatesBakeBeforePublication()
        {
            using var controller = new BakeController(volume,settings); controller.Start(); controller.Complete();
            var vertices = mesh.vertices; vertices[0] *= 2; mesh.vertices = vertices;
            Assert.Throws<InvalidOperationException>(()=>controller.Publish(output)); Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Invalidated));
            Assert.That(File.Exists(output), Is.False);
        }
        [Test]
        public void CancelledControllerDoesNotPublishAndCanBeDisposedRepeatedly()
        {
            var controller = new BakeController(volume,settings); controller.Start(); controller.Cancel();
            Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Cancelled));
            Assert.Throws<InvalidOperationException>(()=>controller.Publish(output)); Assert.That(File.Exists(output), Is.False);
            controller.Dispose(); controller.Dispose();
        }
        [UnityTest]
        public IEnumerator EditorTicksAdvanceBatchesWithoutCallingComplete()
        {
            using var controller = new BakeController(volume,settings); controller.Start();
            double deadline = EditorApplication.timeSinceStartup+10; float progress = 0;
            while (controller.Status == BakeControllerStatus.Running && EditorApplication.timeSinceStartup < deadline)
            {
                controller.Tick(); Assert.That(controller.Progress, Is.GreaterThanOrEqualTo(progress)); progress = controller.Progress; yield return null;
            }
            Assert.That(controller.Status, Is.EqualTo(BakeControllerStatus.Ready)); Assert.That(controller.Progress, Is.EqualTo(1));
        }
    }
}
