using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic
{
    public sealed class OcclusionBlocks
    {
        public readonly float BlockSize;
        public readonly int MaxBlocksPerBounds;
        public OcclusionBlocks(float blockSize, int maxBlocksPerBounds = 4096)
        {
            if (!OcclusionValidity.Finite(blockSize) || blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
            if (maxBlocksPerBounds <= 0) throw new ArgumentOutOfRangeException(nameof(maxBlocksPerBounds));
            BlockSize = blockSize; MaxBlocksPerBounds = maxBlocksPerBounds;
        }
        /// <summary>Replaces output with all touched cells, including exact upper-bound contact.</summary>
        public void GetOverlappingBlocks(in Bounds bounds, NativeList<int3> output)
        {
            output.Clear(); var range = GetRange(bounds);
            if (output.Capacity < range.Count) output.Capacity = range.Count;
            for (int i = 0; i < range.Count; i++) output.Add(range.Cell(i));
        }

        internal CellRange GetRange(in Bounds bounds)
        {
            if (!math.all(math.isfinite((float3)bounds.min)) || !math.all(math.isfinite((float3)bounds.max)) ||
                !math.all(math.isfinite((float3)bounds.center)) || !math.all(math.isfinite((float3)bounds.extents)) || math.any((float3)bounds.extents < 0))
                throw new ArgumentException("Block bounds must be finite and nonnegative.", nameof(bounds));
            var min = bounds.min; var max = bounds.max;
            var first = new int3(Cell(min.x), Cell(min.y), Cell(min.z));
            var last = new int3(Cell(max.x), Cell(max.y), Cell(max.z));
            long x = (long)last.x - first.x + 1, y = (long)last.y - first.y + 1, z = (long)last.z - first.z + 1;
            if (x <= 0 || y <= 0 || z <= 0 || x > MaxBlocksPerBounds || y > MaxBlocksPerBounds / x || z > MaxBlocksPerBounds / (x * y))
                throw new ArgumentException("The bounds exceed the configured block membership limit.", nameof(bounds));
            long count = x * y * z;
            return new CellRange(first, last, (int)count);
        }

        private int Cell(float value)
        {
            double coordinate = Math.Floor((double)value / BlockSize);
            if (coordinate < int.MinValue || coordinate > int.MaxValue)
                throw new ArgumentException("A block coordinate is outside the supported integer range.");
            return (int)coordinate;
        }
        internal readonly struct CellRange : IEquatable<CellRange>
        {
            internal readonly int3 Min, Max;
            internal readonly int Count;
            internal CellRange(int3 min, int3 max, int count) { Min = min; Max = max; Count = count; }
            internal int3 Cell(int index)
            {
                int width = (int)((long)Max.x - Min.x + 1), height = (int)((long)Max.y - Min.y + 1);
                return new int3((int)((long)Min.x + index % width), (int)((long)Min.y + index / width % height),
                    (int)((long)Min.z + index / (width * height)));
            }
            public bool Equals(CellRange other) => Min.Equals(other.Min) && Max.Equals(other.Max);
        }
    }
}
