using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    public class IndirectTransportProbeTests
    {
        [UnityTest]
        public IEnumerator GpuArgumentCountControlsEachViewWithoutReadback()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube); source.SetActive(false);
            var material = new Material(Shader.Find("Hidden/CullingSystemDOTS/IndirectTransport"));
            var cameras = new Camera[2]; var targets = new RenderTexture[2];
            IndirectTransportProbe probe = null;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    cameras[i] = new GameObject("Owned indirect view " + i).AddComponent<Camera>();
                    cameras[i].transform.position = new Vector3(0, 0, -5); cameras[i].depth = 100 + i;
                    cameras[i].cullingMask = 1 << 31; cameras[i].clearFlags = CameraClearFlags.SolidColor; cameras[i].backgroundColor = Color.black;
                    targets[i] = new RenderTexture(128, 128, 24); targets[i].Create(); cameras[i].targetTexture = targets[i];
                }
                probe = new IndirectTransportProbe(source.GetComponent<MeshFilter>().sharedMesh, material, Matrix4x4.identity, cameras);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[0]).r, Is.GreaterThan(200), "Indirect count one must render first.");
                probe.Hidden[0] = true;
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[0]).r, Is.LessThan(5), "GPU-written zero count must remove color.");
                Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[1]).r, Is.GreaterThan(200), "Other view must keep its own count.");
                probe.Hidden[0] = false; probe.Hidden[1] = true;
                for (int i = 0; i < 10; i++)
                {
                    cameras[0].transform.position += new Vector3(.01f, 0, 0);
                    cameras[0].transform.rotation = Quaternion.Euler(0, i * .02f, 0);
                    yield return null;
                }
                Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[0]).r, Is.GreaterThan(200), "Moving view must consume restored GPU count.");
                Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[1]).r, Is.LessThan(5));
                for (int i = 0; i < 8; i++)
                {
                    probe.Hidden[0] = (i & 1) == 0; probe.Hidden[1] = !probe.Hidden[0];
                    cameras[0].transform.position += new Vector3(.005f, 0, 0);
                    yield return null;
                    Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[0]).r > 200, Is.EqualTo(!probe.Hidden[0]), "Current-frame count, view 0, edit " + i);
                    Assert.That(DynamicOcclusionIntegrationProbeTests.Center(targets[1]).r > 200, Is.EqualTo(!probe.Hidden[1]), "Current-frame count, view 1, edit " + i);
                }
                probe.Dispose();
                for (int i = 0; i < 120 && !probe.ResourcesReleased; i++) yield return null;
                Assert.That(probe.ResourcesReleased, Is.True, "Retirement must cover queued color readers.");
            }
            finally
            {
                probe?.Dispose();
                for (int i = 0; i < 2; i++)
                {
                    if (cameras[i] != null) { cameras[i].targetTexture = null; Object.Destroy(cameras[i].gameObject); }
                    if (targets[i] != null) { targets[i].Release(); Object.Destroy(targets[i]); }
                }
                Object.Destroy(material); Object.Destroy(source);
            }
        }

        [UnityTest]
        public IEnumerator HiddenCameraDrawRetainsIndependentOrdinaryShadow()
        {
            var lights = new Dictionary<Light, bool>();
            var previousSun = RenderSettings.sun; var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            GameObject root = null; Material material = null; IndirectTransportProbe probe = null;
            var cameras = new Camera[2]; var targets = new RenderTexture[2];
            try
            {
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                { lights.Add(light, light.enabled); light.enabled = false; }
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                root = Object.Instantiate(Resources.Load<GameObject>("RenderParityStand"));
                Assert.That(root, Is.Not.Null);
                var caster = root.transform.Find("Legacy shadow caster").GetComponent<MeshRenderer>();
                caster.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                RenderSettings.sun = root.transform.Find("Stand directional light").GetComponent<Light>();
                for (int i = 0; i < 2; i++)
                {
                    cameras[i] = root.transform.Find("Shadow parity camera " + i).GetComponent<Camera>();
                    targets[i] = new RenderTexture(256, 256, 24); targets[i].Create(); cameras[i].targetTexture = targets[i];
                }
                for (int i = 0; i < 4; i++) yield return null;
                var ordinary = Pixels(targets[0]);
                caster.forceRenderingOff = true;
                for (int i = 0; i < 3; i++) yield return null;
                var bare = Pixels(targets[0]);
                Assert.That(Differences(ordinary, bare), Is.GreaterThan(0), "Fixture must show a visible shadow from its color-hidden caster.");
                material = new Material(Shader.Find("Hidden/CullingSystemDOTS/IndirectTransport"));
                probe = new IndirectTransportProbe(caster.GetComponent<MeshFilter>().sharedMesh, material, caster.transform.localToWorldMatrix, cameras);
                probe.Hidden[0] = probe.Hidden[1] = true; probe.CastShadows = true;
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(Differences(ordinary, Pixels(targets[0])), Is.Zero, "Full shadow arguments must match the ordinary caster despite zero color counts.");
                probe.Dispose();
                for (int i = 0; i < 120 && !probe.ResourcesReleased; i++) yield return null;
                Assert.That(probe.ResourcesReleased, Is.True, "Retirement must follow shadow consumers too.");
            }
            finally
            {
                probe?.Dispose();
                for (int i = 0; i < 2; i++)
                {
                    if (cameras[i] != null) cameras[i].targetTexture = null;
                    if (targets[i] != null) { targets[i].Release(); Object.Destroy(targets[i]); }
                }
                if (root != null) { root.SetActive(false); Object.Destroy(root); }
                if (material != null) Object.Destroy(material);
                foreach (var pair in lights) if (pair.Key != null) pair.Key.enabled = pair.Value;
                RenderSettings.sun = previousSun; RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbient;
            }
        }

        private static Color32[] Pixels(RenderTexture target)
        {
            var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        [Test]
        public void IndirectConstructorFailureReleasesPartialArguments()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube); source.SetActive(false);
            var material = new Material(Shader.Find("Hidden/CullingSystemDOTS/IndirectTransport"));
            var camera = new GameObject("Owned indirect failure camera").AddComponent<Camera>(); camera.enabled = false;
            GraphicsBuffer first = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() => new IndirectTransportProbe(source.GetComponent<MeshFilter>().sharedMesh,
                    material, Matrix4x4.identity, new[] { camera }, () => {
                        if (first != null) throw new InvalidOperationException("Owned indirect allocation failure");
                        return first = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw,
                            1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                    }));
                Assert.That(first.IsValid(), Is.False, "Failed construction must release the full-shadow argument allocation.");
            }
            finally { first?.Dispose(); Object.Destroy(camera.gameObject); Object.Destroy(material); Object.Destroy(source); }
        }

        private static int Differences(Color32[] a, Color32[] b)
        {
            Assert.That(b.Length, Is.EqualTo(a.Length)); int differences = 0;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) differences++;
            return differences;
        }
    }
}
