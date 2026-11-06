using CullingSystemDOTS.Dynamic.Gpu;
using UnityEditor;

namespace CullingSystemDOTS.Dynamic.Editor
{
    [InitializeOnLoad]
    internal static class OcclusionGpuEditorRetirement
    {
        static OcclusionGpuEditorRetirement()
        {
            EditorApplication.update += OcclusionGpuRetirement.Poll;
            AssemblyReloadEvents.beforeAssemblyReload += OcclusionGpuRetirement.DrainForShutdown;
            EditorApplication.quitting += OcclusionGpuRetirement.DrainForShutdown;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) OcclusionGpuRetirement.DrainForShutdown();
            };
        }
    }
}
