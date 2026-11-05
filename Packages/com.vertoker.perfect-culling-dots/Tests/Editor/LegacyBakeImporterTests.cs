using System;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Editor;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEditor;

namespace PerfectCullingDOTS.Tests
{
    public class LegacyBakeImporterTests
    {
        private static readonly uint4 VolumeId = new uint4(1, 2, 3, 4);

        [Test]
        public void ImportKeepsOriginalGroupIndicesGridAndMergeMetadata()
        {
            var source = Source();
            var mapping = Mapping(2);
            using var result = LegacyBakeImporter.Import(source, mapping);
            try
            {
                Assert.That(result.Success, Is.True, result.Diagnostic);
                ref var blob = ref result.Blob.Value;
                Assert.That(blob.GroupCount, Is.EqualTo(2));
                Assert.That(blob.Groups[0], Is.EqualTo(mapping[0]));
                Assert.That(blob.Groups[1], Is.EqualTo(mapping[1]));
                Assert.That(blob.BakedGrid.CellCount, Is.EqualTo(source.Layout.CellCount));
                Assert.That(blob.BakedGrid.CellSize, Is.EqualTo(source.Layout.CellSize));
                Assert.That(blob.BakedGrid.BakeOrientation, Is.EqualTo(source.Layout.BakeOrientation));
                Assert.That(blob.Cells[0].HasMergeMetadata, Is.True);
                Assert.That(blob.Cells[0].MergeBitmask, Is.EqualTo(uint.MaxValue));
                Assert.That(blob.Cells[1].HasMergeMetadata, Is.True);
                Assert.That(blob.Cells[1].MergeBitmask, Is.Zero);
                using var groups = new NativeList<ushort>(Allocator.Temp);
                var output = groups;
                Assert.That(LegacyCellDecoder.DecodeCell(ref blob, 0, ref output).Status, Is.EqualTo(DecodeStatus.Success));
                CollectionAssert.AreEqual(new ushort[] { 0, 1 }, groups.AsArray().ToArray());
            }
            finally { if (result.Asset != null) UnityEngine.Object.DestroyImmediate(result.Asset); }
        }

        [Test]
        public void AssetOwnsItsSnapshotAfterTheCallerMutatesSourceArrays()
        {
            var source = Source();
            var mapping = Mapping(2);
            using var result = LegacyBakeImporter.Import(source, mapping);
            try
            {
                Assert.That(result.Success, Is.True, result.Diagnostic);
                source.Cells[0].Compressed[0] = 0;
                mapping[0] = new CullingGroupKey(new uint4(9), 9);
                Assert.That(result.Asset.TryCreateBlob(out var restored, out var error, out var diagnostic), Is.True, diagnostic);
                using (restored)
                {
                    Assert.That(error, Is.EqualTo(BakeDataError.None));
                    Assert.That(restored.Value.Groups[0], Is.EqualTo(new CullingGroupKey(VolumeId, 0)));
                    using var groups = new NativeList<ushort>(Allocator.Temp);
                    var output = groups;
                    Assert.That(LegacyCellDecoder.DecodeCell(ref restored.Value, 0, ref output).Status, Is.EqualTo(DecodeStatus.Success));
                    CollectionAssert.AreEqual(new ushort[] { 0, 1 }, groups.AsArray().ToArray());
                }
            }
            finally { if (result.Asset != null) UnityEngine.Object.DestroyImmediate(result.Asset); }
        }

        [Test]
        public void MissingLegacyGroupCountUsesTheExplicitMappingIncludingNeverVisibleGroups()
        {
            var source = Source();
            source.DeclaredGroupCount = -1;
            source.Cells[1].HasMergeMetadata = false;
            using var result = LegacyBakeImporter.Import(source, Mapping(3));
            try
            {
                Assert.That(result.Success, Is.True, result.Diagnostic);
                Assert.That(result.Blob.Value.GroupCount, Is.EqualTo(3));
                Assert.That(result.Blob.Value.Groups[2].GroupIndex, Is.EqualTo(2));
                Assert.That(result.Blob.Value.Cells[1].HasMergeMetadata, Is.False);
            }
            finally { if (result.Asset != null) UnityEngine.Object.DestroyImmediate(result.Asset); }
        }

        [Test]
        public void ImportedAssetSurvivesSavingUnloadingAndReloading()
        {
            using var result = LegacyBakeImporter.Import(Source(), Mapping(2));
            Assert.That(result.Success, Is.True, result.Diagnostic);
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/PerfectCullingDOTS_ImportRoundTrip.asset");
            try
            {
                AssetDatabase.CreateAsset(result.Asset, path);
                AssetDatabase.SaveAssetIfDirty(result.Asset);
                Resources.UnloadAsset(result.Asset);
                var loaded = AssetDatabase.LoadAssetAtPath<PerfectCullingDOTS.Runtime.CullingBakeAsset>(path);
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.TryCreateBlob(out var blob, out var error, out var diagnostic), Is.True, diagnostic);
                using (blob)
                {
                    Assert.That(error, Is.EqualTo(BakeDataError.None));
                    Assert.That(blob.Value.Groups[1], Is.EqualTo(new CullingGroupKey(VolumeId, 1)));
                    Assert.That(blob.Value.Cells[0].HasMergeMetadata, Is.True);
                    Assert.That(blob.Value.Cells[0].MergeBitmask, Is.EqualTo(uint.MaxValue));
                    using var groups = new NativeList<ushort>(Allocator.Temp);
                    var output = groups;
                    Assert.That(LegacyCellDecoder.DecodeCell(ref blob.Value, 0, ref output).Status, Is.EqualTo(DecodeStatus.Success));
                    CollectionAssert.AreEqual(new ushort[] { 0, 1 }, groups.AsArray().ToArray());
                }
            }
            finally
            {
                if (!AssetDatabase.DeleteAsset(path) && result.Asset != null)
                    UnityEngine.Object.DestroyImmediate(result.Asset);
            }
        }

