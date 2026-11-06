using UnityEngine;
namespace CullingSystemDOTS.Editor.Baking
{
    [CreateAssetMenu(menuName = "Culling System DOTS/Bake Settings")]
    public sealed class BakeSettingsAsset : ScriptableObject
    {
        [Tooltip("Auto/GPU use Unity compute with a complete CPU restart on GPU failure. CPU never uses the graphics device.")]
        public BakeExecutionMode ExecutionMode = BakeExecutionMode.Auto;
        [Tooltip("The shared geometry and ray sampling profile used by CPU and GPU baking.")]
        public BakeInputProfile InputProfile = BakeInputProfile.ComputeSoftwareInput;
        public Vector3 CellSize = new Vector3(10,5,10);
        public int Resolution = 768;
        public float Far = 1000;
        public bool RenderTransparency = true;
        public int MemoryBudgetMiB = 64, MaxCellsPerBatch = 8;
        public Vector3Int[] MergeAxes = System.Array.Empty<Vector3Int>();
    }
}
