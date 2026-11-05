using UnityEngine;
namespace PerfectCullingDOTS.Authoring
{
    [DisallowMultipleComponent]
    public sealed class CullingPortalAuthoring : MonoBehaviour
    {
        public CullingVolumeAuthoring Volume;
        public int RegistrationOrder;
    }
}
