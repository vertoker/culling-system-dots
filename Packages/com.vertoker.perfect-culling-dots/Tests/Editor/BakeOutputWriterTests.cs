using System;
using System.IO;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Runtime;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Editor.Baking.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
namespace PerfectCullingDOTS.Tests
{
    public class BakeOutputWriterTests
    {
        private static readonly uint4 Id = new uint4(8,7,6,5);
        private static CullingBakeLayout Layout => new CullingBakeLayout { CellCount = new float3(75,1,1), CellSize = new float3(1), BakeOrientation = quaternion.identity };
        private static CullingGroupKey[] Mapping(int count = 6)
        {
            var mapping = new CullingGroupKey[count]; for (int i = 0; i < count; i++) mapping[i] = new CullingGroupKey(Id,(ushort)i); return mapping;
        }
        [TestCase(BakeMergeMetadata.ComputeNeighbors)] [TestCase(BakeMergeMetadata.NativeDefault)]
        public void OutputKeepsExcludedFullGridIndicesAndExplicitMetadataProfile(BakeMergeMetadata profile)
        {
            var asset = BakeOutputWriter.CreateAsset(new[] { new BakeCellResult { FullGridIndex = 74, Groups = new ushort[] { 0,1,17,39 }, NeighborMask = 0x123456 } },Id,Layout,Mapping(40),profile);
            try
            {
                Assert.That(asset.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
                using (blob)
                {
                    Assert.That(blob.Value.Cells.Length, Is.EqualTo(75));
                    using var decoded = new NativeList<ushort>(Allocator.Temp); var output = decoded;
                    LegacyCellDecoder.DecodeCell(ref blob.Value,74,ref output);
                    CollectionAssert.AreEqual(new ushort[] { 0,1,17,39 },decoded.AsArray().ToArray());
                    Assert.That(blob.Value.Cells[74].MergeBitmask, Is.EqualTo(profile == BakeMergeMetadata.NativeDefault ? uint.MaxValue : 0x123456u));
                    output.Clear(); LegacyCellDecoder.DecodeCell(ref blob.Value,73,ref output); Assert.That(output.Length, Is.Zero);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }
        [Test]
        public void InvalidDuplicateAndOutOfRangeResultsAreRejectedBeforePublication()
        {
            var cell = new BakeCellResult { FullGridIndex = 2,Groups = new ushort[] { 1,1 } };
            Assert.Throws<ArgumentException>(() => BakeOutputWriter.CreateAsset(new[] { cell },Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors));
            cell.Groups = new ushort[] { 1 }; Assert.Throws<ArgumentException>(() => BakeOutputWriter.CreateAsset(new[] { cell,cell },Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors));
            cell.FullGridIndex = 75; Assert.Throws<ArgumentException>(() => BakeOutputWriter.CreateAsset(new[] { cell },Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors));
        }
        [Test]
        public void BakeSaveIsAtomicAndKeepsExistingGuid()
        {
            string path = "Assets/PvsAtomic_"+Guid.NewGuid().ToString("N")+".asset";
            using var fixture = new BakeJobsFixture(); using var initial = fixture.Capture(true,false); using var next = fixture.Capture(false,false);
            using var first = new JobsBakeBackend().Begin(initial); using var second = new JobsBakeBackend().Begin(next);
            try
            {
                first.Complete(); BakeOutputWriter.SaveCompleted(path,first,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors);
                Assert.That(Directory.GetFiles("Assets","PvsBakeStage_*.asset.meta"), Is.Empty,"Orphan staging metadata");
                Assert.That(Directory.GetDirectories("Assets","PvsBakeStage_*"), Is.Empty,"Orphan staging directory");
                string guid = AssetDatabase.AssetPathToGUID(path); byte[] previous = File.ReadAllBytes(path);
                second.Complete(); var saved = BakeOutputWriter.SaveCompleted(path,second,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors);
                Assert.That(saved.name, Is.EqualTo(Path.GetFileNameWithoutExtension(path)));
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid)); CollectionAssert.AreNotEqual(previous,File.ReadAllBytes(path));
                Assert.That(saved.TryCreateBlob(out var blob,out _,out var diagnostic), Is.True,diagnostic);
                using (blob)
                {
                    using var decoded = new NativeList<ushort>(Allocator.Temp); var output = decoded;
                    LegacyCellDecoder.DecodeCell(ref blob.Value,2,ref output); CollectionAssert.AreEqual(second.Complete()[0].Groups,output.AsArray().ToArray());
                }
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
        [Test]
        public void CancellationKeepsPreviousBakeBytesAndGuid()
        {
            string path = "Assets/PvsCancel_"+Guid.NewGuid().ToString("N")+".asset";
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(false,false);
            using var initial = new JobsBakeBackend().Begin(snapshot); using var cancelled = new JobsBakeBackend().Begin(snapshot);
            try
            {
                initial.Complete(); BakeOutputWriter.SaveCompleted(path,initial,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors);
                byte[] previous = File.ReadAllBytes(path); string guid = AssetDatabase.AssetPathToGUID(path);
                cancelled.Tick(); cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() => BakeOutputWriter.SaveCompleted(path,cancelled,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors));
                CollectionAssert.AreEqual(previous,File.ReadAllBytes(path)); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
        [Test]
        public void LockedDestinationFailsWithoutTouchingPreviousBake()
        {
            string path = "Assets/PvsLocked_"+Guid.NewGuid().ToString("N")+".asset";
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(false,false); using var session = new JobsBakeBackend().Begin(snapshot);
            try
            {
                session.Complete(); BakeOutputWriter.SaveCompleted(path,session,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors);
                byte[] previous = File.ReadAllBytes(path); string guid = AssetDatabase.AssetPathToGUID(path);
                using (var locked = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                    Assert.Throws<IOException>(() => BakeOutputWriter.SaveCompleted(path,session,Id,Layout,Mapping(),BakeMergeMetadata.ComputeNeighbors));
                CollectionAssert.AreEqual(previous,File.ReadAllBytes(path)); Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