        [TestCase("reordered", BakeDataError.InvalidGroupMapping)]
        [TestCase("wrongVolume", BakeDataError.InvalidGroupMapping)]
        [TestCase("missingMapping", BakeDataError.InvalidGroupMapping)]
        [TestCase("groupCountMismatch", BakeDataError.InvalidGroupMapping)]
        [TestCase("groupLimit", BakeDataError.InvalidGroupMapping)]
        [TestCase("zeroVolumeId", BakeDataError.InvalidGroupMapping)]
        [TestCase("fractionalGrid", BakeDataError.InvalidLayout)]
        [TestCase("cellCountMismatch", BakeDataError.InvalidLayout)]
        [TestCase("zeroCellSize", BakeDataError.InvalidLayout)]
        [TestCase("nanCellSize", BakeDataError.InvalidLayout)]
        [TestCase("nanOrientation", BakeDataError.InvalidLayout)]
        [TestCase("badDeclaredCount", BakeDataError.InvalidLayout)]
        [TestCase("truncated", BakeDataError.InvalidCellData)]
        [TestCase("duplicateIds", BakeDataError.InvalidCellData)]
        [TestCase("unsupportedVersion", BakeDataError.UnsupportedVersion)]
        [TestCase("missingCells", BakeDataError.MissingData)]
        public void InvalidDataIsRejectedWithAReasonAndNoPartialResult(string fault, BakeDataError expected)
        {
            var source = Source();
            var mapping = Mapping(2);
            switch (fault)
            {
                case "reordered": Array.Reverse(mapping); break;
                case "wrongVolume": mapping[1].VolumeId = new uint4(9); break;
                case "missingMapping": mapping = null; break;
                case "groupCountMismatch": source.DeclaredGroupCount = 3; break;
                case "groupLimit": source.DeclaredGroupCount = -1; mapping = new CullingGroupKey[65536]; break;
                case "zeroVolumeId": source.VolumeId = default; break;
                case "fractionalGrid": source.Layout.CellCount.x = 1.5f; break;
                case "cellCountMismatch": source.Layout.CellCount.x = 3; break;
                case "zeroCellSize": source.Layout.CellSize.x = 0; break;
                case "nanCellSize": source.Layout.CellSize.x = float.NaN; break;
                case "nanOrientation": source.Layout.BakeOrientation.value.x = float.NaN; break;
                case "badDeclaredCount": source.DeclaredGroupCount = -2; break;
                case "truncated": source.Cells[0].EncodedLength = 99; break;
                case "duplicateIds": source.Cells[0].Compressed[0] = 0; break;
                case "unsupportedVersion": source.FormatVersion = 99; break;
                case "missingCells": source.Cells = null; break;
            }
            using var result = LegacyBakeImporter.Import(source, mapping);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(expected));
            Assert.That(result.Diagnostic, Is.Not.Empty);
            Assert.That(result.Blob.IsCreated, Is.False);
            Assert.That(result.Asset, Is.Null);
        }

        [Test]
        public void FailedImportKeepsThePreviousValidBakeUsable()
        {
            using var previous = LegacyBakeImporter.Import(Source(), Mapping(2));
            try
            {
                Assert.That(previous.Success, Is.True, previous.Diagnostic);
                var invalid = Source();
                invalid.Cells[0].Compressed = Array.Empty<byte>();
                using var failed = LegacyBakeImporter.Import(invalid, Mapping(2));
                Assert.That(failed.Success, Is.False);
                Assert.That(previous.Blob.IsCreated, Is.True);
                using var output = new NativeList<ushort>(Allocator.Temp);
                var destination = output;
                Assert.That(LegacyCellDecoder.DecodeCell(ref previous.Blob.Value, 0, ref destination).Status, Is.EqualTo(DecodeStatus.Success));
                CollectionAssert.AreEqual(new ushort[] { 0, 1 }, output.AsArray().ToArray());
            }
            finally { if (previous.Asset != null) UnityEngine.Object.DestroyImmediate(previous.Asset); }
        }

        private static LegacyBakeSource Source() => new LegacyBakeSource {
            VolumeId = VolumeId, DeclaredGroupCount = 2,
            Layout = new CullingBakeLayout { CellCount = new float3(2, 1, 1), CellSize = new float3(2, 1, 2), BakeOrientation = quaternion.identity },
            Cells = new[] {
                new LegacyVisibilityCell { Compressed = new byte[] { 32 }, EncodedLength = 2, MergeBitmask = uint.MaxValue, HasMergeMetadata = true },
                new LegacyVisibilityCell { Compressed = null, EncodedLength = 0, MergeBitmask = 0, HasMergeMetadata = true }
            }
        };

        private static CullingGroupKey[] Mapping(int count)
        {
            var result = new CullingGroupKey[count];
            for (int i = 0; i < count; i++) result[i] = new CullingGroupKey(VolumeId, (ushort)i);
            return result;
        }
    }
}
