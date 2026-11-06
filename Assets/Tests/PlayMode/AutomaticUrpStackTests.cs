using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
namespace CullingSystemDOTS.Rendering.Tests
{
    public class AutomaticUrpStackTests
    {
        [UnityTest] public IEnumerator OverlayWithLowerDepthUsesStackOrder() => Check(false);
        [UnityTest] public IEnumerator SharedOverlayConsumesASeparatePassForEachBase() => Check(true);
        private static IEnumerator Check(bool sharedOverlay)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var cameras = new List<Camera>(); var targets = new List<RenderTexture>();
            var actual = new List<Camera>(); var published = new List<ulong>();
            System.Action<ScriptableRenderContext,Camera> capture = (_,camera)=> {if (cameras.Contains(camera)) actual.Add(camera);};
            try
            {
                Camera Create(string name,int depth)
                {
                    var go = new GameObject(name); var camera = go.AddComponent<Camera>(); camera.depth = depth; camera.cullingMask = 0;
                    go.AddComponent<CullingCameraAuthoring>(); cameras.Add(camera); return camera;
                }
                var first = Create("PVS URP base",100); var overlay = Create("PVS URP overlay",-100);
                var data = overlay.GetUniversalAdditionalCameraData(); data.renderType = CameraRenderType.Overlay;
                first.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
                if (sharedOverlay)
                {
                    var second = Create("PVS URP second base",200); second.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
                }
                foreach (var camera in cameras.Where(c=>c!=overlay))
                {
                    var target = new RenderTexture(16,16,24); target.Create(); targets.Add(target); camera.targetTexture = target;
                }
                RenderPipelineManager.beginCameraRendering += capture;
                // The first context creates the owned adapter. A second context must
                // consume every pass, including a shared overlay's repeated callbacks.
                yield return null;
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>(); Assert.That(owner, Is.Not.Null);
                var adapter = owner.GetOrCreateRenderingAdapter(); adapter.ApplyingPass += (_,id)=>published.Add(id);
                actual.Clear(); published.Clear(); yield return null;
                Assert.That(adapter.LastFault, Is.Null.Or.Empty);
                CollectionAssert.AreEqual(sharedOverlay ? new[] {first,overlay,cameras[2],overlay} : new[] {first,overlay},actual);
                Assert.That(published.Count, Is.GreaterThanOrEqualTo(actual.Count),"Every actual camera callback must consume its ordered publication.");
                actual.Clear(); published.Clear(); overlay.enabled = false; yield return null;
                Assert.That(adapter.LastFault, Is.Null.Or.Empty);
                Assert.That(actual.Contains(overlay), Is.False);
                Assert.That(published.Count, Is.GreaterThanOrEqualTo(actual.Count));
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= capture;
                foreach (var camera in cameras) if (camera != null) Object.DestroyImmediate(camera.gameObject);
                var apply = world.GetExistingSystemManaged<CullingApplySystem>(); if (apply != null) world.DestroySystemManaged(apply);
                var captureSystem = world.GetExistingSystemManaged<CullingViewCaptureSystem>(); if (captureSystem != null) world.DestroySystemManaged(captureSystem);
                var owner = world.GetExistingSystemManaged<CullingWorldSystem>(); if (owner != null) world.DestroySystemManaged(owner);
                foreach (var target in targets) {target.Release();Object.DestroyImmediate(target);}
            }
        }
    }
}
