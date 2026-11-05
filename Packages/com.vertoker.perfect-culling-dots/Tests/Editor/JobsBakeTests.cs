using System;
using System.Collections.Generic;
using NUnit.Framework;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Core;
using PerfectCullingDOTS.Editor.Baking;
using PerfectCullingDOTS.Editor.Baking.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;
namespace PerfectCullingDOTS.Tests
{
    public class JobsBakeTests
    {
        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void JobsBakeEqualsCpuReference(bool blocker, bool transformed)
        {
            using var fixture = new BakeJobsFixture();
            using var snapshot = fixture.Capture(blocker, transformed);
            var expected = ReferenceCpuTraversal.Bake(snapshot);
            using var session = new JobsBakeBackend().Begin(snapshot, new BakeBackendOptions { MaxCellsPerBatch = 1 });
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Pending));
            AssertResults(expected, session.Complete());
            Assert.That(session.Progress, Is.EqualTo(1));
        }
        [Test]
        public void SharedBlasWithInteriorNodesAndMoreThan32GroupsMatchesReference()
        {
            using var fixture = new BakeJobsFixture(40,3); using var snapshot = fixture.Capture(false,false);
            using var session = new JobsBakeBackend().Begin(snapshot);
            var expected = ReferenceCpuTraversal.Bake(snapshot);
            Assert.That(Array.Exists(expected[2].Groups,g => g >= 32), Is.True);
            AssertResults(expected,session.Complete());
        }
        [Test]
        public void CoincidentOpaqueSurfacesKeepOriginalInstanceOrder()
        {
            using var fixture = new BakeJobsFixture(); fixture.MoveAsOpaque(1,4);
            using var snapshot = fixture.Capture(false,false); using var session = new JobsBakeBackend().Begin(snapshot);
            var expected = ReferenceCpuTraversal.Bake(snapshot);
            CollectionAssert.Contains(expected[0].Groups,(ushort)1); CollectionAssert.DoesNotContain(expected[0].Groups,(ushort)3);
            AssertResults(expected,session.Complete());
        }
        [Test]
        public void WorkerCountDoesNotChangeResultsAndExcludedCellsKeepFullGridIndex()
        {
            using var fixture = new BakeJobsFixture(); using var snapshot = fixture.Capture(false, true);
            int previous = JobsUtility.JobWorkerCount;
            try
            {
                JobsUtility.JobWorkerCount = 1;
                using var first = new JobsBakeBackend().Begin(snapshot); var expected = first.Complete();
                JobsUtility.JobWorkerCount = math.min(2, JobsUtility.JobWorkerMaximumCount);
                using var second = new JobsBakeBackend().Begin(snapshot);
                AssertResults(expected, second.Complete());
                CollectionAssert.AreEqual(new[] { 2, 19, 74 }, Array.ConvertAll(expected, r => r.FullGridIndex));
            }
            finally { JobsUtility.JobWorkerCount = previous; }
        }
        internal static void AssertResults(BakeCellResult[] expected, BakeCellResult[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i].FullGridIndex, Is.EqualTo(expected[i].FullGridIndex), "cell " + i);
                CollectionAssert.AreEqual(expected[i].Groups, actual[i].Groups, "groups cell " + expected[i].FullGridIndex);
                Assert.That(actual[i].NeighborMask, Is.EqualTo(expected[i].NeighborMask), "neighbors cell " + expected[i].FullGridIndex);
            }
        }
    }
    internal sealed class BakeJobsFixture : IDisposable
    {
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Renderer[] renderers;
        public BakeJobsFixture(int count = 6, int subdivisions = 1)
        {
            renderers = new Renderer[count]; var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int y = 0; y < subdivisions; y++) for (int x = 0; x < subdivisions; x++)
            {
                int v = vertices.Count; float x0 = -8+16f*x/subdivisions, x1 = -8+16f*(x+1)/subdivisions;
                float y0 = -8+16f*y/subdivisions, y1 = -8+16f*(y+1)/subdivisions;
                vertices.AddRange(new[] { new Vector3(x0,y0,0),new Vector3(x0,y1,0),new Vector3(x1,y1,0),new Vector3(x1,y0,0) });
                triangles.AddRange(new[] { v,v+1,v+2,v,v+2,v+3 });
            }
            mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 2000 };
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i] = Plane(i + 1);
                var tag = renderers[i].gameObject.AddComponent<CullingBakeRendererTag>();
                tag.DoubleSided = true; tag.Surface = i == 3 ? BakeSurfaceOverride.Opaque : BakeSurfaceOverride.Transparent;
            }
        }
        public void MoveAsOpaque(int index, float z)
        {
            renderers[index].transform.position = new Vector3(0,0,z);
            renderers[index].GetComponent<CullingBakeRendererTag>().Surface = BakeSurfaceOverride.Opaque;
        }
        private Renderer Plane(float z)
        {
            var go = new GameObject("Jobs bake fixture"); go.SetActive(false); objects.Add(go);
            go.transform.position = new Vector3(0,0,z); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }
        public BakeSnapshot Capture(bool blocker, bool transformed)
        {
            if (transformed) foreach (var r in renderers)
            {
                r.transform.rotation = Quaternion.Euler(13,29,7); r.transform.localScale = new Vector3(-1.4f,.8f,2);
            }
            var groups = new BakeGroupSource[renderers.Length];
            for (int i = 0; i < groups.Length; i++) groups[i] = new BakeGroupSource {
                Key = new CullingGroupKey(new uint4(8,7,6,5), (ushort)i), Renderers = new[] { renderers[i] } };
            return MeshSnapshotExtractor.Capture(new BakeSnapshotRequest { Profile = BakeInputProfile.ComputeSoftwareInput,
                Groups = groups, AdditionalOccluders = blocker ? new[] { Plane(.5f) } : Array.Empty<Renderer>(),
                Rays = new BakeRaySettings { Width = 17, Height = 19, Far = math.max(20,renderers.Length+2), Rotation = quaternion.EulerXYZ(.1f,.2f,.3f), CellSize = new float3(1,2,3) },
                Samples = new[] { new BakeSampleLocation { FullGridIndex = 2, Position = float3.zero },
                    new BakeSampleLocation { FullGridIndex = 19, Position = new float3(.1f,.2f,.4f) },
                    new BakeSampleLocation { FullGridIndex = 74, Position = new float3(0,0,7) } } });
        }
        public void Dispose() { foreach (var go in objects) Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
    }
}
