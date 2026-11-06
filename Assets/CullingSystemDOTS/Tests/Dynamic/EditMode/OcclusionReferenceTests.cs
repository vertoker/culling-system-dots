using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Tests
{
    public class OcclusionReferenceTests
    {
        private static ProjectedBounds Rect(int x0, int y0, int x1, int y1, float nearest = 5) =>
            new ProjectedBounds { Min = new int2(x0, y0), Max = new int2(x1, y1), NearestDepth = nearest, Valid = true };
        [Test] public void FullWallHidesOnlyStrictlyDeeperCandidates()
        {
            using var depth = new NativeArray<float>(new float[] { 2, 2, 2, 2 }, Allocator.Temp);
            var image = new ReferenceDepthImage(depth, new int2(2), 100);
            Assert.That(OcclusionReference.Test(Rect(0, 0, 2, 2), image), Is.EqualTo(OcclusionVisibility.Occluded));
            Assert.That(OcclusionReference.Test(Rect(0, 0, 2, 2, 2), image), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
            Assert.That(OcclusionReference.Test(Rect(0, 0, 2, 2, 2.0005f), image), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
        }
        [Test] public void OneUncoveredSamplePreservesDoorwayAndThinSliver()
        {
            var samples = new float[35];
            for (int i = 0; i < samples.Length; i++) samples[i] = 2;
            samples[3 * 7 + 4] = 100;
            using var depth = new NativeArray<float>(samples, Allocator.Temp);
            var image = new ReferenceDepthImage(depth, new int2(7, 5), 100);
            Assert.That(OcclusionReference.Test(Rect(0, 0, 7, 5), image), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
            Assert.That(OcclusionReference.Test(Rect(0, 0, 4, 5), image), Is.EqualTo(OcclusionVisibility.Occluded));
            Assert.That(OcclusionReference.Test(Rect(4, 3, 5, 4), image), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1)] [TestCase(100)]
        public void InvalidOrFarDepthCannotProveOcclusion(float value)
        {
            using var depth = new NativeArray<float>(new[] { value }, Allocator.Temp);
            Assert.That(OcclusionReference.Test(Rect(0, 0, 1, 1), new ReferenceDepthImage(depth, new int2(1), 100)), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
        }
        [Test] public void InvalidRectangleAndBiasCannotProveOcclusion()
        {
            using var depth = new NativeArray<float>(new float[] { 2, 2, 2, 2 }, Allocator.Temp);
            var image = new ReferenceDepthImage(depth, new int2(2), 100);
            foreach (var rect in new[] { Rect(-1, 0, 1, 1), Rect(1, 1, 1, 2), Rect(0, 0, 3, 1), Rect(0, 0, 1, 1, float.NaN), default(ProjectedBounds) })
                Assert.That(OcclusionReference.Test(rect, image), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
            Assert.That(OcclusionReference.Test(Rect(0, 0, 1, 1), image, -1), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
        }
        [TestCase(false)] [TestCase(true)]
        public void ProjectionEnclosesAllCornerSamplesAtOddDimensions(bool orthographic)
        {
            var size = new int2(101, 59);
            var projection = orthographic ? Matrix4x4.Ortho(-4, 4, -3, 3, 0.1f, 100) : Matrix4x4.Perspective(60, 101f / 59, 0.1f, 100);
            var bounds = new Bounds(new Vector3(-0.7f, 0.3f, -6), new Vector3(1.3f, 2.1f, 1.7f));
            var projected = OcclusionReference.Project(bounds, Matrix4x4.identity, projection, size, 0.1f);
            Assert.That(projected.Valid, Is.True); Assert.That(projected.NearestDepth, Is.EqualTo(5.15f).Within(0.0001f));
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector4 clip = projection * new Vector4(corner.x, corner.y, corner.z, 1);
                float x = (clip.x / clip.w * 0.5f + 0.5f) * size.x, y = (clip.y / clip.w * 0.5f + 0.5f) * size.y;
                Assert.That(projected.Min.x, Is.LessThanOrEqualTo(x)); Assert.That(projected.Max.x, Is.GreaterThanOrEqualTo(x));
                Assert.That(projected.Min.y, Is.LessThanOrEqualTo(y)); Assert.That(projected.Max.y, Is.GreaterThanOrEqualTo(y));
            }
        }
        [Test] public void CameraInsideNearCrossingAndInvalidGeometryKeepOrdinaryRendering()
        {
            var projection = Matrix4x4.Perspective(60, 1, .1f, 100); var size = new int2(101, 59);
            foreach (var bounds in new[] { new Bounds(Vector3.zero, Vector3.one), new Bounds(new Vector3(0, 0, -.15f), Vector3.one),
                new Bounds(new Vector3(float.NaN, 0, -5), Vector3.one) })
                Assert.That(OcclusionReference.Project(bounds, Matrix4x4.identity, projection, size, .1f).Valid, Is.False);
        }
        [Test] public void LowResolutionCenterSampleLosesAReferenceGridOpening()
        {
            using var full = new NativeArray<float>(new float[] { 2, 2, 100, 2 }, Allocator.Temp);
            using var coarse = new NativeArray<float>(new float[] { 2 }, Allocator.Temp);
            Assert.That(OcclusionReference.Test(Rect(0, 0, 4, 1), new ReferenceDepthImage(full, new int2(4, 1), 100)), Is.EqualTo(OcclusionVisibility.PotentiallyVisible));
            Assert.That(OcclusionReference.Test(Rect(0, 0, 1, 1), new ReferenceDepthImage(coarse, new int2(1), 100)), Is.EqualTo(OcclusionVisibility.Occluded),
                "A coarse center can falsely hide through a full-resolution uncovered sample; production must preserve its coverage.");
        }
    }
}
