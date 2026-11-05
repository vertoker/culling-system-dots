using Unity.Collections;

namespace PerfectCullingDOTS.Core
{
    public enum DecodeStatus
    {
        Success, UnsupportedFormat, InvalidCell, InvalidRange, TruncatedData,
        UnterminatedDelta, NonIncreasingGroupIndex, InvalidGroupIndex
    }

    public struct DecodeResult
    {
        public DecodeStatus Status;
        public int DecodedCount;
    }

    public static class LegacyCellDecoder
    {
        public static DecodeResult DecodeCell(ref CullingVolumeBlob volume, int cellIndex, ref NativeList<ushort> destination)
        {
            if (volume.FormatVersion != CullingVolumeBlob.CurrentFormatVersion)
                return new DecodeResult { Status = DecodeStatus.UnsupportedFormat };
            if ((uint)cellIndex >= (uint)volume.Cells.Length)
                return new DecodeResult { Status = DecodeStatus.InvalidCell };
            ref var cell = ref volume.Cells[cellIndex];
            if (cell.ByteOffset < 0 || cell.ByteLength < 0 || cell.ByteLength > volume.CompressedBytes.Length ||
                cell.ByteOffset > volume.CompressedBytes.Length - cell.ByteLength ||
                cell.EncodedLength < 0 || cell.EncodedLength > ushort.MaxValue)
                return new DecodeResult { Status = DecodeStatus.InvalidRange };
            if (volume.GroupCount < 0 || volume.GroupCount > ushort.MaxValue)
                return new DecodeResult { Status = DecodeStatus.InvalidGroupIndex };

            int originalLength = destination.Length;
            int accumulator = 0;
            int previousGroup = -1;
            long bitPosition = 0;
            bool continuation = false;
            for (int i = 0; i < cell.EncodedLength; i++)
            {
                if (!TryRead(ref volume, ref cell, ref bitPosition, 2, out uint header) ||
                    !TryRead(ref volume, ref cell, ref bitPosition, (int)header + 1, out uint value))
                    return Fail(ref destination, originalLength, DecodeStatus.TruncatedData);

                accumulator += (int)value;
                if (accumulator >= volume.GroupCount)
                    return Fail(ref destination, originalLength, DecodeStatus.InvalidGroupIndex);
                continuation = value == 15;
                if (continuation) continue;
                if (accumulator <= previousGroup)
                    return Fail(ref destination, originalLength, DecodeStatus.NonIncreasingGroupIndex);
                destination.Add((ushort)accumulator);
                previousGroup = accumulator;
            }
            if (continuation)
                return Fail(ref destination, originalLength, DecodeStatus.UnterminatedDelta);
            return new DecodeResult { Status = DecodeStatus.Success, DecodedCount = destination.Length - originalLength };
        }

        private static DecodeResult Fail(ref NativeList<ushort> destination, int originalLength, DecodeStatus status)
        {
            destination.ResizeUninitialized(originalLength);
            return new DecodeResult { Status = status };
        }

        private static bool TryRead(ref CullingVolumeBlob volume, ref CullingCellRange cell,
            ref long bitPosition, int bitCount, out uint value)
        {
            value = 0;
            if (bitPosition + bitCount > (long)cell.ByteLength * 8) return false;
            for (int bit = 0; bit < bitCount; bit++, bitPosition++)
            {
                byte current = volume.CompressedBytes[cell.ByteOffset + (int)(bitPosition >> 3)];
                value |= (uint)((current >> (int)(bitPosition & 7)) & 1) << bit;
            }
            return true;
        }
    }
}
