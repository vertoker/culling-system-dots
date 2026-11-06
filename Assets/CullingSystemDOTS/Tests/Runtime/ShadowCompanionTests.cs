using NUnit.Framework;
using CullingSystemDOTS.Rendering;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Tests
{
    public class ShadowCompanionTests
    {
        private struct SimulationOnly : IComponentData { public int Value; }

        [Test]
        public void CompanionCopiesGraphicsAndFollowsMainWithoutDuplicatingSimulation()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            using var world = new World("Graphics-only companion");
            var manager = world.EntityManager;
            var owner = world.GetOrCreateSystemManaged<CullingWorldSystem>();
            try
            {
                var main = manager.CreateEntity(typeof(SimulationOnly));
                var child = manager.CreateEntity(typeof(SimulationOnly));
                var hierarchy = manager.AddBuffer<LinkedEntityGroup>(main);
                hierarchy.Add(new LinkedEntityGroup { Value = main }); hierarchy.Add(new LinkedEntityGroup { Value = child });
                manager.SetComponentData(main, new SimulationOnly { Value = 19 });
                RenderMeshUtility.AddComponents(main, manager, new RenderMeshDescription(ShadowCastingMode.On),
                    new RenderMeshArray(new[] { material }, new[] { cube.GetComponent<MeshFilter>().sharedMesh }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                var transform = float4x4.TRS(new float3(2, 3, 4), quaternion.RotateY(.5f), new float3(2, 3, 4));
                manager.SetComponentData(main, new LocalToWorld { Value = transform });
                var companion = owner.ShadowCompanions.GetOrCreate(main);
                Assert.That(companion, Is.Not.EqualTo(Entity.Null));
                Assert.That(companion, Is.Not.EqualTo(main));
                Assert.That(owner.ShadowCompanions.GetOrCreate(main), Is.EqualTo(companion));
                Assert.That(owner.ShadowCompanions.Count, Is.EqualTo(1));
                Assert.That(manager.HasComponent<SimulationOnly>(companion), Is.False);
                Assert.That(manager.HasComponent<CullingGroupState>(companion), Is.False);
                Assert.That(manager.HasComponent<LinkedEntityGroup>(companion), Is.False);
                using (var query = manager.CreateEntityQuery(typeof(SimulationOnly))) Assert.That(query.CalculateEntityCount(), Is.EqualTo(2), "Gameplay children must not be cloned.");
                Assert.That(manager.GetComponentData<SimulationOnly>(main).Value, Is.EqualTo(19));
                Assert.That(manager.GetComponentData<Parent>(companion).Value, Is.EqualTo(main));
                Assert.That(manager.GetComponentData<LocalToWorld>(companion).Value, Is.EqualTo(transform));
                Assert.That(manager.GetSharedComponent<RenderFilterSettings>(companion).ShadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
                Assert.That(manager.GetComponentData<MaterialMeshInfo>(companion), Is.EqualTo(manager.GetComponentData<MaterialMeshInfo>(main)));
                Assert.That(manager.GetSharedComponentManaged<RenderMeshArray>(companion).GetHashCode(), Is.EqualTo(manager.GetSharedComponentManaged<RenderMeshArray>(main).GetHashCode()));
                Assert.That(manager.IsEnabled(main), Is.True);
                owner.ShadowCompanions.RetainOnly(new System.Collections.Generic.HashSet<Entity>());
                Assert.That(manager.Exists(companion), Is.False);
                var replacement = owner.ShadowCompanions.GetOrCreate(main);
                Assert.That(replacement, Is.Not.EqualTo(companion)); companion = replacement;
                manager.DestroyEntity(main);
                owner.ShadowCompanions.Prune();
                Assert.That(owner.ShadowCompanions.Count, Is.Zero);
                Assert.That(manager.Exists(companion), Is.False);
            }
            finally { Object.DestroyImmediate(cube); Object.DestroyImmediate(material); }
        }
    }
}
