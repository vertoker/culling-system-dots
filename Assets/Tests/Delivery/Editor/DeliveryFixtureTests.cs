using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Scenes;
namespace PerfectCullingDOTS.Delivery.Tests
{
    public class DeliveryFixtureTests
    {
        [Test]
        public void GeneratedSampleIsSelfContainedAndDoesNotSaveOrCloseTheHostScene()
        {
            string folder = "Assets/DeliveryFixture_"+Guid.NewGuid().ToString("N");
            var host = SceneManager.GetActiveScene(); bool dirty = host.isDirty; int count = SceneManager.sceneCount;
            var opened = default(Scene);
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("PerfectCullingDOTS.Delivery.Editor.DeliveryFixtureBuilder")).FirstOrDefault(t=>t != null);
                Assert.That(type, Is.Not.Null,"The sample fixture builder has not been implemented.");
                var create = type.GetMethod("CreateSample",BindingFlags.Public|BindingFlags.Static);
                string scenePath = (string)create.Invoke(null,new object[] {folder});
                Assert.That(SceneManager.sceneCount, Is.EqualTo(count)); Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(host)); Assert.That(host.isDirty, Is.EqualTo(dirty));
                Assert.That(File.Exists(scenePath), Is.True); Assert.That(File.Exists(folder+"/Geometry.unity"), Is.True);
                var bake = AssetDatabase.LoadAssetAtPath<PerfectCullingDOTS.Runtime.CullingBakeAsset>(folder+"/Bake.asset"); Assert.That(bake, Is.Not.Null); Assert.That(bake.GroupCount, Is.EqualTo(3));
                Assert.That(bake.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
                using (blob) { Assert.That(blob.Value.Cells.Length, Is.EqualTo(4)); Assert.That(blob.Value.CompressedBytes.Length, Is.Positive); }
                byte[] previous = File.ReadAllBytes(scenePath); var failure = Assert.Throws<TargetInvocationException>(()=>create.Invoke(null,new object[] {folder}));
                Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>()); CollectionAssert.AreEqual(previous,File.ReadAllBytes(scenePath));
                opened = EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
                var roots = opened.GetRootGameObjects(); Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Count(), Is.EqualTo(1));
                Assert.That(roots.SelectMany(r=>r.GetComponentsInChildren<Light>(true)).Count(), Is.EqualTo(1));
                var sub = roots.SelectMany(r=>r.GetComponentsInChildren<SubScene>(true)).Single(); Assert.That(AssetDatabase.GetAssetPath(sub.SceneAsset), Is.EqualTo(folder+"/Geometry.unity"));
            }
            finally { if (opened.IsValid()) EditorSceneManager.CloseScene(opened,true); AssetDatabase.DeleteAsset(folder); SceneManager.SetActiveScene(host); }
        }
    }
}
