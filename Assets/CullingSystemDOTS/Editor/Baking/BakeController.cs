using System;
using System.Collections.Generic;
using System.Linq;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Runtime;
using CullingSystemDOTS.Editor.Baking.Jobs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeControllerStatus { Idle, Running, Ready, Published, Cancelled, Invalidated, Faulted }
    public sealed class BakeController : IDisposable
    {
        private static readonly HashSet<BakeController> Active = new HashSet<BakeController>();
        private readonly CullingVolumeAuthoring volume;
        private readonly BakeSettingsAsset settings;
        private IBakeBackend backend;
        private CollectedBakeInput input;
        private IBakeSession session;
        private BakeCellResult[] completed;
        private Vector3Int[] mergeAxes;
        private bool disposed;
        private string effectiveBackend,fallbackReason;
        public BakeControllerStatus Status { get; private set; }
        public float Progress { get; private set; }
        public Exception Error { get; private set; }
        public string BackendName => effectiveBackend ?? backend?.Name ?? "Not started";
        public string FallbackReason => fallbackReason;
        static BakeController()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
            EditorApplication.quitting += DisposeAll;
            EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.ExitingEditMode) DisposeAll(); };
        }
        private static void DisposeAll() { foreach (var controller in Active.ToArray()) controller.Dispose(); }
        public BakeController(CullingVolumeAuthoring volume, BakeSettingsAsset settings)
        {
            this.volume = volume == null ? throw new ArgumentNullException(nameof(volume)) : volume;
            this.settings = settings == null ? throw new ArgumentNullException(nameof(settings)) : settings;
            Active.Add(this);
        }
        public void Start()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Idle) throw new InvalidOperationException("Create a new controller for a new bake.");
            if (Active.Any(c=>c != this && c.volume == volume && (c.Status == BakeControllerStatus.Running || c.Status == BakeControllerStatus.Ready)))
                throw new InvalidOperationException("This volume already has a running or unpublished bake.");
            try
            {
                input = SceneBakeCollector.Capture(volume,settings); mergeAxes = (Vector3Int[])settings.MergeAxes.Clone();
                if (settings.MemoryBudgetMiB <= 0 || settings.MemoryBudgetMiB > int.MaxValue/(1024*1024) || settings.MaxCellsPerBatch <= 0)
                    throw new ArgumentException("Positive supported memory budget and batch size are required.");
                backend = BakeBackendFactory.Create(input.Snapshot.Profile,settings.ExecutionMode);
                session = backend.Begin(input.Snapshot,new BakeBackendOptions { MemoryBudgetBytes = checked(settings.MemoryBudgetMiB*1024*1024), MaxCellsPerBatch = settings.MaxCellsPerBatch });
                Status = BakeControllerStatus.Running; session.Tick(); UpdateCompletion();
            }
            catch (Exception error) { Fail(error); throw; }
        }
        public void Tick()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Running) return;
            try { session.Tick(); UpdateCompletion(); } catch (Exception error) { Fail(error); throw; }
        }
        public void Complete()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Running) throw new InvalidOperationException("Bake is not running.");
            try { session.Complete(); UpdateCompletion(); } catch (Exception error) { Fail(error); throw; }
        }
        private void UpdateCompletion()
        {
            effectiveBackend = (session as IBakeSessionDiagnostics)?.BackendName ?? backend.Name;
            fallbackReason = (session as IBakeSessionDiagnostics)?.FallbackReason;
            Progress = session.Progress;
            if (session.Status != BakeSessionStatus.Completed) return;
            completed = session.Complete(); session.Dispose(); session = null;
            input.Snapshot.Dispose(); input.Snapshot = null; Progress = 1; Status = BakeControllerStatus.Ready;
        }
        public bool InputsAreCurrent()
        {
            EnsureAlive(); if (input == null || volume == null || settings == null) return false;
            using var current = SceneBakeCollector.Capture(volume,settings); return current.Fingerprint == input.Fingerprint;
        }
        public CullingBakeAsset Publish(string path)
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Ready) throw new InvalidOperationException("Only a completed, unpublished bake can be saved.");
            bool current;
            try { current = InputsAreCurrent(); }
            catch (Exception error) { Error = error; Status = BakeControllerStatus.Invalidated; Release(); throw; }
            if (!current)
            {
                Status = BakeControllerStatus.Invalidated; Release();
                throw new InvalidOperationException("Bake inputs changed. Bake again before saving.");
            }
            // Keep ready results on IO failure, so a locked/path error can be retried without rebaking.
            var processed = BakePostprocessor.Process(input.Layout,completed,input.Metadata,mergeAxes);
            var asset = BakeOutputWriter.SaveProcessed(path,processed,input.VolumeId,input.Mapping,input.Fingerprint);
            Undo.RecordObject(volume,"Assign Culling System DOTS bake"); volume.Bake = asset; EditorUtility.SetDirty(volume);
            if (!EditorSceneManager.IsPreviewScene(volume.gameObject.scene)) EditorSceneManager.MarkSceneDirty(volume.gameObject.scene);
            Status = BakeControllerStatus.Published; Release(); return asset;
        }
        public void Cancel()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Running && Status != BakeControllerStatus.Ready) return;
            try { session?.Cancel(); } finally { Release(); Status = BakeControllerStatus.Cancelled; }
        }
        private void Fail(Exception error) { Error = error; Status = BakeControllerStatus.Faulted; Release(); }
        private void Release()
        {
            try { session?.Dispose(); } finally { session = null; input?.Dispose(); input = null; completed = null; }
        }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(BakeController)); }
        public void Dispose()
        {
            if (disposed) return;
            try { Release(); } finally { if (Status == BakeControllerStatus.Running || Status == BakeControllerStatus.Ready) Status = BakeControllerStatus.Cancelled;
                Active.Remove(this); disposed = true; }
        }
    }
}
