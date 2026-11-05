using PerfectCullingDOTS.Core;
using Unity.Entities;

namespace PerfectCullingDOTS.Runtime
{
    // Last ordered group operation applied to this explicit receiver. Does not disable the Entity.
    public struct CullingGroupState : IComponentData
    {
        public CullingGroupKey Group;
        public byte Visible;
    }
}
