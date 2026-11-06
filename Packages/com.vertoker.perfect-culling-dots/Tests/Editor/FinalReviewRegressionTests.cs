using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Editor;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Editor.Baking.Jobs;
using PerfectCullingDOTS.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PerfectCullingDOTS.Tests
{
    public class FinalReviewRegressionTests
    {
        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
        public void DisabledAuthoringIsExcludedByInstalledBakingPipeline(bool volumeEnabled, bool portalEnabled)
        {
            var id = new uint4(41, 2, 3, 4);
            using var imported = LegacyBakeImporter.Import(new LegacyBakeSource { VolumeId = id, DeclaredGroupCount = 1,
                Layout = new CullingBakeLayout { CellCount = new float3(1), CellSize = new float3(2), BakeOrientation = quaternion.identity },
                Cells = new[] { new LegacyVisibilityCell { EncodedLength = 0 } }
            }, new[] { new CullingGroupKey(id, 0) });
            var root = new GameObject("disabled authoring regression");
            using var store = new BlobAssetStore(128);
            using var world = new World("Disabled authoring", WorldFlags.Conversion);
            try
            {
                var volume = root.AddComponent<CullingVolumeAuthoring>(); volume.Bake = imported.Asset; volume.enabled = volumeEnabled;
                var portal = root.AddComponent<CullingPortalAuthoring>(); portal.Volume = volume; portal.enabled = portalEnabled;
                var settingsType = typeof(Baker<>).Assembly.GetType("Unity.Entities.BakingSettings", true);
                var settings = Activator.CreateInstance(settingsType); settingsType.GetProperty("BlobAssetStore").SetValue(settings, store);
                typeof(Baker<>).Assembly.GetType("Unity.Entities.BakingUtility", true).GetMethod("BakeGameObjects", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { world, new[] { root }, settings });
                var em = world.EntityManager;
                using var volumes = em.CreateEntityQuery(typeof(CullingVolume));
                using var portals = em.CreateEntityQuery(typeof(CullingPortalLink));
                Assert.That(volumes.CalculateEntityCount(), Is.EqualTo(volumeEnabled ? 1 : 0), "Disabled authoring must not become active culling.");
                Assert.That(portals.CalculateEntityCount(), Is.EqualTo(portalEnabled ? 1 : 0));
                using var all = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<CullingVolume>() }, Options = EntityQueryOptions.IgnoreComponentEnabledState | EntityQueryOptions.IncludeDisabledEntities });
                Assert.That(all.CalculateEntityCount(), Is.EqualTo(volumeEnabled ? 1 : 0), "The installed pipeline must omit disabled authoring, even when disabled entities are included.");
                var capture = world.GetOrCreateSystemManaged<CullingViewCaptureSystem>();
                using var frame = capture.Capture(1);
                Assert.That(frame.Input.Volumes.Length, Is.EqualTo(volumeEnabled ? 1 : 0));
                Assert.That(frame.Input.Portals.Length, Is.EqualTo(volumeEnabled && portalEnabled ? 1 : 0));
                Assert.That(root.activeSelf, Is.True);

            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(imported.Asset); }
        }

        [Test]
        public void ComputeSkinnedGroupHasIndependentlyVisibleStaticPoseOnCpuAndGpu()
        {
            var root = new GameObject("skinned visibility regression");
            var mesh = new Mesh { vertices = new[] { new Vector3(-4,-4,2), new Vector3(-4,4,2), new Vector3(4,4,2), new Vector3(4,-4,2) },
                triangles = new[] { 0,1,2,0,2,3 }, bindposes = new[] { Matrix4x4.identity } };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 4).ToArray();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var bone = new GameObject("bone"); bone.transform.SetParent(root.transform, false);
                var skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.sharedMaterial = material; skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform;
                using var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest { Profile = BakeInputProfile.ComputeSoftwareInput,
                    Groups = new[] { new BakeGroupSource { Key = new CullingGroupKey(new uint4(41,2,3,4),0), Renderers = new Renderer[] { skin } } },
                    Samples = new[] { new BakeSampleLocation() }, Rays = new BakeRaySettings { Width = 3, Height = 3, Far = 10, CellSize = new float3(2), Rotation = quaternion.identity } });
                using var cpu = new SourceComputeJobsBackend().Begin(snapshot);
                CollectionAssert.AreEqual(new ushort[] { 0 }, cpu.Complete()[0].Groups, "Front-facing plane at z2 must be visible to the central +Z ray.");
                using var gpu = new PerfectCullingDOTS.Editor.Baking.Gpu.ComputeBakeBackend().Begin(snapshot);
                CollectionAssert.AreEqual(new ushort[] { 0 }, gpu.Complete()[0].Groups);
                Assert.That(root.GetComponent<MeshFilter>(), Is.Null); Assert.That(skin.sharedMesh, Is.SameAs(mesh));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }

        [Test]
        public void CpuStackOverflowIsReportedBySafetyOffBurstWorker()
        {
            var field = typeof(CwbvhRayTraversalJob).GetField("TileErrors");
            Assert.That(field, Is.Not.Null, "A Burst worker must report overflow before the session can publish.");
            const int depth = 512;
            using var nodes = new NativeArray<uint4>((2 * depth + 1) * 5, Allocator.TempJob);
            var writableNodes = nodes;
            // Acyclic binary chain: both child boxes contain the origin; +++ rays
            // descend the first child and retain one sibling at every level.
            for (int i = 0; i < depth; i++)
            {
                int node = (i == 0 ? 0 : 2 * i - 1) * 5;
                writableNodes[node] = new uint4(math.asuint(new float3(-1)), 0x037f7f7fu);
                writableNodes[node + 1] = new uint4((uint)(2 * i + 1), 0, 0x00003938u, 0);
                writableNodes[node + 3] = new uint4(0, 0, 0x00000202u, 0);
                writableNodes[node + 4] = new uint4(0x00000202u, 0, 0x00000202u, 0);
            }
            using var triangles = new NativeArray<float4>(0, Allocator.TempJob);
            using var records = new NativeArray<float4>(0, Allocator.TempJob);
            using var instances = new NativeArray<CwbvhBakeInstance>(1, Allocator.TempJob);
            using var samples = new NativeArray<BakeSampleLocation>(1, Allocator.TempJob);
            using var bits = new NativeArray<uint>(1, Allocator.TempJob);
            using var neighbors = new NativeArray<uint>(1, Allocator.TempJob);
            using var burst = new NativeArray<byte>(1, Allocator.TempJob);
            using var errors = new NativeArray<byte>(1, Allocator.TempJob);
            object boxed = new CwbvhRayTraversalJob { Nodes = nodes.AsReadOnly(), BlasTriangles = triangles.AsReadOnly(), TlasInstances = records.AsReadOnly(),
                Instances = instances.AsReadOnly(), Samples = samples.AsReadOnly(), TileBits = bits, TileNeighbors = neighbors, BurstFlags = burst,
                OpaqueRoot = 0, TransparentRoot = -1, GroupCount = 1, Words = 1, TilesPerFace = 1,
                Settings = new BakeRaySettings { Width = 3, Height = 3, Far = 10, CellSize = new float3(2), Rotation = quaternion.identity } };
            field.SetValue(boxed, errors);
            new SafetyOffTraversal { Inner = (CwbvhRayTraversalJob)boxed }.Schedule().Complete();
            Assert.That(burst[0], Is.EqualTo(1), "The safety-off regression must actually execute in Burst.");
            Assert.That(errors[0], Is.EqualTo(1), "Deep input must fault explicitly, without unchecked writes or a partial success.");
        }

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        private struct SafetyOffTraversal : IJob
        {
            public CwbvhRayTraversalJob Inner;
            public void Execute() => Inner.Execute(0);
        }

        [Test]
        public void WorkerFailureFaultsWholeSessionAndReleasesUnpublishedResults()
        {
            using var snapshot = MeshSnapshotExtractor.Capture(new BakeSnapshotRequest { Profile = BakeInputProfile.ComputeSoftwareInput,
                Groups = Array.Empty<BakeGroupSource>(), Samples = new[] { new BakeSampleLocation() },
                Rays = new BakeRaySettings { Width = 3, Height = 3, Far = 10, CellSize = new float3(2), Rotation = quaternion.identity } });
            using var session = new SourceComputeJobsBackend().Begin(snapshot);
            var type = typeof(BakeJobHandle); var field = type.GetField("tileErrors", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            session.Tick();
            ((JobHandle)type.GetField("dependency", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(session)).Complete();
            var errors = (NativeArray<byte>)field.GetValue(session); errors[0] = 1;
            Assert.Throws<InvalidOperationException>(() => session.Complete());
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Faulted));
            Assert.That(((BakeJobHandle)session).NativeBytes, Is.Zero);
            Assert.That(session.Progress, Is.LessThan(1));
        }
    }
}
