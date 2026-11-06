using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Runtime;
using CullingSystemDOTS.Editor.Baking.Jobs;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeControllerStatus { Idle, Running, Ready, Published, Cancelled, Invalidated, Faulted, Publishing }
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
        private BakeCaptureOperation capture;
        private Task<LegacyBakeSource> publication;
        private CancellationTokenSource publicationCancellation;
        private LegacyBakeSource prepared;
        private string publicationPath;
        private static long editRevision;
        private long validationRevision;
        private BakeSettingsAsset executionSettings;
        public BakeControllerStatus Status { get; private set; }
        public float Progress { get; private set; }
        public Exception Error { get; private set; }
        public string ProgressMessage { get; private set; } = "Not started";
        public int ActiveSampleCount { get; private set; }
        public string BackendName => effectiveBackend ?? backend?.Name ?? "Not started";
        public string FallbackReason => fallbackReason;
        static BakeController()
        {
            AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
            EditorApplication.quitting += DisposeAll;
            EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.ExitingEditMode) DisposeAll(); };
            Undo.postprocessModifications += modifications => { editRevision++; return modifications; };
            Undo.undoRedoPerformed += () => editRevision++;
            EditorApplication.hierarchyChanged += () => editRevision++;
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
            StartIncremental();
            try
            {
                input = capture.Complete(); capture.Dispose(); capture = null;
                BeginSession(false); session.Tick(); UpdateCompletion();
            }
            catch (Exception error) { Fail(error); throw; }
        }
        public void StartIncremental()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Idle) throw new InvalidOperationException("Create a new controller for a new bake.");
            if (Active.Any(c=>c != this && c.volume == volume && (c.Status == BakeControllerStatus.Running || c.Status == BakeControllerStatus.Ready || c.Status == BakeControllerStatus.Publishing)))
                throw new InvalidOperationException("This volume already has a running or unpublished bake.");
            try
            {
                if (settings.MergeAxes == null) throw new ArgumentException("Merge axes are required.");
                executionSettings = UnityEngine.Object.Instantiate(settings);
                mergeAxes = (Vector3Int[])executionSettings.MergeAxes.Clone();
                if (settings.MemoryBudgetMiB <= 0 || settings.MemoryBudgetMiB > int.MaxValue/(1024*1024) || settings.MaxCellsPerBatch <= 0)
                    throw new ArgumentException("Positive supported memory budget and batch size are required.");
                capture = SceneBakeCollector.BeginCapture(volume,executionSettings);
                Status = BakeControllerStatus.Running; ProgressMessage = "Capturing scene inputs";
            }
            catch (Exception error) { Fail(error); throw; }
        }
        private void BeginSession(bool backgroundPreparation)
        {
            ActiveSampleCount = input.Snapshot.Samples.Length;
            backend = BakeBackendFactory.Create(input.Snapshot.Profile,executionSettings.ExecutionMode);
            session = backend.Begin(input.Snapshot,new BakeBackendOptions { MemoryBudgetBytes = checked(executionSettings.MemoryBudgetMiB*1024*1024),
                MaxCellsPerBatch = executionSettings.MaxCellsPerBatch, BackgroundPreparation = backgroundPreparation });
            ProgressMessage = "Building acceleration structures";
        }
        public void Tick()
        {
            EnsureAlive();
            if (Status == BakeControllerStatus.Publishing)
            {
                try { TickPublication(); }
                catch (Exception error) { if (Status != BakeControllerStatus.Invalidated && Status != BakeControllerStatus.Ready) Fail(error); throw; }
                return;
            }
            if (Status != BakeControllerStatus.Running) return;
            try
            {
                if (capture != null)
                {
                    capture.Tick(); Progress = .15f*capture.Progress;
                    if (capture.IsCompleted) { input = capture.TakeResult(); capture.Dispose(); capture = null; BeginSession(true); }
                    return;
                }
                session.Tick(); UpdateCompletion();
            }
            catch (Exception error) { Fail(error); throw; }
        }
        public void Complete()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Running) throw new InvalidOperationException("Bake is not running.");
            try
            {
                if (capture != null) { input = capture.Complete(); capture.Dispose(); capture = null; BeginSession(false); }
                session.Complete(); UpdateCompletion();
            }
            catch (Exception error) { Fail(error); throw; }
        }
        private void UpdateCompletion()
        {
            effectiveBackend = (session as IBakeSessionDiagnostics)?.BackendName ?? backend.Name;
            fallbackReason = (session as IBakeSessionDiagnostics)?.FallbackReason;
            Progress = .15f + .85f*session.Progress;
            ProgressMessage = session.Status == BakeSessionStatus.Pending ? "Building acceleration structures" : "Tracing visibility";
            if (session.Status != BakeSessionStatus.Completed) return;
            completed = session.Complete(); session.Dispose(); session = null;
            input.Snapshot.Dispose(); input.Snapshot = null; Progress = 1; Status = BakeControllerStatus.Ready; ProgressMessage = "Ready to publish";
        }
        public bool InputsAreCurrent()
        {
            EnsureAlive(); if (input == null || volume == null || settings == null) return false;
            using var capture = SceneBakeCollector.BeginValidation(volume,settings,input.ValidationBounds);
            using var current = capture.Complete(); return current.Fingerprint == input.Fingerprint;
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
        public void BeginPublish(string path)
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Ready) throw new InvalidOperationException("Only a completed bake can be published.");
            Error = null;
            Undo.FlushUndoRecordObjects();
            publicationPath = path; validationRevision = editRevision;
            capture = SceneBakeCollector.BeginValidation(volume,settings,input.ValidationBounds);
            Status = BakeControllerStatus.Publishing; Progress = 0; ProgressMessage = "Validating current inputs";
        }
        private void TickPublication()
        {
            if (editRevision != validationRevision)
            {
                Status = BakeControllerStatus.Invalidated; Release();
                throw new InvalidOperationException("Scene inputs were edited during publication. Bake again before saving.");
            }
            if (capture != null)
            {
                capture.Tick(); Progress = .5f*capture.Progress;
                if (!capture.IsCompleted) return;
                using var current = capture.TakeResult(); capture.Dispose(); capture = null;
                if (current.Fingerprint != input.Fingerprint)
                {
                    Status = BakeControllerStatus.Invalidated; Release();
                    throw new InvalidOperationException("Bake inputs changed. Bake again before saving.");
                }
                publicationCancellation = new CancellationTokenSource(); var token = publicationCancellation.Token;
                var layout = input.Layout; var metadata = input.Metadata; var mapping = input.Mapping; var id = input.VolumeId; var cells = completed; var axes = mergeAxes;
                publication = Task.Run(() =>
                {
                    var processed = BakePostprocessor.Process(layout,cells,metadata,axes,token);
                    return BakeOutputWriter.PrepareResults(processed.Cells,id,processed.Layout,mapping,BakeMergeMetadata.PreserveMergeMasks,token);
                });
                ProgressMessage = "Merging and encoding cells"; Progress = .5f; return;
            }
            if (prepared == null)
            {
                if (!publication.IsCompleted) return;
                prepared = publication.GetAwaiter().GetResult();
                ProgressMessage = "Writing and importing output asset"; Progress = .9f; return;
            }
            CullingBakeAsset asset;
            // No Editor update or background gap is allowed between this final source check
            // and atomic publication. Direct scripts need not emit Undo/hierarchy events.
            if (!InputsAreCurrent())
            {
                Status = BakeControllerStatus.Invalidated; Release();
                throw new InvalidOperationException("Bake inputs changed before the final publication boundary.");
            }
            try { asset = BakeOutputWriter.SavePrepared(publicationPath,prepared,input.Mapping,input.Fingerprint); }
            catch (Exception error)
            {
                Error = error; Status = BakeControllerStatus.Ready; Progress = 1; ProgressMessage = "Ready; publication failed";
                ReleasePublication(); throw;
            }
            Undo.RecordObject(volume,"Assign Culling System DOTS bake"); volume.Bake = asset; EditorUtility.SetDirty(volume);
            if (!EditorSceneManager.IsPreviewScene(volume.gameObject.scene)) EditorSceneManager.MarkSceneDirty(volume.gameObject.scene);
            Status = BakeControllerStatus.Published; Progress = 1; ProgressMessage = "Published"; Release();
        }
        public void Cancel()
        {
            EnsureAlive(); if (Status != BakeControllerStatus.Running && Status != BakeControllerStatus.Ready && Status != BakeControllerStatus.Publishing) return;
            try { session?.Cancel(); } finally { Release(); Status = BakeControllerStatus.Cancelled; ProgressMessage = "Cancelled"; }
        }
        private void Fail(Exception error) { Error = error; Status = BakeControllerStatus.Faulted; ProgressMessage = "Failed"; Release(); }
        private void Release()
        {
            try
            {
                ReleasePublication();
                capture?.Dispose(); capture = null;
                session?.Dispose();
            }
            finally
            {
                session = null; input?.Dispose(); input = null; completed = null;
                if (executionSettings != null) UnityEngine.Object.DestroyImmediate(executionSettings);
                executionSettings = null;
            }
        }
        private void ReleasePublication()
        {
            publicationCancellation?.Cancel();
            try { publication?.GetAwaiter().GetResult(); } catch (Exception) { }
            finally { publicationCancellation?.Dispose(); publicationCancellation = null; publication = null; prepared = null; }
        }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(BakeController)); }
        public void Dispose()
        {
            if (disposed) return;
            try { Release(); } finally { if (Status == BakeControllerStatus.Running || Status == BakeControllerStatus.Ready || Status == BakeControllerStatus.Publishing) Status = BakeControllerStatus.Cancelled;
                Active.Remove(this); disposed = true; }
        }
    }
}
