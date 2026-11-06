using System;
using System.Collections;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    public class DynamicOwnershipTests
    {
        private sealed class Stand : IDisposable
        {
            internal readonly World World = World.DefaultGameObjectInjectionWorld;
            internal readonly OcclusionWorld Registry = new OcclusionWorld();
            internal readonly DynamicOcclusionBindings Dynamic;
            internal readonly EcsRenderVisibilityAdapter Legacy;
            internal readonly Mesh Mesh;
            internal readonly GeometryHandle Geometry;
            internal readonly Entity First, Second;
            internal Stand()
            {
                Mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                Geometry = Registry.RegisterGeometry(Mesh);
                First = Create(); Second = Create();
                Dynamic = new DynamicOcclusionBindings(World, Registry); Legacy = new EcsRenderVisibilityAdapter(World);
            }
            private Entity Create()
            {
                var e = World.EntityManager.CreateEntity(typeof(MaterialMeshInfo), typeof(LocalToWorld));
                World.EntityManager.SetComponentData(e, new LocalToWorld { Value = float4x4.identity }); return e;
            }
            internal void Bind(Entity entity) => Dynamic.Bind(entity, new OcclusionInstanceDescriptor { Geometry = Geometry,
                State = new OcclusionInstanceState { Roles = OcclusionRole.Occludee, Color = new float4(1), Opaque = true } });
            internal void Publish(params Entity[] entities)
            {
                var frame = CullingFrameState.Create(entities.Length, 0, 1, 0, Allocator.TempJob);
                var volumes = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = entities.Length } }, Allocator.TempJob);
                var rows = new NativeArray<RenderGroupBinding>(entities.Length, Allocator.TempJob);
                try
                {
                    frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var key = new CullingGroupKey(new uint4(987, 0, 0, 0), (ushort)i);
                        frame.Groups[i] = new CullingGroupSnapshot { Key = key, StateGeneration = 1 };
                        rows[i] = new RenderGroupBinding { Group = key, Main = entities[i], InitialRendererEnabled = true,
                            ToggleMode = CullingRenderToggleMode.ShadowcastMode, OriginalShadowMode = ShadowCastingMode.Off };
                    }
                    Legacy.Publish(in frame, volumes, rows);
                }
                finally { rows.Dispose(); volumes.Dispose(); frame.Dispose(); }
            }
            public void Dispose()
            {
                Legacy.Dispose(); Dynamic.Dispose(); Registry.Dispose();
                World.EntityManager.CompleteAllTrackedJobs();
                if (World.EntityManager.Exists(First)) World.EntityManager.DestroyEntity(First);
                if (World.EntityManager.Exists(Second)) World.EntityManager.DestroyEntity(Second);
                UnityEngine.Object.Destroy(Mesh);
            }
        }

        [UnityTest] public IEnumerator LegacyPublicationRejectsDynamicOwnerBeforeChangingGraphics()
        {
            using (var s = new Stand())
            {
                s.World.EntityManager.SetComponentEnabled<MaterialMeshInfo>(s.First, false); s.Bind(s.First);
                Assert.Throws<InvalidOperationException>(() => s.Publish(s.First));
                Assert.That(s.World.EntityManager.IsComponentEnabled<MaterialMeshInfo>(s.First), Is.False);
            }
            yield return null;
        }
        [UnityTest] public IEnumerator DynamicRegistrationRejectsPublishedLegacyOwnerWithoutLeaking()
        {
            using (var s = new Stand())
            {
                s.Publish(s.First);
                Assert.Throws<InvalidOperationException>(() => s.Bind(s.First));
                Assert.That(s.Dynamic.Count, Is.Zero);
                using (var snapshot = s.Registry.AcquireSnapshot()) Assert.That(snapshot.Instances.Length, Is.Zero);
                s.Legacy.Dispose(); Assert.DoesNotThrow(() => s.Bind(s.First));
            }
            yield return null;
        }
        [UnityTest] public IEnumerator RejectedPublicationPreservesPreviousClaimsAndDoesNotReserveNewOnes()
        {
            using (var s = new Stand())
            {
                s.Publish(s.First); s.Bind(s.Second);
                Assert.Throws<InvalidOperationException>(() => s.Publish(s.Second));
                using (var other = new GraphicsVisibilityOwner(s.World))
                {
                    Assert.Throws<InvalidOperationException>(() => other.Acquire(s.First));
                    s.Dynamic.Unbind(s.Second); Assert.DoesNotThrow(() => other.Acquire(s.Second));
                    s.Legacy.Dispose(); Assert.DoesNotThrow(() => other.Acquire(s.First));
                }
            }
            yield return null;
        }
    }
}
