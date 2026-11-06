using System;
using System.Collections.Generic;
using NUnit.Framework;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Editor.Baking;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CullingSystemDOTS.Tests
{
    public class CpuTraversalTests
    {
        [Test]
        public void TransparentLayersBeforeOpaqueAreVisibleAndLayersBehindAreNot()
        {
            using var f = new Fixture(); using var snapshot = MeshSnapshotExtractor.Capture(f.Request(false));
            var result = ReferenceCpuTraversal.Bake(snapshot);
            Assert.That(result.Length, Is.EqualTo(1)); Assert.That(result[0].FullGridIndex, Is.EqualTo(7));
            CollectionAssert.AreEqual(new ushort[] { 0, 1, 2 }, result[0].Groups);
            var repeated = ReferenceCpuTraversal.Bake(snapshot);
            CollectionAssert.AreEqual(result[0].Groups, repeated[0].Groups); Assert.That(result[0].NeighborMask, Is.EqualTo(repeated[0].NeighborMask));
        }
        [Test]
        public void AdditionalOpaqueOccluderBlocksWithoutReturningAnId()
        {
            using var f = new Fixture(); using var snapshot = MeshSnapshotExtractor.Capture(f.Request(true));
            var result = ReferenceCpuTraversal.Bake(snapshot);
            Assert.That(result.Length, Is.EqualTo(1)); Assert.That(result[0].Groups, Is.Empty);
        }
        [Test]
        public void FarClipExcludesPlanesBeyondAllSixFaces()
        {
            using var f = new Fixture(); var request = f.Request(false);
            var rays = request.Rays; rays.Far = .9f; request.Rays = rays;
            using var snapshot = MeshSnapshotExtractor.Capture(request);
            var result = ReferenceCpuTraversal.Bake(snapshot);
            Assert.That(result.Length, Is.EqualTo(1)); Assert.That(result[0].Groups, Is.Empty);
        }
        private sealed class Fixture : IDisposable
        {
            private readonly Mesh mesh;
            private readonly Material material;
            private readonly List<GameObject> objects = new List<GameObject>();
            private readonly Renderer[] renderers = new Renderer[4];
            public Fixture()
            {
                mesh = new Mesh { vertices = new[] { new Vector3(-10, -10, 0), new Vector3(-10, 10, 0), new Vector3(10, 10, 0), new Vector3(10, -10, 0) },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 2000 };
                for (int i = 0; i < 4; i++)
                {
                    renderers[i] = Plane(i + 1);
                    renderers[i].gameObject.AddComponent<CullingBakeRendererTag>().Surface = i == 2 ? BakeSurfaceOverride.Opaque : BakeSurfaceOverride.Transparent;
                }
            }
            private Renderer Plane(float z)
            {
                var go = new GameObject("CPU traversal plane " + z); objects.Add(go); go.SetActive(false); go.transform.position = new Vector3(0, 0, z);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; return r;
            }
            public BakeSnapshotRequest Request(bool occluder)
            {
                var groups = new BakeGroupSource[4];
                for (int g = 0; g < groups.Length; g++) groups[g] = new BakeGroupSource { Key = new CullingGroupKey(new uint4(4, 3, 2, 1), (ushort)g), Renderers = new[] { renderers[g] } };
                return new BakeSnapshotRequest { Profile = BakeInputProfile.ComputeSoftwareInput, Groups = groups,
                    AdditionalOccluders = occluder ? new[] { Plane(.5f) } : Array.Empty<Renderer>(),
                    Samples = new[] { new BakeSampleLocation { FullGridIndex = 7, Position = new float3(0) } } };
            }
            public void Dispose() { foreach (var go in objects) Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }
    }
}
