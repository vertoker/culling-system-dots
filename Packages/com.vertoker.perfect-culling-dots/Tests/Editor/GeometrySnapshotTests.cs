using System;
using NUnit.Framework;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Editor.Baking;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PerfectCullingDOTS.Tests
{
    public class GeometrySnapshotTests
    {
        [TestCase(BakeInputProfile.NativeVulkanInput), TestCase(BakeInputProfile.ComputeSoftwareInput)]
        public void LowestNonEmptyLodKeepsTheBackendSpecificBackCopy(BakeInputProfile profile)
        {
            using var f = new Fixture(); var root = new GameObject("Snapshot LOD fixture");
            try
            {
                f.First.transform.parent = root.transform; f.Second.transform.parent = root.transform;
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.8f, Array.Empty<Renderer>()), new LOD(.4f, new Renderer[] { f.First }), new LOD(.1f, new Renderer[] { f.Second }) });
                var request = f.Request(profile, f.Second, f.First); request.LodGroups = new[] { lod };
                request.Policy = r => new BakeRendererPolicy { DoubleSided = true };
                using var snapshot = MeshSnapshotExtractor.Capture(request);
                Assert.That(snapshot.Instances.Length, Is.EqualTo(profile == BakeInputProfile.NativeVulkanInput ? 2 : 1));
                if (profile == BakeInputProfile.NativeVulkanInput)
                    Assert.That(snapshot.Instances[1].LocalToWorld.c3.xyz, Is.EqualTo(new float3(3, 4, 5)), "Native reverse copies are not listed in the original LODGroup and survive its filtering.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SkinnedPoseIsCopiedWithoutAddingMeshFilterAndComputeGroupPrefilterStaysExplicit()
        {
            using var f = new Fixture(); var root = new GameObject("Snapshot skin fixture"); root.SetActive(false);
            var mesh = Object.Instantiate(f.Mesh); var expected = new Mesh();
            try
            {
                mesh.boneWeights = EnumerableWeights(); mesh.bindposes = new[] { Matrix4x4.identity };
                var bone = new GameObject("Bone"); bone.transform.parent = root.transform; bone.transform.localPosition = Vector3.right;
                var skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.sharedMaterial = f.Material;
                skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform; skin.BakeMesh(expected);
                using var native = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.NativeVulkanInput, skin));
                Assert.That(native.Instances.Length, Is.EqualTo(1));
                for (int i = 0; i < 3; i++) Assert.That(native.Vertices[i], Is.EqualTo((float3)expected.vertices[i]));
                using var filtered = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.ComputeSoftwareInput, skin));
                Assert.That(filtered.Instances.Length, Is.Zero, "The source compute constructor filters group Skinned renderers without MeshFilter before SoftwareRT.");
                var additional = f.Request(BakeInputProfile.ComputeSoftwareInput); additional.AdditionalOccluders = new Renderer[] { skin };
                using var computeOccluder = MeshSnapshotExtractor.Capture(additional);
                Assert.That(computeOccluder.Instances.Length, Is.EqualTo(1));
                Assert.That(computeOccluder.Instances[0].GroupIndex, Is.EqualTo(-1));
                Assert.That(skin.GetComponent<MeshFilter>(), Is.Null);
                Assert.That(skin.sharedMesh, Is.EqualTo(mesh));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); Object.DestroyImmediate(expected); }
        }
        private static BoneWeight[] EnumerableWeights() => new[] {
            new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 }, new BoneWeight { boneIndex0 = 0, weight0 = 1 } };

        [Test]
        public void NativeDoubleSidedBackCopyFlattensMaterialsLikeSceneColor()
        {
            using var f = new Fixture();
            var transparent = new Material(f.Material) { renderQueue = 3000 };
            try
            {
                f.Mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
                f.Mesh.subMeshCount = 2; f.Mesh.SetTriangles(new[] { 0, 1, 2 }, 0); f.Mesh.SetTriangles(new[] { 0, 2, 3 }, 1);
                f.First.sharedMaterials = new[] { f.Material, transparent };
                f.First.gameObject.AddComponent<CullingBakeRendererTag>().DoubleSided = true;
                var original = f.Mesh.triangles;
                using var native = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.NativeVulkanInput, f.First));
                Assert.That(native.Instances.Length, Is.EqualTo(3), "Source creates two original submeshes and one flattened reversed clone.");
                Assert.That(native.Instances[0].Transparent, Is.Zero); Assert.That(native.Instances[1].Transparent, Is.EqualTo(1));
                var back = native.Instances[2];
                Assert.That(native.Geometry[back.Geometry].IndexCount, Is.EqualTo(6));
                Assert.That(back.Transparent, Is.Zero, "The source reversed mesh collapses to material0.");
                Assert.That(back.DoubleSided, Is.Zero, "Native input is explicit reversed geometry.");
                using var compute = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.ComputeSoftwareInput, f.First));
                Assert.That(compute.Instances.Length, Is.EqualTo(2));
                Assert.That(compute.Instances[0].DoubleSided, Is.EqualTo(1));
                CollectionAssert.AreEqual(original, f.Mesh.triangles);
                Assert.That(f.First.transform.childCount, Is.Zero, "Snapshot extraction must not clone scene GameObjects.");
            }
            finally { Object.DestroyImmediate(transparent); }
        }

        [Test]
        public void NativeOnlyAddsTheFirstEligibleDoubleSidedRendererPerGroup()
        {
            using var f = new Fixture(); var request = f.Request(BakeInputProfile.NativeVulkanInput, f.First, f.Second);
            request.Policy = r => new BakeRendererPolicy { DoubleSided = true };
            using var snapshot = MeshSnapshotExtractor.Capture(request);
            Assert.That(snapshot.Instances.Length, Is.EqualTo(3), "SceneColor breaks after adding one reversed renderer to each group.");
        }

        [Test]
        public void SnapshotSharesGeometryAndOwnsItsDataAfterUnityObjectsChange()
        {
            using var f = new Fixture();
            using var snapshot = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.ComputeSoftwareInput, f.First, f.Second));
            Assert.That(snapshot.Vertices.Length, Is.EqualTo(3));
            Assert.That(snapshot.Geometry.Length, Is.EqualTo(1));
            Assert.That(snapshot.Instances.Length, Is.EqualTo(2));
            Assert.That(snapshot.Groups[0].GroupIndex, Is.Zero);
            var old = snapshot.Instances[0].LocalToWorld;
            f.Mesh.vertices = new[] { Vector3.one * 10, Vector3.one * 20, Vector3.one * 30 };
            f.First.transform.position = Vector3.one * 99; f.Material.renderQueue = 3000;
            Assert.That(snapshot.Vertices[1], Is.EqualTo(new float3(1, 0, 0)));
            Assert.That(snapshot.Instances[0].LocalToWorld, Is.EqualTo(old));
            Assert.That(snapshot.Instances[0].Transparent, Is.Zero);
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
        public void NativeInputKeepsTheSourcePerAxisWindingCorrection(int negativeAxes)
        {
            using var f = new Fixture();
            var scale = new Vector3((negativeAxes & 1) == 0 ? 2 : -2, (negativeAxes & 2) == 0 ? 3 : -3, (negativeAxes & 4) == 0 ? 4 : -4);
            f.First.transform.localScale = scale;
            var lossy = f.First.transform.lossyScale;
            using var snapshot = MeshSnapshotExtractor.Capture(f.Request(BakeInputProfile.NativeVulkanInput, f.First));
            Assert.That(snapshot.Instances.Length, Is.EqualTo(1));
            var indices = new[] { 0, 1, 2 };
            if (lossy.x < 0) (indices[0], indices[2]) = (indices[2], indices[0]);
            if (lossy.y < 0) (indices[0], indices[1]) = (indices[1], indices[0]);
            if (lossy.z < 0) (indices[1], indices[2]) = (indices[2], indices[1]);
            for (int i = 0; i < 3; i++) Assert.That(snapshot.Indices[i], Is.EqualTo(indices[i]));
            Assert.That(snapshot.Vertices[1].x, Is.EqualTo(Mathf.Sign(lossy.x)));
            float4x4 expected = Matrix4x4.TRS(f.First.transform.position, f.First.transform.rotation, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            Assert.That(snapshot.Instances[0].LocalToWorld, Is.EqualTo(expected));
        }

        [Test]
        public void AdditionalOccluderNeverAcquiresAGroup()
        {
            using var f = new Fixture(); var request = f.Request(BakeInputProfile.ComputeSoftwareInput, f.First);
            request.AdditionalOccluders = new Renderer[] { f.Second };
            using var snapshot = MeshSnapshotExtractor.Capture(request);
            Assert.That(snapshot.Instances.Length, Is.EqualTo(2));
            Assert.That(snapshot.Groups.Length, Is.EqualTo(1));
            Assert.That(snapshot.Instances[0].GroupIndex, Is.Zero);
            Assert.That(snapshot.Instances[1].GroupIndex, Is.EqualTo(-1));
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly MeshRenderer First, Second;
            public Fixture()
            {
                Mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                Material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 2000 };
                First = Renderer("Snapshot first"); Second = Renderer("Snapshot second");
                Second.transform.position = new Vector3(3, 4, 5);
            }
            private MeshRenderer Renderer(string name)
            {
                var go = new GameObject(name); go.SetActive(false); go.AddComponent<MeshFilter>().sharedMesh = Mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Material; return renderer;
            }
            public BakeSnapshotRequest Request(BakeInputProfile profile, params Renderer[] renderers)
                => new BakeSnapshotRequest { Profile = profile, Groups = new[] { new BakeGroupSource {
                    Key = new CullingGroupKey(new uint4(1, 2, 3, 4), 0), Renderers = renderers } } };
            public void Dispose() { if (First != null) Object.DestroyImmediate(First.gameObject); if (Second != null) Object.DestroyImmediate(Second.gameObject); Object.DestroyImmediate(Mesh); Object.DestroyImmediate(Material); }
        }
    }
}
