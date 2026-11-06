using System;
using CullingSystemDOTS.Authoring;
using UnityEditor;
using UnityEngine;
namespace CullingSystemDOTS.Editor.Baking
{
    public sealed class BakeWindow : EditorWindow
    {
        [SerializeField] private CullingVolumeAuthoring volume;
        [SerializeField] private BakeSettingsAsset settings;
        [SerializeField] private string outputPath = "Assets/CullingBake.asset";
        private BakeController controller;
        private UnityEditor.Editor settingsEditor;
        private string error, validity;
        private bool autoPublish;
        [MenuItem("Tools/Culling System DOTS/Bake")]
        public static void Open() => GetWindow<BakeWindow>("PVS Bake");
        private void OnEnable() { minSize = new Vector2(360,400); EditorApplication.update += UpdateBake; }
        private void OnDisable()
        {
            EditorApplication.update -= UpdateBake; controller?.Dispose(); controller = null;
            if (settingsEditor != null) DestroyImmediate(settingsEditor);
        }
        private void OnGUI()
        {
            bool running = controller?.Status == BakeControllerStatus.Running;
            EditorGUILayout.LabelField("Culling System DOTS",EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(running))
            {
                volume = (CullingVolumeAuthoring)EditorGUILayout.ObjectField("Volume",volume,typeof(CullingVolumeAuthoring),true);
                var selected = (BakeSettingsAsset)EditorGUILayout.ObjectField("Settings",settings,typeof(BakeSettingsAsset),false);
                if (selected != settings) { settings = selected; validity = null; if (settingsEditor != null) DestroyImmediate(settingsEditor); }
                EditorGUILayout.BeginHorizontal(); outputPath = EditorGUILayout.TextField("Output asset",outputPath);
                if (GUILayout.Button("Choose",GUILayout.Width(65)))
                {
                    string chosen = EditorUtility.SaveFilePanelInProject("PVS bake output",volume == null ? "CullingBake" : volume.name+" Bake","asset","Choose the output bake asset.");
                    if (!string.IsNullOrEmpty(chosen)) outputPath = chosen;
                }
                EditorGUILayout.EndHorizontal();
                if (settings != null)
                {
                    UnityEditor.Editor.CreateCachedEditor(settings,null,ref settingsEditor);
                    EditorGUI.BeginChangeCheck(); settingsEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck()) validity = "Settings changed. Validate the saved bake or bake again.";
                }
                else if (GUILayout.Button("Create bake settings"))
                {
                    string path = EditorUtility.SaveFilePanelInProject("New PVS settings","PVS Bake Settings","asset","Save the bake settings asset.");
                    if (!string.IsNullOrEmpty(path)) { settings = CreateInstance<BakeSettingsAsset>(); AssetDatabase.CreateAsset(settings,path); AssetDatabase.SaveAssetIfDirty(settings); }
                }
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(volume == null || settings == null || running || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Bake")) Run(() => { controller?.Dispose(); controller = new BakeController(volume,settings); controller.Start(); autoPublish = true; error = null; validity = null; });
            if (running && GUILayout.Button("Cancel")) Run(() => { autoPublish = false; controller.Cancel(); });
            if (controller != null)
            {
                EditorGUILayout.LabelField("Backend",controller.BackendName);
                if (!string.IsNullOrEmpty(controller.FallbackReason)) EditorGUILayout.HelpBox("Restarted on CPU: "+controller.FallbackReason,MessageType.Warning);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false,20),controller.Progress,controller.Status.ToString());
                if (controller.Status == BakeControllerStatus.Ready && GUILayout.Button("Save completed bake")) Run(Publish);
            }
            using (new EditorGUI.DisabledScope(running || volume == null || settings == null))
                if (GUILayout.Button("Validate saved bake")) Run(() => validity = SceneBakeCollector.IsBakeCurrent(volume,settings) ? "Saved bake matches the current inputs." : "Saved bake is missing, imported without a fingerprint, or out of date. Bake again.");
            if (!string.IsNullOrEmpty(validity)) EditorGUILayout.HelpBox(validity,MessageType.Info);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error,MessageType.Error);
        }
        private void UpdateBake()
        {
            if (controller == null) return;
            if (controller.Status == BakeControllerStatus.Running) Run(controller.Tick);
            if (autoPublish && controller.Status == BakeControllerStatus.Ready) { autoPublish = false; Run(Publish); }
            Repaint();
        }
        private void Publish()
        {
            controller.Publish(outputPath); validity = "Bake saved and assigned to the volume."; error = null;
        }
        private void Run(Action action) { try { action(); } catch (Exception failure) { autoPublish = false; error = failure.Message; } }
    }
}
