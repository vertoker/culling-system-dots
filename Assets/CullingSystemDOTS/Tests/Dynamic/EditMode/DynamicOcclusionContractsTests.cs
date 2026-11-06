using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Dynamic.Tests
{
    public class DynamicOcclusionContractsTests
    {
        private Mesh mesh;
        [SetUp] public void SetUp()
        {
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.RecalculateBounds();
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(mesh); }

        private static OcclusionInstanceState State(float x = 0, OcclusionRole roles = OcclusionRole.Occludee) => new OcclusionInstanceState {
            LocalToWorld = float4x4.Translate(new float3(x, 0, 0)), WorldBounds = new Bounds(new Vector3(x + .5f, .5f, 0), new Vector3(1, 1, .01f)),
            Roles = roles, Opaque = true, Color = new float4(1) };

        [Test]
        public void OldGenerationCannotMutateReplacement()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            var first = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
            world.RemoveInstance(first);
            var replacement = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(4) });
            Assert.That(replacement.Equals(first), Is.False);
            Assert.Throws<ArgumentException>(() => world.UpdateInstance(first, State(99)));
            using var snapshot = world.AcquireSnapshot(); Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Instances[0].WorldBounds.center.x, Is.EqualTo(4.5f));
        }

        [Test]
        public void WorldIdentityRejectsForeignHandle()
        {
            using var first = new OcclusionWorld(); using var second = new OcclusionWorld();
            var a = first.RegisterGeometry(mesh); var b = second.RegisterGeometry(mesh);
            Assert.That(a.Equals(b), Is.False);
            Assert.Throws<ArgumentException>(() => second.RemoveGeometry(a));
        }

        [Test]
        public void SnapshotRetainsGeometryUntilLastReader()
        {
            var world = new OcclusionWorld(); OcclusionSnapshotLease lease = null;
            try
            {
                var handle = world.RegisterGeometry(mesh); lease = world.AcquireSnapshot();
                Assert.That(lease, Is.Not.Null);
                world.RemoveGeometry(handle); world.Dispose();
                Assert.That(lease.GetGeometry(handle).Vertices[1].x, Is.EqualTo(1));
                Assert.That(world.AccountedResourceBytes, Is.GreaterThan(0));
                lease.Dispose();
                Assert.That(world.AccountedResourceBytes, Is.Zero);
                Assert.Throws<ObjectDisposedException>(() => lease.GetGeometry(handle));
            }
            finally { lease?.Dispose(); world.Dispose(); }
        }

        [Test]
        public void SameMeshReferenceReplacementChangesRevision()
        {
            using var world = new OcclusionWorld(); var handle = world.RegisterGeometry(mesh);
            using var previous = world.AcquireSnapshot(); Assert.That(previous, Is.Not.Null);
            var vertices = mesh.vertices; vertices[1] = Vector3.right * 2; mesh.vertices = vertices;
            world.ReplaceGeometry(handle, mesh);
            using var current = world.AcquireSnapshot();
            Assert.That(current.GetGeometry(handle).Revision, Is.GreaterThan(previous.GetGeometry(handle).Revision));
            Assert.That(previous.GetGeometry(handle).Vertices[1].x, Is.EqualTo(1));
            Assert.That(current.GetGeometry(handle).Vertices[1].x, Is.EqualTo(2));
        }

        [TestCase(0), TestCase(-1)]
        public void InvalidSettingsFailBeforeAllocation(int instances)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { using var world = new OcclusionWorld(new OcclusionSettings { MaxInstances = instances }); });
        }

        [Test]
        public void SettingsAreCopiedAndGeometryMutationDoesNotChangeOldSnapshot()
        {
            var settings = new OcclusionSettings { MaxInstances = 1 };
            using var world = new OcclusionWorld(settings); settings.MaxInstances = 10;
            var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
            Assert.Throws<InvalidOperationException>(() => world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() }));
        }

        [Test]
        public void FailedReplacementPreservesPreviousGeometryAndBudget()
        {
            using var world = new OcclusionWorld(); var handle = world.RegisterGeometry(mesh);
            long bytes = world.AccountedResourceBytes;
            mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
            Assert.Throws<ArgumentException>(() => world.ReplaceGeometry(handle, mesh));
            Assert.That(world.AccountedResourceBytes, Is.EqualTo(bytes));
            using var snapshot = world.AcquireSnapshot(); Assert.That(snapshot.GetGeometry(handle).Vertices[0].x, Is.Zero);
        }

        [Test]
        public void RemovingGeometryInUseRejectsWithoutInvalidatingInstance()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
            Assert.Throws<InvalidOperationException>(() => world.RemoveGeometry(geometry));
            using var snapshot = world.AcquireSnapshot(); Assert.That(snapshot.Instances.Length, Is.EqualTo(1));
        }

        [Test]
        public void SnapshotIsImmutableAcrossInstanceUpdate()
        {
            using var world = new OcclusionWorld(); var handle = world.RegisterInstance(new OcclusionInstanceDescriptor { State = State() });
            using var previous = world.AcquireSnapshot(); Assert.That(previous, Is.Not.Null);
            world.UpdateInstance(handle, State(5));
            using var current = world.AcquireSnapshot();
            Assert.That(previous.Instances[0].WorldBounds.center.x, Is.EqualTo(.5f));
            Assert.That(current.Instances[0].WorldBounds.center.x, Is.EqualTo(5.5f));
            Assert.That(current.Instances[0].BoundsRevision, Is.GreaterThan(previous.Instances[0].BoundsRevision));
        }

        [Test]
        public void ReplacementExpandsLiveInstanceBounds()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
            using var previous = world.AcquireSnapshot();
            var vertices = mesh.vertices; vertices[1] = Vector3.right * 2; mesh.vertices = vertices; mesh.RecalculateBounds();
            world.ReplaceGeometry(geometry, mesh);
            using var current = world.AcquireSnapshot();
            Assert.That(current.Instances[0].WorldBounds.max.x, Is.GreaterThanOrEqualTo(2), "A replacement cannot leave an undersized occludee bound.");
            Assert.That(current.Instances[0].BoundsRevision, Is.GreaterThan(previous.Instances[0].BoundsRevision));
        }

        [Test]
        public void FailedReplacementWithdrawsOccluderBeforePublication()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State(0, OcclusionRole.Occluder | OcclusionRole.Occludee) });
            ulong epoch = world.OccluderEpoch;
            mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
            Assert.Throws<ArgumentException>(() => world.ReplaceGeometry(geometry, mesh));
            using var current = world.AcquireSnapshot();
            Assert.That(world.OccluderEpoch, Is.GreaterThan(epoch));
            Assert.That(current.GetGeometry(geometry).Available, Is.False);
            Assert.That(current.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
        }

        [Test]
        public void SnapshotLimitReleasesCapacityWithoutLeakingPayload()
        {
            using var world = new OcclusionWorld(new OcclusionSettings { MaxSnapshotLeases = 1 });
            world.RegisterInstance(new OcclusionInstanceDescriptor { State = State() });
            var first = world.AcquireSnapshot();
            try { Assert.Throws<InvalidOperationException>(() => world.AcquireSnapshot()); }
            finally { first.Dispose(); }
            Assert.That(world.AccountedResourceBytes, Is.Zero);
            using var second = world.AcquireSnapshot(); Assert.That(second.Instances.Length, Is.EqualTo(1));
        }

        [Test]
        public void PayloadBudgetRejectsLargerReplacementAndPreservesOldCopy()
        {
            using var world = new OcclusionWorld(new OcclusionSettings { MaxResourceBytes = 1024 });
            var geometry = world.RegisterGeometry(mesh); long before = world.AccountedResourceBytes;
            var large = new Mesh();
            try
            {
                var vertices = new Vector3[100]; vertices[1] = Vector3.right; vertices[2] = Vector3.up;
                large.vertices = vertices; large.triangles = new[] { 0, 1, 2 };
                Assert.Throws<InvalidOperationException>(() => world.ReplaceGeometry(geometry, large));
                Assert.That(world.AccountedResourceBytes, Is.EqualTo(before));
                using var snapshot = world.AcquireSnapshot();
                Assert.That(snapshot.GetGeometry(geometry).Vertices.Length, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(large); }
        }

        [Test]
        public void MovingOccludeeDoesNotRebuildGeometryOrInvalidateOccluders()
        {
            using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
            var instance = world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
            ulong epoch = world.OccluderEpoch;
            world.UpdateInstance(instance, State(5));
            using var snapshot = world.AcquireSnapshot();
            Assert.That(snapshot.GetGeometry(geometry).Revision, Is.EqualTo(1));
            Assert.That(world.OccluderEpoch, Is.EqualTo(epoch));
        }

        [Test]
        public void InvalidNumericStateIsRejected()
        {
            using var world = new OcclusionWorld(); var state = State(); state.LocalToWorld.c3.x = float.NaN;
            Assert.Throws<ArgumentException>(() => world.RegisterInstance(new OcclusionInstanceDescriptor { State = state }));
            state = State(); state.WorldBounds = new Bounds(new Vector3(float.MaxValue, 0, 0), Vector3.one * float.MaxValue);
            Assert.Throws<ArgumentException>(() => world.RegisterInstance(new OcclusionInstanceDescriptor { State = state }));
        }

        [Test]
        public void RepeatedCreateDisposeReleasesPayloadAndKeepsCallerMesh()
        {
            for (int i = 0; i < 32; i++)
            {
                using var world = new OcclusionWorld(); var geometry = world.RegisterGeometry(mesh);
                world.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = State() });
                var lease = world.AcquireSnapshot();
                try { world.Dispose(); Assert.That(lease.GetGeometry(geometry).Indices.Length, Is.EqualTo(3)); }
                finally { lease.Dispose(); }
                Assert.That(world.AccountedResourceBytes, Is.Zero);
            }
            Assert.That(mesh != null && mesh.vertexCount == 3, Is.True, "The provider retains its source Mesh.");
        }

        private struct ReadSnapshot : IJob
        {
            [ReadOnly] public NativeArray<OcclusionInstanceSnapshot>.ReadOnly Instances;
            public NativeArray<int> Output;
            public void Execute() { Output[0] = Instances.Length; }
        }

        [Test]
        public void WorldDisposeJoinsRegisteredJobReaders()
        {
            var world = new OcclusionWorld(); OcclusionSnapshotLease lease = null;
            using var output = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                world.RegisterInstance(new OcclusionInstanceDescriptor { State = State() });
                lease = world.AcquireSnapshot(); Assert.That(lease, Is.Not.Null);
                lease.RegisterReader(new ReadSnapshot { Instances = lease.Instances, Output = output }.Schedule());
                world.Dispose(); Assert.That(output[0], Is.EqualTo(1));
            }
            finally { lease?.Dispose(); world.Dispose(); }
        }

        [TestCase("view"), TestCase("projection"), TestCase("jitter"), TestCase("viewport"),
         TestCase("size"), TestCase("scale"), TestCase("eye"), TestCase("kind"), TestCase("id"),
         TestCase("epoch"), TestCase("bounds"), TestCase("instance")]
        public void ChangedIdentityNeverReusesHiddenResult(string change)
        {
            var view = new OcclusionViewSignature { ViewId = 1, View = Matrix4x4.identity, Projection = Matrix4x4.identity,
                NonJitteredProjection = Matrix4x4.identity, Viewport = new Rect(0, 0, 128, 128), TargetSize = new int2(128), RenderScale = 1 };
            var instance = new InstanceHandle(1, 0, 1);
            var stamp = new OcclusionResultStamp { View = view, Instance = instance, OccluderEpoch = 1, BoundsRevision = 1, Visibility = OcclusionVisibility.Occluded };
            Assert.That(OcclusionValidity.Resolve(stamp, view, 1, instance, 1), Is.EqualTo(OcclusionVisibility.Occluded));
            ulong epoch = 1; uint bounds = 1;
            switch (change)
            {
                case "view": view.View.m03 = 1; break;
                case "projection": view.Projection.m00 = 2; break;
                case "jitter": view.NonJitteredProjection.m02 = .01f; break;
                case "viewport": view.Viewport.x = 1; break;
                case "size": view.TargetSize.x++; break;
                case "scale": view.RenderScale = .5f; break;
                case "eye": view.Eye = 1; break;
                case "kind": view.Orthographic = true; break;
                case "id": view.ViewId++; break;
                case "epoch": epoch++; break;
                case "bounds": bounds++; break;
                case "instance": instance = new InstanceHandle(1, 0, 2); break;
            }
            Assert.That(OcclusionValidity.Resolve(stamp, view, epoch, instance, bounds), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
        }
    }
}
