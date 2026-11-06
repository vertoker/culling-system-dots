using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Tests
{
    public class DynamicOcclusionUpdateTests
    {
        private Mesh mesh;
        [SetUp] public void SetUp()
        {
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateBounds();
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(mesh); }
        private static OcclusionInstanceState State(float x, OcclusionRole role) => new OcclusionInstanceState {
            LocalToWorld = float4x4.Translate(new float3(x, 1, 1)),
            WorldBounds = new Bounds(new Vector3(x + .5f, 1.5f, 1), new Vector3(1, 1, .01f)),
            Roles = role, Opaque = true, Color = new float4(1) };
        private static void Drain(OcclusionWorld world)
        {
            int count = 0;
            while ((world.PendingUpdateCount > 0 || world.QueueOverflowed) && count++ < 1000)
                world.ProcessUpdates(new OcclusionUpdateBudget(1024, 8));
            Assert.That(count, Is.LessThan(1000), "Bounded updates must eventually settle when edits stop.");
        }

        [Test]
        public void PendingOccluderIsOmittedUntilPreparationCompletes()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            Assert.That(world.PendingUpdateCount, Is.GreaterThan(0));
            using (var before = world.AcquireSnapshot()) Assert.That(before.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
            Drain(world);
            using var after = world.AcquireSnapshot();
            Assert.That(after.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.Occluder));
        }

        [Test]
        public void RemovingLastMemberDeletesTheEmptyBlockImmediately()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            var instance = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            Drain(world); ulong epoch = world.OccluderEpoch;
            Assert.That(world.ActiveBlockCount, Is.EqualTo(1));
            world.RemoveInstance(instance);
            Assert.That(world.ActiveBlockCount, Is.Zero); Assert.That(world.OccluderEpoch, Is.GreaterThan(epoch));
            using var snapshot = world.AcquireSnapshot(); Assert.That(snapshot.Instances.Length, Is.Zero);
        }

        [Test]
        public void MovingOccludeeDoesNotDirtyOccluderBlockGeometry()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            var moving = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(2, OcclusionRole.Occludee) });
            Drain(world); var original = world.GetBlockInfo(default); ulong epoch = world.OccluderEpoch;
            world.UpdateInstance(moving, State(3, OcclusionRole.Occludee));
            var current = world.GetBlockInfo(default);
            Assert.That(current.Revision, Is.EqualTo(original.Revision));
            Assert.That(current.PreparedRevision, Is.EqualTo(current.Revision)); Assert.That(world.OccluderEpoch, Is.EqualTo(epoch));
            Assert.That(world.PendingUpdateCount, Is.GreaterThan(0), "Only candidate data still needs preparation.");
        }

        [Test]
        public void MovingOccluderInvalidatesBeforeQueuedPreparation()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            var moving = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            Drain(world); ulong epoch = world.OccluderEpoch;
            world.UpdateInstance(moving, State(17, OcclusionRole.Occluder));
            Assert.That(world.OccluderEpoch, Is.GreaterThan(epoch));
            Assert.That(world.GetBlockInfo(new int3(1, 0, 0)).PreparedRevision,
                Is.LessThan(world.GetBlockInfo(new int3(1, 0, 0)).Revision));
            using var snapshot = world.AcquireSnapshot();
            Assert.That(snapshot.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
        }

        [Test]
        public void FloodCoalescesAndEventuallyPublishesNewestRevision()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            var instance = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            for (int i = 0; i < 500; i++) world.UpdateInstance(instance, State(1 + (i % 2) * .1f, OcclusionRole.Occluder));
            Assert.That(world.PendingUpdateCount, Is.LessThanOrEqualTo(3));
            Drain(world); using var snapshot = world.AcquireSnapshot();
            Assert.That(snapshot.Instances[0].WorldBounds.center.x, Is.EqualTo(1.6f));
            Assert.That(snapshot.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.Occluder));
        }

        [Test]
        public void QueueOverflowRescansUntilEveryLiveOccluderIsPrepared()
        {
            using var world = new OcclusionWorld(new OcclusionSettings { MaxQueuedUpdates = 2 });
            var geometry = world.RegisterGeometry(mesh);
            for (int i = 0; i < 10; i++)
                world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1 + i * 16, OcclusionRole.Occluder) });
            Assert.That(world.QueueOverflowed, Is.True); Assert.That(world.PendingUpdateCount, Is.LessThanOrEqualTo(2));
            Drain(world);
            using var snapshot = world.AcquireSnapshot();
            for (int i = 0; i < snapshot.Instances.Length; i++)
                Assert.That(snapshot.Instances[i].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.Occluder));
            Assert.That(world.QueueOverflowed, Is.False);
        }

        [Test]
        public void RejectedMoveWithdrawsOccluderAndPreservesPreviousData()
        {
            using var world = new OcclusionWorld(new OcclusionSettings { MaxActiveBlocks = 1 }); var geometry = world.RegisterGeometry(mesh);
            var handle = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(1, OcclusionRole.Occluder) });
            Drain(world); ulong epoch = world.OccluderEpoch;
            Assert.Throws<System.InvalidOperationException>(() => world.UpdateInstance(handle, State(15.5f, OcclusionRole.Occluder)));
            Assert.That(world.OccluderEpoch, Is.GreaterThan(epoch), "Failed capture cannot leave an old wall's visibility proof valid.");
            using var snapshot = world.AcquireSnapshot();
            Assert.That(snapshot.Instances[0].WorldBounds.center.x, Is.EqualTo(1.5f));
            Assert.That(snapshot.Instances[0].Prepared, Is.Zero);
            Assert.That(snapshot.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
            Assert.That(world.ActiveBlockCount, Is.EqualTo(1));
        }

        [Test]
        public void PreparationCannotExceedConfiguredPerFrameBytes()
        {
            using var world = new OcclusionWorld(new OcclusionSettings { MaxUploadBytesPerFrame = 16 });
            world.RegisterGeometry(mesh);
            var batch = world.ProcessUpdates(new OcclusionUpdateBudget(100000));
            Assert.That(batch.PreparedBytes, Is.EqualTo(16)); Assert.That(batch.Slices[0].Complete, Is.False);
        }
    }
}
