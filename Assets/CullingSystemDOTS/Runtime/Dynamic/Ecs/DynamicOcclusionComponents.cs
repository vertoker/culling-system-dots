using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;

[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.ECS.Tests")]
[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.GPU")]

namespace CullingSystemDOTS.Dynamic.Ecs
{
    /// <summary>Disable this opt-in to restore stock graphics without disabling simulation.</summary>
    public struct DynamicOcclusionBinding : IComponentData, IEnableableComponent
    {
        public InstanceHandle Handle;
        public OcclusionRole Roles;
        public float4 Color;
        public byte Opaque;
    }
}
