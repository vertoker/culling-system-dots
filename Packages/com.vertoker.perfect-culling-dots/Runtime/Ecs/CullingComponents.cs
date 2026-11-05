using PerfectCullingDOTS.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace PerfectCullingDOTS.Runtime
{
    public struct CullingVolume : IComponentData
    {
        public BlobAssetReference<CullingVolumeBlob> Bake;
        public uint4 InstanceId;
        public float3 Size;
        public uint Layers;
        public CullingOutOfBoundsPolicy OutOfBounds;
        public CullingEmptyCellPolicy EmptyCell;
        public bool SearchNonEmpty;
        public int RegistrationOrder;
    }
    public struct CullingGroupDefinition : IComponentData
    {
        public Entity Volume;
        public ushort GroupIndex;
        public ulong StateGeneration;
        public int VertexCount;
    }
    public struct CullingPortalLink : IComponentData
    {
        public Entity Volume;
        public int RegistrationOrder;
    }
    public struct CullingBakedRenderBinding : IBufferElementData
    {
        public Entity Main;
        public Entity ShadowOnly;
        public ShadowCastingMode OriginalShadowMode;
        public byte InitialRendererEnabled;
        public byte InitialForceRenderingOff;
    }

    // Last ordered group operation applied to this explicit receiver. Does not disable the Entity.
    public struct CullingGroupState : IComponentData
    {
        public CullingGroupKey Group;
        public byte Visible;
    }
}
