using NUnit.Framework;
using CullingSystemDOTS.Editor.Baking;
using Unity.Mathematics;

namespace CullingSystemDOTS.Tests
{
    public class IndependentTriangleTests
    {
        [Test]
        public void NearClipAndClosedTriangleEdgesDoNotLoseOccluders()
        {
            var triangle = ReferenceCpuTraversal.PrecomputeTriangle(new float3(-1,-1,1), new float3(0,1,1), new float3(1,-1,1));
            var ray = new BakeRay { Origin = float3.zero, Direction = new float3(0,0,1), Near = 1.1f, Far = 10 };
            Assert.That(ReferenceCpuTraversal.Intersect(in ray, in triangle, 10, false), Is.EqualTo(float.PositiveInfinity), "A hit before the near plane must be rejected.");
            ray.Near = .0001f; ray.Origin = new float3(.5f,0,0);
            Assert.That(ReferenceCpuTraversal.Intersect(in ray, in triangle, 10, false), Is.EqualTo(1f), "A shared triangle edge must remain closed.");
            ray.Origin = new float3(2,0,0);
            Assert.That(ReferenceCpuTraversal.Intersect(in ray, in triangle, 10, true), Is.EqualTo(float.PositiveInfinity));
        }
    }
}
