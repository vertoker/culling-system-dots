using Unity.Entities;

namespace PerfectCullingDOTS.Core
{
    public struct CullingCellRange
    {
        public int ByteOffset;
        public int ByteLength;
        // Number of encoded delta tokens, not number of decoded groups.
        public int EncodedLength;
        public uint MergeBitmask;
    }

    public struct CullingVolumeBlob
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion;
        public int GroupCount;
        public BlobArray<CullingCellRange> Cells;
        public BlobArray<byte> CompressedBytes;
    }
}
