using System.Collections;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Rendering;
using PerfectCullingDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Material = UnityEngine.Material;

namespace PerfectCullingDOTS.Rendering.Tests
{
    public struct RenderLogicProbe : IComponentData { public int Ticks; }

    [DisableAutoCreation]
    public partial class RenderLogicProbeSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            foreach (var probe in SystemAPI.Query<RefRW<RenderLogicProbe>>()) probe.ValueRW.Ticks++;
        }
    }

    public class RenderSimulationParityTests
    {
        [UnityTest]
        public IEnumerator GraphicsToggleKeepsPhysicsAndLogicAlive()
        {
            // Own the World so cached PhysicsWorld collider pointers die before the owned blob.
            var world = new World("PVS physics and logic gate", WorldFlags.Game);
            var collider = Unity.Physics.SphereCollider.Create(new SphereGeometry { Radius = 0.5f },
                CollisionFilter.Default, Unity.Physics.Material.Default);
            var frame = CullingFrameState.Create(1, 1, 1, 1, Allocator.Persistent);
            var volumes = new NativeArray<RenderVolumeRange>(new[] { new RenderVolumeRange { GroupCount = 1 } }, Allocator.Persistent);
            var bindings = new NativeArray<RenderGroupBinding>(1, Allocator.Persistent);
            EcsRenderVisibilityAdapter adapter = null;
            CullingWorldSystem cullingOwner = null;
            var meshSource = GameObject.CreatePrimitive(PrimitiveType.Cube); meshSource.SetActive(false);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", Color.red);
            var cameraObject = new GameObject("Physics gate camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(0, 2, -8);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(256, 256, 24); target.Create(); camera.targetTexture = target;
            try
            {
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world,
                    DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default));
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                var logic = world.GetOrCreateSystemManaged<RenderLogicProbeSystem>();
                var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
                simulation.AddSystemToUpdateList(logic); simulation.SortSystems();
                var manager = world.EntityManager;
                var body = manager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(PhysicsCollider),
                    typeof(PhysicsMass), typeof(PhysicsVelocity), typeof(PhysicsGravityFactor), typeof(RenderLogicProbe));
                manager.AddSharedComponent(body, new PhysicsWorldIndex { Value = 0 });
                manager.SetComponentData(body, LocalTransform.FromPosition(0, 2, 0));
                manager.SetComponentData(body, new PhysicsCollider { Value = collider });
                manager.SetComponentData(body, PhysicsMass.CreateDynamic(collider.Value.MassProperties, 1));
                manager.SetComponentData(body, new PhysicsGravityFactor { Value = 1 });
                RenderMeshUtility.AddComponents(body, manager, new RenderMeshDescription(ShadowCastingMode.On, layer: 31),
                    new RenderMeshArray(new[] { material }, new[] { meshSource.GetComponent<MeshFilter>().sharedMesh }),
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                for (int i = 0; i < 3; i++) yield return null;
                float beforeY = manager.GetComponentData<LocalTransform>(body).Position.y;
                int beforeTicks = manager.GetComponentData<RenderLogicProbe>(body).Ticks;
                var key = new CullingGroupKey(new uint4(1, 0, 0, 0), 0);
                frame.Groups[0] = new CullingGroupSnapshot { Key = key, StateGeneration = 1, InitialApplied = 1 };
                frame.Result[0] = new CullingEvaluationResult { Status = CullingEvaluationStatus.Success };
                frame.Passes[0] = new CullingViewPass { RenderViewId = 1, ViewIndex = 0 };
                frame.PassStates[0] = 0; frame.PassVolumeApplications[0] = 1;
                cullingOwner = world.GetOrCreateSystemManaged<CullingWorldSystem>();
                var companion = cullingOwner.ShadowCompanions.GetOrCreate(body);
                Assert.That(manager.HasComponent<PhysicsCollider>(companion), Is.False);
                Assert.That(manager.HasComponent<PhysicsMass>(companion), Is.False);
                Assert.That(manager.HasComponent<PhysicsVelocity>(companion), Is.False);
                Assert.That(manager.HasComponent<RenderLogicProbe>(companion), Is.False);
                bindings[0] = new RenderGroupBinding { Group = key, Main = body, ShadowOnly = companion, OriginalShadowMode = ShadowCastingMode.On,
                    ToggleMode = CullingRenderToggleMode.ShadowcastMode, InitialRendererEnabled = true };
                adapter = cullingOwner.GetOrCreateRenderingAdapter(); adapter.RegisterCamera(camera, 1);
                for (int i = 0; i < 20; i++) { adapter.Publish(in frame, volumes, bindings); yield return null; }
                Assert.That(manager.GetComponentData<LocalTransform>(body).Position.y, Is.LessThan(beforeY), "Real Unity Physics gravity stopped while graphics were hidden.");
                Assert.That(manager.GetComponentData<PhysicsVelocity>(body).Linear.y, Is.LessThan(0));
                Assert.That(manager.GetComponentData<RenderLogicProbe>(body).Ticks, Is.GreaterThan(beforeTicks), "Normal ECS logic stopped while hidden.");
                Assert.That(manager.IsEnabled(body), Is.True);
                Assert.That(manager.IsComponentEnabled<MaterialMeshInfo>(body), Is.True);
                Assert.That(manager.GetComponentData<LocalToWorld>(companion).Value, Is.EqualTo(manager.GetComponentData<LocalToWorld>(body).Value), "Graphics-only child did not follow the moving physics body.");
                Assert.That(adapter.LastFault, Is.Null);
                var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    foreach (var pixel in image.GetPixels32()) Assert.That(pixel, Is.EqualTo(new Color32(0, 0, 0, 255)), "Hidden physics body still rendered.");
                }
                finally { RenderTexture.active = previous; Object.Destroy(image); }
            }
            finally
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world); world.Dispose(); collider.Dispose();
                if (adapter != null) Assert.That(adapter.IsDisposed, Is.True, "World destruction must detach its global render callbacks.");
                if (cullingOwner != null) Assert.That(cullingOwner.ShadowCompanions.Count, Is.Zero);
                frame.Dispose(); volumes.Dispose(); bindings.Dispose();
                camera.targetTexture = null; cameraObject.SetActive(false);
                Object.Destroy(cameraObject); Object.Destroy(meshSource); Object.Destroy(material); target.Release(); Object.Destroy(target);
            }
        }
    }
}
