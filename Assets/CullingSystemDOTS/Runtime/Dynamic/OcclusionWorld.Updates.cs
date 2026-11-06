using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic
{
    public readonly struct OcclusionBlockInfo
    {
        public readonly bool Exists;
        public readonly int Members, Occluders;
        public readonly ulong Revision, PreparedRevision;
        internal OcclusionBlockInfo(int members, int occluders, ulong revision, ulong prepared)
        { Exists = true; Members = members; Occluders = occluders; Revision = revision; PreparedRevision = prepared; }
    }
    public sealed partial class OcclusionWorld
    {
        private sealed class SpatialBlock
        {
            internal readonly HashSet<InstanceHandle> Members = new HashSet<InstanceHandle>();
            internal readonly HashSet<InstanceHandle> Occluders = new HashSet<InstanceHandle>();
            internal ulong Revision, PreparedRevision;
        }
        private OcclusionBlocks blockLayout;
        private OcclusionUpdateQueue updates;
        private readonly Dictionary<int3, SpatialBlock> spatialBlocks = new Dictionary<int3, SpatialBlock>();
        private long membershipCount;
        private ulong blockRevision;
        public int ActiveBlockCount => spatialBlocks.Count;
        public int PendingUpdateCount => updates.PendingCount;
        public bool QueueOverflowed => updates.Overflowed;
        public OcclusionBlockInfo GetBlockInfo(int3 cell)
        {
            CheckAlive();
            return spatialBlocks.TryGetValue(cell, out var block)
                ? new OcclusionBlockInfo(block.Members.Count, block.Occluders.Count, block.Revision, block.PreparedRevision) : default;
        }

        /// <summary>Prepares bounded logical payload slices, not GPU completion. The GPU owner must validate its actual upload separately.</summary>
        public OcclusionUpdateBatch ProcessUpdates(in OcclusionUpdateBudget budget)
        {
            CheckAlive();
            if (budget.MaxBytes <= 0 || budget.MaxItems <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (updates.Overflowed) RescanUnqueuedUpdates();
            var limited = new OcclusionUpdateBudget(Math.Min(budget.MaxBytes, settings.MaxUploadBytesPerFrame), budget.MaxItems);
            var batch = updates.Process(limited);
            foreach (var slice in batch.Slices)
            {
                if (!slice.Complete) continue;
                if (slice.Key.Kind == OcclusionUpdateKind.Geometry)
                {
                    var handle = slice.Key.Geometry;
                    if (handle.World == Identity && (uint)handle.Index < geometries.Count)
                    {
                        var slot = geometries[handle.Index];
                        if (slot.Data != null && slot.Generation == handle.Generation && slot.Data.Revision == slice.Revision)
                            slot.PreparedRevision = (uint)slice.Revision;
                    }
                }
                else if (slice.Key.Kind == OcclusionUpdateKind.Instance)
                {
                    var handle = slice.Key.Instance;
                    if (handle.World == Identity && (uint)handle.Index < instances.Count)
                    {
                        var slot = instances[handle.Index];
                        if (slot.Active && slot.Generation == handle.Generation && slot.BoundsRevision == slice.Revision)
                            slot.PreparedRevision = (uint)slice.Revision;
                    }
                }
                else if (spatialBlocks.TryGetValue(slice.Key.Block, out var block) && block.Revision == slice.Revision)
                    block.PreparedRevision = slice.Revision;
            }
            return batch;
        }
        public Bounds GetGeometryBounds(GeometryHandle handle) { CheckAlive(); return GetGeometry(handle).Data.LocalBounds; }

        private void InitializeUpdates()
        {
            blockLayout = new OcclusionBlocks(settings.BlockSize, settings.MaxBlocksPerInstance);
            updates = new OcclusionUpdateQueue(settings.MaxQueuedUpdates);
        }

        private void ValidateMembershipChange(OcclusionBlocks.CellRange previous, OcclusionBlocks.CellRange next)
        {
            if (membershipCount - previous.Count + next.Count > settings.MaxBlockMemberships)
                throw new InvalidOperationException("Block membership capacity is exhausted.");
            int added = MissingCells(next), removed = 0;
            for (int i = 0; i < previous.Count; i++)
            {
                var cell = previous.Cell(i);
                bool retained = next.Count > 0 && math.all(cell >= next.Min) && math.all(cell <= next.Max);
                if (!retained && spatialBlocks.TryGetValue(cell, out var block) && block.Members.Count == 1) removed++;
            }
            if ((long)spatialBlocks.Count + added - removed > settings.MaxActiveBlocks)
                throw new InvalidOperationException("Active block capacity is exhausted.");
        }
        private int MissingCells(OcclusionBlocks.CellRange range)
        {
            int missing = 0;
            for (int i = 0; i < range.Count; i++) if (!spatialBlocks.ContainsKey(range.Cell(i))) missing++;
            return missing;
        }

        private void ValidateReplacementMembership(GeometryHandle geometry, OcclusionGeometry replacement)
        {
            long added = 0, members = 0;
            foreach (var slot in instances)
            {
                if (!slot.Active || !slot.Descriptor.Geometry.Equals(geometry)) continue;
                var bounds = slot.Descriptor.State.WorldBounds;
                bounds.Encapsulate(TransformBounds(replacement.Mesh.bounds, slot.Descriptor.State.LocalToWorld));
                var range = blockLayout.GetRange(bounds);
                added += MissingCells(range); members += Math.Max(0, range.Count - slot.Range.Count);
            }
            // A conservative reservation may reject an extreme replacement rather than partially publish it.
            if (spatialBlocks.Count + added > settings.MaxActiveBlocks || membershipCount + members > settings.MaxBlockMemberships)
                throw new InvalidOperationException("Replacement exceeds the block membership capacity.");
        }

        private void TrackInstance(InstanceHandle handle, InstanceSlot slot, OcclusionInstanceState previous, OcclusionBlocks.CellRange previousRange)
        {
            var state = slot.Descriptor.State; var next = blockLayout.GetRange(state.WorldBounds);
            bool wasOccluder = (previous.Roles & OcclusionRole.Occluder) != 0;
            bool isOccluder = (state.Roles & OcclusionRole.Occluder) != 0;
            if (!previousRange.Equals(next) || previousRange.Count == 0)
            {
                RemoveMembership(handle, previousRange);
                for (int i = 0; i < next.Count; i++)
                {
                    var cell = next.Cell(i);
                    if (!spatialBlocks.TryGetValue(cell, out var block)) spatialBlocks.Add(cell, block = new SpatialBlock());
                    block.Members.Add(handle);
                    if (isOccluder) { block.Occluders.Add(handle); Dirty(cell, block); }
                }
                membershipCount += next.Count;
            }
            else if (wasOccluder || isOccluder)
                for (int i = 0; i < next.Count; i++)
                {
                    var cell = next.Cell(i); var block = spatialBlocks[cell];
                    if (isOccluder) block.Occluders.Add(handle); else block.Occluders.Remove(handle);
                    Dirty(cell, block);
                }
            slot.Range = next; slot.PreparedRevision = 0;
            updates.Enqueue(OcclusionUpdateKey.ForInstance(handle), slot.BoundsRevision, UnsafeUtility.SizeOf<OcclusionInstanceSnapshot>());
        }

        private void RemoveMembership(InstanceHandle handle, OcclusionBlocks.CellRange range)
        {
            for (int i = 0; i < range.Count; i++)
            {
                var cell = range.Cell(i); var block = spatialBlocks[cell];
                block.Members.Remove(handle); bool changed = block.Occluders.Remove(handle);
                if (block.Members.Count == 0) { spatialBlocks.Remove(cell); updates.Remove(OcclusionUpdateKey.ForBlock(cell)); }
                else if (changed) Dirty(cell, block);
            }
            membershipCount -= range.Count;
        }
        private void Dirty(int3 cell, SpatialBlock block)
        {
            block.Revision = checked(++blockRevision);
            updates.Enqueue(OcclusionUpdateKey.ForBlock(cell), block.Revision, checked(16 + block.Occluders.Count * 4));
        }
        private void QueueGeometry(GeometryHandle handle, GeometrySlot slot)
        {
            slot.PreparedRevision = 0;
            updates.Enqueue(OcclusionUpdateKey.ForGeometry(handle), slot.Data.Revision,
                checked(slot.Data.Vertices.Length * 12 + slot.Data.Indices.Length * 4));
        }
        private bool GeometryPrepared(GeometryHandle handle) => !handle.IsValid ||
            (geometries[handle.Index].Available && geometries[handle.Index].PreparedRevision == geometries[handle.Index].Data.Revision);
        private bool InstancePrepared(InstanceSlot slot) => slot.Available && slot.PreparedRevision == slot.BoundsRevision && GeometryPrepared(slot.Descriptor.Geometry);
        private bool OccluderPrepared(InstanceSlot slot)
        {
            if (!InstancePrepared(slot)) return false;
            for (int i = 0; i < slot.Range.Count; i++)
            {
                var block = spatialBlocks[slot.Range.Cell(i)];
                if (block.PreparedRevision != block.Revision) return false;
            }
            return true;
        }
        private void RescanUnqueuedUpdates()
        {
            updates.ClearOverflow();
            for (int i = 0; i < geometries.Count; i++)
            {
                var slot = geometries[i]; if (slot.Data == null || slot.PreparedRevision == slot.Data.Revision) continue;
                updates.Enqueue(OcclusionUpdateKey.ForGeometry(new GeometryHandle(Identity, i, slot.Generation)), slot.Data.Revision,
                    checked(slot.Data.Vertices.Length * 12 + slot.Data.Indices.Length * 4));
            }
            for (int i = 0; i < instances.Count; i++)
            {
                var slot = instances[i]; if (!slot.Active || !slot.Available || slot.PreparedRevision == slot.BoundsRevision) continue;
                updates.Enqueue(OcclusionUpdateKey.ForInstance(new InstanceHandle(Identity, i, slot.Generation)), slot.BoundsRevision,
                    UnsafeUtility.SizeOf<OcclusionInstanceSnapshot>());
            }
            foreach (var pair in spatialBlocks)
                if (pair.Value.PreparedRevision != pair.Value.Revision)
                    updates.Enqueue(OcclusionUpdateKey.ForBlock(pair.Key), pair.Value.Revision, checked(16 + pair.Value.Occluders.Count * 4));
        }
        private void ClearUpdates() { updates.Clear(); spatialBlocks.Clear(); membershipCount = 0; }
    }
}
