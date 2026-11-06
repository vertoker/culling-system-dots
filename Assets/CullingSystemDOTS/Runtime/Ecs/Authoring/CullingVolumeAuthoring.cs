using CullingSystemDOTS.Runtime;
using CullingSystemDOTS.Core;
using UnityEngine;

namespace CullingSystemDOTS.Authoring
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
