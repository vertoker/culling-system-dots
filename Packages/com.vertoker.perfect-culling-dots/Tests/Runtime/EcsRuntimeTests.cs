using System;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PerfectCullingDOTS.Tests
{
    public class EcsRuntimeTests
    {
        [TestCase(CullingExecutionMode.Sequential), TestCase(CullingExecutionMode.Jobs)]
        public void WorldsOwnIndependentSnapshotsAndRejectEachOthersTickets(CullingExecutionMode mode)
        {
            using var source = new Snapshot();
            using var a = new World("First culling world");
            using var b = new World("Second culling world");
            var first = a.GetOrCreateSystemManaged<CullingWorldSystem>();
            var second = b.GetOrCreateSystemManaged<CullingWorldSystem>();
            var ta = first.Schedule(in source.Input, mode);
            source.MoveToSecondCell();
            var tb = second.Schedule(in source.Input, mode);
            source.DisposeArrays(); // World owns copies. Only the immutable blob remains borrowed.
            Assert.That(second.CompleteAndPublish(ta), Is.EqualTo(CullingPublicationStatus.WrongTicket));
            Assert.That(second.HasPendingEvaluation, Is.True);
            Assert.That(first.CompleteAndPublish(ta), Is.EqualTo(CullingPublicationStatus.Published));
            Assert.That(second.CompleteAndPublish(tb), Is.EqualTo(CullingPublicationStatus.Published));
            State(first, 0, true, true); State(first, 1, false, false);
            State(second, 0, false, false); State(second, 1, true, true);
            Assert.That(first.AcceptedVersion, Is.EqualTo(ta.Request));
            Assert.That(first.LastEvaluationResult.RanInBurst, Is.EqualTo(mode == CullingExecutionMode.Jobs));
            Assert.That(first.TryGetViewState(1, out var stats), Is.True);
            Assert.That(stats.Visible, Is.EqualTo(1)); Assert.That(stats.Total, Is.EqualTo(2));
        }

        [TestCase(CullingExecutionMode.Sequential), TestCase(CullingExecutionMode.Jobs)]
        public void InvalidatedOrCorruptWorkKeepsTheLastAcceptedState(CullingExecutionMode mode)
        {
            using var source = new Snapshot();
            using var world = new World("Culling publication gate");
            var system = world.GetOrCreateSystemManaged<CullingWorldSystem>();
            var initial = system.Schedule(in source.Input, mode);
            Assert.That(system.CompleteAndPublish(initial), Is.EqualTo(CullingPublicationStatus.Published));
            source.MoveToSecondCell();
            var stale = system.Schedule(in source.Input, mode);
            system.Invalidate();
            Assert.That(system.CompleteAndPublish(stale), Is.EqualTo(CullingPublicationStatus.Stale));
            State(system, 0, true, true); State(system, 1, false, false);
            using var corrupt = new Snapshot(corrupt: true); corrupt.MoveToSecondCell();
            var damaged = system.Schedule(in corrupt.Input, mode);
            Assert.That(system.CompleteAndPublish(damaged), Is.EqualTo(CullingPublicationStatus.InvalidData));
            Assert.That(system.LastEvaluationResult.DecodeStatus, Is.EqualTo(DecodeStatus.TruncatedData));
            State(system, 0, true, true);
            var valid = system.Schedule(in source.Input, mode);
            Assert.That(system.CompleteAndPublish(valid), Is.EqualTo(CullingPublicationStatus.Published));
            State(system, 0, false, false); State(system, 1, true, true);
        }

        [TestCase(CullingExecutionMode.Sequential), TestCase(CullingExecutionMode.Jobs)]
        public void DestroyWorldCompletesPendingWorkAndReleasesNativeState(CullingExecutionMode mode)
        {
            using var source = new Snapshot();
            var world = new World("Disposed culling world");
            var system = world.GetOrCreateSystemManaged<CullingWorldSystem>();
            try
            {
                system.Schedule(in source.Input, mode);
                Assert.That(system.HasNativeState, Is.True);
                Assert.That(system.HasPendingEvaluation, Is.True);
                source.DisposeArrays();
            }
            finally { world.Dispose(); }
            Assert.That(system.HasNativeState, Is.False);
            Assert.That(system.HasPendingEvaluation, Is.False);
            Assert.Throws<ObjectDisposedException>(() => system.Schedule(in source.Input, mode));
        }

        [Test]
        public void CancelledWorkCannotConsumeTheNextRequest()
        {
            using var source = new Snapshot();
            using var world = new World("Culling cancellation gate");
            var system = world.GetOrCreateSystemManaged<CullingWorldSystem>();
            var cancelled = system.Schedule(in source.Input);
            system.CancelPending();
            var next = system.Schedule(in source.Input);
            Assert.That(system.CompleteAndPublish(cancelled), Is.EqualTo(CullingPublicationStatus.WrongTicket));
            Assert.That(system.CompleteAndPublish(next), Is.EqualTo(CullingPublicationStatus.Published));
            Assert.That(system.HasPendingEvaluation, Is.False);
        }

        private static void State(CullingWorldSystem system, ushort index, bool queued, bool applied)
        {
            Assert.That(system.TryGetGroupState(new CullingGroupKey(new uint4(1, 0, 0, 0), index), out bool q, out bool a), Is.True);
            Assert.That(q, Is.EqualTo(queued)); Assert.That(a, Is.EqualTo(applied));
        }

        private sealed class Snapshot : IDisposable
        {
            public CullingFrameInput Input;
            private BlobAssetReference<CullingVolumeBlob> bake;
            public Snapshot(bool corrupt = false)
            {
                using var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
                root.FormatVersion = CullingVolumeBlob.CurrentFormatVersion; root.VolumeId = new uint4(1, 0, 0, 0); root.GroupCount = 2;
                root.BakedGrid = new CullingBakeLayout { CellCount = new float3(2, 1, 1), CellSize = new float3(2), BakeOrientation = quaternion.identity };
                var keys = builder.Allocate(ref root.Groups, 2);
                for (int g = 0; g < 2; g++) keys[g] = new CullingGroupKey(root.VolumeId, (ushort)g);
                var cells = builder.Allocate(ref root.Cells, 2);
                cells[0] = new CullingCellRange { ByteLength = 1, EncodedLength = 1 };
                cells[1] = new CullingCellRange { ByteOffset = 1, ByteLength = corrupt ? 0 : 1, EncodedLength = 1 };
                var bytes = builder.Allocate(ref root.CompressedBytes, 2); bytes[0] = 0; bytes[1] = 4;
                bake = builder.CreateBlobAssetReference<CullingVolumeBlob>(Allocator.Persistent);
                Input = new CullingFrameInput {
                    Volumes = new NativeArray<CullingVolumeSnapshot>(new[] { new CullingVolumeSnapshot { Bake = bake, Layers = uint.MaxValue,
                        Grid = new CullingGridSnapshot { Size = new float3(4, 2, 2), CellCount = new float3(2, 1, 1), CellSize = new float3(2),
                            BakeOrientation = quaternion.identity, InverseBakeOrientation = quaternion.identity, InverseCurrentOrientation = quaternion.identity } } }, Allocator.Persistent),
                    Views = new NativeArray<CullingViewSnapshot>(new[] { new CullingViewSnapshot { ViewId = 1, Position = new float3(-1, 0, 0), Layers = uint.MaxValue } }, Allocator.Persistent),
                    Groups = new NativeArray<CullingGroupSnapshot>(new[] {
                        new CullingGroupSnapshot { Key = new CullingGroupKey(root.VolumeId, 0), InitialApplied = 1, StateGeneration = 1 },
                        new CullingGroupSnapshot { Key = new CullingGroupKey(root.VolumeId, 1), InitialApplied = 1, StateGeneration = 1 } }, Allocator.Persistent),
                    Portals = new NativeArray<CullingPortalSnapshot>(0, Allocator.Persistent),
                    Passes = new NativeArray<CullingViewPass>(new[] { new CullingViewPass { RenderViewId = 1, ViewIndex = 0 } }, Allocator.Persistent),
                    FrameId = 1, SnapshotVersion = 10
                };
            }
            public void MoveToSecondCell()
            {
                var view = Input.Views[0]; view.Position = new float3(1, 0, 0); Input.Views[0] = view; Input.FrameId++;
            }
            public void DisposeArrays()
            {
                if (Input.Volumes.IsCreated) Input.Volumes.Dispose(); if (Input.Views.IsCreated) Input.Views.Dispose();
                if (Input.Groups.IsCreated) Input.Groups.Dispose(); if (Input.Portals.IsCreated) Input.Portals.Dispose();
                if (Input.Passes.IsCreated) Input.Passes.Dispose();
            }
            public void Dispose() { DisposeArrays(); if (bake.IsCreated) bake.Dispose(); }
        }
    }
}
