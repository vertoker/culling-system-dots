using System;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace CullingSystemDOTS.Editor
{
    public struct LegacyVisibilityCell
    {
        public byte[] Compressed;
        public int EncodedLength;
        public uint MergeBitmask;
        public bool HasMergeMetadata;
    }

    // A managed Editor boundary snapshot. This package has no dependency on the legacy assemblies.
    public sealed class LegacyBakeSource
    {
        public int FormatVersion = CullingVolumeBlob.CurrentFormatVersion;
        public uint4 VolumeId;
        public CullingBakeLayout Layout;
        public int DeclaredGroupCount = -1;
        public LegacyVisibilityCell[] Cells;
    }

    // The caller owns the returned Asset. Dispose releases only the temporary validated blob.
    public sealed class LegacyImportResult : IDisposable
    {
        public bool Success => Error == BakeDataError.None;
        public BakeDataError Error { get; }
        public string Diagnostic { get; }
        public CullingBakeAsset Asset { get; }
        public BlobAssetReference<CullingVolumeBlob> Blob { get; private set; }

        internal LegacyImportResult(BakeDataError error, string diagnostic, CullingBakeAsset asset = null,
            BlobAssetReference<CullingVolumeBlob> blob = default)
        {
            Error = error;
            Diagnostic = diagnostic;
            Asset = asset;
            Blob = blob;
        }

        public void Dispose()
        {
            if (!Blob.IsCreated) return;
            var blob = Blob;
            blob.Dispose();
            Blob = default;
        }
    }

    public static class LegacyBakeImporter
    {
        public static LegacyImportResult Import(LegacyBakeSource source, CullingGroupKey[] mapping)
        {
            if (source == null || source.Cells == null)
                return new LegacyImportResult(BakeDataError.MissingData, "Legacy source cells are missing.");
            if (source.FormatVersion != CullingVolumeBlob.CurrentFormatVersion)
                return new LegacyImportResult(BakeDataError.UnsupportedVersion, "Unknown legacy codec version.");
            if (mapping == null || mapping.Length > ushort.MaxValue)
                return new LegacyImportResult(BakeDataError.InvalidGroupMapping, "An explicit mapping of at most 65535 groups is required.");
            if (source.DeclaredGroupCount < -1)
                return new LegacyImportResult(BakeDataError.InvalidLayout, "Invalid declared legacy group count.");
            if (source.DeclaredGroupCount >= 0 && source.DeclaredGroupCount != mapping.Length)
                return new LegacyImportResult(BakeDataError.InvalidGroupMapping, "The group mapping count differs from the legacy bake.");
            long byteCount = 0;
            foreach (var cell in source.Cells)
            {
                byteCount += cell.Compressed?.LongLength ?? 0;
                if (byteCount > int.MaxValue)
                    return new LegacyImportResult(BakeDataError.InvalidCellData, "Compressed data exceeds the supported contiguous buffer size.");
            }
            var ranges = new CullingCellRange[source.Cells.Length];
            var bytes = new byte[(int)byteCount];
            int offset = 0;
            for (int i = 0; i < source.Cells.Length; i++)
            {
                var cell = source.Cells[i];
                int length = cell.Compressed?.Length ?? 0;
                ranges[i] = new CullingCellRange { ByteOffset = offset, ByteLength = length,
                    EncodedLength = cell.EncodedLength, MergeBitmask = cell.MergeBitmask, HasMergeMetadata = cell.HasMergeMetadata };
                if (length > 0) Array.Copy(cell.Compressed, 0, bytes, offset, length);
                offset += length;
            }
            var asset = UnityEngine.ScriptableObject.CreateInstance<CullingBakeAsset>();
            asset.name = "Imported Visibility Bake";
            try
            {
                asset.Initialize(source.FormatVersion, source.VolumeId, source.Layout, mapping, ranges, bytes);
                if (!asset.TryCreateBlob(out var blob, out var error, out var diagnostic, Allocator.Persistent))
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                    return new LegacyImportResult(error, diagnostic);
                }
                return new LegacyImportResult(BakeDataError.None, string.Empty, asset, blob);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
        }
    }
}
