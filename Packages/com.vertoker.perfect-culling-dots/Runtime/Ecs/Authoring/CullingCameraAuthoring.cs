using UnityEngine;
namespace PerfectCullingDOTS.Authoring
{
    [RequireComponent(typeof(Camera)), DisallowMultipleComponent]
    public sealed class CullingCameraAuthoring : MonoBehaviour
    {
        public uint Layers = uint.MaxValue;
        public int NeighborRadius;
        public bool Invert;
        public ulong InvalidationVersion { get; private set; }
        public void SetDirty() { InvalidationVersion = checked(InvalidationVersion+1); }
        private void OnEnable() => Runtime.CullingCameraDriver.Activate(this);
        private void OnDisable() => Runtime.CullingCameraDriver.Deactivate(this);
    }
}
