using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace CullingSystemDOTS.Dynamic
{
    public enum OcclusionUpdateKind : byte { Geometry, Instance, OccluderBlock }
    public readonly struct OcclusionUpdateKey : IEquatable<OcclusionUpdateKey>
    {
        public readonly OcclusionUpdateKind Kind;
        public readonly GeometryHandle Geometry;
        public readonly InstanceHandle Instance;
        public readonly int3 Block;
        private OcclusionUpdateKey(OcclusionUpdateKind kind, GeometryHandle geometry, InstanceHandle instance, int3 block)
        { Kind = kind; Geometry = geometry; Instance = instance; Block = block; }
        public static OcclusionUpdateKey ForGeometry(GeometryHandle handle) => new OcclusionUpdateKey(OcclusionUpdateKind.Geometry, handle, default, default);
        public static OcclusionUpdateKey ForInstance(InstanceHandle handle) => new OcclusionUpdateKey(OcclusionUpdateKind.Instance, default, handle, default);
        public static OcclusionUpdateKey ForBlock(int3 block) => new OcclusionUpdateKey(OcclusionUpdateKind.OccluderBlock, default, default, block);
        public bool Equals(OcclusionUpdateKey other) => Kind == other.Kind && Geometry.Equals(other.Geometry) && Instance.Equals(other.Instance) && Block.Equals(other.Block);
        public override bool Equals(object other) => other is OcclusionUpdateKey key && Equals(key);
        public override int GetHashCode() => unchecked(((int)Kind * 397 ^ Geometry.GetHashCode()) * 397 ^ Instance.GetHashCode() ^ (int)math.hash(Block));
    }

    public readonly struct OcclusionUpdateBudget
    {
        public readonly int MaxBytes, MaxItems;
        public OcclusionUpdateBudget(int maxBytes, int maxItems = 64) { MaxBytes = maxBytes; MaxItems = maxItems; }
    }
    public readonly struct OcclusionUploadSlice
    {
        public readonly OcclusionUpdateKey Key;
        public readonly ulong Revision;
        public readonly int ByteOffset, ByteCount;
        public readonly bool Complete;
        internal OcclusionUploadSlice(OcclusionUpdateKey key, ulong revision, int offset, int count, bool complete)
        { Key = key; Revision = revision; ByteOffset = offset; ByteCount = count; Complete = complete; }
    }
    public sealed class OcclusionUpdateBatch
    {
        public IReadOnlyList<OcclusionUploadSlice> Slices { get; }
        public int PreparedBytes { get; }
        public int RemainingItems { get; }
        internal OcclusionUpdateBatch(List<OcclusionUploadSlice> slices, int bytes, int remaining)
        { Slices = slices.AsReadOnly(); PreparedBytes = bytes; RemainingItems = remaining; }
    }
    public sealed class OcclusionUpdateQueue
    {
        private sealed class Pending
        {
            internal OcclusionUpdateKey Key;
            internal ulong Revision;
            internal int Bytes, Offset;
        }
        private readonly Dictionary<OcclusionUpdateKey, LinkedListNode<Pending>> pending = new Dictionary<OcclusionUpdateKey, LinkedListNode<Pending>>();
        private readonly LinkedList<Pending> order = new LinkedList<Pending>();
        public readonly int Capacity;
        public int PendingCount => pending.Count;
        public bool Overflowed { get; private set; }
        public OcclusionUpdateQueue(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }
        public bool Enqueue(OcclusionUpdateKey key, ulong revision, int bytes)
        {
            if (revision == 0 || bytes <= 0) throw new ArgumentOutOfRangeException(nameof(revision), "Updates need a positive revision and payload size.");
            if ((uint)key.Kind > (uint)OcclusionUpdateKind.OccluderBlock ||
                (key.Kind == OcclusionUpdateKind.Geometry && !key.Geometry.IsValid) ||
                (key.Kind == OcclusionUpdateKind.Instance && !key.Instance.IsValid)) throw new ArgumentException("Invalid update key.", nameof(key));
            if (pending.TryGetValue(key, out var existing))
            {
                var value = existing.Value;
                if (revision < value.Revision) return false;
                if (revision == value.Revision)
                {
                    if (bytes != value.Bytes) throw new ArgumentException("A revision cannot identify two different payload sizes.");
                    return true;
                }
                value.Revision = revision; value.Bytes = bytes; value.Offset = 0; return true;
            }
            if (pending.Count == Capacity) { Overflowed = true; return false; }
            var node = order.AddLast(new Pending { Key = key, Revision = revision, Bytes = bytes });
            pending.Add(key, node); return true;
        }
        public OcclusionUpdateBatch Process(in OcclusionUpdateBudget budget)
        {
            if (budget.MaxBytes <= 0 || budget.MaxItems <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
            var slices = new List<OcclusionUploadSlice>(); int prepared = 0;
            while (order.First != null && prepared < budget.MaxBytes && slices.Count < budget.MaxItems)
            {
                var node = order.First; var item = node.Value;
                int count = Math.Min(item.Bytes - item.Offset, budget.MaxBytes - prepared);
                bool complete = count == item.Bytes - item.Offset;
                slices.Add(new OcclusionUploadSlice(item.Key, item.Revision, item.Offset, count, complete));
                item.Offset += count; prepared += count; order.RemoveFirst();
                if (complete) pending.Remove(item.Key); else order.AddLast(node);
            }
            return new OcclusionUpdateBatch(slices, prepared, pending.Count);
        }
        public void ClearOverflow() { Overflowed = false; }
        internal bool Contains(OcclusionUpdateKey key) => pending.ContainsKey(key);
        internal void Remove(OcclusionUpdateKey key)
        {
            if (!pending.TryGetValue(key, out var node)) return;
            order.Remove(node); pending.Remove(key);
        }
        internal void Clear() { pending.Clear(); order.Clear(); Overflowed = false; }
    }
}
