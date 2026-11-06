using System;
using System.Collections;
using CullingSystemDOTS.Dynamic.Gpu;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Rendering.Tests
{
    public class DynamicGeometryCacheTests
    {
        private Mesh mesh;
        [SetUp] public void SetUp() => mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(0,1,0) },
            normals = new[] { Vector3.forward,Vector3.forward,Vector3.forward }, triangles = new[] { 0,1,2 } };
        [TearDown] public void TearDown()
        {
            OcclusionGpuRetirement.DrainForShutdown(); UnityEngine.Object.Destroy(mesh);
        }
        private InstanceHandle Instance(OcclusionWorld registry, GeometryHandle geometry) => registry.RegisterInstance(
            new OcclusionInstanceDescriptor { Geometry = geometry, State = new OcclusionInstanceState { LocalToWorld = float4x4.identity,
                WorldBounds = new Bounds(Vector3.zero, Vector3.one * 4), Roles = OcclusionRole.Occluder, Opaque = true } });
        [Test] public void InvalidCreationLimitFailsBeforeWorldAllocation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { using (var registry = new OcclusionWorld(new OcclusionSettings { MaxGeometryCreationsPerFrame = 0 })) { } });
        }
        [UnityTest] public IEnumerator SupersededGeometryCannotPublishButItsOldReaderRemainsAlive()
        {
            using (var registry = new OcclusionWorld())
            using (var cache = new OcclusionGpuGeometryCache(registry))
            {
                var geometry = registry.RegisterGeometry(mesh); Instance(registry,geometry);
                using (var before = registry.AcquireSnapshot())
                {
                    var budget = new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1); cache.Process(before,budget);
                    Assert.That(cache.CachedCount, Is.EqualTo(1));
                    using (var oldReader = cache.Acquire(before))
                    {
                        Assert.That(oldReader.TryGet(geometry,1,out var oldMesh), Is.True);
                        var points = mesh.vertices; for (int i = 0; i < points.Length; i++) points[i] *= 2; mesh.vertices = points;
                        registry.ReplaceGeometry(geometry,mesh);
                        using (var after = registry.AcquireSnapshot())
                        {
                            budget.BeginFrame(2); cache.Process(after,budget);
                            using (var current = cache.Acquire(after))
                            using (var stale = cache.Acquire(before))
                            {
                                Assert.That(stale.Count, Is.Zero, "A fresh request cannot resurrect a superseded mesh.");
                                Assert.That(current.TryGet(geometry,2,out var newMesh), Is.True);
                                Assert.That(current.TryGet(geometry,1,out _), Is.False);
                                Assert.That(oldMesh != null, Is.True); Assert.That(oldMesh.bounds.size.x, Is.EqualTo(2));
                                Assert.That(newMesh.bounds.size.x, Is.EqualTo(4));
                            }
                        }
                    }
                }
            }
            yield return null;
        }
        [Test] public void GeometryCreationCapIsSharedAcrossRepeatedCallsInTheSameFrame()
        {
            using (var registry = new OcclusionWorld(new OcclusionSettings { MaxGeometryCreationsPerFrame = 2 }))
            using (var cache = new OcclusionGpuGeometryCache(registry))
            {
                for (int i = 0; i < 10; i++) Instance(registry,registry.RegisterGeometry(mesh));
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var budget = new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1);
                    cache.Process(snapshot,budget); cache.Process(snapshot,budget);
                    Assert.That(cache.CachedCount, Is.EqualTo(2)); Assert.That(cache.PendingCount, Is.EqualTo(8));
                    budget.BeginFrame(2); cache.Process(snapshot,budget); Assert.That(cache.CachedCount, Is.EqualTo(4));
                }
            }
        }
        [UnityTest] public IEnumerator MultipleGeometryUploadsShareOneBoundedFrameBudget()
        {
            using (var registry = new OcclusionWorld())
            using (var cache = new OcclusionGpuGeometryCache(registry))
            {
                Instance(registry,registry.RegisterGeometry(mesh)); Instance(registry,registry.RegisterGeometry(mesh));
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var budget = new OcclusionGpuUploadBudget(64); int total = 0;
                    budget.BeginFrame(1); total += cache.Process(snapshot,budget);
                    Assert.That(budget.Used, Is.GreaterThan(0)); Assert.That(cache.PendingCount, Is.GreaterThan(0));
                    total += cache.Process(snapshot,budget); Assert.That(budget.Used, Is.LessThanOrEqualTo(64));
                    for (ulong frame = 2; cache.PendingCount > 0 && frame < 20; frame++)
                    { budget.BeginFrame(frame); total += cache.Process(snapshot,budget); Assert.That(budget.Used, Is.LessThanOrEqualTo(64)); yield return null; }
                    Assert.That(cache.PendingCount, Is.Zero); Assert.That(total, Is.EqualTo(168));
                    using (var ready = cache.Acquire(snapshot)) Assert.That(ready.Count, Is.EqualTo(2));
                }
            }
        }
        [Test] public void RemovedBindingsWithdrawTheCachedSourceWithoutDestroyingItsReader()
        {
            using (var registry = new OcclusionWorld())
            using (var cache = new OcclusionGpuGeometryCache(registry))
            {
                var geometry = registry.RegisterGeometry(mesh); var instance = Instance(registry,geometry);
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var budget = new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1); cache.Process(snapshot,budget);
                    using (var reader = cache.Acquire(snapshot))
                    {
                        Assert.That(reader.TryGet(geometry,1,out var retained), Is.True);
                        registry.RemoveInstance(instance);
                        using (var empty = registry.AcquireSnapshot())
                        {
                            budget.BeginFrame(2); cache.Process(empty,budget);
                            using (var current = cache.Acquire(empty)) Assert.That(current.Count, Is.Zero);
                            Assert.That(cache.CachedCount, Is.Zero); Assert.That(retained != null, Is.True);
                        }
                    }
                }
            }
        }
        [Test] public void ForeignSnapshotsAreRejectedBeforeAllocatingOrPublishing()
        {
            using (var registry = new OcclusionWorld())
            using (var foreign = new OcclusionWorld())
            using (var cache = new OcclusionGpuGeometryCache(registry))
            using (var snapshot = foreign.AcquireSnapshot())
            {
                var budget = new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1);
                Assert.Throws<ArgumentException>(() => cache.Process(snapshot,budget));
                Assert.Throws<ArgumentException>(() => cache.Acquire(snapshot)); Assert.That(cache.CachedCount, Is.Zero);
            }
        }
    }
}
