using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionGpuRetirementPump : MonoBehaviour
    {
        private void Update() => OcclusionGpuRetirement.Poll();
    }
}
