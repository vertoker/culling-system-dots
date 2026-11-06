using UnityEngine;
namespace CullingSystemDOTS.Authoring
{
    [DisallowMultipleComponent]
    public sealed class CullingBakeInputsAuthoring : MonoBehaviour
    {
        public Renderer[] AdditionalOccluders = System.Array.Empty<Renderer>();
        public MonoBehaviour[] SamplingProviders = System.Array.Empty<MonoBehaviour>();
    }
}
