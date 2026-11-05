using System;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace PerfectCullingDOTS.Tests
{
    public class FrameEvaluatorTests
    {
        [Test]
        public void ExplicitDataInvalidationOverridesSameCellHashAndPreservesOldBuffer()
        {
            using var firstBake = Bake(0);
            using var replacement = Bake(1);
            using var first = new Snapshot(firstBake);
            var previous = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var initial = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            var output = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            try
            {
                CullingFrameEvaluator.Schedule(in first.Input, in previous, ref initial).Complete();
                Assert.That(initial.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.Success));
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, initial.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 1 }, initial.StartApplied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 0, 0 }, initial.StartAppliedValid.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 1 }, initial.AppliedValid.ToArray());
                using var next = new Snapshot(replacement);
                var view = next.Input.Views[0]; view.InvalidationVersion++; next.Input.Views[0] = view;
                next.Input.FrameId = 2;
                next.Input.SnapshotVersion = 20;
                CullingFrameEvaluator.Schedule(in next.Input, in initial, ref output).Complete();
                Assert.That(output.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.Success));
                CollectionAssert.AreEqual(new byte[] { 0, 1 }, output.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, output.StartApplied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 1 }, output.StartAppliedValid.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, initial.Applied.ToArray(), "Read-only previous buffer changed.");
                Assert.That(output.Control[0].SnapshotVersion, Is.EqualTo(20));
            }
            finally { output.Dispose(); initial.Dispose(); previous.Dispose(); }
        }

        [Test]
        public void CorruptionAfterAnEarlierPassRollsBackAllStateAndOperations()
        {
            using var valid = Bake(0);
            using var corrupt = Bake(1, corrupt: true);
            using var first = new Snapshot(valid);
            var empty = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var previous = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            var output = CullingFrameState.Create(2, 2, 1, 2, Allocator.Persistent);
            try
            {
                CullingFrameEvaluator.Schedule(in first.Input, in empty, ref previous).Complete();
                using var next = new Snapshot(corrupt, views: 2, passes: 2);
                // First pass changes visibility, then the second reaches a malformed cell.
                var left = next.Input.Views[0]; left.Position = new float3(-1, 0, 0); next.Input.Views[0] = left;
                var right = next.Input.Views[1]; right.Position = new float3(1, 0, 0); next.Input.Views[1] = right;
                next.Input.Passes[1] = new CullingViewPass { ViewIndex = 1, RenderViewId = 2 };
                next.Input.FrameId = 2;
                CullingFrameEvaluator.Schedule(in next.Input, in previous, ref output).Complete();
                var result = output.Result[0];
                Assert.That(result.Status, Is.EqualTo(CullingEvaluationStatus.InvalidCellData));
                Assert.That(result.DecodeStatus, Is.EqualTo(DecodeStatus.TruncatedData));
                Assert.That(result.PassIndex, Is.EqualTo(1));
                Assert.That(result.VolumeIndex, Is.Zero);
                Assert.That(result.CellIndex, Is.EqualTo(1));
                CollectionAssert.AreEqual(previous.Queued.ToArray(), output.Queued.ToArray());
                CollectionAssert.AreEqual(previous.Applied.ToArray(), output.Applied.ToArray());
                Assert.That(output.Views[0].Hash, Is.EqualTo(previous.Views[0].Hash));
                Assert.That(output.Control[0].LastClearFrame, Is.EqualTo(previous.Control[0].LastClearFrame));
                CollectionAssert.AreEqual(new byte[] { 0, 0 }, output.PassVolumeApplications.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, previous.Applied.ToArray());
                var successor = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
                try
                {
                    CullingFrameEvaluator.Schedule(in first.Input, in output, ref successor).Complete();
                    Assert.That(successor.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.InvalidPreviousState),
                        "A dependent frame must not silently publish after its prerequisite failed.");
                }
                finally { successor.Dispose(); }
            }
            finally { output.Dispose(); previous.Dispose(); empty.Dispose(); }
        }

        [Test]
        public void BindingGenerationReinitializesOnlyReplacedGroupsWithoutRendering()
        {
            using var bake = Bake(0);
            using var first = new Snapshot(bake);
            var empty = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var previous = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            var output = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            try
            {
                CullingFrameEvaluator.Schedule(in first.Input, in empty, ref previous).Complete();
                using var next = new Snapshot(bake);
                var replacement = next.Input.Groups[1];
                replacement.StateGeneration++; replacement.InitialApplied = 1; replacement.InitialQueued = 1;
                next.Input.Groups[1] = replacement;
                next.Input.Passes[0] = new CullingViewPass { ViewIndex = -1, RenderViewId = 999 };
                CullingFrameEvaluator.Schedule(in next.Input, in previous, ref output).Complete();
                Assert.That(output.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.Success));
                CollectionAssert.AreEqual(new byte[] { 1, 1 }, output.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, previous.Applied.ToArray());
                Assert.That(output.PassVolumeApplications[0], Is.Zero);
            }
            finally { output.Dispose(); previous.Dispose(); empty.Dispose(); }
        }

        [Test]
        public void ReusingPreviousAsOutputIsRejectedBeforeScheduling()
        {
            using var bake = Bake(0);
            using var input = new Snapshot(bake);
            var state = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            var alias = state;
            try
            {
                Assert.Throws<ArgumentException>(() => CullingFrameEvaluator.Schedule(in input.Input, in state, ref alias));
                Assert.That(state.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.NotEvaluated));
            }
            finally { state.Dispose(); }
        }

        [Test]
        public void GroupKeysRemapStateAcrossVolumeReorderAndRemoval()
        {
            using var a = Bake(0);
            using var b = Bake(1, volumeId: 2);
            using var first = new Snapshot(a, secondBake: b);
            using var reordered = new Snapshot(b, secondBake: a);
            using var removed = new Snapshot(a);
            var empty = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var previous = CullingFrameState.Create(4, 1, 2, 1, Allocator.Persistent);
            var next = CullingFrameState.Create(4, 1, 2, 1, Allocator.Persistent);
            var final = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            try
            {
                CullingFrameEvaluator.Schedule(in first.Input, in empty, ref previous).Complete();
                CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 1 }, previous.Applied.ToArray());
                reordered.Input.Passes[0] = new CullingViewPass { ViewIndex = -1, RenderViewId = 999 };
                CullingFrameEvaluator.Schedule(in reordered.Input, in previous, ref next).Complete();
                CollectionAssert.AreEqual(new byte[] { 0, 1, 1, 0 }, next.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 0, 1, 1, 0 }, next.Queued.ToArray());
                removed.Input.Passes[0] = new CullingViewPass { ViewIndex = -1, RenderViewId = 999 };
                CullingFrameEvaluator.Schedule(in removed.Input, in next, ref final).Complete();
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, final.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 1 }, previous.Applied.ToArray());
            }
            finally { final.Dispose(); next.Dispose(); previous.Dispose(); empty.Dispose(); }
        }

        [Test]
        public void DependentEvaluationCanBeScheduledBeforeThePreviousJobCompletes()
        {
            using var a = Bake(0);
            using var b = Bake(1);
            using var first = new Snapshot(a);
            using var second = new Snapshot(b);
            second.Input.FrameId = 2;
            var view = second.Input.Views[0]; view.InvalidationVersion++; second.Input.Views[0] = view;
            var empty = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var previous = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            var next = CullingFrameState.Create(2, 1, 1, 1, Allocator.Persistent);
            Unity.Jobs.JobHandle handle = default;
            try
            {
                handle = CullingFrameEvaluator.Schedule(in first.Input, in empty, ref previous);
                handle = CullingFrameEvaluator.Schedule(in second.Input, in previous, ref next, handle);
                handle.Complete();
                Assert.That(next.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.Success));
                CollectionAssert.AreEqual(new byte[] { 1, 0 }, previous.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 0, 1 }, next.Applied.ToArray());
            }
            finally { handle.Complete(); next.Dispose(); previous.Dispose(); empty.Dispose(); }
        }

        [Test]
        public void InstancesSharingOneBakeKeepIndependentStateAndKeys()
        {
            using var bake = Bake(0);
            using var first = new Snapshot(bake, secondBake: bake);
            using var second = new Snapshot(bake, secondBake: bake);
            foreach (var snapshot in new[] { first, second })
            {
                for (int v = 0; v < 2; v++)
                {
                    var volume = snapshot.Input.Volumes[v];
                    volume.InstanceId = new uint4((uint)(10 + v), 0, 0, 0);
                    volume.Layers = 1u << v; snapshot.Input.Volumes[v] = volume;
                    for (int g = 0; g < 2; g++)
                    {
                        var group = snapshot.Input.Groups[v * 2 + g];
                        group.Key = new CullingGroupKey(volume.InstanceId, (ushort)g);
                        snapshot.Input.Groups[v * 2 + g] = group;
                    }
                }
            }
            var view = first.Input.Views[0]; view.Layers = 1; first.Input.Views[0] = view;
            view = second.Input.Views[0]; view.Layers = 2; view.InvalidationVersion++; second.Input.Views[0] = view;
            second.Input.FrameId = 2;
            var empty = CullingFrameState.Create(0, 0, 0, 0, Allocator.Persistent);
            var previous = CullingFrameState.Create(4, 1, 2, 1, Allocator.Persistent);
            var next = CullingFrameState.Create(4, 1, 2, 1, Allocator.Persistent);
            try
            {
                CullingFrameEvaluator.Schedule(in first.Input, in empty, ref previous).Complete();
                Assert.That(previous.Result[0].Status, Is.EqualTo(CullingEvaluationStatus.Success));
                CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 1 }, previous.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0 }, previous.Queued.ToArray());
                CullingFrameEvaluator.Schedule(in second.Input, in previous, ref next).Complete();
                CollectionAssert.AreEqual(new byte[] { 1, 0, 1, 0 }, next.Applied.ToArray());
                CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 0 }, next.Queued.ToArray());
                Assert.That(bake.Value.Groups[0].VolumeId, Is.EqualTo(new uint4(1, 0, 0, 0)), "Shared immutable bake identity changed.");
            }
            finally { next.Dispose(); previous.Dispose(); empty.Dispose(); }
        }

        private static BlobAssetReference<CullingVolumeBlob> Bake(int selectedGroup, bool corrupt = false, uint volumeId = 1)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
            root.FormatVersion = CullingVolumeBlob.CurrentFormatVersion;
            root.VolumeId = new uint4(volumeId, 0, 0, 0);
            root.GroupCount = 2;
            root.BakedGrid = new CullingBakeLayout { CellCount = new float3(corrupt ? 2 : 1, 1, 1), CellSize = new float3(2),
                BakeOrientation = quaternion.identity, InverseBakeOrientation = quaternion.identity };
            var groups = builder.Allocate(ref root.Groups, 2);
            for (int i = 0; i < 2; i++) groups[i] = new CullingGroupKey(root.VolumeId, (ushort)i);
            var cells = builder.Allocate(ref root.Cells, corrupt ? 2 : 1);
            cells[0] = new CullingCellRange { ByteOffset = 0, ByteLength = 1, EncodedLength = 1 };
            if (corrupt) cells[1] = new CullingCellRange { ByteOffset = 1, ByteLength = 0, EncodedLength = 1 };
            var bytes = builder.Allocate(ref root.CompressedBytes, 1);
            bytes[0] = (byte)(selectedGroup << 2); // LSB header 00, one data bit.
            return builder.CreateBlobAssetReference<CullingVolumeBlob>(Allocator.Persistent);
        }

        private sealed class Snapshot : IDisposable
        {
            public CullingFrameInput Input;
            public Snapshot(BlobAssetReference<CullingVolumeBlob> bake, int views = 1, int passes = 1,
                BlobAssetReference<CullingVolumeBlob> secondBake = default)
            {
                var bakes = secondBake.IsCreated ? new[] { bake, secondBake } : new[] { bake };
                Input = new CullingFrameInput {
                    Volumes = new NativeArray<CullingVolumeSnapshot>(bakes.Length, Allocator.TempJob),
                    Views = new NativeArray<CullingViewSnapshot>(views, Allocator.TempJob),
                    Groups = new NativeArray<CullingGroupSnapshot>(2 * bakes.Length, Allocator.TempJob),
                    Portals = new NativeArray<CullingPortalSnapshot>(0, Allocator.TempJob),
                    Passes = new NativeArray<CullingViewPass>(passes, Allocator.TempJob), FrameId = 1
                };
                for (int v = 0; v < bakes.Length; v++)
                {
                    Input.Volumes[v] = new CullingVolumeSnapshot { Bake = bakes[v], GroupOffset = v * 2, Layers = uint.MaxValue,
                        TotalVertexCount = 30, Grid = new CullingGridSnapshot { Size = bakes[v].Value.BakedGrid.CellCount * 2,
                            CellCount = bakes[v].Value.BakedGrid.CellCount, CellSize = new float3(2), BakeOrientation = quaternion.identity,
                            InverseBakeOrientation = quaternion.identity, InverseCurrentOrientation = quaternion.identity } };
                    for (int g = 0; g < 2; g++) Input.Groups[v * 2 + g] = new CullingGroupSnapshot { Key = bakes[v].Value.Groups[g],
                        StateGeneration = 1, InitialApplied = 1, VertexCount = (g + 1) * 10 };
                }
                for (int v = 0; v < views; v++) Input.Views[v] = new CullingViewSnapshot { ViewId = (ulong)v + 1, Layers = uint.MaxValue };
                for (int p = 0; p < passes; p++) Input.Passes[p] = new CullingViewPass { RenderViewId = 1, ViewIndex = 0 };
            }
            public void Dispose()
            {
                Input.Volumes.Dispose(); Input.Views.Dispose(); Input.Groups.Dispose(); Input.Portals.Dispose(); Input.Passes.Dispose();
            }
        }
    }
}
