using UnityEngine;

namespace CullingSystemDOTS.Authoring
{
    public enum BakeSurfaceOverride { Automatic, Opaque, Transparent }

    [DisallowMultipleComponent]
    public sealed class CullingBakeRendererTag : MonoBehaviour
    {
        public BakeSurfaceOverride Surface;
        public bool DoubleSided;
        public bool ExcludeFromBake;
    }
}
