using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    internal static class DynamicOcclusionIntegrationProbe
    {
        public static void ApplyReferenceMask(EntityManager manager, Entity entity, bool hidden)
        {
            manager.CompleteAllTrackedJobs();
            manager.SetComponentEnabled<MaterialMeshInfo>(entity, !hidden);
        }
        public const bool SupportedStockGpuHandoff = false;
    }

    internal sealed class GpuWallMask : IDisposable
    {
        private sealed class Slot
        {
            public ComputeBuffer Buffer;
            public AsyncGPUReadbackRequest Request;
            public bool Pending;
            public ViewToken Token;
            public ulong Serial;
        }

        private readonly Slot[] slots = new Slot[2];
        private readonly ComputeBuffer bounds;
        private readonly ComputeShader shader;
        private readonly uint[] bits;
        private ViewToken resultToken;
        private ulong serial, resultSerial;
        private bool hasResult, disposed;
        public int Issued, Completed, Errors, Saturated, TeardownWaits;

        public GpuWallMask(Vector4[] values, Func<int, int, ComputeBuffer> createBuffer = null)
        {
            if (values == null || values.Length == 0) throw new ArgumentException("Nonempty fixture bounds are required.", nameof(values));
            shader = RequireShader();
            try
            {
                bits = new uint[values.Length];
                bounds = createBuffer != null ? createBuffer(values.Length, 16) : new ComputeBuffer(values.Length, 16);
                bounds.SetData(values);
                for (int i = 0; i < slots.Length; i++) slots[i] = new Slot {
                    Buffer = createBuffer != null ? createBuffer(values.Length, 4) : new ComputeBuffer(values.Length, 4) };
            }
            catch { Dispose(); throw; }
        }

        internal static ComputeShader RequireShader()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                throw new NotSupportedException("G1 readback probe requires compute and async readback.");
            ComputeShader fixture = null;
            #if UNITY_EDITOR
            fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Tests/DynamicPrototype/FixtureWallMask.compute");
            #endif
            if (fixture == null) throw new InvalidOperationException("The root-only GPU fixture shader is missing.");
            return fixture;
        }

        public void Submit(Camera camera, uint epoch, uint revision, bool wall)
        {
            CheckAlive(); Poll();
            foreach (var slot in slots)
            {
                if (slot.Pending) continue;
                slot.Token = ViewToken.Capture(camera, epoch, revision);
                slot.Serial = ++serial;
                shader.SetInt("Count", bits.Length);
                // This closed-form fixture is valid only for parallel +Z rays from before the wall.
                // It intentionally omits the general depth producer so G1 measures the handoff lower bound.
                bool supported = camera.orthographic && camera.transform.rotation.Equals(Quaternion.identity) &&
                    camera.transform.position.z + camera.nearClipPlane < .875f &&
                    camera.targetTexture != null && camera.targetTexture.antiAliasing == 1;
                shader.SetInt("WallPresent", wall && supported ? 1 : 0);
                shader.SetBuffer(0, "Bounds", bounds); shader.SetBuffer(0, "Hidden", slot.Buffer);
                shader.Dispatch(0, (bits.Length + 63) / 64, 1, 1);
                slot.Request = AsyncGPUReadback.Request(slot.Buffer); slot.Pending = true; Issued++;
                return;
            }
            Saturated++;
        }

        public uint[] Read(Camera camera, uint epoch, uint revision)
        {
            CheckAlive(); Poll();
            return hasResult && resultToken.Equals(ViewToken.Capture(camera, epoch, revision)) ? bits : null;
        }

        public void CompleteLatestForDiagnostic()
        {
            CheckAlive();
            foreach (var slot in slots) if (slot.Pending && !slot.Request.done) slot.Request.WaitForCompletion();
            Poll();
        }

        private void Poll()
        {
            foreach (var slot in slots)
            {
                if (!slot.Pending || !slot.Request.done) continue;
                slot.Pending = false; Completed++;
                if (slot.Request.hasError) { Errors++; hasResult = false; continue; }
                if (slot.Serial <= resultSerial) continue;
                slot.Request.GetData<uint>().CopyTo(bits);
                resultSerial = slot.Serial; resultToken = slot.Token; hasResult = true;
            }
        }

        private void CheckAlive() { if (disposed) throw new ObjectDisposedException(nameof(GpuWallMask)); }
        public void Dispose()
        {
            if (disposed) return;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                if (slot.Pending && !slot.Request.done) { slot.Request.WaitForCompletion(); TeardownWaits++; }
                slot.Buffer?.Dispose();
            }
            bounds?.Dispose(); disposed = true;
        }
    }

    internal struct ViewToken : IEquatable<ViewToken>
    {
        private Camera camera;
        private RenderTexture target;
        private int width, height, samples, eye, kind, cullingMask;
        private uint epoch, revision;
        private Matrix4x4 view, projection, unjittered, culling;
        private Rect rect;
        private float scale, near, far;
        private bool orthographic, dynamicResolution;

        public static ViewToken Capture(Camera camera, uint epoch, uint revision)
        {
            var target = camera.targetTexture;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return new ViewToken {
                camera = camera, target = target,
                width = target != null ? target.width : camera.pixelWidth,
                height = target != null ? target.height : camera.pixelHeight,
                samples = target != null ? target.antiAliasing : QualitySettings.antiAliasing,
                eye = (int)camera.stereoActiveEye, kind = (int)camera.cameraType,
                cullingMask = camera.cullingMask, view = camera.worldToCameraMatrix,
                projection = camera.projectionMatrix, unjittered = camera.nonJitteredProjectionMatrix,
                culling = camera.cullingMatrix, rect = camera.pixelRect,
                scale = pipeline != null ? pipeline.renderScale : 1,
                near = camera.nearClipPlane, far = camera.farClipPlane,
                orthographic = camera.orthographic, dynamicResolution = camera.allowDynamicResolution,
                epoch = epoch, revision = revision
            };
        }

        public bool Equals(ViewToken other) => ReferenceEquals(camera, other.camera) && ReferenceEquals(target, other.target) &&
            width == other.width && height == other.height && samples == other.samples && eye == other.eye &&
            kind == other.kind && cullingMask == other.cullingMask && epoch == other.epoch && revision == other.revision &&
            view.Equals(other.view) && projection.Equals(other.projection) && unjittered.Equals(other.unjittered) &&
            culling.Equals(other.culling) && rect.Equals(other.rect) && scale.Equals(other.scale) &&
            near.Equals(other.near) && far.Equals(other.far) && orthographic == other.orthographic &&
            dynamicResolution == other.dynamicResolution;
    }

    internal enum ProbeRoute { Ordinary, StrictAsync, BlockingDiagnostic }

    // Everything in this stand is development-owned. No production registry/evaluator is involved.
    internal sealed class IntegrationStand : IDisposable
    {
        public readonly Camera[] Cameras;
        public readonly RenderTexture[] Targets;
        public readonly GpuWallMask[] Masks;
        public readonly NativeArray<Entity> Entities;
        public readonly World World;
        public ProbeRoute Route;
        public uint Epoch = 1, BoundsRevision = 1;
        public bool WallPresent = true;
        public int Views, AcceptedViews, HiddenApplications, FailOpenViews, JobCompletions;
        public double BeginMs, EndMs, BlockingWaitMs;
        private readonly GameObject cube, wall;
        private readonly Material red, blue;
        private readonly bool[] baselines;
        private bool disposed;

        public IntegrationStand(int count, int cameras, Func<IntegrationStand, Vector4[], GpuWallMask> createMask = null)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (cameras <= 0) throw new ArgumentOutOfRangeException(nameof(cameras));
            GpuWallMask.RequireShader();
            World = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (World?.GetExistingSystemManaged<EntitiesGraphicsSystem>() == null)
                throw new InvalidOperationException("The main World needs stock Entities Graphics.");
            try
            {
                var manager = World.EntityManager;
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
                cube.name = "Owned G1 mesh source";
                red = new Material(Shader.Find("Universal Render Pipeline/Unlit")); red.SetColor("_BaseColor", Color.red);
                blue = new Material(red); blue.SetColor("_BaseColor", Color.blue);
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Owned G1 wall"; wall.layer = 31;
                wall.transform.position = new Vector3(-8.125f, 0, 1); wall.transform.localScale = new Vector3(15.75f, 32, .25f);
                wall.GetComponent<Renderer>().sharedMaterial = blue;
                Entities = new NativeArray<Entity>(count, Allocator.Persistent);
                var prototype = manager.CreateEntity();
                try
                {
                    RenderMeshUtility.AddComponents(prototype, manager, new RenderMeshDescription(ShadowCastingMode.Off, layer: 31),
                        new RenderMeshArray(new[] { red }, new[] { cube.GetComponent<MeshFilter>().sharedMesh }),
                        MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                    manager.Instantiate(prototype, Entities);
                }
                finally { if (manager.Exists(prototype)) manager.DestroyEntity(prototype); }
                var bounds = new Vector4[count]; baselines = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    var center = count == 2 ? new float3(i == 0 ? -1 : 1, 0, 4)
                        : new float3((i % 64 - 31.5f) * .45f, (i / 64 - (count / 64 - 1) * .5f) * .45f, 4);
                    float size = count == 2 ? 1 : .24f;
                    manager.SetComponentData(Entities[i], new LocalToWorld { Value = float4x4.TRS(center, quaternion.identity, new float3(size)) });
                    bounds[i] = new Vector4(center.x, center.y, center.z, size * .5f);
                    baselines[i] = manager.IsComponentEnabled<MaterialMeshInfo>(Entities[i]);
                }
                Cameras = new Camera[cameras]; Targets = new RenderTexture[cameras]; Masks = new GpuWallMask[cameras];
                for (int i = 0; i < cameras; i++)
                {
                    Cameras[i] = new GameObject("Owned G1 camera " + i).AddComponent<Camera>();
                    Cameras[i].transform.position = new Vector3(count == 2 ? -1 : i * .01f, 0, -8);
                    Cameras[i].depth = 100 + i; Cameras[i].cullingMask = 1 << 31;
                    Cameras[i].orthographic = true; Cameras[i].orthographicSize = count == 2 ? 2 : 18;
                    Cameras[i].clearFlags = CameraClearFlags.SolidColor; Cameras[i].backgroundColor = Color.black;
                    Targets[i] = new RenderTexture(128, 128, 24); Targets[i].Create(); Cameras[i].targetTexture = Targets[i];
                    Masks[i] = createMask != null ? createMask(this, bounds) : new GpuWallMask(bounds);
                }
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }
            catch { Dispose(); throw; }
        }

        public void SetWall(bool present)
        {
            if (WallPresent == present) return;
            WallPresent = present; Epoch++;
            wall.GetComponent<Renderer>().enabled = present;
        }

        public void ResetCounters()
        {
            Views = AcceptedViews = HiddenApplications = FailOpenViews = JobCompletions = 0;
            BeginMs = EndMs = BlockingWaitMs = 0;
        }

        private void Begin(ScriptableRenderContext context, Camera camera)
        {
            int index = Array.IndexOf(Cameras, camera); if (index < 0) return;
            long start = Stopwatch.GetTimestamp(); Views++;
            if (Route != ProbeRoute.Ordinary)
            {
                if (Route == ProbeRoute.BlockingDiagnostic)
                {
                    Masks[index].Submit(camera, Epoch, BoundsRevision, WallPresent);
                    long wait = Stopwatch.GetTimestamp(); Masks[index].CompleteLatestForDiagnostic();
                    BlockingWaitMs += MillisecondsSince(wait);
                }
                var hidden = Masks[index].Read(camera, Epoch, BoundsRevision);
                if (hidden == null) FailOpenViews++;
                int applied = Apply(hidden);
                HiddenApplications += applied; if (applied > 0) AcceptedViews++;
            }
            BeginMs += MillisecondsSince(start);
        }

        private void End(ScriptableRenderContext context, Camera camera)
        {
            int index = Array.IndexOf(Cameras, camera); if (index < 0) return;
            long start = Stopwatch.GetTimestamp();
            if (Route != ProbeRoute.Ordinary) Apply(null);
            if (Route == ProbeRoute.StrictAsync) Masks[index].Submit(camera, Epoch, BoundsRevision, WallPresent);
            EndMs += MillisecondsSince(start);
        }

        private int Apply(uint[] hidden)
        {
            var manager = World.EntityManager;
            manager.CompleteAllTrackedJobs(); JobCompletions++;
            int count = 0;
            for (int i = 0; i < Entities.Length; i++)
            {
                bool hide = hidden != null && hidden[i] != 0;
                manager.SetComponentEnabled<MaterialMeshInfo>(Entities[i], baselines[i] && !hide);
                if (hide) count++;
            }
            return count;
        }

        private static double MillisecondsSince(long ticks) => (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End;
            if (Masks != null) foreach (var mask in Masks) mask?.Dispose();
            if (World != null && World.IsCreated)
            {
                var manager = World.EntityManager; manager.CompleteAllTrackedJobs();
                if (Entities.IsCreated) foreach (var entity in Entities) if (manager.Exists(entity)) manager.DestroyEntity(entity);
            }
            if (Entities.IsCreated) Entities.Dispose();
            for (int i = 0; Cameras != null && i < Cameras.Length; i++)
            {
                if (Cameras[i] != null) { Cameras[i].targetTexture = null; UnityEngine.Object.Destroy(Cameras[i].gameObject); }
                if (Targets != null && Targets[i] != null) { Targets[i].Release(); UnityEngine.Object.Destroy(Targets[i]); }
            }
            UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(cube);
            UnityEngine.Object.Destroy(red); UnityEngine.Object.Destroy(blue);
        }
    }
}
