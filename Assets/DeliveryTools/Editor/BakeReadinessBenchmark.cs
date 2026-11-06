using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Delivery.Editor;
using CullingSystemDOTS.Editor.Baking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Delivery
{
    public static class BakeReadinessBenchmark
    {
        [Serializable] public sealed class Phase { public string name; public double mainThreadTickSeconds, sampledWallSeconds, maxTickMilliseconds; public int ticks; }
        [Serializable] public sealed class Run
        {
            public string requestedBackend, effectiveBackend, fallbackReason, error;
            public double totalSeconds, maxTickMilliseconds, maxEditorGapMilliseconds;
            public long sampledPeakWorkingSetBytes = -1, sampledPeakUnityAllocatedBytes, sampledPeakManagedBytes;
            public string workingSetStatus = "Unavailable from Unity Mono; see the external Windows process-memory samples.";
            public int actualActiveSamples;
            public int updates; public List<Phase> phases = new List<Phase>();
        }
        [Serializable] public sealed class Report
        {
            public string revision, utc, unity, gpu, graphicsApi;
            public string memoryScope = "Peak of per-update samples, not an allocation cap or exact transient/VRAM peak.";
            public string timingScope = "Editor update driven bake, including capture, BVH, tracing, revalidation, encoding and own asset publication. Fixture setup excluded.";
            public string samplingIsolation = "Volume-restricted AlwaysInclude verified against an owned additive scene with an unrestricted Exclude region; actual active sample count checked.";
            public int groups = 512, rendererInstances = 512, uniqueMeshes = 16, uniqueTriangles = 32768;
            public int instancedTriangles = 1048576, cells = 128, resolution = 32, raysPerCell = 6144;
            public List<Run> runs = new List<Run>(); public bool completed;
        }
        private static Report report;
        private static Run current;
        private static BakeController controller;
        private static BakeSettingsAsset settings;
        private static CullingVolumeAuthoring volume;
        private static Scene scene;
        private static Scene foreignRegionScene;
        private static Material material;
        private static readonly List<Mesh> meshes = new List<Mesh>();
        private static readonly Stopwatch watch = new Stopwatch();
        private static double lastUpdate;
        private static string folder;
        private static Phase activePhase;
        private static double phaseBegan;
        public static bool IsRunning => report != null;
        public static string ReportPath => Path.GetFullPath("Docs/Validation/Readiness-Bake.json");
        public static string StatusPath => Path.GetFullPath(".utmp/CullingReadiness.status");

        [MenuItem("Tools/Culling System DOTS/Measure Bake Readiness")]
        public static void Begin()
        {
            if (IsRunning || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use an idle EditMode Editor.");
            report = new Report { unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), utc = DateTime.UtcNow.ToString("O"), revision = Revision() };
            try
            {
                folder = "Assets/CullingReadiness_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
                var host = SceneManager.GetActiveScene();
                foreignRegionScene = DeliveryFixtureBuilder.CreateOwnedScene(folder + "/HostExclusion.unity");
                var foreign = new GameObject("Owned unrelated sampling exclusion").AddComponent<CullingSamplingRegionAuthoring>();
                foreign.Mode = CullingSamplingRegionMode.Exclude; foreign.Size = new Vector3(10000,10000,10000);
                SceneManager.SetActiveScene(host);
                scene = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("Owned culling readiness benchmark"); SceneManager.MoveGameObjectToScene(root,scene);
                volume = root.AddComponent<CullingVolumeAuthoring>(); volume.Size = new Vector3(32,8,32);
                var region = root.AddComponent<CullingSamplingRegionAuthoring>(); region.Mode = CullingSamplingRegionMode.AlwaysInclude;
                region.Size = new Vector3(64,16,64); region.RestrictToVolumes = new[] {volume};
                settings = ScriptableObject.CreateInstance<BakeSettingsAsset>(); settings.CellSize = new Vector3(4,4,4);
                settings.Resolution = report.resolution; settings.Far = 60; settings.MemoryBudgetMiB = 256; settings.MaxCellsPerBatch = 8;
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                for (int m = 0; m < report.uniqueMeshes; m++) meshes.Add(Grid(m));
                for (int i = 0; i < report.groups; i++)
                {
                    var go = new GameObject("Synthetic panel " + i); go.transform.SetParent(root.transform,false);
                    go.transform.localPosition = new Vector3((i%16)*2-15,(i/256)*4-2,((i/16)%16)*2-15);
                    go.transform.localRotation = Quaternion.Euler(0,(i%4)*45,0);
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[i%meshes.Count];
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    go.AddComponent<CullingBakeRendererTag>().DoubleSided = true;
                    var group = go.AddComponent<CullingGroupAuthoring>(); group.Volume = volume; group.GroupIndex = i; group.Renderers = new Renderer[] { renderer };
                }
                EditorApplication.update += Update; AssemblyReloadEvents.beforeAssemblyReload += Stop;
                StartRun(BakeExecutionMode.Cpu);
            }
            catch { Stop(); throw; }
        }
        private static Mesh Grid(int variant)
        {
            var vertices = new Vector3[33*33]; var indices = new int[32*32*6];
            for (int y = 0; y <= 32; y++) for (int x = 0; x <= 32; x++)
                vertices[y*33+x] = new Vector3(x/16f-1,y/16f-1,.01f*Mathf.Sin((x+y+variant)*.3f));
            for (int y = 0, t = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            { int a = y*33+x; indices[t++] = a; indices[t++] = a+33; indices[t++] = a+1; indices[t++] = a+1; indices[t++] = a+33; indices[t++] = a+34; }
            var mesh = new Mesh { name = "Independent panel grid " + variant, vertices = vertices, triangles = indices }; mesh.RecalculateBounds(); return mesh;
        }
        private static void StartRun(BakeExecutionMode mode)
        {
            settings.ExecutionMode = mode; current = new Run { requestedBackend = mode.ToString() }; report.runs.Add(current);
            controller = new BakeController(volume,settings); controller.StartIncremental();
            watch.Restart(); lastUpdate = 0; activePhase = null; TrackPhase(controller.ProgressMessage,0);
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)); File.WriteAllText(StatusPath,mode.ToString());
        }
        private static void Update()
        {
            try
            {
                double now = watch.Elapsed.TotalSeconds;
                if (lastUpdate > 0) current.maxEditorGapMilliseconds = Math.Max(current.maxEditorGapMilliseconds,(now-lastUpdate)*1000);
                lastUpdate = now;
                string phase = controller.ProgressMessage;
                var timer = Stopwatch.StartNew(); controller.Tick(); timer.Stop();
                current.updates++; current.maxTickMilliseconds = Math.Max(current.maxTickMilliseconds,timer.Elapsed.TotalMilliseconds);
                var entry = current.phases.Find(p => p.name == phase);
                if (entry == null) { entry = new Phase { name = phase }; current.phases.Add(entry); }
                entry.ticks++; entry.mainThreadTickSeconds += timer.Elapsed.TotalSeconds;
                entry.maxTickMilliseconds = Math.Max(entry.maxTickMilliseconds,timer.Elapsed.TotalMilliseconds);
                using (var process = Process.GetCurrentProcess())
                {
                    long bytes = process.WorkingSet64;
                    if (bytes > 0) { current.sampledPeakWorkingSetBytes = Math.Max(current.sampledPeakWorkingSetBytes,bytes); current.workingSetStatus = "Per-update Process.WorkingSet64 samples."; }
                }
                current.sampledPeakUnityAllocatedBytes = Math.Max(current.sampledPeakUnityAllocatedBytes,UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                current.sampledPeakManagedBytes = Math.Max(current.sampledPeakManagedBytes,GC.GetTotalMemory(false));
                if (controller.Status == BakeControllerStatus.Ready)
                {
                    current.actualActiveSamples = controller.ActiveSampleCount;
                    if (current.actualActiveSamples != report.cells) throw new InvalidOperationException("Sampling regions/providers changed the benchmark workload.");
                    controller.BeginPublish(folder + "/" + current.requestedBackend + ".asset");
                }
                TrackPhase(controller.ProgressMessage,watch.Elapsed.TotalSeconds);
                if (controller.Status == BakeControllerStatus.Published)
                {
                    TrackPhase(null,watch.Elapsed.TotalSeconds);
                    current.totalSeconds = watch.Elapsed.TotalSeconds; current.effectiveBackend = controller.BackendName; current.fallbackReason = controller.FallbackReason;
                    controller.Dispose(); controller = null;
                    if (report.runs.Count == 1) StartRun(BakeExecutionMode.Gpu);
                    else { report.completed = true; Save(); Stop(); }
                }
            }
            catch (Exception error) { if (current != null) current.error = error.ToString(); Save(); Stop(); Debug.LogException(error); }
        }
        private static void TrackPhase(string name, double now)
        {
            if (activePhase != null && activePhase.name == name) return;
            if (activePhase != null) activePhase.sampledWallSeconds += now-phaseBegan;
            activePhase = name == null ? null : current.phases.Find(p => p.name == name);
            if (name != null && activePhase == null) { activePhase = new Phase { name = name }; current.phases.Add(activePhase); }
            phaseBegan = now;
        }
        private static string Revision()
        {
            var start = new ProcessStartInfo("git","rev-parse HEAD") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var process = Process.Start(start); string value = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
            var dirty = new ProcessStartInfo("git","status --porcelain -- Assets/CullingSystemDOTS Assets/DeliveryTools Assets/Tests/PlayMode")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var state = Process.Start(dirty); string changes = state.StandardOutput.ReadToEnd(); state.WaitForExit();
            return string.IsNullOrWhiteSpace(changes) ? value : value + " + working tree";
        }
        private static void Save() { Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true)); }
        public static void Stop()
        {
            EditorApplication.update -= Update; AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            try { controller?.Dispose(); controller = null; }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (foreignRegionScene.IsValid()) EditorSceneManager.CloseScene(foreignRegionScene,true);
                scene = default; foreignRegionScene = default;
                if (settings != null) Object.DestroyImmediate(settings); if (material != null) Object.DestroyImmediate(material);
                foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh); meshes.Clear();
                if (!string.IsNullOrEmpty(folder)) AssetDatabase.DeleteAsset(folder);
                folder = null; report = null; current = null;
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath)); File.WriteAllText(StatusPath,"Finished");
            }
        }
    }
}
