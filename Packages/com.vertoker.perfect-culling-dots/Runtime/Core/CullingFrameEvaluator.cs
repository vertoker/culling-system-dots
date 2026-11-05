using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PerfectCullingDOTS.Core
{
    public static class CullingFrameEvaluator
    {
        /// <summary>Managed sequential reference, using the same deterministic replay as the Burst job.</summary>
        public static void Run(in CullingFrameInput input, in CullingFrameState previous, ref CullingFrameState output)
        {
            Validate(in input, in previous, in output);
            int largestGroupCount = 0;
            for (int i = 0; i < input.Volumes.Length; i++)
                largestGroupCount = math.max(largestGroupCount, input.Volumes[i].Bake.Value.GroupCount);
            NativeArray<byte> visible = default;
            NativeList<ushort> decoded = default;
            NativeParallelHashMap<CullingGroupKey, int> oldGroups = default;
            try
            {
                visible = new NativeArray<byte>(largestGroupCount, Allocator.Temp);
                decoded = new NativeList<ushort>(largestGroupCount, Allocator.Temp);
                oldGroups = new NativeParallelHashMap<CullingGroupKey, int>(math.max(1, previous.Groups.Length), Allocator.Temp);
                new EvaluateJob { Input = input, Previous = previous, Output = output,
                    Visible = visible, Decoded = decoded, OldGroups = oldGroups }.Execute();
            }
            finally
            {
                if (visible.IsCreated) visible.Dispose();
                if (decoded.IsCreated) decoded.Dispose();
                if (oldGroups.IsCreated) oldGroups.Dispose();
            }
        }

        /// <summary>
        /// Snapshots must be fully captured and immutable before scheduling. Registration/data changes
        /// must increment affected views' InvalidationVersion; SnapshotVersion only identifies publication.
        /// This preserves source hash collisions and source layer/radius edits that do not call SetDirty.
        /// Previous and output must be distinct owned buffers. The returned handle includes scratch cleanup.
        /// </summary>
        public static JobHandle Schedule(in CullingFrameInput input, in CullingFrameState previous,
            ref CullingFrameState output, JobHandle dependency = default)
        {
            Validate(in input, in previous, in output);
            int largestGroupCount = 0;
            for (int i = 0; i < input.Volumes.Length; i++)
                largestGroupCount = math.max(largestGroupCount, input.Volumes[i].Bake.Value.GroupCount);
            NativeArray<byte> visible = default;
            NativeList<ushort> decoded = default;
            NativeParallelHashMap<CullingGroupKey, int> oldGroups = default;
            try
            {
                visible = new NativeArray<byte>(largestGroupCount, Allocator.TempJob);
                decoded = new NativeList<ushort>(largestGroupCount, Allocator.TempJob);
                oldGroups = new NativeParallelHashMap<CullingGroupKey, int>(math.max(1, previous.Groups.Length), Allocator.TempJob);
                var handle = new EvaluateJob { Input = input, Previous = previous, Output = output,
                    Visible = visible, Decoded = decoded, OldGroups = oldGroups }.Schedule(dependency);
                handle = visible.Dispose(handle);
                handle = decoded.Dispose(handle);
                return oldGroups.Dispose(handle);
            }
            catch
            {
                if (visible.IsCreated) visible.Dispose();
                if (decoded.IsCreated) decoded.Dispose();
                if (oldGroups.IsCreated) oldGroups.Dispose();
                throw;
            }
        }

        private static void Validate(in CullingFrameInput input, in CullingFrameState previous, in CullingFrameState output)
        {
            if (!input.Volumes.IsCreated || !input.Views.IsCreated || !input.Groups.IsCreated ||
                !input.Portals.IsCreated || !input.Passes.IsCreated)
                throw new ArgumentException("All snapshot arrays must be created, including empty arrays.");
            if (!IsCreated(in previous) || !IsCreated(in output) ||
                output.Groups.Length != input.Groups.Length || output.Queued.Length != input.Groups.Length ||
                output.Applied.Length != input.Groups.Length || output.StartApplied.Length != input.Groups.Length || output.Views.Length != input.Views.Length ||
                output.Passes.Length != input.Passes.Length || output.PassStatistics.Length != input.Passes.Length ||
                output.PassStates.Length != checked(input.Passes.Length * input.Groups.Length) ||
                output.PassVolumeApplications.Length != checked(input.Passes.Length * input.Volumes.Length) ||
                output.Control.Length != 1 || output.Result.Length != 1 || previous.Control.Length != 1 ||
                previous.Result.Length != 1 || previous.Queued.Length != previous.Groups.Length ||
                previous.Applied.Length != previous.Groups.Length || previous.StartApplied.Length != previous.Groups.Length)
                throw new ArgumentException("Buffer shapes do not match the snapshots.");
            if (Aliases(output.Result, previous.Result) || Aliases(output.Control, previous.Control) ||
                Aliases(output.Groups, previous.Groups) || Aliases(output.Queued, previous.Queued) ||
                Aliases(output.Applied, previous.Applied) || Aliases(output.StartApplied, previous.StartApplied) || Aliases(output.Views, previous.Views) ||
                Aliases(output.Passes, previous.Passes) || Aliases(output.PassStates, previous.PassStates) ||
                Aliases(output.PassVolumeApplications, previous.PassVolumeApplications) ||
                Aliases(output.PassStatistics, previous.PassStatistics))
                throw new ArgumentException("Previous and output buffers must not alias.");
            var viewIds = new HashSet<ulong>();
            for (int i = 0; i < input.Views.Length; i++)
            {
                var view = input.Views[i];
                if (view.ViewId == 0 || !viewIds.Add(view.ViewId) || !math.all(math.isfinite(view.Position)) ||
                    view.NeighborRadius < 0 || view.NeighborRadius > 2)
                    throw new ArgumentException("Views require unique nonzero IDs, finite positions and source inspector radii 0..2.");
            }
            for (int i = 0; i < input.Passes.Length; i++)
            {
                var pass = input.Passes[i];
                if (pass.ViewIndex < -1 || pass.ViewIndex >= input.Views.Length ||
                    (pass.ViewIndex >= 0 && pass.RenderViewId != input.Views[pass.ViewIndex].ViewId))
                    throw new ArgumentException("A registered pass must address its actual render view.");
            }
            int groupOffset = 0;
            var keys = new HashSet<CullingGroupKey>();
            var volumeIds = new HashSet<uint4>();
            for (int i = 0; i < input.Volumes.Length; i++)
            {
                var volume = input.Volumes[i];
                if (!volume.Bake.IsCreated || volume.GroupOffset != groupOffset || volume.PortalOffset < 0 ||
                    volume.PortalCount < 0 || volume.PortalCount > input.Portals.Length ||
                    volume.PortalOffset > input.Portals.Length - volume.PortalCount ||
                    (uint)volume.OutOfBounds > 2 || (uint)volume.EmptyCell > 1)
                    throw new ArgumentException("Invalid volume range or policy.");
                ref var blob = ref volume.Bake.Value;
                var instanceId = math.any(volume.InstanceId != 0) ? volume.InstanceId : blob.VolumeId;
                if (math.all(instanceId == 0) || !volumeIds.Add(instanceId))
                    throw new ArgumentException("Volumes require distinct nonzero instance identities, including shared bake instances.");
                if (blob.GroupCount < 0 || blob.GroupCount > ushort.MaxValue || blob.Groups.Length != blob.GroupCount ||
                    blob.GroupCount > input.Groups.Length - groupOffset)
                    throw new ArgumentException("Invalid group mapping.");
                var grid = volume.Grid;
                if (!math.all(math.isfinite(grid.Origin)) || !math.all(math.isfinite(grid.Size)) ||
                    !math.all(math.isfinite(grid.CellCount)) || !math.all(math.isfinite(grid.CellSize)) ||
                    !math.all(grid.Size > 0) || !math.all(grid.CellCount > 0) || !math.all(grid.CellSize > 0) ||
                    !math.all(grid.CellCount == math.floor(grid.CellCount)) ||
                    (double)grid.CellCount.x * grid.CellCount.y * grid.CellCount.z > int.MaxValue ||
                    !math.all(math.isfinite(grid.BakeOrientation.value)) ||
                    !math.all(math.isfinite(grid.InverseBakeOrientation.value)) ||
                    !math.all(math.isfinite(grid.InverseCurrentOrientation.value)))
                    throw new ArgumentException("Invalid captured grid.");
                for (int g = 0; g < blob.GroupCount; g++)
                {
                    var group = input.Groups[groupOffset + g];
                    var sourceKey = blob.Groups[g];
                    if (sourceKey.GroupIndex != g || !math.all(sourceKey.VolumeId == blob.VolumeId) ||
                        group.Key.GroupIndex != sourceKey.GroupIndex || !math.all(group.Key.VolumeId == instanceId) || !keys.Add(group.Key) ||
                        group.InitialQueued > 1 || group.InitialApplied > 1)
                        throw new ArgumentException("Snapshot mapping must match the bake, without duplicate keys.");
                }
                groupOffset += blob.GroupCount;
            }
            if (groupOffset != input.Groups.Length) throw new ArgumentException("Unmapped groups in snapshot.");
            for (int i = 0; i < input.Portals.Length; i++)
                if (!math.all(math.isfinite(input.Portals[i].WorldPosition))) throw new ArgumentException("Invalid portal.");
        }

        private static bool Aliases<T>(NativeArray<T> a, NativeArray<T> b) where T : struct
            => a.Length > 0 && a.Equals(b);

        private static bool IsCreated(in CullingFrameState state)
            => state.Groups.IsCreated && state.Queued.IsCreated && state.Applied.IsCreated && state.StartApplied.IsCreated && state.Views.IsCreated &&
                state.Passes.IsCreated && state.PassStates.IsCreated && state.PassVolumeApplications.IsCreated &&
                state.PassStatistics.IsCreated && state.Control.IsCreated && state.Result.IsCreated;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        private struct EvaluateJob : IJob
        {
            [ReadOnly] public CullingFrameInput Input;
            [ReadOnly] public CullingFrameState Previous;
            public CullingFrameState Output;
            public NativeArray<byte> Visible;
            public NativeList<ushort> Decoded;
            public NativeParallelHashMap<CullingGroupKey, int> OldGroups;

            public void Execute()
            {
                for (int i = 0; i < Previous.Groups.Length; i++) OldGroups.TryAdd(Previous.Groups[i].Key, i);
                Initialize();
                bool burst = true;
                MarkManaged(ref burst);
                var previousStatus = Previous.Result[0].Status;
                if (previousStatus != CullingEvaluationStatus.Success &&
                    !(previousStatus == CullingEvaluationStatus.NotEvaluated && Previous.Groups.Length == 0 && Previous.Views.Length == 0))
                {
                    var failure = Previous.Result[0];
                    failure.Status = CullingEvaluationStatus.InvalidPreviousState;
                    failure.RanInBurst = burst;
                    Output.Result[0] = failure;
                    return;
                }
                if (Input.RestoreAllVisibility)
                {
                    for (int i = 0; i < Output.Queued.Length; i++) { Output.Queued[i] = 1; Output.Applied[i] = 1; Output.StartApplied[i] = 1; }
                    var control = Output.Control[0]; control.RestoreApplied = true; Output.Control[0] = control;
                }
                for (int passIndex = 0; passIndex < Input.Passes.Length; passIndex++)
                {
                    var pass = Input.Passes[passIndex];
                    Output.Passes[passIndex] = pass;
                    if (pass.ViewIndex >= 0)
                    {
                        var view = Input.Views[pass.ViewIndex];
                        var state = Output.Views[pass.ViewIndex];
                        int hash = CalculateHash(view, pass.ViewIndex);
                        if (hash != state.Hash)
                        {
                            var control = Output.Control[0];
                            if (control.LastClearFrame != Input.FrameId)
                            {
                                for (int g = 0; g < Output.Queued.Length; g++) Output.Queued[g] = (byte)(view.Invert ? 1 : 0);
                                control.LastClearFrame = Input.FrameId; Output.Control[0] = control;
                            }
                            state.Hash = hash;
                            state.Total = state.Visible = state.TotalVertices = state.VisibleVertices = 0;
                            for (int v = 0; v < Input.Volumes.Length; v++)
                            {
                                var volume = Input.Volumes[v];
                                if ((view.Layers & volume.Layers) == 0) continue;
                                var primary = Sample(view.Position, volume);
                                if (volume.OutOfBounds == CullingOutOfBoundsPolicy.IgnoreDoNothing && primary.IsOutOfBounds) continue;
                                for (int g = 0; g < volume.Bake.Value.GroupCount; g++) Visible[g] = 0;
                                state.TotalVertices = unchecked(state.TotalVertices + volume.TotalVertexCount);
                                bool stop = false;
                                int offsets = view.NeighborRadius == 0 ? 1 : 7;
                                for (int offset = 0; offset < offsets && !stop; offset++)
                                for (int distance = 1; distance <= view.NeighborRadius + 1; distance++)
                                {
                                    var position = view.Position + GridSampling.GetNeighborOffset(offset) * distance * volume.Grid.CellSize;
                                    var sample = Sample(position, volume);
                                    if (volume.OutOfBounds == CullingOutOfBoundsPolicy.Cull && sample.IsOutOfBounds) continue;
                                    Decoded.Clear();
                                    var decoded = LegacyCellDecoder.DecodeCell(ref volume.Bake.Value, sample.Index, ref Decoded);
                                    if (decoded.Status != DecodeStatus.Success)
                                    {
                                        // Rebuild from previous; no partially updated buffer is publishable.
                                        Initialize();
                                        Output.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.InvalidCellData,
                                            DecodeStatus = decoded.Status, PassIndex = passIndex, VolumeIndex = v,
                                            CellIndex = sample.Index, RanInBurst = burst };
                                        return;
                                    }
                                    if (volume.EmptyCell == CullingEmptyCellPolicy.CullNothing && offset == 0 && Decoded.Length == 0)
                                    {
                                        for (int g = 0; g < volume.Bake.Value.GroupCount; g++) Select(g, volume, view.Invert, ref state);
                                        stop = true; break;
                                    }
                                    for (int g = 0; g < Decoded.Length; g++)
                                        if (Visible[Decoded[g]] == 0) Select(Decoded[g], volume, view.Invert, ref state);
                                }
                                state.Total = unchecked(state.Total + volume.Bake.Value.GroupCount);
                                for (int g = 0; g < volume.Bake.Value.GroupCount; g++)
                                    Output.Applied[volume.GroupOffset + g] = Output.Queued[volume.GroupOffset + g];
                                Output.PassVolumeApplications[passIndex * Input.Volumes.Length + v] = 1;
                            }
                            Output.Views[pass.ViewIndex] = state;
                        }
                        Output.PassStatistics[passIndex] = state;
                    }
                    for (int g = 0; g < Output.Applied.Length; g++)
                        Output.PassStates[passIndex * Input.Groups.Length + g] = Output.Applied[g];
                }
                Output.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success, RanInBurst = burst };
            }

            private void Initialize()
            {
                for (int i = 0; i < Input.Groups.Length; i++)
                {
                    var group = Input.Groups[i];
                    Output.Groups[i] = group;
                    bool reuse = OldGroups.TryGetValue(group.Key, out int old) && Previous.Groups[old].StateGeneration == group.StateGeneration;
                    Output.Queued[i] = reuse ? Previous.Queued[old] : group.InitialQueued;
                    Output.Applied[i] = reuse ? Previous.Applied[old] : group.InitialApplied;
                    Output.StartApplied[i] = Output.Applied[i];
                }
                for (int i = 0; i < Input.Views.Length; i++)
                {
                    var view = Input.Views[i];
                    var state = new CullingViewState { ViewId = view.ViewId, Hash = -1, InvalidationVersion = view.InvalidationVersion };
                    for (int j = 0; j < Previous.Views.Length; j++)
                        if (Previous.Views[j].ViewId == view.ViewId)
                        {
                            state = Previous.Views[j];
                            if (state.InvalidationVersion != view.InvalidationVersion) state.Hash = -1;
                            state.InvalidationVersion = view.InvalidationVersion;
                            break;
                        }
                    Output.Views[i] = state;
                }
                Output.Control[0] = new CullingFrameControl { LastClearFrame = Previous.Control[0].LastClearFrame, SnapshotVersion = Input.SnapshotVersion };
                for (int i = 0; i < Output.PassVolumeApplications.Length; i++) Output.PassVolumeApplications[i] = 0;
                for (int i = 0; i < Output.PassStates.Length; i++) Output.PassStates[i] = 0;
                for (int i = 0; i < Output.PassStatistics.Length; i++) Output.PassStatistics[i] = default;
                for (int i = 0; i < Output.Passes.Length; i++) Output.Passes[i] = default;
            }

            private int CalculateHash(CullingViewSnapshot caller, int registrationIndex)
            {
                int hash = unchecked(13 * 17 + registrationIndex);
                int offsets = caller.NeighborRadius == 0 ? 1 : 7;
                for (int v = 0; v < Input.Volumes.Length; v++)
                {
                    var volume = Input.Volumes[v];
                    if ((caller.Layers & volume.Layers) == 0) continue;
                    for (int c = 0; c < Input.Views.Length; c++)
                    for (int offset = 0; offset < offsets; offset++)
                    for (int distance = 1; distance <= caller.NeighborRadius + 1; distance++)
                    {
                        var position = Input.Views[c].Position + GridSampling.GetNeighborOffset(offset) * distance * volume.Grid.CellSize;
                        var sample = Sample(position, volume);
                        if (sample.IsOutOfBounds && volume.OutOfBounds != CullingOutOfBoundsPolicy.Clamp) continue;
                        hash = unchecked(hash * 17 + sample.Index);
                    }
                }
                return hash;
            }

            private CullingCellSample Sample(float3 position, CullingVolumeSnapshot volume)
                => GridSampling.ResolveSample(position, in volume.Grid, ref volume.Bake.Value,
                    Input.Portals.GetSubArray(volume.PortalOffset, volume.PortalCount), volume.SearchNonEmpty);

            private void Select(int group, CullingVolumeSnapshot volume, bool invert, ref CullingViewState state)
            {
                Visible[group] = 1;
                Output.Queued[volume.GroupOffset + group] = (byte)(invert ? 0 : 1);
                state.Visible = unchecked(state.Visible + 1);
                state.VisibleVertices = unchecked(state.VisibleVertices + Input.Groups[volume.GroupOffset + group].VertexCount);
            }

            [BurstDiscard] private static void MarkManaged(ref bool burst) { burst = false; }
        }
    }
}
