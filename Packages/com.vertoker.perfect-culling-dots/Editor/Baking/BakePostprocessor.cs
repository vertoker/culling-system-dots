using System;
using System.Collections.Generic;
using PerfectCullingDOTS.Core;
using UnityEngine;
using Unity.Mathematics;
namespace PerfectCullingDOTS.Editor.Baking
{
    public sealed class BakePostprocessResult
    {
        public CullingBakeLayout Layout;
        public BakeCellResult[] Cells;
    }
    public static class BakePostprocessor
    {
        public static BakePostprocessResult Process(CullingBakeLayout layout, BakeCellResult[] activeCells,
            BakeMergeMetadata metadata, Vector3Int[] mergeAxes)
        {
            if (activeCells == null || mergeAxes == null) throw new ArgumentNullException();
            if ((uint)metadata > (uint)BakeMergeMetadata.PreserveMergeMasks) throw new ArgumentOutOfRangeException(nameof(metadata));
            var dimensions = layout.CellCount;
            double count = (double)dimensions.x*dimensions.y*dimensions.z;
            if (!math.all(math.isfinite(dimensions)) || !math.all(dimensions > 0) ||
                !math.all(math.floor(dimensions) == dimensions) || count > int.MaxValue) throw new ArgumentException("Invalid bake dimensions.");
            var cells = new BakeCellResult[(int)count]; var seen = new HashSet<int>();
            for (int i = 0; i < cells.Length; i++) cells[i] = new BakeCellResult { FullGridIndex = i, Groups = Array.Empty<ushort>() };
            foreach (var sample in activeCells)
            {
                if (sample == null || sample.Groups == null || (uint)sample.FullGridIndex >= cells.Length || !seen.Add(sample.FullGridIndex))
                    throw new ArgumentException("Invalid or repeated original FullGridIndex.");
                for (int i = 1; i < sample.Groups.Length; i++) if (sample.Groups[i] <= sample.Groups[i-1]) throw new ArgumentException("Groups must be sorted and unique.");
                cells[sample.FullGridIndex] = new BakeCellResult { FullGridIndex = sample.FullGridIndex, Groups = (ushort[])sample.Groups.Clone(),
                    NeighborMask = metadata == BakeMergeMetadata.NativeDefault ? uint.MaxValue : sample.NeighborMask };
            }
            foreach (var axes in mergeAxes)
            {
                if (axes == Vector3Int.zero || math.all(layout.CellCount == new float3(1))) continue;
                int3 old = (int3)layout.CellCount, factor = new int3(axes.x == 0 ? 1 : 2, axes.y == 0 ? 1 : 2, axes.z == 0 ? 1 : 2);
                int3 next = (old+factor-1)/factor; int nextCount = checked(next.x*next.y*next.z);
                var sets = new HashSet<ushort>[nextCount]; var masks = new uint[nextCount];
                for (int i = 0; i < sets.Length; i++) sets[i] = new HashSet<ushort>();
                for (int index = 0; index < cells.Length; index++)
                {
                    int x = index%old.x, y = (index/old.x)%old.y, z = index/(old.x*old.y);
                    int coarse = (z/factor.z)*next.x*next.y+(y/factor.y)*next.x+x/factor.x;
                    uint currentMask = cells[index].NeighborMask, newMask = currentMask;
                    for (int zz = -1; zz <= 1; zz++) for (int yy = -1; yy <= 1; yy++) for (int xx = -1; xx <= 1; xx++)
                    {
                        if ((axes.x == 0 && xx != 0) || (axes.y == 0 && yy != 0) || (axes.z == 0 && zz != 0)) continue;
                        int nx = x+xx, ny = y+yy, nz = z+zz;
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= old.x || ny >= old.y || nz >= old.z) continue;
                        bool self = xx == 0 && yy == 0 && zz == 0; int bit = xx+1+3*(yy+1+3*(zz+1));
                        if (!self && (currentMask & (1u<<bit)) == 0) continue;
                        var neighbor = cells[nz*old.x*old.y+ny*old.x+nx];
                        if (!self) newMask |= neighbor.NeighborMask;
                        foreach (var group in neighbor.Groups) sets[coarse].Add(group);
                    }
                    masks[coarse] |= newMask;
                }
                cells = new BakeCellResult[nextCount];
                for (int i = 0; i < cells.Length; i++)
                {
                    var groups = new ushort[sets[i].Count]; sets[i].CopyTo(groups); Array.Sort(groups);
                    cells[i] = new BakeCellResult { FullGridIndex = i, Groups = groups, NeighborMask = masks[i] };
                }
                layout.CellCount = next; layout.CellSize *= factor;
            }
            return new BakePostprocessResult { Layout = layout, Cells = cells };
        }
    }
}
