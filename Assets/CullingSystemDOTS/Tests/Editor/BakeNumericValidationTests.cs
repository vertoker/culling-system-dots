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
        [TestCase(false,"vertex")] [TestCase(true,"vertex")]
        [TestCase(false,"transform")] [TestCase(true,"transform")]
        [TestCase(false,"inverse")] [TestCase(true,"inverse")]
        [TestCase(false,"bounds")] [TestCase(true,"bounds")]
        [TestCase(false,"singular")] [TestCase(true,"singular")]
        public void InvalidGeometryFaultsWithoutReturningPartialVisibility(bool gpu,string kind)
        {
            var vertices = new[] {new float3(-1,-1,1),new float3(0,1,1),new float3(1,-1,1)};
            var instance = new BakeInstance {Geometry = 0,GroupIndex = 0,LocalToWorld = float4x4.identity,
                WorldToLocal = float4x4.identity,BoundsMin = new float3(-1,-1,1),BoundsMax = new float3(1,1,1)};
            if (kind == "vertex") vertices[0].x = float.NaN;
            if (kind == "transform") instance.LocalToWorld.c0.x = float.PositiveInfinity;
            if (kind == "inverse") instance.WorldToLocal.c3.z = float.NaN;
            if (kind == "bounds") instance.BoundsMin.x = 2;
            if (kind == "singular") instance.LocalToWorld.c0 = float4.zero;
            var request = new BakeSnapshotRequest {Rays = new BakeRaySettings {Width=3,Height=3,Far=10,CellSize=new float3(2),Rotation=quaternion.identity}};
            using var snapshot = new BakeSnapshot(vertices,new[] {0,1,2},new[] {new BakeGeometry {VertexCount=3,IndexCount=3}},
                new[] {instance},new[] {new CullingSystemDOTS.Core.CullingGroupKey(new uint4(1,2,3,4),0)},new[] {new BakeSampleLocation()},request);
            IBakeBackend backend = gpu ? new ComputeBakeBackend() : new JobsBakeBackend();
            using var session = backend.Begin(snapshot);
            Assert.Throws<ArgumentException>(()=>session.Tick(),kind);
            Assert.That(session.Status,Is.EqualTo(BakeSessionStatus.Faulted));
            Assert.Throws<ArgumentException>(()=>session.Complete());
            Assert.That(session.Progress,Is.LessThan(1));
        }
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
