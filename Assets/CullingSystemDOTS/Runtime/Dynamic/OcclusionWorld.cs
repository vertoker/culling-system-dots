using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic
{
    public sealed partial class OcclusionWorld : IDisposable
    {
        private sealed class GeometrySlot
        {
            internal uint Generation = 1, PreparedRevision;
            internal OcclusionGeometry Data;
            internal bool Available;
        }
        private sealed class InstanceSlot
        {
            internal uint Generation = 1, BoundsRevision = 1, PreparedRevision;
            internal OcclusionBlocks.CellRange Range;
            internal bool Active, Available;
            internal OcclusionInstanceDescriptor Descriptor;
        }
        private static long identityCounter;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly OcclusionSettings settings;
        private readonly OcclusionMemoryBudget budget;
        private readonly List<GeometrySlot> geometries = new List<GeometrySlot>();
        private readonly List<InstanceSlot> instances = new List<InstanceSlot>();
        private readonly Stack<int> freeGeometries = new Stack<int>(), freeInstances = new Stack<int>();
        private readonly HashSet<OcclusionSnapshotLease> leases = new HashSet<OcclusionSnapshotLease>();
        private int geometryCount, instanceCount;
        private bool disposed;
        public ulong Identity { get; }
        public ulong Revision { get; private set; } = 1;
        public ulong OccluderEpoch { get; private set; } = 1;
        public long AccountedResourceBytes => budget.Used;

        public OcclusionWorld(OcclusionSettings settings = null)
        {
            this.settings = (settings ?? new OcclusionSettings()).Copy(); this.settings.Validate();
            budget = new OcclusionMemoryBudget(this.settings.MaxResourceBytes);
            long identity = Interlocked.Increment(ref identityCounter);
            if (identity <= 0) throw new InvalidOperationException("Runtime occlusion World identity space is exhausted.");
            Identity = (ulong)identity;
            InitializeUpdates();
        }

        public GeometryHandle RegisterGeometry(Mesh mesh)
        {
            CheckAlive();
            if (geometryCount >= settings.MaxGeometries) throw new InvalidOperationException("Geometry capacity is exhausted.");
            var data = new OcclusionGeometry(mesh, 1, budget);
            int index = freeGeometries.Count > 0 ? freeGeometries.Pop() : geometries.Count;
            if (index == geometries.Count) geometries.Add(new GeometrySlot());
            var slot = geometries[index]; slot.Data = data; slot.Available = true;
            geometryCount++; Revision = checked(Revision + 1);
            var handle = new GeometryHandle(Identity, index, slot.Generation); QueueGeometry(handle, slot); return handle;
        }

        /// <summary>Copies the source again. A rejected replacement preserves old data but withdraws it as an occluder.</summary>
        public void ReplaceGeometry(GeometryHandle handle, Mesh mesh)
        {
            CheckAlive(); var slot = GetGeometry(handle);
            slot.Available = false; OccluderEpoch = checked(OccluderEpoch + 1); Revision = checked(Revision + 1);
            var next = new OcclusionGeometry(mesh, checked(slot.Data.Revision + 1), budget);
            try
            {
                ValidateReplacementMembership(handle, next);
                // Validate every dependent binding before publishing the replacement. A larger mesh
                // must never reuse its former, potentially undersized candidate bounds.
                foreach (var instance in instances)
                    if (instance.Active && instance.Descriptor.Geometry.Equals(handle))
                    {
                        if ((uint)instance.Descriptor.SubMesh >= next.Mesh.subMeshCount)
                            throw new ArgumentException("Replacement removes a submesh used by a live instance.", nameof(mesh));
                        var expanded = instance.Descriptor.State.WorldBounds;
                        expanded.Encapsulate(TransformBounds(next.Mesh.bounds, instance.Descriptor.State.LocalToWorld));
                        if (!BoundsFinite(expanded) || instance.BoundsRevision == uint.MaxValue)
                            throw new ArgumentException("Replacement overflows live instance bounds or revision.", nameof(mesh));
                    }
            }
            catch { next.Dispose(); throw; }
            var previous = slot.Data; slot.Data = next; slot.Available = true; previous.Dispose();
            QueueGeometry(handle, slot);
            for (int index = 0; index < instances.Count; index++)
            {
                var instance = instances[index];
                if (instance.Active && instance.Descriptor.Geometry.Equals(handle))
                {
                    var oldState = instance.Descriptor.State; var oldRange = instance.Range;
                    var state = oldState;
                    state.WorldBounds.Encapsulate(TransformBounds(next.Mesh.bounds, state.LocalToWorld));
                    instance.Descriptor.State = state; instance.BoundsRevision++;
                    TrackInstance(new InstanceHandle(Identity, index, instance.Generation), instance, oldState, oldRange);
                }
            }
        }

        public InstanceHandle RegisterInstance(in OcclusionInstanceDescriptor descriptor)
        {
            CheckAlive(); Validate(descriptor, descriptor.State);
            var range = blockLayout.GetRange(descriptor.State.WorldBounds); ValidateMembershipChange(default, range);
            if (instanceCount >= settings.MaxInstances) throw new InvalidOperationException("Instance capacity is exhausted.");
            int index = freeInstances.Count > 0 ? freeInstances.Pop() : instances.Count;
            if (index == instances.Count) instances.Add(new InstanceSlot());
            var slot = instances[index]; slot.Active = true; slot.Available = true; slot.Descriptor = descriptor; slot.BoundsRevision = 1;
            instanceCount++; Changed(descriptor.State.Roles);
            var handle = new InstanceHandle(Identity, index, slot.Generation); TrackInstance(handle, slot, default, default); return handle;
        }

        public void UpdateInstance(InstanceHandle handle, in OcclusionInstanceState state)
        {
            CheckAlive(); var slot = GetInstance(handle);
            try
            {
                Validate(slot.Descriptor, state);
                var range = blockLayout.GetRange(state.WorldBounds); ValidateMembershipChange(slot.Range, range);
                uint nextRevision = checked(slot.BoundsRevision + 1);
                var previous = slot.Descriptor.State; var oldRange = slot.Range; Changed(previous.Roles | state.Roles);
                slot.Descriptor.State = state; slot.BoundsRevision = nextRevision; slot.Available = true;
                TrackInstance(handle, slot, previous, oldRange);
            }
            catch
            {
                // Failed capture must not leave an obsolete pose eligible to occlude or own rendering.
                slot.Available = false; slot.PreparedRevision = 0;
                updates.Remove(OcclusionUpdateKey.ForInstance(handle)); Changed(slot.Descriptor.State.Roles);
                throw;
            }
        }

        public void RemoveInstance(InstanceHandle handle)
        {
            CheckAlive(); var slot = GetInstance(handle); Changed(slot.Descriptor.State.Roles);
            RemoveMembership(handle, slot.Range); updates.Remove(OcclusionUpdateKey.ForInstance(handle)); slot.Range = default;
            slot.Active = false; slot.Descriptor = default; instanceCount--;
            if (slot.Generation != uint.MaxValue) { slot.Generation++; freeInstances.Push(handle.Index); }
        }

        public void RemoveGeometry(GeometryHandle handle)
        {
            CheckAlive(); var slot = GetGeometry(handle);
            foreach (var instance in instances)
                if (instance.Active && instance.Descriptor.Geometry.Equals(handle))
                    throw new InvalidOperationException("Remove referencing instances before removing their geometry.");
            slot.Data.Dispose(); slot.Data = null; slot.Available = false; geometryCount--;
            updates.Remove(OcclusionUpdateKey.ForGeometry(handle));
            if (slot.Generation != uint.MaxValue) { slot.Generation++; freeGeometries.Push(handle.Index); }
            Changed(OcclusionRole.Occluder);
        }

        public OcclusionSnapshotLease AcquireSnapshot()
        {
            CheckAlive();
            if (leases.Count >= settings.MaxSnapshotLeases) throw new InvalidOperationException("Snapshot lease capacity is exhausted.");
            long bytes = checked((long)instanceCount * UnsafeUtility.SizeOf<OcclusionInstanceSnapshot>());
            budget.Reserve(bytes);
            var native = default(NativeArray<OcclusionInstanceSnapshot>);
            var retained = new Dictionary<GeometryHandle, OcclusionGeometry>();
            try
            {
                var unavailable = new HashSet<GeometryHandle>();
                for (int i = 0; i < geometries.Count; i++)
                {
                    var slot = geometries[i]; if (slot.Data == null) continue;
                    var handle = new GeometryHandle(Identity, i, slot.Generation);
                    slot.Data.Retain(); retained.Add(handle, slot.Data); if (!GeometryPrepared(handle)) unavailable.Add(handle);
                }
                native = new NativeArray<OcclusionInstanceSnapshot>(instanceCount, Allocator.Persistent);
                var materials = new Material[instanceCount]; int output = 0;
                for (int i = 0; i < instances.Count; i++)
                {
                    var slot = instances[i]; if (!slot.Active) continue;
                    var descriptor = slot.Descriptor; var state = descriptor.State;
                    var roles = state.Roles;
                    if ((roles & OcclusionRole.Occluder) != 0 && (unavailable.Contains(descriptor.Geometry) || !OccluderPrepared(slot))) roles &= ~OcclusionRole.Occluder;
                    native[output] = new OcclusionInstanceSnapshot { Handle = new InstanceHandle(Identity, i, slot.Generation),
                        Geometry = descriptor.Geometry, LocalToWorld = state.LocalToWorld, WorldBounds = state.WorldBounds,
                        Color = state.Color, BoundsRevision = slot.BoundsRevision, Roles = roles, Opaque = state.Opaque ? (byte)1 : (byte)0,
                        Prepared = InstancePrepared(slot) ? (byte)1 : (byte)0,
                        SubMesh = descriptor.SubMesh, Layer = descriptor.Layer, Shadows = descriptor.Shadows };
                    materials[output++] = descriptor.Material;
                }
                var lease = new OcclusionSnapshotLease(Identity, Revision, OccluderEpoch, native, materials, retained, unavailable,
                    budget, bytes, released => leases.Remove(released));
                leases.Add(lease); return lease;
            }
            catch
            {
                if (native.IsCreated) native.Dispose();
                foreach (var geometry in retained.Values) geometry.Dispose(); budget.Release(bytes); throw;
            }
        }

        private void Validate(in OcclusionInstanceDescriptor descriptor, in OcclusionInstanceState state)
        {
            var matrix = state.LocalToWorld;
            if (!math.all(math.isfinite(matrix.c0)) || !math.all(math.isfinite(matrix.c1)) || !math.all(math.isfinite(matrix.c2)) ||
                !math.all(math.isfinite(matrix.c3)) || matrix.c0.w != 0 || matrix.c1.w != 0 || matrix.c2.w != 0 || matrix.c3.w != 1 ||
                !BoundsFinite(state.WorldBounds) || !math.all(math.isfinite(state.Color)) || ((int)state.Roles & ~7) != 0)
                throw new ArgumentException("Instance state needs finite affine geometry, enclosing bounds and known roles.");
            bool geometryRequired = (state.Roles & (OcclusionRole.Occluder | OcclusionRole.Renderable)) != 0;
            if (geometryRequired && !descriptor.Geometry.IsValid) throw new ArgumentException("This role requires registered geometry.");
            if (descriptor.Geometry.IsValid)
            {
                var geometry = GetGeometry(descriptor.Geometry).Data;
                if ((uint)descriptor.SubMesh >= geometry.Mesh.subMeshCount) throw new ArgumentException("Submesh is outside the registered Mesh.");
                var bounds = TransformBounds(geometry.Mesh.bounds, matrix);
                if (math.any((float3)state.WorldBounds.min > (float3)bounds.min) || math.any((float3)state.WorldBounds.max < (float3)bounds.max))
                    throw new ArgumentException("World bounds must enclose the transformed registered geometry.");
            }
            else if (!descriptor.Geometry.Equals(default)) throw new ArgumentException("Invalid geometry handle.");
            if ((state.Roles & OcclusionRole.Occluder) != 0 && !state.Opaque) throw new ArgumentException("Only explicitly opaque geometry may occlude.");
            if ((state.Roles & OcclusionRole.Renderable) != 0 && descriptor.Material == null) throw new ArgumentException("A render binding requires an explicit indirect material.");
            if ((uint)descriptor.Layer > 31 || (uint)descriptor.Shadows > 3) throw new ArgumentException("Invalid rendering layer or shadow mode.");
        }

        internal static Bounds TransformBounds(Bounds local, float4x4 matrix)
        {
            float3 center = math.transform(matrix, (float3)local.center), extents = local.extents;
            float3 worldExtents = math.abs(matrix.c0.xyz) * extents.x + math.abs(matrix.c1.xyz) * extents.y + math.abs(matrix.c2.xyz) * extents.z;
            if (!math.all(math.isfinite(center)) || !math.all(math.isfinite(worldExtents))) throw new ArgumentException("Transformed bounds overflow.");
            var bounds = new Bounds(center, worldExtents * 2);
            if (!BoundsFinite(bounds)) throw new ArgumentException("Transformed bounds overflow.");
            return bounds;
        }
        private static bool BoundsFinite(Bounds bounds) => math.all(math.isfinite((float3)bounds.center)) &&
            math.all(math.isfinite((float3)bounds.extents)) && !math.any((float3)bounds.extents < 0) &&
            math.all(math.isfinite((float3)bounds.min)) && math.all(math.isfinite((float3)bounds.max));
        private void Changed(OcclusionRole roles)
        {
            Revision = checked(Revision + 1);
            if ((roles & OcclusionRole.Occluder) != 0) OccluderEpoch = checked(OccluderEpoch + 1);
        }
        private GeometrySlot GetGeometry(GeometryHandle handle)
        {
            if (handle.World != Identity || (uint)handle.Index >= geometries.Count || handle.Generation != geometries[handle.Index].Generation || geometries[handle.Index].Data == null)
                throw new ArgumentException("Stale or foreign geometry handle.", nameof(handle));
            return geometries[handle.Index];
        }
        private InstanceSlot GetInstance(InstanceHandle handle)
        {
            if (handle.World != Identity || (uint)handle.Index >= instances.Count || handle.Generation != instances[handle.Index].Generation || !instances[handle.Index].Active)
                throw new ArgumentException("Stale or foreign instance handle.", nameof(handle));
            return instances[handle.Index];
        }
        private void CheckAlive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(OcclusionWorld));
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Mutate the runtime occlusion registry on its owning thread.");
        }
        public void Dispose()
        {
            if (disposed) return; CheckAlive();
            foreach (var lease in leases) lease.CompleteReaders();
            disposed = true;
            ClearUpdates();
            foreach (var slot in geometries) { slot.Data?.Dispose(); slot.Data = null; }
            geometries.Clear(); instances.Clear(); freeGeometries.Clear(); freeInstances.Clear();
        }
    }
}
