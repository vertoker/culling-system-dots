using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Runtime;
using Unity.Entities;
using UnityEngine;
namespace PerfectCullingDOTS.Tests
{
    public class CameraDriverTests
    {
        [Test]
        public void CameraSettingsAndActualRenderOrderFlowIntoCapturedViews()
        {
            using var world = new World("Camera driver"); using var driver = new CullingCameraDriver(world);
            var a = new GameObject("view A"); var b = new GameObject("view B"); var unknown = new GameObject("unregistered camera");
            try
            {
                var ca = a.AddComponent<Camera>(); var cb = b.AddComponent<Camera>(); var cu = unknown.AddComponent<Camera>();
                var av = a.AddComponent<CullingCameraAuthoring>(); var bv = b.AddComponent<CullingCameraAuthoring>();
                av.Layers = 1u<<31; av.NeighborRadius = 2; av.Invert = true;
                driver.Register(av); driver.Register(bv);
                using var frame = driver.Capture(11,cb,cu,ca,cb);
                Assert.That(frame.Input.Views.Length, Is.EqualTo(2));
                Assert.That(frame.Input.Views[0].Layers, Is.EqualTo(1u<<31)); Assert.That(frame.Input.Views[0].NeighborRadius, Is.EqualTo(2)); Assert.That(frame.Input.Views[0].Invert, Is.True);
                Assert.That(frame.Input.Passes.Length, Is.EqualTo(4));
                CollectionAssert.AreEqual(new[] {1,-1,0,1},new[] {frame.Input.Passes[0].ViewIndex,frame.Input.Passes[1].ViewIndex,frame.Input.Passes[2].ViewIndex,frame.Input.Passes[3].ViewIndex});
                ulong previous = frame.Input.Views[0].InvalidationVersion;
                av.SetDirty(); using var dirty = driver.Capture(12,ca);
                Assert.That(dirty.Input.Views[0].InvalidationVersion, Is.GreaterThan(previous));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(unknown); }
        }
        [Test]
        public void DisabledOrRemovedViewsRestoreAndWorldDisposalCanPrecedeDriverDisposal()
        {
            var world = new World("Camera lifetime"); var driver = new CullingCameraDriver(world); var go = new GameObject("camera lifetime");
            try
            {
                go.AddComponent<Camera>(); var view = go.AddComponent<CullingCameraAuthoring>(); driver.Register(view);
                view.enabled = false;
                using (var frame = driver.Capture(1)) { Assert.That(frame.Input.Views.Length, Is.Zero); Assert.That(frame.Input.RestoreAllVisibility, Is.True); }
                view.enabled = true; driver.Register(view); driver.Unregister(view);
                using (var frame = driver.Capture(2)) Assert.That(frame.Input.Views.Length, Is.Zero);
                driver.Register(view); world.Dispose(); driver.Dispose(); driver.Dispose();
            }
            finally { Object.DestroyImmediate(go); if (world.IsCreated) world.Dispose(); driver.Dispose(); }
        }
    }
}
