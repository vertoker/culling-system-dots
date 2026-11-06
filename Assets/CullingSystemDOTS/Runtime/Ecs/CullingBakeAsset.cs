using System;
using CullingSystemDOTS.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Runtime
{
    public sealed class CullingBakeAsset : ScriptableObject
    {
        [SerializeField] private int formatVersion;
        [SerializeField] private uint4 volumeId;
        [SerializeField] private CullingBakeLayout layout;
        [SerializeField] private CullingGroupKey[] groups;
        [SerializeField] private CullingCellRange[] cells;
        [SerializeField] private byte[] compressed;
        [SerializeField] private string inputFingerprint;
        public int GroupCount => groups?.Length ?? 0;
        public uint4 VolumeId => volumeId;
        public string InputFingerprint => inputFingerprint;
        internal void SetInputFingerprint(string value) { inputFingerprint = value; }

        internal void Initialize(int version, uint4 stableVolumeId, CullingBakeLayout bakeLayout,
            CullingGroupKey[] mapping, CullingCellRange[] ranges, byte[] bytes)
        {
            formatVersion = version;
            volumeId = stableVolumeId;
            layout = bakeLayout;
            groups = (CullingGroupKey[])mapping.Clone();
            cells = (CullingCellRange[])ranges.Clone();
            compressed = (byte[])bytes.Clone();
        }

        // Main-thread loading boundary. The caller owns the resulting blob or transfers it to BlobAssetStore.
        public bool TryCreateBlob(out BlobAssetReference<CullingVolumeBlob> blob, out BakeDataError error,
            out string diagnostic, Allocator allocator = Allocator.Persistent)
        {
            blob = default;
            if (!ValidateMetadata(out error, out diagnostic)) return false;
            var rotation = new Quaternion(layout.BakeOrientation.value.x, layout.BakeOrientation.value.y,
                layout.BakeOrientation.value.z, layout.BakeOrientation.value.w);
            var inverse = Quaternion.Inverse(rotation);
            var inverseValue = new quaternion(inverse.x, inverse.y, inverse.z, inverse.w);
            if (!math.all(math.isfinite(inverseValue.value)))
            {
                error = BakeDataError.InvalidLayout;
                diagnostic = "Bake orientation has a non-finite inverse.";
                return false;
            }

            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<CullingVolumeBlob>();
                root.FormatVersion = formatVersion;
                root.VolumeId = volumeId;
                root.GroupCount = groups.Length;
                root.BakedGrid = layout;
                root.BakedGrid.InverseBakeOrientation = inverseValue;
                var keys = builder.Allocate(ref root.Groups, groups.Length);
                for (int i = 0; i < groups.Length; i++) keys[i] = groups[i];
                var ranges = builder.Allocate(ref root.Cells, cells.Length);
                for (int i = 0; i < cells.Length; i++) ranges[i] = cells[i];
                var bytes = builder.Allocate(ref root.CompressedBytes, compressed.Length);
                for (int i = 0; i < compressed.Length; i++) bytes[i] = compressed[i];
                blob = builder.CreateBlobAssetReference<CullingVolumeBlob>(allocator);
            }

            try
            {
                using var decoded = new NativeList<ushort>(Allocator.Temp);
                var destination = decoded;
                for (int i = 0; i < cells.Length; i++)
                {
                    destination.Clear();
                    var result = LegacyCellDecoder.DecodeCell(ref blob.Value, i, ref destination);
                    if (result.Status == DecodeStatus.Success) continue;
                    error = BakeDataError.InvalidCellData;
                    diagnostic = $"Cell {i}: {result.Status}.";
                    blob.Dispose();
                    blob = default;
                    return false;
                }
                error = BakeDataError.None;
                diagnostic = string.Empty;
                return true;
            }
            catch
            {
                if (blob.IsCreated) blob.Dispose();
                blob = default;
                throw;
            }
        }

        private bool ValidateMetadata(out BakeDataError error, out string diagnostic)
        {
            if (formatVersion != CullingVolumeBlob.CurrentFormatVersion)
                return Fail(BakeDataError.UnsupportedVersion, "Unsupported bake format version.", out error, out diagnostic);
            if (cells == null || cells.Length == 0 || compressed == null)
                return Fail(BakeDataError.MissingData, "Cell ranges or compressed bytes are missing.", out error, out diagnostic);
            if (groups == null || groups.Length > ushort.MaxValue || math.all(volumeId == uint4.zero))
                return Fail(BakeDataError.InvalidGroupMapping, "A stable volume ID and at most 65535 mapped groups are required.", out error, out diagnostic);
            for (int i = 0; i < groups.Length; i++)
                if (groups[i].GroupIndex != i || !math.all(groups[i].VolumeId == volumeId))
                    return Fail(BakeDataError.InvalidGroupMapping, $"Group mapping must preserve source index {i} and volume ID.", out error, out diagnostic);
            if (!math.all(math.isfinite(layout.CellCount)) || !math.all(layout.CellCount > 0) ||
                !math.all(math.floor(layout.CellCount) == layout.CellCount) ||
                (double)layout.CellCount.x * layout.CellCount.y * layout.CellCount.z != cells.Length)
                return Fail(BakeDataError.InvalidLayout, "Grid dimensions must be positive integers matching the cell array.", out error, out diagnostic);
            if (!math.all(math.isfinite(layout.CellSize)) || !math.all(layout.CellSize > 0) ||
                !math.all(math.isfinite(layout.BakeOrientation.value)))
                return Fail(BakeDataError.InvalidLayout, "Cell size must be finite and positive, and orientation must be finite.", out error, out diagnostic);
            error = BakeDataError.None;
            diagnostic = string.Empty;
            return true;
        }

        private static bool Fail(BakeDataError reason, string detail, out BakeDataError error, out string diagnostic)
        {
            error = reason;
            diagnostic = detail;
            return false;
        }
    }
}
