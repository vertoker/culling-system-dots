using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace CullingSystemDOTS.Core
{
    public static partial class CullingFrameEvaluator
    {
        private struct VolumeHash { public int Factor, Contribution; public CullingCellSample Primary; }
        private struct ViewHash { public int Hash; public bool Dirty; }
        private struct VolumeCandidates { public DecodeStatus Status; public int CellIndex; }

        private struct ParallelScratch : IDisposable
        {
            public NativeArray<VolumeHash> Hashes;
            public NativeArray<ViewHash> Views;
            public NativeArray<VolumeCandidates> Candidates;
            public NativeArray<byte> Selected;
            public static ParallelScratch Create(in CullingFrameInput input)
            {
                int work = checked(input.Views.Length * input.Volumes.Length);
                int masks = checked(input.Views.Length * input.Groups.Length);
                var scratch = new ParallelScratch();
                try
                {
                    scratch.Hashes = new NativeArray<VolumeHash>(work, Allocator.TempJob);
                    scratch.Views = new NativeArray<ViewHash>(input.Views.Length, Allocator.TempJob);
                    scratch.Candidates = new NativeArray<VolumeCandidates>(work, Allocator.TempJob);
                    scratch.Selected = new NativeArray<byte>(masks, Allocator.TempJob);
                    return scratch;
                }
                catch { scratch.Dispose(); throw; }
            }
            public JobHandle Dispose(JobHandle dependency)
            {
                dependency = Hashes.Dispose(dependency); dependency = Views.Dispose(dependency);
                dependency = Candidates.Dispose(dependency); return Selected.Dispose(dependency);
            }
            public void Dispose()
            {
                if (Hashes.IsCreated) Hashes.Dispose(); if (Views.IsCreated) Views.Dispose();
                if (Candidates.IsCreated) Candidates.Dispose(); if (Selected.IsCreated) Selected.Dispose();
            }
        }

        private static CullingCellSample Resolve(float3 position, CullingVolumeSnapshot volume,
            NativeArray<CullingPortalSnapshot> portals)
            => GridSampling.ResolveSample(position, in volume.Grid, ref volume.Bake.Value,
                portals.GetSubArray(volume.PortalOffset, volume.PortalCount), volume.SearchNonEmpty);

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        private struct VolumeHashJob : IJobParallelFor
        {
            [ReadOnly] public CullingFrameInput Input;
            public NativeArray<VolumeHash> Hashes;
            public void Execute(int index)
            {
                int viewIndex = index / Input.Volumes.Length, volumeIndex = index % Input.Volumes.Length;
                var caller = Input.Views[viewIndex]; var volume = Input.Volumes[volumeIndex];
                var result = new VolumeHash { Factor = 1 };
                if ((caller.Layers & volume.Layers) != 0)
                {
                    int offsets = caller.NeighborRadius == 0 ? 1 : 7;
                    for (int c = 0; c < Input.Views.Length; c++)
                    for (int offset = 0; offset < offsets; offset++)
                    for (int distance = 1; distance <= caller.NeighborRadius + 1; distance++)
                    {
                        var position = Input.Views[c].Position + GridSampling.GetNeighborOffset(offset) * distance * volume.Grid.CellSize;
                        var sample = Resolve(position, volume, Input.Portals);
                        if (sample.IsOutOfBounds && volume.OutOfBounds != CullingOutOfBoundsPolicy.Clamp) continue;
                        result.Factor = unchecked(result.Factor * 17);
                        result.Contribution = unchecked(result.Contribution * 17 + sample.Index);
                    }
                    result.Primary = Resolve(caller.Position, volume, Input.Portals);
                }
                Hashes[index] = result;
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        private struct ViewHashJob : IJobParallelFor
        {
            [ReadOnly] public CullingFrameInput Input;
            [ReadOnly] public NativeArray<CullingViewState> PreviousViews;
            [ReadOnly] public NativeArray<VolumeHash> Hashes;
            public NativeArray<ViewHash> Views;
            public void Execute(int index)
            {
                int hash = unchecked(13 * 17 + index);
                for (int v = 0; v < Input.Volumes.Length; v++)
                {
                    var part = Hashes[index * Input.Volumes.Length + v];
                    hash = unchecked(hash * part.Factor + part.Contribution);
                }
                var view = Input.Views[index]; int oldHash = -1;
                for (int i = 0; i < PreviousViews.Length; i++)
                    if (PreviousViews[i].ViewId == view.ViewId)
                    {
                        if (PreviousViews[i].InvalidationVersion == view.InvalidationVersion) oldHash = PreviousViews[i].Hash;
                        break;
                    }
                bool rendered = false;
                for (int p = 0; p < Input.Passes.Length; p++) if (Input.Passes[p].ViewIndex == index) { rendered = true; break; }
                Views[index] = new ViewHash { Hash = hash, Dirty = rendered && hash != oldHash };
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        private struct VolumeCandidatesJob : IJobParallelFor
        {
            [ReadOnly] public CullingFrameInput Input;
            [ReadOnly] public NativeArray<VolumeHash> Hashes;
            [ReadOnly] public NativeArray<ViewHash> Views;
            // Each work item owns [view * groupCount + volume.GroupOffset, + volume.GroupCount).
            // Validate enforces non-overlapping volume ranges. There are no shared OR writes.
            [NativeDisableParallelForRestriction] public NativeArray<byte> Selected;
            public NativeArray<VolumeCandidates> Results;
            public void Execute(int index)
            {
                int viewIndex = index / Input.Volumes.Length, volumeIndex = index % Input.Volumes.Length;
                var view = Input.Views[viewIndex]; var volume = Input.Volumes[volumeIndex];
                var result = new VolumeCandidates { Status = DecodeStatus.Success };
                if (!Views[viewIndex].Dirty || (view.Layers & volume.Layers) == 0 ||
                    (volume.OutOfBounds == CullingOutOfBoundsPolicy.IgnoreDoNothing && Hashes[index].Primary.IsOutOfBounds))
                { Results[index] = result; return; }
                int start = viewIndex * Input.Groups.Length + volume.GroupOffset;
                var decoded = new NativeList<ushort>(volume.Bake.Value.GroupCount, Allocator.Temp);
                int offsets = view.NeighborRadius == 0 ? 1 : 7;
                bool stop = false;
                for (int offset = 0; offset < offsets && !stop; offset++)
                for (int distance = 1; distance <= view.NeighborRadius + 1; distance++)
                {
                    var position = view.Position + GridSampling.GetNeighborOffset(offset) * distance * volume.Grid.CellSize;
                    var sample = Resolve(position, volume, Input.Portals);
                    if (volume.OutOfBounds == CullingOutOfBoundsPolicy.Cull && sample.IsOutOfBounds) continue;
                    decoded.Clear();
                    var status = LegacyCellDecoder.DecodeCell(ref volume.Bake.Value, sample.Index, ref decoded);
                    if (status.Status != DecodeStatus.Success)
                    {
                        result.Status = status.Status; result.CellIndex = sample.Index;
                        decoded.Dispose(); Results[index] = result; return;
                    }
                    if (volume.EmptyCell == CullingEmptyCellPolicy.CullNothing && offset == 0 && decoded.Length == 0)
                    {
                        for (int g = 0; g < volume.Bake.Value.GroupCount; g++) Selected[start + g] = 1;
                        stop = true; break;
                    }
                    for (int g = 0; g < decoded.Length; g++) Selected[start + decoded[g]] = 1;
                }
                decoded.Dispose(); Results[index] = result;
            }
        }
    }
}
