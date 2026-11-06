using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Tests
{
    public class DynamicOcclusionBlocksTests
    {
        [Test]
        public void NegativeCoordinatesUseFloorDivision()
        {
            var blocks = new OcclusionBlocks(16); using var output = new NativeList<int3>(Allocator.Temp);
            blocks.GetOverlappingBlocks(new Bounds(new Vector3(-.1f, -16.1f, -32.1f), Vector3.zero), output);
            Assert.That(output.Length, Is.EqualTo(1)); Assert.That(output[0], Is.EqualTo(new int3(-1, -2, -3)));
        }

        [Test]
        public void CrossBlockBoundsIncludeExactUpperContact()
        {
            var blocks = new OcclusionBlocks(16); using var output = new NativeList<int3>(Allocator.Temp);
            blocks.GetOverlappingBlocks(new Bounds(Vector3.zero, new Vector3(32, 0, 0)), output);
            Assert.That(output.Length, Is.EqualTo(3));
            Assert.That(output[0], Is.EqualTo(new int3(-1, 0, 0))); Assert.That(output[2], Is.EqualTo(new int3(1, 0, 0)));
        }

        [Test]
        public void OffGridRotatedGeometryDoesNotNeedGridAlignedPieces()
        {
            var transform = float4x4.TRS(new float3(-3.5f, 0, 1.25f), quaternion.RotateY(math.radians(45)), new float3(2, 1, 3));
            var bounds = OcclusionWorld.TransformBounds(new Bounds(Vector3.zero, Vector3.one * 2), transform);
            var blocks = new OcclusionBlocks(16); using var output = new NativeList<int3>(Allocator.Temp);
            blocks.GetOverlappingBlocks(bounds, output);
            Assert.That(output.Length, Is.EqualTo(8));
        }

        [Test]
        public void HugeOrInvalidRangesFailBeforeEnumeration()
        {
            var blocks = new OcclusionBlocks(1, 64); using var output = new NativeList<int3>(Allocator.Temp);
            Assert.Throws<ArgumentException>(() => blocks.GetOverlappingBlocks(new Bounds(Vector3.zero, Vector3.one * 1000000), output));
            Assert.Throws<ArgumentException>(() => blocks.GetOverlappingBlocks(new Bounds(new Vector3(float.MaxValue, 0, 0), Vector3.zero), output));
            Assert.Throws<ArgumentException>(() => blocks.GetOverlappingBlocks(new Bounds(new Vector3(float.NaN, 0, 0), Vector3.zero), output));
            Assert.That(output.Length, Is.Zero);
        }

        [Test]
        public void NewRevisionSupersedesPartialPreparation()
        {
            var queue = new OcclusionUpdateQueue(4); var key = OcclusionUpdateKey.ForBlock(new int3(1, 2, 3));
            queue.Enqueue(key, 1, 96);
            var first = queue.Process(new OcclusionUpdateBudget(32));
            Assert.That(first.PreparedBytes, Is.EqualTo(32)); Assert.That(first.Slices[0].Complete, Is.False);
            queue.Enqueue(key, 2, 96);
            var second = queue.Process(new OcclusionUpdateBudget(64));
            Assert.That(second.Slices[0].Revision, Is.EqualTo(2)); Assert.That(second.Slices[0].ByteOffset, Is.Zero);
            Assert.That(first.Slices[0].Revision, Is.EqualTo(1), "Older batches remain immutable.");
            Assert.That(queue.Enqueue(key, 1, 96), Is.False, "An older revision cannot replace current work.");
            var last = queue.Process(new OcclusionUpdateBudget(64));
            Assert.That(last.PreparedBytes, Is.EqualTo(32)); Assert.That(last.Slices[0].Complete, Is.True);
            Assert.That(queue.PendingCount, Is.Zero);
        }

        [Test]
        public void EditFloodBoundsQueueSizeAndReportsOverflow()
        {
            var queue = new OcclusionUpdateQueue(4); int accepted = 0;
            for (int i = 0; i < 1000; i++) if (queue.Enqueue(OcclusionUpdateKey.ForBlock(new int3(i, 0, 0)), (ulong)i + 1, 64)) accepted++;
            Assert.That(accepted, Is.EqualTo(4)); Assert.That(queue.PendingCount, Is.EqualTo(4)); Assert.That(queue.Overflowed, Is.True);
            var batch = queue.Process(new OcclusionUpdateBudget(128, 4));
            Assert.That(batch.PreparedBytes, Is.EqualTo(128)); Assert.That(batch.RemainingItems, Is.EqualTo(2));
        }

        [Test]
        public void PartialLargeItemDoesNotStarveSmallItem()
        {
            var queue = new OcclusionUpdateQueue(4);
            queue.Enqueue(OcclusionUpdateKey.ForBlock(new int3(1)), 1, 1000);
            var small = OcclusionUpdateKey.ForBlock(new int3(2)); queue.Enqueue(small, 1, 16);
            queue.Process(new OcclusionUpdateBudget(16, 1));
            var next = queue.Process(new OcclusionUpdateBudget(16, 1));
            Assert.That(next.Slices[0].Key, Is.EqualTo(small)); Assert.That(next.Slices[0].Complete, Is.True);
        }

        [Test]
        public void GeometryAndInstanceKeysRemainIndependent()
        {
            var queue = new OcclusionUpdateQueue(4);
            queue.Enqueue(OcclusionUpdateKey.ForGeometry(new GeometryHandle(1, 0, 1)), 1, 16);
            queue.Enqueue(OcclusionUpdateKey.ForInstance(new InstanceHandle(1, 0, 1)), 1, 16);
            Assert.That(queue.PendingCount, Is.EqualTo(2));
        }

        [Test]
        public void InvalidBudgetDoesNotConsumeWork()
        {
            var queue = new OcclusionUpdateQueue(1); queue.Enqueue(OcclusionUpdateKey.ForBlock(default), 1, 32);
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.Process(new OcclusionUpdateBudget(0)));
            Assert.That(queue.PendingCount, Is.EqualTo(1));
        }
    }
}
