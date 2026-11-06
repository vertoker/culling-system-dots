using UnityEngine;
namespace CullingSystemDOTS.Authoring
{
    public enum CullingSamplingRegionMode { AlwaysInclude, Exclude }
    [DisallowMultipleComponent]
    public sealed class CullingSamplingRegionAuthoring : MonoBehaviour
    {
        public CullingSamplingRegionMode Mode;
        public Vector3 Size = Vector3.one;
        public CullingVolumeAuthoring[] RestrictToVolumes = System.Array.Empty<CullingVolumeAuthoring>();
    }
}
