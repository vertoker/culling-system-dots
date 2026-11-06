using System;
using Unity.Mathematics;

namespace CullingSystemDOTS.Core
{
    [Serializable]
    public struct CullingGroupKey : IEquatable<CullingGroupKey>
    {
        public uint4 VolumeId;
        public ushort GroupIndex;

        public CullingGroupKey(uint4 volumeId, ushort groupIndex)
        {
            VolumeId = volumeId;
            GroupIndex = groupIndex;
        }

        public bool Equals(CullingGroupKey other) => math.all(VolumeId == other.VolumeId) && GroupIndex == other.GroupIndex;
        public override bool Equals(object other) => other is CullingGroupKey key && Equals(key);
        public override int GetHashCode() => unchecked((int)(math.hash(VolumeId) * 397u ^ GroupIndex));
    }

    [Serializable]
    public struct CullingBakeLayout
    {
        public float3 CellCount;
        public float3 CellSize;
        public quaternion BakeOrientation;
        public quaternion InverseBakeOrientation;
    }

    public enum BakeDataError
    {
        None, MissingData, UnsupportedVersion, InvalidLayout, InvalidGroupMapping, InvalidCellData
    }
}
