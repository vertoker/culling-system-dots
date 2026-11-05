using PerfectCullingDOTS.Runtime;
using PerfectCullingDOTS.Core;
using UnityEngine;

namespace PerfectCullingDOTS.Authoring
{
    [DisallowMultipleComponent]
    public sealed class CullingVolumeAuthoring : MonoBehaviour
    {
        public CullingBakeAsset Bake;
        public Vector3 Size = Vector3.one;
        public uint Layers = uint.MaxValue;
        public CullingOutOfBoundsPolicy OutOfBounds;
        public CullingEmptyCellPolicy EmptyCell;
        public bool SearchNonEmpty;
        public int RegistrationOrder;
    }
}
