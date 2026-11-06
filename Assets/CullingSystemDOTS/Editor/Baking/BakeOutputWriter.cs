using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Runtime;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeMergeMetadata { ComputeNeighbors, NativeDefault, PreserveMergeMasks }
    public static class BakeOutputWriter
    {
        public static CullingBakeAsset CreateAsset(BakeCellResult[] results, uint4 volumeId, CullingBakeLayout layout,
            CullingGroupKey[] groups, BakeMergeMetadata metadata)
        {
            return CreatePreparedAsset(PrepareResults(results,volumeId,layout,groups,metadata),groups);
        }
        internal static LegacyBakeSource PrepareResults(BakeCellResult[] results, uint4 volumeId, CullingBakeLayout layout,
            CullingGroupKey[] groups, BakeMergeMetadata metadata, CancellationToken cancellation = default)
        {
            if (results == null || groups == null) throw new ArgumentNullException();
            if ((uint)metadata > (uint)BakeMergeMetadata.PreserveMergeMasks) throw new ArgumentOutOfRangeException(nameof(metadata));
            double count = (double)layout.CellCount.x*layout.CellCount.y*layout.CellCount.z;
            if (!math.all(math.isfinite(layout.CellCount)) || !math.all(layout.CellCount > 0) ||
                !math.all(math.floor(layout.CellCount) == layout.CellCount) || count > int.MaxValue)
                throw new ArgumentException("Positive integer full-grid dimensions required.");
            var cells = new LegacyVisibilityCell[(int)count]; var seen = new HashSet<int>();
            for (int i = 0; i < cells.Length; i++) cells[i] = new LegacyVisibilityCell {
                Compressed = Array.Empty<byte>(), HasMergeMetadata = true,
                MergeBitmask = 0 };
            foreach (var cell in results)
            {
                cancellation.ThrowIfCancellationRequested();
                if (cell == null || cell.Groups == null || (uint)cell.FullGridIndex >= (uint)cells.Length || !seen.Add(cell.FullGridIndex))
                    throw new ArgumentException("Each active sample must have a unique valid FullGridIndex and group set.");
                var encoded = Encode(cell.Groups,groups.Length);
                encoded.HasMergeMetadata = true;
                encoded.MergeBitmask = metadata == BakeMergeMetadata.NativeDefault ? uint.MaxValue : cell.NeighborMask;
                cells[cell.FullGridIndex] = encoded;
            }
            return new LegacyBakeSource { VolumeId = volumeId, Layout = layout, Cells = cells, DeclaredGroupCount = groups.Length };
        }
        private static CullingBakeAsset CreatePreparedAsset(LegacyBakeSource source, CullingGroupKey[] groups)
        {
            using var imported = LegacyBakeImporter.Import(source,groups);
            if (!imported.Success) throw new ArgumentException(imported.Diagnostic);
            imported.Asset.name = "Culling System DOTS Bake";
            return imported.Asset;
        }
        private static LegacyVisibilityCell Encode(ushort[] groups, int groupCount)
        {
            var bytes = new List<byte>(); int bits = 0, tokens = 0, previous = 0;
            void Write(uint value, int width)
            {
                for (int i = 0; i < width; i++, bits++)
                {
                    if ((bits & 7) == 0) bytes.Add(0);
                    bytes[bits >> 3] |= (byte)(((value >> i)&1) << (bits & 7));
                }
            }
            void Token(int value)
            {
                if (++tokens > ushort.MaxValue) throw new ArgumentException("Cell exceeds the legacy encoded token limit.");
                int header = value <= 1 ? 0 : value <= 3 ? 1 : value <= 7 ? 2 : 3;
                Write((uint)header,2); Write((uint)value,header+1);
            }
            for (int i = 0; i < groups.Length; i++)
            {
                int current = groups[i];
                if (current >= groupCount || (i > 0 && current <= previous)) throw new ArgumentException("Groups must be unique, sorted and within the original mapping.");
                int delta = current - previous; while (delta >= 15) { Token(15); delta -= 15; }
                Token(delta); previous = current;
            }
            return new LegacyVisibilityCell { Compressed = bytes.ToArray(), EncodedLength = tokens };
        }
        public static CullingBakeAsset SaveCompleted(string path, IBakeSession session, uint4 volumeId, CullingBakeLayout layout,
            CullingGroupKey[] groups, BakeMergeMetadata metadata)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Status == BakeSessionStatus.Cancelled) throw new OperationCanceledException();
            if (session.Status == BakeSessionStatus.Faulted) session.Complete();
            if (session.Status != BakeSessionStatus.Completed) throw new InvalidOperationException("Only a completed bake can be published.");
            return SaveResults(path,session.Complete(),volumeId,layout,groups,metadata);
        }
        public static CullingBakeAsset SaveProcessed(string path, BakePostprocessResult result, uint4 volumeId, CullingGroupKey[] groups, string inputFingerprint = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return SaveResults(path,result.Cells,volumeId,result.Layout,groups,BakeMergeMetadata.PreserveMergeMasks,inputFingerprint);
        }
        private static CullingBakeAsset SaveResults(string path, BakeCellResult[] results, uint4 volumeId, CullingBakeLayout layout,
            CullingGroupKey[] groups, BakeMergeMetadata metadata, string inputFingerprint = null)
        {
            return SavePrepared(path,PrepareResults(results,volumeId,layout,groups,metadata),groups,inputFingerprint);
        }
        internal static CullingBakeAsset SavePrepared(string path, LegacyBakeSource source, CullingGroupKey[] groups, string inputFingerprint)
        {
            string root = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            if (!full.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".asset",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be an .asset inside this project's Assets directory.");
            path = "Assets/"+full.Substring(root.Length).Replace('\\','/');
            if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("Choose an existing Assets directory.");
            bool existed = File.Exists(full);
            if (existed)
            {
                var previous = AssetDatabase.LoadAssetAtPath<CullingBakeAsset>(path);
                if (previous == null) throw new ArgumentException("Existing output must be a CullingBakeAsset.");
                if (EditorUtility.IsDirty(previous)) throw new InvalidOperationException("Output asset has unsaved edits.");
            }
            var asset = CreatePreparedAsset(source,groups);
            asset.SetInputFingerprint(inputFingerprint);
            string parent = Path.GetDirectoryName(path).Replace('\\','/'), stagingName = "PvsBakeStage_"+Guid.NewGuid().ToString("N");
            string stagingDirectory = parent+"/"+stagingName, stagingPath = stagingDirectory+"/"+Path.GetFileName(path);
            string stagingFull = Path.GetFullPath(stagingPath), backup = full+".pvs-backup-"+Guid.NewGuid().ToString("N");
            bool replaced = false, published = false;
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                AssetDatabase.CreateFolder(parent,stagingName);
                AssetDatabase.CreateAsset(asset,stagingPath);
                // CreateAsset assigns the staging filename; serialize the final name before replacing.
                asset.name = Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                if (!File.Exists(stagingFull)) throw new IOException("Staged bake was not written.");
                // Same-directory replace/rename is the publication boundary. Existing .meta is untouched.
                if (existed) File.Replace(stagingFull,full,backup); else File.Move(stagingFull,full);
                replaced = true;
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var saved = AssetDatabase.LoadAssetAtPath<CullingBakeAsset>(path);
                if (saved == null || !saved.TryCreateBlob(out var validation,out _,out _)) throw new IOException("Published bake failed reload validation.");
                validation.Dispose(); published = true; return saved;
            }
            catch
            {
                if (replaced)
                {
                    if (existed) File.Replace(backup,full,null);
                    else AssetDatabase.DeleteAsset(path);
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                throw;
            }
            finally
            {
                // If rollback itself fails, retain the backup for recovery.
                try
                {
                    if (published && File.Exists(backup)) File.Delete(backup);
                    AssetDatabase.DeleteAsset(stagingDirectory);
                    if (asset != null && !EditorUtility.IsPersistent(asset)) UnityEngine.Object.DestroyImmediate(asset);
                }
                finally { AssetDatabase.AllowAutoRefresh(); }
            }
        }
    }
}
