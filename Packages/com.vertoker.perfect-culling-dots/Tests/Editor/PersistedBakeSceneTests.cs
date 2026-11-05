using System;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Core;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace PerfectCullingDOTS.Tests
{
    public class PersistedBakeSceneTests
    {
        private string folder;
        private Scene owned;
        [SetUp] public void SetUp()
        {
            var host = SceneManager.GetActiveScene();
            Assert.That(SceneManager.sceneCount, Is.EqualTo(1));
            Assert.That(host.path, Is.Empty, "Requires the Test Framework's disposable host.");
            Assert.That(host.isDirty, Is.False, "Do not discard user work.");
            Assert.That(host.GetRootGameObjects().Length, Is.EqualTo(2));
            folder = "Assets/PvsScene_"+Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets",folder.Substring(7));
            owned = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(owned,folder+"/Main.unity"), Is.True);
        }
        [TearDown] public void TearDown()
        {
            if (owned.IsValid()) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }
        private static CullingVolumeAuthoring Volume()
        {
            var volume = new GameObject("PVS volume").AddComponent<CullingVolumeAuthoring>(); volume.Size = new Vector3(4,2,2); return volume;
        }
        private BakeSettingsAsset Settings()
        {
            var settings = ScriptableObject.CreateInstance<BakeSettingsAsset>(); settings.CellSize = new Vector3(2,2,2); settings.Resolution = 8; settings.Far = 20;
            AssetDatabase.CreateAsset(settings,folder+"/Settings.asset"); return settings;
        }
        [Test]
        public void RegionsAndExplicitGroupsInOtherLoadedScenesKeepOriginalIndices()
        {
            var volume = Volume(); var settings = Settings();
            var first = new GameObject("group0").AddComponent<CullingGroupAuthoring>(); first.Volume = volume; first.GroupIndex = 0;
            var other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(other);
            var second = new GameObject("group1").AddComponent<CullingGroupAuthoring>(); second.Volume = volume; second.GroupIndex = 1;
            var region = new GameObject("other scene region").AddComponent<CullingSamplingRegionAuthoring>();
            region.Mode = CullingSamplingRegionMode.Exclude; region.transform.position = Vector3.left; region.Size = Vector3.one;
            using var input = SceneBakeCollector.Capture(volume,settings);
            Assert.That(input.Mapping.Length, Is.EqualTo(2)); Assert.That(input.Mapping[1].GroupIndex, Is.EqualTo(1));
            Assert.That(input.Snapshot.Samples.Length, Is.EqualTo(1)); Assert.That(input.Snapshot.Samples[0].FullGridIndex, Is.EqualTo(1));
        }
        [Test]
        public void DisabledRegionOnActiveObjectStillParticipatesLikeSourceFindObjects()
        {
            var volume = Volume(); var settings = Settings();
            var region = new GameObject("disabled region").AddComponent<CullingSamplingRegionAuthoring>(); region.Mode = CullingSamplingRegionMode.Exclude;
            region.Size = Vector3.one; region.transform.position = Vector3.left; region.enabled = false;
            using var input = SceneBakeCollector.Capture(volume,settings);
            Assert.That(input.Snapshot.Samples.Length, Is.EqualTo(1)); Assert.That(input.Snapshot.Samples[0].FullGridIndex, Is.EqualTo(1));
            region.gameObject.SetActive(false);
            using var without = SceneBakeCollector.Capture(volume,settings); Assert.That(without.Snapshot.Samples.Length, Is.EqualTo(2));
        }
        [Test]
        public void NewSceneBakesWithJobsAndReopensWithIdenticalRuntimeDataAndReferences()
        {
            var volume = Volume(); var settings = Settings();
            var mesh = new Mesh { vertices = new[] { new Vector3(-8,-8,0),new Vector3(-8,8,0),new Vector3(8,8,0),new Vector3(8,-8,0) },triangles = new[] {0,1,2,0,2,3} };
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(mesh,folder+"/Quad.asset"); AssetDatabase.CreateAsset(material,folder+"/Material.mat");
            var go = new GameObject("original group zero"); go.transform.position = Vector3.forward;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            var group = go.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = 0; group.Renderers = new Renderer[] {renderer};
            Assert.That(EditorSceneManager.SaveScene(owned), Is.True);
            using (var controller = new BakeController(volume,settings)) { controller.Start(); controller.Complete(); controller.Publish(folder+"/Bake.asset"); }
            var id = volume.Bake.VolumeId; var fingerprint = volume.Bake.InputFingerprint;
            Assert.That(EditorSceneManager.SaveScene(owned), Is.True);
            owned = EditorSceneManager.OpenScene(folder+"/Main.unity",OpenSceneMode.Single);
            settings = AssetDatabase.LoadAssetAtPath<BakeSettingsAsset>(folder+"/Settings.asset");
            volume = null; group = null;
            foreach (var root in owned.GetRootGameObjects())
            {
                if (root.TryGetComponent<CullingVolumeAuthoring>(out var v)) volume = v;
                if (root.TryGetComponent<CullingGroupAuthoring>(out var g)) group = g;
            }
            Assert.That(volume, Is.Not.Null); Assert.That(group.Volume, Is.SameAs(volume)); Assert.That(group.GroupIndex, Is.Zero);
            Assert.That(group.Renderers[0].GetComponent<MeshFilter>().sharedMesh, Is.SameAs(AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/Quad.asset")));
            Assert.That(volume.Bake.VolumeId, Is.EqualTo(id)); Assert.That(volume.Bake.InputFingerprint, Is.EqualTo(fingerprint));
            Assert.That(SceneBakeCollector.IsBakeCurrent(volume,settings), Is.True);
            Assert.That(volume.Bake.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
            using (blob)
            using (var decoded = new NativeList<ushort>(Allocator.Temp))
            {
                var output = decoded;
                Assert.That(blob.Value.Groups.Length, Is.EqualTo(1)); Assert.That(blob.Value.Groups[0].GroupIndex, Is.Zero);
                for (int i = 0; i < 2; i++) { output.Clear(); LegacyCellDecoder.DecodeCell(ref blob.Value,i,ref output); CollectionAssert.AreEqual(new ushort[] {0},output.AsArray().ToArray()); }
            }
            Assert.That(owned.isDirty, Is.False);
        }
    }
}
