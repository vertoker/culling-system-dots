using System;
using System.Collections.Generic;
using System.Linq;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Rendering;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
namespace CullingSystemDOTS.Runtime
{
    // One driver per World. Explicit drivers support custom Worlds; the default bridge
    // publishes once per SRP context, before any of that context's camera callbacks.
    public sealed class CullingCameraDriver : IDisposable
    {
        private readonly World world;
        private readonly CullingViewCaptureSystem capture;
        private readonly Dictionary<CullingCameraAuthoring,Record> registered = new Dictionary<CullingCameraAuthoring,Record>();
        private readonly Dictionary<Camera,ulong> contextCameras = new Dictionary<Camera,ulong>();
        private EcsRenderVisibilityAdapter renderAdapter;
        private sealed class Record { public Camera Camera; public ulong Id, Version; public CullingViewRegistration Settings; }
        private bool disposed;
        private static ulong nextViewId = 1UL<<62;
        private static readonly List<CullingCameraAuthoring> active = new List<CullingCameraAuthoring>();
        private static CullingCameraDriver automatic;
        /// <summary>Pipeline integration supplies the actual callback order before
        /// publication. Repeated cameras (for example a shared overlay) are retained.</summary>
        public static Func<IReadOnlyList<Camera>,Camera[]> AutomaticRenderOrderResolver { get; set; }
        public CullingCameraDriver(World world)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.");
            this.world = world; capture = world.GetOrCreateSystemManaged<CullingViewCaptureSystem>();
        }
        public void Register(CullingCameraAuthoring authoring)
        {
            CheckAlive();
            if (authoring == null || authoring.GetComponent<Camera>() == null) throw new ArgumentException("A camera authoring component is required.");
            if (registered.ContainsKey(authoring)) return;
            var record = new Record { Camera = authoring.GetComponent<Camera>(), Id = checked(++nextViewId) };
            record.Settings = capture.RegisterView(record.Camera,record.Id); registered.Add(authoring,record);
        }
        public void Unregister(CullingCameraAuthoring authoring)
        {
            if (!registered.TryGetValue(authoring,out var record)) return;
            registered.Remove(authoring);
            if (!disposed && world.IsCreated)
            {
                capture.UnregisterView(record.Id);
                if (renderAdapter != null && !renderAdapter.IsDisposed) renderAdapter.UnregisterCamera(record.Camera);
            }
        }
        private ulong[] Configure(Camera[] renderOrder)
        {
            CheckAlive();
            foreach (var entry in registered.ToArray())
            {
                var authoring = entry.Key; var record = entry.Value;
                if (authoring == null || !authoring.isActiveAndEnabled || record.Camera == null) { Unregister(authoring); continue; }
                record.Settings.Layers = authoring.Layers; record.Settings.NeighborRadius = authoring.NeighborRadius; record.Settings.Invert = authoring.Invert;
                record.Settings.RegistrationOrder = authoring.RegistrationOrder;
                if (record.Version != authoring.InvalidationVersion) { record.Version = authoring.InvalidationVersion; record.Settings.SetDirty(); }
            }
            if (renderOrder == null || renderOrder.Length == 0) return Array.Empty<ulong>();
            return renderOrder.Select(camera=>
            {
                if (camera == null) throw new ArgumentException("Render order contains a missing camera.");
                var view = registered.Values.FirstOrDefault(v=>v.Camera == camera);
                if (view != null) return view.Id;
                if (!contextCameras.TryGetValue(camera,out var id)) contextCameras.Add(camera,id = checked(++nextViewId));
                return id;
            }).ToArray();
        }
        public CullingCapturedFrame Capture(int frameId, params Camera[] renderOrder) => capture.Capture(frameId,Configure(renderOrder));
        public void EvaluateAndRender(int frameId, params Camera[] renderOrder)
        {
            var order = Configure(renderOrder);
            renderAdapter = world.GetOrCreateSystemManaged<CullingWorldSystem>().GetOrCreateRenderingAdapter();
            foreach (var camera in contextCameras.Keys.ToArray())
            {
                if (renderOrder == null || !renderOrder.Contains(camera)) { renderAdapter.UnregisterCamera(camera); contextCameras.Remove(camera); }
                else renderAdapter.RegisterCamera(camera,contextCameras[camera]);
            }
            capture.EvaluateAndRender(frameId,CullingExecutionMode.Jobs,order);
        }
        private void CheckAlive() { if (disposed || !world.IsCreated) throw new ObjectDisposedException(nameof(CullingCameraDriver)); }
        public void Dispose()
        {
            if (disposed) return;
            foreach (var authoring in registered.Keys.ToArray()) Unregister(authoring);
            if (world.IsCreated && renderAdapter != null)
                world.GetExistingSystemManaged<CullingWorldSystem>()?.ReleaseRenderingAdapter(renderAdapter);
            contextCameras.Clear(); renderAdapter = null;
            disposed = true;
        }
        internal static void Activate(CullingCameraAuthoring camera)
        {
            if (!active.Contains(camera)) active.Add(camera);
            EnsureHook();
        }
        internal static void Deactivate(CullingCameraAuthoring camera)
        {
            active.Remove(camera); automatic?.Unregister(camera);
            if (active.Count == 0) { automatic?.Dispose(); automatic = null; }
        }
        private static void EnsureHook()
        {
            RenderPipelineManager.beginContextRendering -= BeginContext;
            RenderPipelineManager.beginContextRendering += BeginContext;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            automatic?.Dispose(); automatic = null; active.Clear();
            EnsureHook();
        }
        private static void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Application.isPlaying || cameras.Count == 0) return;
            var current = World.DefaultGameObjectInjectionWorld;
            if (automatic != null && (current != automatic.world || !automatic.world.IsCreated)) { automatic.Dispose(); automatic = null; }
            active.RemoveAll(c=>c == null);
            if (active.Count == 0 || current == null || !current.IsCreated) return;
            if (automatic == null) automatic = new CullingCameraDriver(current);
            foreach (var camera in active) automatic.Register(camera);
            var order = AutomaticRenderOrderResolver == null ? cameras.ToArray() : AutomaticRenderOrderResolver(cameras);
            if (order == null) throw new InvalidOperationException("The automatic render-order resolver returned null.");
            if (order.Length != 0) automatic.EvaluateAndRender(Time.frameCount,order);
        }
    }
}
