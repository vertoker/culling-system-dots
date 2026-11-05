using NUnit.Framework;
using PerfectCullingDOTS.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

namespace PerfectCullingDOTS.Tests
{
    public class LegacyFormatTests
    {
        [TestCase(new byte[] { 32 }, 2, new ushort[] { 0, 1 })]
        [TestCase(new byte[] { 63, 0 }, 2, new ushort[] { 15 })]
        [TestCase(new byte[] { 63, 1 }, 2, new ushort[] { 16 })]
        [TestCase(new byte[] { }, 0, new ushort[] { })]
        public void DecodesLegacyLeastSignificantBitAndContinuationEncoding(byte[] bytes, int encodedLength, ushort[] expected)
        {
            using var blob = Build(bytes, 0, bytes.Length, encodedLength);
            using var destination = new NativeList<ushort>(Allocator.Temp);
            var output = destination;
            var result = LegacyCellDecoder.DecodeCell(ref blob.Value, 0, ref output);
            Assert.That(result.Status, Is.EqualTo(DecodeStatus.Success));
            Assert.That(result.DecodedCount, Is.EqualTo(expected.Length));
            CollectionAssert.AreEqual(expected, destination.AsArray().ToArray());
        }

        [TestCase(new byte[] { }, 0, 0, 1, DecodeStatus.TruncatedData)]
        [TestCase(new byte[] { 255 }, 0, 1, 2, DecodeStatus.TruncatedData)]
        [TestCase(new byte[] { 63 }, 0, 1, 1, DecodeStatus.UnterminatedDelta)]
        [TestCase(new byte[] { 0 }, -1, 1, 1, DecodeStatus.InvalidRange)]
        [TestCase(new byte[] { 0 }, 0, 2, 1, DecodeStatus.InvalidRange)]
        [TestCase(new byte[] { 0 }, int.MaxValue, 1, 1, DecodeStatus.InvalidRange)]
        [TestCase(new byte[] { 0 }, 0, 1, 2, DecodeStatus.NonIncreasingGroupIndex)]
        public void CorruptDataPreservesTheCallersPreviousResult(byte[] bytes, int offset, int length, int tokens, DecodeStatus expected)
        {
            using var blob = Build(bytes, offset, length, tokens);
            using var destination = new NativeList<ushort>(Allocator.Temp);
            destination.Add(123);
            var output = destination;
            var result = LegacyCellDecoder.DecodeCell(ref blob.Value, 0, ref output);
            Assert.That(result.Status, Is.EqualTo(expected));
            Assert.That(result.DecodedCount, Is.Zero);
            CollectionAssert.AreEqual(new ushort[] { 123 }, destination.AsArray().ToArray());
        }

        [Test]
        public void DecodeAppendsToTheDestinationLikeLegacySampling()
        {
            using var blob = Build(new byte[] { 32 }, 0, 1, 2);
            using var destination = new NativeList<ushort>(Allocator.Temp);
            destination.Add(9);
            var output = destination;
            Assert.That(LegacyCellDecoder.DecodeCell(ref blob.Value, 0, ref output).Status, Is.EqualTo(DecodeStatus.Success));
            CollectionAssert.AreEqual(new ushort[] { 9, 0, 1 }, destination.AsArray().ToArray());
        }

        [Test]
        public void InvalidCellDoesNotReadTheBlobArray()
        {
            using var blob = Build(new byte[] { 32 }, 0, 1, 2);
            using var destination = new NativeList<ushort>(Allocator.Temp);
            var output = destination;
            Assert.That(LegacyCellDecoder.DecodeCell(ref blob.Value, -1, ref output).Status, Is.EqualTo(DecodeStatus.InvalidCell));
            Assert.That(LegacyCellDecoder.DecodeCell(ref blob.Value, 1, ref output).Status, Is.EqualTo(DecodeStatus.InvalidCell));
        }

        [Test]
        public void GroupIndexOutsideTheBakeMappingIsRejectedAtomically()
        {
            using var blob = Build(new byte[] { 32 }, 0, 1, 2, 1);
            using var destination = new NativeList<ushort>(Allocator.Temp);
            var output = destination;
            Assert.That(LegacyCellDecoder.DecodeCell(ref blob.Value, 0, ref output).Status, Is.EqualTo(DecodeStatus.InvalidGroupIndex));
            Assert.That(destination.Length, Is.Zero);
        }

        [Test]
        public void DecoderRunsInsideABurstCompiledJob()
        {
            using var blob = Build(new byte[] { 32 }, 0, 1, 2);
            using var destination = new NativeList<ushort>(Allocator.TempJob);
            using var result = new NativeArray<DecodeResult>(1, Allocator.TempJob);
            using var burstState = new NativeArray<int>(1, Allocator.TempJob);
            new DecodeJob { Blob = blob, Destination = destination, Result = result, BurstState = burstState }.Schedule().Complete();
            Assert.That(result[0].Status, Is.EqualTo(DecodeStatus.Success));
            Assert.That(burstState[0], Is.EqualTo(1), "The scheduled decoder job must actually execute compiled Burst code.");
            CollectionAssert.AreEqual(new ushort[] { 0, 1 }, destination.AsArray().ToArray());
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct DecodeJob : IJob
        {
            [ReadOnly] public BlobAssetReference<CullingVolumeBlob> Blob;
            public NativeList<ushort> Destination;
            public NativeArray<DecodeResult> Result;
            public NativeArray<int> BurstState;
            public void Execute()
            {
                int compiled = 1;
                ManagedFallback(ref compiled);
                BurstState[0] = compiled;
                Result[0] = LegacyCellDecoder.DecodeCell(ref Blob.Value, 0, ref Destination);
            }

            [BurstDiscard]
            private static void ManagedFallback(ref int compiled) => compiled = 0;
        }

        private static BlobAssetReference<CullingVolumeBlob> Build(byte[] bytes, int offset, int length, int tokens, int groups = 65535)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
            root.FormatVersion = CullingVolumeBlob.CurrentFormatVersion;
            root.GroupCount = groups;
            var cells = builder.Allocate(ref root.Cells, 1);
            cells[0] = new CullingCellRange { ByteOffset = offset, ByteLength = length, EncodedLength = tokens };
            var data = builder.Allocate(ref root.CompressedBytes, bytes.Length);
            for (int i = 0; i < bytes.Length; i++) data[i] = bytes[i];
            return builder.CreateBlobAssetReference<CullingVolumeBlob>(Allocator.Persistent);
        }
    }
}
