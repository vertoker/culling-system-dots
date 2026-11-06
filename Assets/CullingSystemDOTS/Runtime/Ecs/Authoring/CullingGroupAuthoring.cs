using UnityEngine;

namespace CullingSystemDOTS.Authoring
{
    public enum CullingGroupKind { Other, LOD, User }
    [DisallowMultipleComponent]
    public sealed class CullingGroupAuthoring : MonoBehaviour
    {
        public CullingVolumeAuthoring Volume;
        public int GroupIndex;
        public Renderer[] Renderers = System.Array.Empty<Renderer>();
        public CullingGroupKind Kind;
        public Behaviour[] UnityBehaviours = System.Array.Empty<Behaviour>();
        public bool UseImportedVertexCount;
        public int ImportedVertexCount;
    }
}
