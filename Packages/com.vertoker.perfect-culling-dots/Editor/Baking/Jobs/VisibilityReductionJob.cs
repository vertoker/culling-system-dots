using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
namespace PerfectCullingDOTS.Editor.Baking.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    internal struct VisibilityReductionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> TileBits, TileNeighbors;
        // Cell c exclusively owns [c*Words,(c+1)*Words).
        [NativeDisableParallelForRestriction] public NativeArray<uint> CellBits;
        [WriteOnly] public NativeArray<uint> CellNeighbors;
        public int Words, TilesPerCell;
        public void Execute(int cell)
        {
            uint neighbors = 0;
            for (int tile = 0; tile < TilesPerCell; tile++)
            {
                int work = cell*TilesPerCell+tile; neighbors |= TileNeighbors[work];
                for (int word = 0; word < Words; word++) CellBits[cell*Words+word] |= TileBits[work*Words+word];
            }
            CellNeighbors[cell] = neighbors;
        }
    }
}
