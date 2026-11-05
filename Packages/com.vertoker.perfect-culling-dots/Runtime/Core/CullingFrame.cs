using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PerfectCullingDOTS.Core
{
    public enum CullingOutOfBoundsPolicy { Clamp, Cull, IgnoreDoNothing }
    public enum CullingEmptyCellPolicy { CullEverything, CullNothing }

    public struct CullingViewSnapshot
    {
        public ulong ViewId;
        public float3 Position;
        public uint Layers;
        public int NeighborRadius;
        public bool Invert;
        // Increment for a source-equivalent SetDirty event, not merely for a new frame.
        public ulong InvalidationVersion;
    }

    public struct CullingGroupSnapshot
    {
        public CullingGroupKey Key;
        // Change when the binding/data is replaced, even if its stable key is unchanged.
        public ulong StateGeneration;
        public int VertexCount;
        public byte InitialQueued;
        public byte InitialApplied;
        public byte InitialAppliedValid;
    }

    public struct CullingVolumeSnapshot
    {
        public BlobAssetReference<CullingVolumeBlob> Bake;
        // Scene/runtime instance identity. Zero keeps the bake's original volume identity.
        public uint4 InstanceId;
        public CullingGridSnapshot Grid;
        public int GroupOffset;
        public int PortalOffset;
        public int PortalCount;
        public uint Layers;
        public CullingOutOfBoundsPolicy OutOfBounds;
        public CullingEmptyCellPolicy EmptyCell;
        public bool SearchNonEmpty;
        public int TotalVertexCount;
    }

    public struct CullingViewPass
    {
        public ulong RenderViewId;
        // -1 represents a render pass without a registered culling camera.
        public int ViewIndex;
    }

    public struct CullingFrameInput
    {
        public NativeArray<CullingVolumeSnapshot> Volumes;
        public NativeArray<CullingViewSnapshot> Views;
        public NativeArray<CullingGroupSnapshot> Groups;
        public NativeArray<CullingPortalSnapshot> Portals;
        public NativeArray<CullingViewPass> Passes;
        public int FrameId;
        public ulong SnapshotVersion;
        // Source camera removal restores visibility without resetting the frame clear marker.
        public bool RestoreAllVisibility;
    }

    public struct CullingViewState
    {
        public ulong ViewId;
        public ulong InvalidationVersion;
        public int Hash;
        public int Total;
        public int Visible;
        public int TotalVertices;
        public int VisibleVertices;
    }

    public struct CullingFrameControl
    {
        public int LastClearFrame;
        public ulong SnapshotVersion;
        public bool RestoreApplied;
    }

    public enum CullingEvaluationStatus { NotEvaluated, Success, InvalidCellData, InvalidPreviousState }
    public struct CullingEvaluationResult
    {
        public CullingEvaluationStatus Status;
        public DecodeStatus DecodeStatus;
        public int PassIndex;
        public int VolumeIndex;
        public int CellIndex;
        public bool RanInBurst;
    }

    /// <summary>
    /// One owner per buffer. Keep input, previous and output alive until the returned handle completes.
    /// Publish only Success, then dispose the previous buffer. Blob ownership stays with the snapshot owner.
    /// PassStates is pass-major; PassVolumeApplications replays ExecuteQueue in volume/group order,
    /// including unchanged values and overlapping renderer/Behaviour bindings.
    /// </summary>
    public struct CullingFrameState : IDisposable
    {
        public NativeArray<CullingGroupSnapshot> Groups;
        public NativeArray<byte> Queued;
        public NativeArray<byte> Applied;
        // Applied group state before this frame's ordered passes (after an optional restore).
        public NativeArray<byte> StartApplied;
        public NativeArray<byte> AppliedValid;
        public NativeArray<byte> StartAppliedValid;
        public NativeArray<CullingViewState> Views;
        public NativeArray<CullingViewPass> Passes;
        public NativeArray<byte> PassStates;
        public NativeArray<byte> PassVolumeApplications;
        public NativeArray<CullingViewState> PassStatistics;
        public NativeArray<CullingFrameControl> Control;
        public NativeArray<CullingEvaluationResult> Result;

        public static CullingFrameState Create(int groups, int views, int volumes, int passes, Allocator allocator)
        {
            if (groups < 0 || views < 0 || volumes < 0 || passes < 0) throw new ArgumentOutOfRangeException();
            int states = checked(groups * passes);
            int applications = checked(volumes * passes);
            var buffer = new CullingFrameState();
            try
            {
                buffer.Groups = new NativeArray<CullingGroupSnapshot>(groups, allocator);
                buffer.Queued = new NativeArray<byte>(groups, allocator);
                buffer.Applied = new NativeArray<byte>(groups, allocator);
                buffer.StartApplied = new NativeArray<byte>(groups, allocator);
                buffer.AppliedValid = new NativeArray<byte>(groups, allocator);
                buffer.StartAppliedValid = new NativeArray<byte>(groups, allocator);
                buffer.Views = new NativeArray<CullingViewState>(views, allocator);
                buffer.Passes = new NativeArray<CullingViewPass>(passes, allocator);
                buffer.PassStates = new NativeArray<byte>(states, allocator);
                buffer.PassVolumeApplications = new NativeArray<byte>(applications, allocator);
                buffer.PassStatistics = new NativeArray<CullingViewState>(passes, allocator);
                buffer.Control = new NativeArray<CullingFrameControl>(1, allocator);
                buffer.Control[0] = new CullingFrameControl { LastClearFrame = -1 };
                buffer.Result = new NativeArray<CullingEvaluationResult>(1, allocator);
                return buffer;
            }
            catch { buffer.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (Groups.IsCreated) Groups.Dispose();
            if (Queued.IsCreated) Queued.Dispose();
            if (Applied.IsCreated) Applied.Dispose();
            if (StartApplied.IsCreated) StartApplied.Dispose();
            if (AppliedValid.IsCreated) AppliedValid.Dispose();
            if (StartAppliedValid.IsCreated) StartAppliedValid.Dispose();
            if (Views.IsCreated) Views.Dispose();
            if (Passes.IsCreated) Passes.Dispose();
            if (PassStates.IsCreated) PassStates.Dispose();
            if (PassVolumeApplications.IsCreated) PassVolumeApplications.Dispose();
            if (PassStatistics.IsCreated) PassStatistics.Dispose();
            if (Control.IsCreated) Control.Dispose();
            if (Result.IsCreated) Result.Dispose();
        }
    }

}
