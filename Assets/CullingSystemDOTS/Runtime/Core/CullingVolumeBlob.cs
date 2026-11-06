using Unity.Entities;

namespace CullingSystemDOTS.Core
{
    [System.Serializable]
    public struct CullingCellRange
    {
        public int ByteOffset;
        public int ByteLength;
        // Number of encoded delta tokens, not number of decoded groups.
        public int EncodedLength;
        public uint MergeBitmask;
        public bool HasMergeMetadata;
    }

    public struct CullingVolumeBlob
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion;
        public int GroupCount;
        public Unity.Mathematics.uint4 VolumeId;
        public CullingBakeLayout BakedGrid;
        public BlobArray<CullingGroupKey> Groups;
        public BlobArray<CullingCellRange> Cells;
        public BlobArray<byte> CompressedBytes;
    }
}
