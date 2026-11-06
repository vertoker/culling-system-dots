using System;
using NUnit.Framework;
using CullingSystemDOTS.Editor.Baking;
using CullingSystemDOTS.Editor.Baking.Gpu;
using CullingSystemDOTS.Editor.Baking.Jobs;
using Unity.Mathematics;
namespace CullingSystemDOTS.Tests
{
    public class BakeNumericValidationTests
    {
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void InvalidSampleOrRotationFaultsBeforePublishingAnEmptyBake(bool gpu,bool badRotation)
        {
            var request = new BakeSnapshotRequest {Profile = BakeInputProfile.ComputeSoftwareInput,
                Samples = new[] {new BakeSampleLocation {FullGridIndex = 3,Position = badRotation ? float3.zero : new float3(float.NaN,0,0)}}};
            if (badRotation) { var rays = request.Rays; rays.Rotation = new quaternion(0,0,0,0); request.Rays = rays; }
            using var snapshot = MeshSnapshotExtractor.Capture(request);
            IBakeBackend backend = gpu ? new ComputeBakeBackend() : new JobsBakeBackend();
            using var session = backend.Begin(snapshot); Assert.Throws<ArgumentException>(()=>session.Tick());
            Assert.That(session.Status, Is.EqualTo(BakeSessionStatus.Faulted)); Assert.Throws<ArgumentException>(()=>session.Complete());
        }
    }
}
