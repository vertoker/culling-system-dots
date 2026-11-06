using Unity.Collections;
using Unity.Mathematics;

namespace CullingSystemDOTS.Core
{
    public struct CullingGridSnapshot
    {
        public float3 Origin;
        public float3 Size;
        public float3 CellCount;
        public float3 CellSize;
        public quaternion BakeOrientation;
        // Authoring/capture computes these using Unity's actual Quaternion.Inverse on the main thread.
        public quaternion InverseBakeOrientation;
        public quaternion InverseCurrentOrientation;
    }

    public struct CullingPortalSnapshot
    {
        public float3 WorldPosition;
    }

    public struct CullingCellSample
    {
        public int Index;
        public bool IsOutOfBounds;
    }

    public static class GridSampling
    {
        public static int GetIndexForWorldPos(float3 position, in CullingGridSnapshot grid, out bool isOutOfBounds)
        {
            // Keep Unity's quaternion operation order: algebraic simplification changes boundary rounding.
            var difference = Q(grid.InverseCurrentOrientation) * Q(grid.BakeOrientation);
            var rotated = difference * V(position - grid.Origin) + V(grid.Origin);
            var local = Q(grid.InverseBakeOrientation) * (rotated - V(grid.Origin)) + 0.5f * V(grid.Size);
            int x = (int)(local.x / grid.CellSize.x);
            int y = (int)(local.y / grid.CellSize.y);
            int z = (int)(local.z / grid.CellSize.z);
            int clampedX = (int)UnityEngine.Mathf.Clamp(x, 0, grid.CellCount.x - 1);
            int clampedY = (int)UnityEngine.Mathf.Clamp(y, 0, grid.CellCount.y - 1);
            int clampedZ = (int)UnityEngine.Mathf.Clamp(z, 0, grid.CellCount.z - 1);
            isOutOfBounds = math.abs(x - clampedX) > 2 || math.abs(y - clampedY) > 2 || math.abs(z - clampedZ) > 2;
            return Flatten(clampedX, clampedY, clampedZ, grid.CellCount);
        }

        public static int FindClosestNonEmptyCell(ref CullingVolumeBlob volume, int index, float3 cellCount)
        {
            int countX = (int)cellCount.x;
            int countY = (int)cellCount.y;
            int countZ = (int)cellCount.z;
            if ((uint)index >= (uint)volume.Cells.Length || countX <= 0 || countY <= 0 || countZ <= 0)
                return -1;
            long planeSize = (long)countX * countY;
            if (planeSize > volume.Cells.Length || planeSize * countZ != volume.Cells.Length)
                return -1;
            if (volume.Cells[index].EncodedLength > 0) return index;
            int x = index % countX;
            int y = (index / countX) % countY;
            int z = index / (int)planeSize;
            int distance = int.MaxValue;
            int result = -1;
            // Source loops are inclusive and X/Y/Z ordered; equal distances retain the first cell.
            for (int dx = -3; dx <= 3; dx++)
            for (int dy = -3; dy <= 3; dy++)
            for (int dz = -3; dz <= 3; dz++)
            {
                int xx = x + dx;
                int yy = y + dy;
                int zz = z + dz;
                if (xx < 0 || yy < 0 || zz < 0 || xx >= cellCount.x || yy >= cellCount.y || zz >= cellCount.z)
                    continue;
                int candidate = Flatten(xx, yy, zz, cellCount);
                if (volume.Cells[candidate].EncodedLength <= 0) continue;
                int squaredDistance = dx * dx + dy * dy + dz * dz;
                if (distance > squaredDistance)
                {
                    distance = squaredDistance;
                    result = candidate;
                }
            }
            return result;
        }

        public static CullingCellSample ResolveSample(float3 position, in CullingGridSnapshot grid,
            ref CullingVolumeBlob volume, NativeArray<CullingPortalSnapshot> portals, bool searchNonEmpty)
        {
            int index = GetIndexForWorldPos(position, in grid, out bool outside);
            if (outside)
            {
                int closest = -1;
                float distance = float.MaxValue;
                for (int i = 0; i < portals.Length; i++)
                {
                    float3 delta = position - portals[i].WorldPosition;
                    float squaredDistance = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                    if (distance > squaredDistance)
                    {
                        distance = squaredDistance;
                        closest = i;
                    }
                }
                if (closest >= 0)
                {
                    // The legacy portal branch returns immediately, bypassing nonempty-cell search.
                    index = GetIndexForWorldPos(portals[closest].WorldPosition, in grid, out outside);
                    return new CullingCellSample { Index = index, IsOutOfBounds = outside };
                }
            }
            if (searchNonEmpty)
            {
                int replacement = FindClosestNonEmptyCell(ref volume, index, volume.BakedGrid.CellCount);
                if (replacement >= 0) index = replacement;
            }
            return new CullingCellSample { Index = index, IsOutOfBounds = outside };
        }

        public static float3 GetNeighborOffset(int index)
        {
            switch (index)
            {
                case 1: return new float3(1, 0, 0);
                case 2: return new float3(-1, 0, 0);
                case 3: return new float3(0, 0, 1);
                case 4: return new float3(0, 0, -1);
                case 5: return new float3(0, 1, 0);
                case 6: return new float3(0, -1, 0);
                default: return float3.zero;
            }
        }

        private static int Flatten(int x, int y, int z, float3 count)
            => (int)System.Math.Ceiling(z * (double)count.x * (double)count.y + y * (double)count.x + x);

        private static UnityEngine.Quaternion Q(quaternion q)
            => new UnityEngine.Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);

        private static UnityEngine.Vector3 V(float3 v) => new UnityEngine.Vector3(v.x, v.y, v.z);
    }
}
