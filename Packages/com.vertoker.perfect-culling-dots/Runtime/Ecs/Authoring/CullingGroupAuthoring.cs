using UnityEngine;

namespace PerfectCullingDOTS.Authoring
{
    [DisallowMultipleComponent]
    public sealed class CullingGroupAuthoring : MonoBehaviour
    {
        public CullingVolumeAuthoring Volume;
        public int GroupIndex;
        public Renderer[] Renderers = System.Array.Empty<Renderer>();
    }
}
