using NUnit.Framework;
using PerfectCullingDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace PerfectCullingDOTS.Tests
{
    public class ViewCaptureTests
    {
        [Test]
        public void CaptureKeepsBakedDimensionsAndExplicitPassOrder()
        {
            using var source = new EcsRuntimeTests.Snapshot();
            using var world = new World("Captured ECS volume");
            var cameraObject = new GameObject("Captured camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(4, 5, 6);
            try
            {
                var manager = world.EntityManager;
                var volume = manager.CreateEntity(typeof(CullingVolume), typeof(LocalTransform), typeof(LocalToWorld));
                manager.SetComponentData(volume, new CullingVolume { Bake = source.Input.Volumes[0].Bake, InstanceId = new uint4(44, 0, 0, 0), Size = new float3(99), Layers = 7 });
                manager.SetComponentData(volume, LocalTransform.FromPositionRotationScale(new float3(3, 4, 5), quaternion.RotateY(.25f), 3));
                manager.SetComponentData(volume, new LocalToWorld { Value = float4x4.TRS(new float3(3, 4, 5), quaternion.RotateY(.25f), new float3(3)) });
                for (int g = 1; g >= 0; g--)
                {
                    var entity = manager.CreateEntity(typeof(CullingGroupDefinition));
                    manager.SetComponentData(entity, new CullingGroupDefinition { Volume = volume, GroupIndex = (ushort)g, StateGeneration = 1, VertexCount = g + 2 });
                    manager.AddBuffer<CullingBakedRenderBinding>(entity);
                }
                var capture = world.GetOrCreateSystemManaged<CullingViewCaptureSystem>();
                var view = capture.RegisterView(camera, 21); view.NeighborRadius = 2; view.Layers = 4;
                using var frame = capture.Capture(5, 999, 21, 21);
                Assert.That(frame.Input.Volumes.IsCreated, Is.True);
                Assert.That(frame.Input.Volumes.Length, Is.EqualTo(1));
                Assert.That(frame.Input.Volumes[0].Grid.CellCount, Is.EqualTo(new float3(2, 1, 1)), "Current volume size must not regenerate baked dimensions.");
                Assert.That(frame.Input.Volumes[0].Grid.Size, Is.EqualTo(new float3(99)));
                Assert.That(frame.Input.Volumes[0].Grid.Origin, Is.EqualTo(new float3(3, 4, 5)));
                Assert.That(frame.Input.Groups[0].Key.GroupIndex, Is.Zero); Assert.That(frame.Input.Groups[1].Key.GroupIndex, Is.EqualTo(1));
                Assert.That(frame.Input.Groups[0].Key.VolumeId, Is.EqualTo(new uint4(44, 0, 0, 0)));
                Assert.That(frame.Input.Views[0].Position, Is.EqualTo(new float3(4, 5, 6)));
                Assert.That(frame.Input.Views[0].NeighborRadius, Is.EqualTo(2));
                Assert.That(frame.Input.Passes[0].ViewIndex, Is.EqualTo(-1));
                Assert.That(frame.Input.Passes[1].ViewIndex, Is.Zero); Assert.That(frame.Input.Passes[2].ViewIndex, Is.Zero);
                capture.UnregisterView(21);
                using var disabled = capture.Capture(5, 21);
                Assert.That(disabled.Input.Views.Length, Is.Zero);
                Assert.That(disabled.Input.RestoreAllVisibility, Is.True);
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }
    }
}
