using System;
using NUnit.Framework;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Editor.Baking;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace CullingSystemDOTS.Tests
{
    public class SceneBakeCollectorTests
    {
        private Scene owned;
        private BakeSettingsAsset settings;
        private CullingVolumeAuthoring volume;
        [SetUp] public void SetUp()
        {
            owned = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Fresh DOTS bake volume"); SceneManager.MoveGameObjectToScene(root,owned);
            volume = root.AddComponent<CullingVolumeAuthoring>(); volume.Size = new Vector3(4,2,2);
            volume.transform.SetPositionAndRotation(new Vector3(3,4,5),Quaternion.Euler(0,90,0));
            settings = ScriptableObject.CreateInstance<BakeSettingsAsset>(); settings.CellSize = new Vector3(2,2,2); settings.Resolution = 8;
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Group "+i); go.transform.SetParent(root.transform,false);
                var group = go.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = i;
            }
        }
        [TearDown] public void TearDown() { EditorSceneManager.ClosePreviewScene(owned); UnityEngine.Object.DestroyImmediate(settings); }
        [Test]
        public void CollectsExplicitStableIndicesAndSourceRotatedCellCentersWithoutDirtyingHost()
        {
            bool dirty = SceneManager.GetActiveScene().isDirty;
            using var input = SceneBakeCollector.Capture(volume,settings);
            Assert.That(input.Mapping.Length, Is.EqualTo(2)); Assert.That(input.Mapping[0].GroupIndex, Is.Zero);
            Assert.That(input.Mapping[1].GroupIndex, Is.EqualTo(1));
            Assert.That(input.Snapshot.Samples.Length, Is.EqualTo(2));
            Assert.That((Vector3)input.Snapshot.Samples[0].Position, Is.EqualTo(volume.transform.position+volume.transform.rotation*new Vector3(-1,0,0)));
            Assert.That((Vector3)input.Snapshot.Samples[1].Position, Is.EqualTo(volume.transform.position+volume.transform.rotation*new Vector3(1,0,0)));
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.EqualTo(dirty));
        }
        [Test]
        public void SettingsAndSceneChangesInvalidateTheFingerprint()
        {
            using var initial = SceneBakeCollector.Capture(volume,settings);
            using var repeat = SceneBakeCollector.Capture(volume,settings); Assert.That(repeat.Fingerprint, Is.EqualTo(initial.Fingerprint));
            settings.Far = 500;
            using var changedSettings = SceneBakeCollector.Capture(volume,settings); Assert.That(changedSettings.Fingerprint, Is.Not.EqualTo(initial.Fingerprint));
            settings.Far = 1000; volume.transform.position += Vector3.right;
            using var moved = SceneBakeCollector.Capture(volume,settings); Assert.That(moved.Fingerprint, Is.Not.EqualTo(initial.Fingerprint));
        }
        [Test]
        public void AlwaysIncludeRegionOverridesExcludeWithoutRenumberingTheCell()
        {
            var exclude = new GameObject("exclude"); SceneManager.MoveGameObjectToScene(exclude,owned);
            var excluded = exclude.AddComponent<CullingSamplingRegionAuthoring>(); excluded.Mode = CullingSamplingRegionMode.Exclude;
            excluded.Size = new Vector3(100,100,100); exclude.transform.position = volume.transform.position;
            var include = new GameObject("include"); SceneManager.MoveGameObjectToScene(include,owned);
            var included = include.AddComponent<CullingSamplingRegionAuthoring>(); included.Mode = CullingSamplingRegionMode.AlwaysInclude;
            included.Size = Vector3.one; include.transform.position = volume.transform.position+volume.transform.rotation*new Vector3(1,0,0);
            using var input = SceneBakeCollector.Capture(volume,settings);
            Assert.That(input.Snapshot.Samples.Length, Is.EqualTo(1)); Assert.That(input.Snapshot.Samples[0].FullGridIndex, Is.EqualTo(1));
        }
        [Test]
        public void GeometryEditsInvalidateAndExcludedRenderersKeepTheirGroupSlot()
        {
            var group = volume.GetComponentsInChildren<CullingGroupAuthoring>()[0];
            var go = group.gameObject; var mesh = new Mesh { vertices = new[] { Vector3.zero,Vector3.up,Vector3.right }, triangles = new[] {0,1,2} };
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; group.Renderers = new Renderer[] { renderer };
                using var initial = SceneBakeCollector.Capture(volume,settings);
                mesh.vertices = new[] { Vector3.zero,Vector3.up*2,Vector3.right };
                using var changed = SceneBakeCollector.Capture(volume,settings); Assert.That(changed.Fingerprint, Is.Not.EqualTo(initial.Fingerprint));
                go.AddComponent<CullingBakeRendererTag>().ExcludeFromBake = true;
                using var excluded = SceneBakeCollector.Capture(volume,settings);
                Assert.That(excluded.Mapping.Length, Is.EqualTo(2)); Assert.That(excluded.Snapshot.Instances.Length, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); }
        }
        [Test]
        public void AdditionalOccludersUseSourceUnrotatedVolumeBoundsAndDoNotBecomeGroups()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero,Vector3.up,Vector3.right }, triangles = new[] {0,1,2} };
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                Renderer Make(Vector3 position)
                {
                    var go = new GameObject("additional occluder"); SceneManager.MoveGameObjectToScene(go,owned);
                    go.transform.position = position; go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
                }
                // With a 90 degree rotated 4x2x2 volume this point lies outside its rotated box,
                // but inside the source's deliberately axis-aligned Bounds.
                var included = Make(volume.transform.position+new Vector3(1.5f,0,0));
                var outside = Make(volume.transform.position+new Vector3(100,0,0));
                volume.gameObject.AddComponent<CullingBakeInputsAuthoring>().AdditionalOccluders = new[] { outside,included,included };
                using var input = SceneBakeCollector.Capture(volume,settings);
                Assert.That(input.Mapping.Length, Is.EqualTo(2));
                Assert.That(input.Snapshot.Instances.Length, Is.EqualTo(1));
                Assert.That(input.Snapshot.Instances[0].GroupIndex, Is.EqualTo(-1));
                Assert.That(input.Snapshot.Instances[0].BoundsMin.x, Is.EqualTo(included.bounds.min.x).Within(.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); }
        }
        [Test]
        public void MissingOrDuplicateGroupIndicesAreDiagnosed()
        {
            var groups = volume.GetComponentsInChildren<CullingGroupAuthoring>(); groups[1].GroupIndex = 0;
            Assert.Throws<ArgumentException>(() => SceneBakeCollector.Capture(volume,settings));
            groups[1].GroupIndex = 2; Assert.Throws<ArgumentException>(() => SceneBakeCollector.Capture(volume,settings));
        }
    }
}
