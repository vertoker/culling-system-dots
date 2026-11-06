using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Rendering;
using CullingSystemDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Rendering.Tests
{
    public class ShadowRenderParityTests
    {
        [UnityTest]
        public IEnumerator HiddenShadowCasterMatchesLegacyInBothCameraOrders()
        {
            foreach (CullingRenderToggleMode toggle in Enum.GetValues(typeof(CullingRenderToggleMode)))
            foreach (ShadowCastingMode shadow in Enum.GetValues(typeof(ShadowCastingMode)))
            foreach (bool reverse in new[] { false, true })
            {
                using var stand = new Stand(toggle, shadow, reverse);
                stand.Renderer.forceRenderingOff = true;
                for (int i = 0; i < 3; i++) yield return null;
                var bare = Pixels(stand.Targets[0]);
                stand.Renderer.forceRenderingOff = false;
                RenderPipelineManager.beginCameraRendering += stand.ToggleReference;
                for (int i = 0; i < 3; i++) yield return null;
                var expected = new[] { Pixels(stand.Targets[0]), Pixels(stand.Targets[1]) };
                RenderPipelineManager.beginCameraRendering -= stand.ToggleReference;
                if (toggle == CullingRenderToggleMode.ShadowcastMode && (shadow == ShadowCastingMode.On || shadow == ShadowCastingMode.ShadowsOnly))
                    Assert.That(Differences(bare, expected[0]), Is.GreaterThan(0), "Stand must produce an actual hidden-object shadow: " + shadow);
                else
                    Assert.That(Differences(bare, expected[0]), Is.Zero, "Hidden Off/TwoSided must leave the bare receiver.");
                stand.Renderer.gameObject.SetActive(false);
                stand.CreateEntities();
                for (int i = 0; i < 2; i++) yield return null;
                stand.CreateAdapter();
                for (int i = 0; i < 3; i++) { stand.Publish(); yield return null; }
                for (int camera = 0; camera < 2; camera++)
                {
                    var actual = Pixels(stand.Targets[camera]);
                    int differences = Differences(expected[camera], actual);
                    string label = (toggle == CullingRenderToggleMode.ShadowcastMode ? "" : toggle + "-") + shadow + "-" + reverse + "-" + camera;
                    Save(expected[camera], "reference-" + label); Save(actual, "dots-" + label);
                    Assert.That(differences, Is.Zero, $"Image parity: toggle={toggle}, shadow={shadow}, reversed={reverse}, camera={camera}; artifacts: Temp/RenderParity");
                }
                Assert.That(stand.Adapter.LastFault, Is.Null);

                TestContext.Out.WriteLine($"Exact two-camera image parity: toggle={toggle}, shadow={shadow}, reverse={reverse}, 256x256, all pixels equal.");
            }
        }

        private static Color32[] Pixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        private static int Differences(Color32[] a, Color32[] b)
        {
            Assert.That(a.Length, Is.EqualTo(b.Length));
            int count = 0;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) count++;
            return count;
        }

        private static void Save(Color32[] pixels, string name)
        {
            Directory.CreateDirectory("Temp/RenderParity");
            var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            try { image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes("Temp/RenderParity/" + name + ".png", image.EncodeToPNG()); }
            finally { Object.Destroy(image); }
        }

        private sealed class Stand : IDisposable
        {
            public readonly Camera[] Cameras = new Camera[2];
            public readonly RenderTexture[] Targets = new RenderTexture[2];
            public readonly MeshRenderer Renderer;
            public EcsRenderVisibilityAdapter Adapter;
            private readonly List<GameObject> objects = new List<GameObject>();
            private readonly Dictionary<Light, bool> previousLights = new Dictionary<Light, bool>();
            private readonly AmbientMode previousAmbient = RenderSettings.ambientMode;
            private readonly Color previousAmbientLight = RenderSettings.ambientLight;
            private readonly Light previousSun = RenderSettings.sun;
            private readonly World world = World.DefaultGameObjectInjectionWorld;
            private readonly ShadowCastingMode shadow;
            private readonly CullingRenderToggleMode toggle;
            private readonly bool reverse;
            private readonly RenderMeshDescription description;
            private Entity main, companion;
            private CullingFrameState frame;
            private NativeArray<RenderVolumeRange> volumes;
            private NativeArray<RenderGroupBinding> bindings;

            public Stand(CullingRenderToggleMode toggle, ShadowCastingMode shadow, bool reverse)
            {
                this.toggle = toggle; this.shadow = shadow; this.reverse = reverse;
                var prefab = Resources.Load<GameObject>("RenderParityStand");
                Assert.That(prefab, Is.Not.Null, "The serialized stand must be present; tests never create/save scenes.");
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                { previousLights.Add(light, light.enabled); light.enabled = false; }
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                var root = Object.Instantiate(prefab); objects.Add(root);
                Renderer = root.transform.Find("Legacy shadow caster").GetComponent<MeshRenderer>();
                Renderer.shadowCastingMode = shadow;
                description = new RenderMeshDescription(Renderer);
                RenderSettings.sun = root.transform.Find("Stand directional light").GetComponent<Light>();
                for (int i = 0; i < 2; i++)
                {
                    var camera = root.transform.Find("Shadow parity camera " + i).GetComponent<Camera>(); Cameras[i] = camera;
                    camera.depth = reverse ? 1 - i : i;
                    var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create();
                    Targets[i] = target; camera.targetTexture = target;
                }
            }

            public void ToggleReference(ScriptableRenderContext context, Camera camera)
            {
                if (camera != Cameras[0] && camera != Cameras[1]) return;
                bool visible = camera == Cameras[1];
                switch (toggle)
                {
                    case CullingRenderToggleMode.ShadowcastMode:
                        Renderer.enabled = visible || shadow == ShadowCastingMode.On || shadow == ShadowCastingMode.ShadowsOnly;
                        Renderer.shadowCastingMode = !visible && shadow == ShadowCastingMode.On ? ShadowCastingMode.ShadowsOnly : shadow;
                        break;
                    case CullingRenderToggleMode.RendererComponent: Renderer.enabled = visible; break;
                    case CullingRenderToggleMode.ForceRenderingOff: Renderer.forceRenderingOff = !visible; break;
                }
            }

            public void CreateEntities()
            {
                var manager = world.EntityManager;
                var mesh = Renderer.GetComponent<MeshFilter>().sharedMesh;
                var array = new RenderMeshArray(new[] { Renderer.sharedMaterial }, new[] { mesh });
                main = manager.CreateEntity();
                RenderMeshUtility.AddComponents(main, manager, in description, array, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                manager.SetComponentData(main, new LocalToWorld { Value = Renderer.transform.localToWorldMatrix });
                if (toggle == CullingRenderToggleMode.ShadowcastMode && shadow == ShadowCastingMode.On)
                {
                    companion = world.GetOrCreateSystemManaged<CullingWorldSystem>().ShadowCompanions.GetOrCreate(main);
                }
            }

            public void CreateAdapter()
            {
                frame = CullingFrameState.Create(1, 2, 1, 2, Allocator.Persistent);
                var key = new CullingGroupKey(new uint4(1, 0, 0, 0), 0);
                frame.Groups[0] = new CullingGroupSnapshot { Key = key, StateGeneration = 1, InitialApplied = 1 };
                frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                for (int p = 0; p < 2; p++)
                {
                    int c = reverse ? 1 - p : p;
                    frame.Passes[p] = new CullingViewPass { ViewIndex = c, RenderViewId = (ulong)c + 1 };
                    frame.PassStates[p] = (byte)c; frame.PassVolumeApplications[p] = 1;
                }
                volumes = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = 1 } }, Allocator.Persistent);
                bindings = new NativeArray<RenderGroupBinding>(new[] { new RenderGroupBinding { Group = key, Main = main, ShadowOnly = companion,
                    OriginalShadowMode = shadow, ToggleMode = toggle, InitialRendererEnabled = true } }, Allocator.Persistent);
                Adapter = new EcsRenderVisibilityAdapter(world);
                Adapter.RegisterCamera(Cameras[0], 1); Adapter.RegisterCamera(Cameras[1], 2);
            }

            public void Publish() => Adapter.Publish(in frame, volumes, bindings);

            public void Dispose()
            {
                RenderPipelineManager.beginCameraRendering -= ToggleReference;
                Adapter?.Dispose();
                if (world != null && world.IsCreated)
                {
                    var manager = world.EntityManager; manager.CompleteAllTrackedJobs();
                    if (manager.Exists(main)) manager.DestroyEntity(main);
                    if (manager.Exists(companion)) manager.DestroyEntity(companion);
                    world.GetExistingSystemManaged<CullingWorldSystem>()?.ShadowCompanions.Prune();
                }
                frame.Dispose(); if (volumes.IsCreated) volumes.Dispose(); if (bindings.IsCreated) bindings.Dispose();
                for (int i = 0; i < 2; i++)
                    if (Cameras[i] != null) { Cameras[i].targetTexture = null; Targets[i].Release(); Object.Destroy(Targets[i]); }
                foreach (var go in objects) if (go != null) { go.SetActive(false); Object.Destroy(go); }
                foreach (var pair in previousLights) if (pair.Key != null) pair.Key.enabled = pair.Value;
                RenderSettings.ambientMode = previousAmbient; RenderSettings.ambientLight = previousAmbientLight; RenderSettings.sun = previousSun;
            }
        }
    }
}
