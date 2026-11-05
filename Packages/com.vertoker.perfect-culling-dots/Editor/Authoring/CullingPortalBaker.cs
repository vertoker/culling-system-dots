using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Runtime;
using Unity.Entities;
namespace PerfectCullingDOTS.Editor
{
    public sealed class CullingPortalBaker : Baker<CullingPortalAuthoring>
    {
        public override void Bake(CullingPortalAuthoring authoring)
        {
            if (authoring.Volume == null) throw new System.ArgumentException("A portal requires its explicit culling volume.");
            AddComponent(GetEntity(TransformUsageFlags.Dynamic),new CullingPortalLink {
                Volume = GetEntity(authoring.Volume,TransformUsageFlags.Dynamic), RegistrationOrder = authoring.RegistrationOrder });
        }
    }
}
