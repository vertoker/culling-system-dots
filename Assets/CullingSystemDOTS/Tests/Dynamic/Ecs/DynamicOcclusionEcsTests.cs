using System;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Ecs.Tests
{
    public class DynamicOcclusionEcsTests
    {
        private World world;
        private DynamicOcclusionSystem system;
        private Mesh mesh;
        private GeometryHandle geometry;
        private struct Logic : IComponentData { public int Tick; }
        [SetUp] public void SetUp()
        {
            world = new World("dynamic capture tests");
            system = world.GetOrCreateSystemManaged<DynamicOcclusionSystem>();
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            geometry = system.Registry.RegisterGeometry(mesh);
        }
        [TearDown] public void TearDown()
        {
            world?.Dispose();
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
        }
        private Entity Create()
        {
            var e = world.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(MaterialMeshInfo), typeof(Logic));
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.identity });
            world.EntityManager.SetComponentData(e, new Logic { Tick = 42 });
            return e;
        }
        private InstanceHandle Bind(Entity entity, OcclusionRole roles = OcclusionRole.Occludee) =>
            system.Bindings.Bind(entity, new OcclusionInstanceDescriptor { Geometry = geometry,
                State = new OcclusionInstanceState { Roles = roles, Opaque = true, Color = new float4(1) } });

        [Test] public void BindingCapturesCurrentTransformWhileStockGraphicsAreOwned()
        {
            var e = Create(); Bind(e);
            Assert.That(system.Bindings.Count, Is.EqualTo(1));
            system.Bindings.SetGraphicsOwned(e, true);
            Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(e), Is.False);
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.Translate(new float3(7, 0, 0)) });
            system.Update();
            using (var snapshot = system.Registry.AcquireSnapshot())
                Assert.That(snapshot.Instances[0].WorldBounds.center.x, Is.EqualTo(7.5f));
            Assert.That(world.EntityManager.IsEnabled(e), Is.True);
            Assert.That(world.EntityManager.GetComponentData<Logic>(e).Tick, Is.EqualTo(42));
            system.Bindings.Unbind(e);
            Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(e), Is.True);
        }
        [Test] public void RemovingOldEntityDoesNotRestoreOrBindAReplacementVersion()
        {
            var old = Create(); Bind(old); system.Bindings.SetGraphicsOwned(old, true);
            world.EntityManager.DestroyEntity(old);
            var replacement = Create(); world.EntityManager.SetComponentEnabled<MaterialMeshInfo>(replacement, false);
            system.Update();
            Assert.That(system.Bindings.Count, Is.Zero);
            Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(replacement), Is.False);
            Assert.That(world.EntityManager.HasComponent<DynamicOcclusionBinding>(replacement), Is.False);
            using (var snapshot = system.Registry.AcquireSnapshot()) Assert.That(snapshot.Instances.Length, Is.Zero);
        }
        [Test] public void DisabledOptInRestoresStockRenderingAndWithdrawsOccluder()
        {
            var e = Create(); Bind(e, OcclusionRole.Occluder); system.Update(); system.Bindings.SetGraphicsOwned(e, true);
            world.EntityManager.SetComponentEnabled<DynamicOcclusionBinding>(e, false); system.Update();
            Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(e), Is.True);
            using (var snapshot = system.Registry.AcquireSnapshot())
                Assert.That(snapshot.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
        }
        [Test] public void InvalidTransformWithdrawsOldOccluderAndKeepsEntityAlive()
        {
            var e = Create(); Bind(e, OcclusionRole.Occluder); system.Update(); var epoch = system.Registry.OccluderEpoch;
            var value = float4x4.identity; value.c3.x = float.NaN;
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = value });
            Assert.DoesNotThrow(() => system.Update());
            using (var snapshot = system.Registry.AcquireSnapshot())
            {
                Assert.That(snapshot.Instances[0].Prepared, Is.Zero);
                Assert.That(snapshot.Instances[0].Roles & OcclusionRole.Occluder, Is.EqualTo(OcclusionRole.None));
            }
            Assert.That(system.Registry.OccluderEpoch, Is.GreaterThan(epoch));
            Assert.That(world.EntityManager.IsEnabled(e), Is.True);
        }
        [Test] public void GraphicsOwnersRejectOverlappingBatchesAtomically()
        {
            var first = Create(); var second = Create();
            using (var a = new GraphicsVisibilityOwner(world))
            using (var b = new GraphicsVisibilityOwner(world))
            using (var c = new GraphicsVisibilityOwner(world))
            {
                a.RetainOnly(new[] { first });
                Assert.Throws<InvalidOperationException>(() => b.RetainOnly(new[] { second, first }));
                Assert.That(b.Count, Is.Zero);
                c.RetainOnly(new[] { second }); Assert.That(c.Count, Is.EqualTo(1));
            }
        }
        [Test] public void GraphicsOwnerReleaseAllowsAnotherOwner()
        {
            var e = Create(); var a = new GraphicsVisibilityOwner(world);
            using (var b = new GraphicsVisibilityOwner(world))
            {
                a.RetainOnly(new[] { e });
                Assert.Throws<InvalidOperationException>(() => b.EnsureAvailable(new[] { e }));
                a.Dispose(); b.RetainOnly(new[] { e }); Assert.That(b.Count, Is.EqualTo(1));
            }
        }

        [Test] public void UnchangedCaptureDoesNotQueueNewGeometryOrBounds()
        {
            var e = Create(); Bind(e, OcclusionRole.Occluder); system.Update();
            ulong revision = system.Registry.Revision, epoch = system.Registry.OccluderEpoch;
            system.Update(); system.Update();
            Assert.That(system.Registry.Revision, Is.EqualTo(revision));
            Assert.That(system.Registry.OccluderEpoch, Is.EqualTo(epoch));
            Assert.That(system.Registry.PendingUpdateCount, Is.Zero);
        }

        [Test] public void CaptureRecoversAfterInvalidTransformAndKeepsOriginalDisabledBaseline()
        {
            var e = Create(); world.EntityManager.SetComponentEnabled<MaterialMeshInfo>(e, false); Bind(e, OcclusionRole.Occluder);
            system.Update(); system.Bindings.SetGraphicsOwned(e, true);
            var value = float4x4.identity; value.c3.x = float.NaN;
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = value }); system.Update();
            Assert.That(system.Bindings.LastFault, Is.Not.Null);
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.identity }); system.Update();
            Assert.That(system.Bindings.LastFault, Is.Null);
            system.Bindings.Unbind(e);
            Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(e), Is.False);
        }

        [Test] public void RejectedRegistrationReleasesItsGraphicsClaim()
        {
            var e = Create();
            Assert.Throws<ArgumentException>(() => system.Bindings.Bind(e, new OcclusionInstanceDescriptor {
                Geometry = geometry, State = new OcclusionInstanceState { Roles = OcclusionRole.Occluder, Opaque = false } }));
            using (var next = new GraphicsVisibilityOwner(world)) Assert.DoesNotThrow(() => next.Acquire(e));
            Assert.That(world.EntityManager.HasComponent<DynamicOcclusionBinding>(e), Is.False);
            using (var snapshot = system.Registry.AcquireSnapshot()) Assert.That(snapshot.Instances.Length, Is.Zero);
        }

        [Test] public void WorldTeardownRestoresAndReleasesBindingsWithoutTouchingSimulation()
        {
            var e = Create(); Bind(e); system.Bindings.SetGraphicsOwned(e, true);
            Assert.DoesNotThrow(() => world.Dispose()); world = null;
        }

        [Test] public void ConfigurationIsValidatedBeforeReplacingAnUnusedRegistry()
        {
            using (var configuredWorld = new World("configured capture tests"))
            {
                var configured = configuredWorld.GetOrCreateSystemManaged<DynamicOcclusionSystem>();
                var original = configured.Registry;
                Assert.Throws<ArgumentOutOfRangeException>(() => configured.Configure(new OcclusionSettings { MaxInstances = 0 }));
                Assert.That(configured.Registry, Is.SameAs(original));
                configured.Configure(new OcclusionSettings { MaxInstances = 1 });
                var g = configured.Registry.RegisterGeometry(mesh);
                var first = configuredWorld.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(MaterialMeshInfo));
                var second = configuredWorld.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(MaterialMeshInfo));
                configuredWorld.EntityManager.SetComponentData(first, new LocalToWorld { Value = float4x4.identity });
                configuredWorld.EntityManager.SetComponentData(second, new LocalToWorld { Value = float4x4.identity });
                var descriptor = new OcclusionInstanceDescriptor { Geometry = g, State = new OcclusionInstanceState { Roles = OcclusionRole.Occludee } };
                configured.Bindings.Bind(first, descriptor);
                Assert.Throws<InvalidOperationException>(() => configured.Bindings.Bind(second, descriptor));
                Assert.Throws<InvalidOperationException>(() => configured.Configure(new OcclusionSettings()));
            }
        }

        [Test] public void CaptureBudgetExhaustionWithdrawsAndRecoversWithoutLosingTheBaseline()
        {
            var e = Create(); Bind(e, OcclusionRole.Occluder); system.Update(); system.Bindings.SetGraphicsOwned(e, true);
            var leases = new System.Collections.Generic.List<OcclusionSnapshotLease>();
            try
            {
                // A bounded low-budget registry is exercised through the public direct binding API.
                using (var registry = new OcclusionWorld(new OcclusionSettings { MaxResourceBytes = 1024 }))
                using (var bindings = new DynamicOcclusionBindings(world, registry))
                {
                    var other = Create(); var g = registry.RegisterGeometry(mesh);
                    bindings.Bind(other, new OcclusionInstanceDescriptor { Geometry = g, State = new OcclusionInstanceState { Roles = OcclusionRole.Occluder, Opaque = true } });
                    registry.ProcessUpdates(new OcclusionUpdateBudget(1024)); bindings.SetGraphicsOwned(other, true);
                    while (registry.AccountedResourceBytes <= 1024 - 160)
                    {
                        try { leases.Add(registry.AcquireSnapshot()); }
                        catch (InvalidOperationException) { break; }
                    }
                    bindings.Capture();
                    Assert.That(bindings.LastFault, Is.Not.Null);
                    Assert.That(world.EntityManager.IsComponentEnabled<MaterialMeshInfo>(other), Is.True);
                    foreach (var lease in leases) lease.Dispose(); leases.Clear();
                    bindings.Capture(); Assert.That(bindings.LastFault, Is.Null);
                }
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
        }

        [TestCase(0.37f, 1.3f)]
        [TestCase(-1.2f, 0.0001f)]
        [TestCase(2.7f, 100f)]
        public void RotatedScaledCaptureProducesValidEnclosingBounds(float angle, float scale)
        {
            var e = Create(); Bind(e, OcclusionRole.Occluder);
            var matrix = float4x4.TRS(new float3(-21.7f, 8.35f, 19.33f), quaternion.EulerXYZ(angle, angle * 0.7f, angle * 0.3f), new float3(scale, scale * 0.7f, scale * 1.1f));
            world.EntityManager.SetComponentData(e, new LocalToWorld { Value = matrix }); system.Update();
            Assert.That(system.Bindings.LastFault, Is.Null);
            using (var snapshot = system.Registry.AcquireSnapshot()) Assert.That(snapshot.Instances[0].Prepared, Is.EqualTo(1));
        }
    }
}
