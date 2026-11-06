using System.Collections;
using System.Linq;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Runtime;
using PerfectCullingDOTS.Sample;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace PerfectCullingDOTS.Delivery.Rendering.Tests
{
    public class SampleRuntimeTests
    {
        private Scene loaded;
        private RenderTexture target;
        private Camera camera;
        [UnityTest]
        public IEnumerator ImportedSampleLoadsLodMaterialBindingsAndActuallyCullsPixels()
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.FindAssets("Basic t:SceneAsset",new[] {"Assets"})
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>p.EndsWith("/Basic.unity") && (p.StartsWith("Assets/DeliverySample/Content/") || p.StartsWith("Assets/Samples/PerfectCullingDOTS/")));
            Assert.That(path, Is.Not.Null.And.Not.Empty,"Generate/import the owned Basic sample before this acceptance test.");
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Additive));
            loaded = SceneManager.GetSceneByPath(path);
#else
            Assert.Fail("This Editor acceptance test loads the imported sample by AssetDatabase. The standalone Player uses its runtime smoke contract.");
#endif
            camera = loaded.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single();
            camera.GetComponent<SampleCameraPath>().AnimateCamera = false; camera.transform.position = new Vector3(-2,0,-3);
            target = new RenderTexture(128,128,24); target.Create(); camera.targetTexture = target;
            var world = World.DefaultGameObjectInjectionWorld; var manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(CullingGroupDefinition));
            double deadline = Time.realtimeSinceStartupAsDouble+60;
            while (query.CalculateEntityCount() != 3 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(3),"Sample SubScene must load actual entities.");
            using (var groups = query.ToEntityArray(Allocator.Temp))
            {
                int lodBindings = -1;
                foreach (var group in groups) if (manager.GetComponentData<CullingGroupDefinition>(group).GroupIndex == 1)
                    lodBindings = manager.GetBuffer<CullingBakedRenderBinding>(group).Length;
                Assert.That(lodBindings, Is.EqualTo(4),"Two LODs × two material graphics entities must all remain bound.");
            }
            for (int i = 0; i < 6; i++) yield return null;
            int normal = Red(target,"normal"); Assert.That(normal, Is.GreaterThan(100),"The actual sample must render a red cube.");
            var culling = camera.GetComponent<CullingCameraAuthoring>(); culling.Invert = true;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Red(target,"inverted"), Is.LessThan(normal/2),"Inversion must hide the previously visible red renderers.");
            culling.Invert = false; for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Red(target,"restored"), Is.GreaterThan(normal/2));
            Assert.That(world.GetExistingSystemManaged<CullingWorldSystem>().GetOrCreateRenderingAdapter().LastFault, Is.Null.Or.Empty);
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (loaded.IsValid() && loaded.isLoaded) yield return SceneManager.UnloadSceneAsync(loaded);
        }
        private static int Red(RenderTexture target,string label)
        {
            var previous = RenderTexture.active; var image = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                System.IO.Directory.CreateDirectory("Temp/DeliverySample"); System.IO.File.WriteAllBytes("Temp/DeliverySample/"+label+".png",image.EncodeToPNG());
                return image.GetPixels32().Count(c=>c.r > 40 && c.r > c.g*1.6f && c.r > c.b*1.6f);
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
    }
}
