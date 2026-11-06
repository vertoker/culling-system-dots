using System;
using System.Collections;
using System.Collections.Generic;
using CullingSystemDOTS.Dynamic.Gpu;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Rendering.Tests
{
    public class DynamicGpuUploadTests
    {
        private static Mesh Triangle() => new Mesh {
            vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(0,1,0) },
            normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward }, triangles = new[] { 0,1,2 } };
        private static GeometryHandle Register(OcclusionWorld registry, Mesh mesh)
        {
            var geometry = registry.RegisterGeometry(mesh);
            registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = new OcclusionInstanceState {
                LocalToWorld = float4x4.identity, WorldBounds = new Bounds(Vector3.zero, Vector3.one * 2), Roles = OcclusionRole.Occluder, Opaque = true } });
            return geometry;
        }
        // Test-only flush; measured/runtime upload work must not wait for a readback.
        private static void DrainGpu()
        {
            using (var sentinel = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4))
            { sentinel.SetData(new[] { 0u }); var request = AsyncGPUReadback.Request(sentinel); request.WaitForCompletion(); }
        }
        [Test] public void BudgetIsSharedAcrossWritersAndCannotResetTwiceInOneFrame()
        {
            var budget = new OcclusionGpuUploadBudget(64); budget.BeginFrame(1);
            Assert.That(budget.TakeAligned(100,24), Is.EqualTo(48));
            Assert.That(budget.TakeAligned(100,4), Is.EqualTo(16));
            Assert.That(budget.Used, Is.EqualTo(64)); budget.BeginFrame(1); Assert.That(budget.Remaining, Is.Zero);
            budget.BeginFrame(2); Assert.That(budget.Remaining, Is.EqualTo(64));
            Assert.Throws<ArgumentException>(() => budget.BeginFrame(1));
        }
        [UnityTest] public IEnumerator PartialUploadNeverPublishesAndCompletedGpuBytesMatchSource()
        {
            var mesh = Triangle();
            using (var registry = new OcclusionWorld())
            {
                var geometry = Register(registry,mesh);
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var uploaded = new OcclusionGpuMesh(registry,snapshot,geometry);
                    try
                    {
                        var budget = new OcclusionGpuUploadBudget(28); int total = 0; ulong frame = 1;
                        budget.BeginFrame(frame++); total += uploaded.Upload(budget);
                        Assert.That(budget.Used, Is.EqualTo(24)); Assert.That(uploaded.Ready, Is.False);
                        Assert.Throws<InvalidOperationException>(() => uploaded.AcquireReader());
                        while (!uploaded.Ready && frame < 20)
                        {
                            budget.BeginFrame(frame++); total += uploaded.Upload(budget);
                            Assert.That(budget.Used, Is.LessThanOrEqualTo(28)); yield return null;
                        }
                        Assert.That(uploaded.Ready, Is.True); Assert.That(total, Is.EqualTo(84));
                        using (var reader = uploaded.AcquireReader())
                        using (var vertices = reader.Mesh.GetVertexBuffer(0))
                        using (var indices = reader.Mesh.GetIndexBuffer())
                        {
                            var vertexReadback = AsyncGPUReadback.Request(vertices); var indexReadback = AsyncGPUReadback.Request(indices);
                            while (!vertexReadback.done || !indexReadback.done) yield return null;
                            Assert.That(vertexReadback.hasError || indexReadback.hasError, Is.False);
                            var actual = vertexReadback.GetData<float>(); var expected = mesh.vertices;
                            for (int i = 0; i < expected.Length; i++)
                            {
                                Assert.That(actual[i*6], Is.EqualTo(expected[i].x)); Assert.That(actual[i*6+1], Is.EqualTo(expected[i].y));
                                Assert.That(actual[i*6+2], Is.EqualTo(expected[i].z)); Assert.That(actual[i*6+5], Is.EqualTo(1));
                            }
                            var actualIndices = indexReadback.GetData<int>();
                            for (int i = 0; i < 3; i++) Assert.That(actualIndices[i], Is.EqualTo(i));
                            #if UNITY_EDITOR
                            var resources = UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(
                                UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
                            var material = new Material(resources.Depth);
                            using (var surface = new OcclusionDepthResources(registry, new int2(33,19)))
                            try
                            {
                                var properties = new MaterialPropertyBlock();
                                properties.SetMatrix("_OcclusionObjectToWorld", Matrix4x4.Translate(new Vector3(0,0,-3)));
                                properties.SetMatrix("_OcclusionView", Matrix4x4.identity);
                                properties.SetMatrix("_OcclusionViewProjection", GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-2,2,-2,2,.1f,100),false));
                                properties.SetFloat("_FarDepth",100);
                                using (var command = new CommandBuffer())
                                {
                                    command.SetRenderTarget(surface.Levels[0]); command.SetViewport(new Rect(0,0,33,19));
                                    command.ClearRenderTarget(false,true,new Color(100,0,0,0));
                                    command.DrawMesh(reader.Mesh,Matrix4x4.identity,material,0,0,properties);
                                    Graphics.ExecuteCommandBuffer(command);
                                }
                                var raster = AsyncGPUReadback.Request(surface.Levels[0],0); while (!raster.done) yield return null;
                                Assert.That(raster.hasError, Is.False);
                                Assert.That(raster.GetData<float>()[9*33+16], Is.EqualTo(3).Within(.001f),
                                    "Rendering must consume the streamed GPU geometry without a CPU Mesh upload replacing it.");
                            }
                            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(material); }
                            #endif
                        }
                    }
                    finally { DrainGpu(); uploaded.Dispose(); uploaded.Poll(); UnityEngine.Object.Destroy(mesh); }
                }
            }
        }
        [UnityTest] public IEnumerator RetiredGpuMeshKeepsItsReadyReaderAcrossWorldTeardown()
        {
            var mesh = Triangle(); var registry = new OcclusionWorld(); OcclusionGpuMesh uploaded = null; OcclusionGpuMeshLease reader = null;
            try
            {
                var geometry = Register(registry,mesh);
                using (var snapshot = registry.AcquireSnapshot())
                {
                    uploaded = new OcclusionGpuMesh(registry,snapshot,geometry);
                    var budget = new OcclusionGpuUploadBudget(1024); budget.BeginFrame(1); uploaded.Upload(budget);
                    reader = uploaded.AcquireReader(); uploaded.Dispose(); registry.Dispose();
                    Assert.That(uploaded.Released, Is.False); Assert.That(reader.Mesh != null, Is.True);
                }
                Assert.That(registry.AccountedResourceBytes, Is.GreaterThan(0));
                reader.Dispose(); int frames = 0;
                while (!uploaded.Released && frames++ < 120) { uploaded.Poll(); yield return null; }
                Assert.That(uploaded.Released, Is.True); Assert.That(registry.AccountedResourceBytes, Is.Zero);
            }
            finally { reader?.Dispose(); DrainGpu(); uploaded?.Dispose(); uploaded?.Poll(); registry.Dispose(); UnityEngine.Object.Destroy(mesh); }
        }
        [Test] public void FailedGpuMeshReservationDoesNotRetainSourceOrBudget()
        {
            var mesh = Triangle(); var leases = new List<OcclusionSnapshotLease>();
            using (var registry = new OcclusionWorld(new OcclusionSettings { MaxResourceBytes = 1024 }))
            try
            {
                var geometry = Register(registry,mesh);
                while (leases.Count < 64)
                {
                    try { leases.Add(registry.AcquireSnapshot()); }
                    catch (InvalidOperationException) { break; }
                }
                long before = registry.AccountedResourceBytes;
                Assert.Throws<InvalidOperationException>(() => new OcclusionGpuMesh(registry,leases[0],geometry));
                Assert.That(registry.AccountedResourceBytes, Is.EqualTo(before));
                foreach (var lease in leases) lease.Dispose(); leases.Clear(); registry.Dispose();
                Assert.That(registry.AccountedResourceBytes, Is.Zero);
            }
            finally { foreach (var lease in leases) lease.Dispose(); UnityEngine.Object.Destroy(mesh); }
        }
        [Test] public void RejectedWorkerDisposalDoesNotLoseTheMainThreadReader()
        {
            var mesh = Triangle();
            using (var registry = new OcclusionWorld())
            {
                var geometry = Register(registry,mesh);
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var uploaded = new OcclusionGpuMesh(registry,snapshot,geometry); OcclusionGpuMeshLease reader = null;
                    try
                    {
                        var budget = new OcclusionGpuUploadBudget(1024); budget.BeginFrame(1); uploaded.Upload(budget);
                        reader = uploaded.AcquireReader(); Exception failure = null;
                        System.Threading.Tasks.Task.Run(() => { try { reader.Dispose(); } catch (Exception exception) { failure = exception; } }).GetAwaiter().GetResult();
                        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                        Assert.That(reader.Mesh != null, Is.True, "Rejected off-thread disposal must leave ownership intact.");
                    }
                    finally
                    {
                        // Keep the negative lifetime test exception-safe if a rejected disposal
                        // incorrectly detaches its reader.
                        if (reader != null)
                        {
                            try { var retained = reader.Mesh; reader.Dispose(); }
                            catch (ObjectDisposedException) { uploaded.ReleaseReader(); }
                        }
                        DrainGpu(); uploaded.Dispose(); uploaded.Poll(); UnityEngine.Object.Destroy(mesh);
                    }
                }
            }
        }
        [Test] public void ShutdownDrainRemovesOwnedPumpAfterItsLastRequestCompletes()
        {
            var mesh = Triangle();
            using (var registry = new OcclusionWorld())
            {
                var geometry = Register(registry,mesh);
                using (var snapshot = registry.AcquireSnapshot())
                {
                    var uploaded = new OcclusionGpuMesh(registry,snapshot,geometry); OcclusionGpuMeshLease reader = null;
                    try
                    {
                        var budget = new OcclusionGpuUploadBudget(1024); budget.BeginFrame(1); uploaded.Upload(budget);
                        reader = uploaded.AcquireReader(); uploaded.Dispose(); reader.Dispose();
                        OcclusionGpuRetirement.DrainForShutdown();
                        Assert.That(uploaded.Released, Is.True);
                        Assert.That(UnityEngine.Resources.FindObjectsOfTypeAll<OcclusionGpuRetirementPump>().Length, Is.Zero);
                    }
                    finally
                    {
                        reader?.Dispose(); DrainGpu(); uploaded.Dispose(); uploaded.Poll();
                        foreach (var pump in UnityEngine.Resources.FindObjectsOfTypeAll<OcclusionGpuRetirementPump>())
                            UnityEngine.Object.DestroyImmediate(pump.gameObject);
                        UnityEngine.Object.Destroy(mesh);
                    }
                }
            }
        }
    }
}
