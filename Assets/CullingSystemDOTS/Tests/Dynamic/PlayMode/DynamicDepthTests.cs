using System;
using System.Collections;
using CullingSystemDOTS.Dynamic.Gpu;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Rendering.Tests
{
    public class DynamicDepthTests
    {
        private struct Candidate { public int2 Min, Max; public float Nearest; public uint Valid; }
        private struct WorldCandidate { public float3 Center; public uint Valid; public float3 Extents; public uint Padding; }
        private static OcclusionGpuMeshSet PrepareMeshes(OcclusionGpuGeometryCache cache, OcclusionSnapshotLease snapshot)
        {
            var budget = new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1); cache.Process(snapshot,budget); return cache.Acquire(snapshot);
        }
        private sealed class DepthReaders : IDisposable
        {
            private readonly OcclusionGpuPipeline pipeline;
            private readonly OcclusionGpuMeshSet meshes;
            internal DepthReaders(OcclusionGpuPipeline pipeline, OcclusionGpuMeshSet meshes) { this.pipeline = pipeline; this.meshes = meshes; }
            public void Dispose() { try { pipeline.Dispose(); } finally { meshes.Dispose(); } }
        }
        private static OcclusionShaderResources LoadResources()
        {
            #if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3");
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(path);
            Assert.That(asset, Is.Not.Null, "The serialized resource asset must travel with the package.");
            return UnityEngine.Object.Instantiate(asset);
            #else
            throw new NotSupportedException("This Editor harness requires a serialized Player fixture for Player runs.");
            #endif
        }
        [UnityTest] public IEnumerator HierarchyPreservesEveryUncoveredSampleAndOddEdge()
        {
            Assert.That(OcclusionGpuPipeline.IsSupported, Is.True, "Initial DX12 acceptance device must run the GPU checks.");
            var resources = LoadResources();
            try
            {
                foreach (var size in new[] { new int2(7, 5), new int2(17, 9), new int2(1, 11), new int2(8) })
                using (var registry = new OcclusionWorld())
                using (var pipeline = new OcclusionGpuPipeline(registry, resources, size, 100))
                {
                    var samples = new float[size.x * size.y];
                    for (int i = 0; i < samples.Length; i++) samples[i] = 2;
                    samples[(size.y / 2) * size.x + size.x / 2] = 100;
                    samples[0] = float.NaN; samples[samples.Length - 1] = float.PositiveInfinity;
                    var texture = new Texture2D(size.x, size.y, TextureFormat.RFloat, false, true);
                    try
                    {
                        texture.SetPixelData(samples, 0); texture.Apply(false);
                        using (var command = new CommandBuffer())
                        { command.CopyTexture(texture, pipeline.Depth.Levels[0]); pipeline.RecordHierarchy(command); Graphics.ExecuteCommandBuffer(command); }
                        var expected = samples; var dimensions = size;
                        for (int level = 0; level < pipeline.Depth.Levels.Length; level++)
                        {
                            var request = AsyncGPUReadback.Request(pipeline.Depth.Levels[level], 0);
                            while (!request.done) yield return null;
                            Assert.That(request.hasError, Is.False);
                            var actual = request.GetData<float>(); Assert.That(actual.Length, Is.EqualTo(expected.Length));
                            for (int i = 0; i < expected.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i]), "Level " + level + " sample " + i + " size " + size);
                            var next = (dimensions + 1) / 2; var reduced = new float[next.x * next.y];
                            for (int y = 0; y < next.y; y++) for (int x = 0; x < next.x; x++)
                            {
                                float farthest = 0;
                                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                                {
                                    int sx = x * 2 + dx, sy = y * 2 + dy;
                                    float value = sx >= dimensions.x || sy >= dimensions.y ? 100 : expected[sy * dimensions.x + sx];
                                    if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0 || value >= 100) value = 100;
                                    farthest = Math.Max(farthest, value);
                                }
                                reduced[y * next.x + x] = farthest;
                            }
                            dimensions = next; expected = reduced;
                        }
                    }
                    finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(texture); }
                }
            }
            finally { UnityEngine.Object.Destroy(resources); }
        }

        [UnityTest] public IEnumerator HierarchicalTestsKeepEveryReferenceVisibleRectangle()
        {
            var resources = LoadResources();
            using (var registry = new OcclusionWorld())
            using (var pipeline = new OcclusionGpuPipeline(registry, resources, new int2(31, 17), 100))
            {
                var size = pipeline.Depth.Size; var values = new float[size.x * size.y];
                for (int i = 0; i < values.Length; i++) values[i] = 2;
                values[8 * size.x + 16] = 100;
                values[1 * size.x + 1] = float.NaN;
                var rows = new Candidate[256]; var random = new Unity.Mathematics.Random(8271);
                for (int i = 0; i < rows.Length; i++)
                {
                    var min = random.NextInt2(0, size); var max = random.NextInt2(min + 1, size + 1);
                    rows[i] = new Candidate { Min = min, Max = max, Nearest = 5, Valid = 1 };
                }
                rows[0] = new Candidate { Min = 0, Max = 1, Nearest = 5, Valid = 1 };
                rows[1] = new Candidate { Min = 0, Max = size, Nearest = 5, Valid = 1 };
                rows[2] = new Candidate { Min = new int2(1), Max = new int2(2), Nearest = 5, Valid = 1 };
                rows[3] = new Candidate { Min = 0, Max = 1, Nearest = float.NaN, Valid = 1 };
                rows[4] = new Candidate { Min = 0, Max = 1, Nearest = 5, Valid = 0 };
                rows[5] = new Candidate { Min = new int2(-1, 0), Max = 1, Nearest = 5, Valid = 1 };
                var texture = new Texture2D(size.x, size.y, TextureFormat.RFloat, false, true);
                using (var candidates = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 24))
                using (var visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 4))
                using (var depth = new NativeArray<float>(values, Allocator.Persistent))
                try
                {
                    texture.SetPixelData(values, 0); texture.Apply(false); candidates.SetData(rows);
                    using (var command = new CommandBuffer())
                    {
                        command.CopyTexture(texture, pipeline.Depth.Levels[0]); pipeline.RecordHierarchy(command);
                        pipeline.RecordProjectedTests(command, candidates, visible, rows.Length); Graphics.ExecuteCommandBuffer(command);
                    }
                    var request = AsyncGPUReadback.Request(visible); while (!request.done) yield return null;
                    Assert.That(request.hasError, Is.False); var actual = request.GetData<uint>();
                    var reference = new ReferenceDepthImage(depth, size, 100); int hidden = 0;
                    for (int i = 0; i < rows.Length; i++)
                    {
                        var row = rows[i]; var bounds = new ProjectedBounds { Min = row.Min, Max = row.Max, NearestDepth = row.Nearest, Valid = row.Valid != 0 };
                        if (OcclusionReference.Test(bounds, reference) != OcclusionVisibility.Occluded)
                            Assert.That(actual[i], Is.EqualTo(1), "Lost reference-visible rectangle " + i);
                        if (actual[i] == 0) hidden++;
                    }
                    Assert.That(actual[0], Is.Zero, "A covered one-pixel footprint must actually hide.");
                    Assert.That(hidden, Is.GreaterThan(0)); Assert.That(actual[1], Is.EqualTo(1), "The uncovered reference pixel must survive reduction.");
                }
                finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(resources); }
            }
        }

        [UnityTest] public IEnumerator ContainingAndNearClippedOccludersCannotContributeDepth()
        {
            var resources=LoadResources();var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.SetActive(false);
            try
            {
                using(var registry=new OcclusionWorld())
                using(var cache=new OcclusionGpuGeometryCache(registry))
                using(var pipeline=new OcclusionGpuPipeline(registry,resources,new int2(33,19),100))
                {
                    var geometry=registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    var descriptor=new OcclusionInstanceDescriptor{Geometry=geometry};
                    descriptor.State=new OcclusionInstanceState{Opaque=true,Roles=OcclusionRole.Occluder};
                    var handle=default(InstanceHandle);
                    foreach(float z in new[]{0f,-.55f,-3f})
                    {
                        descriptor.State.LocalToWorld=float4x4.TRS(new float3(0,0,z),quaternion.identity,new float3(5,5,1));
                        descriptor.State.WorldBounds=new Bounds(new Vector3(0,0,z),new Vector3(5,5,1));
                        if(!handle.IsValid)handle=registry.RegisterInstance(descriptor);else registry.UpdateInstance(handle,descriptor.State);
                        while(registry.PendingUpdateCount>0)registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                        using(var snapshot=registry.AcquireSnapshot())
                        using(var meshes=PrepareMeshes(cache,snapshot))
                        {
                            using(var command=new CommandBuffer())
                            {pipeline.RecordDepth(command,snapshot,Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60,33f/19,.1f,100),false),meshes,nearPlane:.1f);Graphics.ExecuteCommandBuffer(command);}
                            var request=AsyncGPUReadback.Request(pipeline.Depth.Levels[0],0);while(!request.done)yield return null;
                            Assert.That(request.hasError,Is.False);
                            var depths=request.GetData<float>();
                            if(z>-1)foreach(float depth in depths)Assert.That(depth,Is.EqualTo(100),"Uncertain near coverage must remain clear.");
                            else Assert.That(depths[9*33+16],Is.EqualTo(2.5f).Within(.001f),"An entirely forward wall must still provide useful depth.");
                        }
                    }
                }
            }
            finally{AsyncGPUReadback.WaitAllRequests();UnityEngine.Object.Destroy(cube);UnityEngine.Object.Destroy(resources);}
        }

        [UnityTest] public IEnumerator RasterDepthIsLinearAcrossBothHardwareZConventions()
        {
            var resources = LoadResources(); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            try
            {
                using (var registry = new OcclusionWorld())
                using (var cache = new OcclusionGpuGeometryCache(registry))
                using (var pipeline = new OcclusionGpuPipeline(registry, resources, new int2(33, 19), 100))
                {
                    var geometry = registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    foreach (float z in new[] { -6f, -3f }) registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry,
                        State = new OcclusionInstanceState { LocalToWorld = float4x4.TRS(new float3(0, 0, z), quaternion.RotateY(math.radians(30)), new float3(1)),
                            WorldBounds = new Bounds(new Vector3(0, 0, z), Vector3.one * 2), Roles = OcclusionRole.Occluder, Opaque = true } });
                    while (registry.PendingUpdateCount > 0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                    using (var snapshot = registry.AcquireSnapshot())
                    using (var meshes = PrepareMeshes(cache,snapshot))
                    foreach (bool orthographic in new[] { false, true })
                    foreach (bool flippedZ in new[] { false, true })
                    {
                        var projection = GL.GetGPUProjectionMatrix(orthographic ? Matrix4x4.Ortho(-2, 2, -2, 2, .1f, 100) : Matrix4x4.Perspective(60, 33f/19, .1f, 100), false);
                        if (flippedZ) for (int c = 0; c < 4; c++) projection[2, c] = projection[3, c] - projection[2, c];
                        using (var command = new CommandBuffer())
                        { pipeline.RecordDepth(command, snapshot, Matrix4x4.identity, projection,meshes); Graphics.ExecuteCommandBuffer(command); }
                        var request = AsyncGPUReadback.Request(pipeline.Depth.Levels[0], 0); while (!request.done) yield return null;
                        Assert.That(request.hasError, Is.False); var actual = request.GetData<float>();
                        float expected = 3 - .5f / math.cos(math.radians(30));
                        Assert.That(actual[9 * 33 + 16], Is.EqualTo(expected).Within(.001f), "Rotated surface linear depth, flipped Z=" + flippedZ + ", orthographic=" + orthographic);
                        Assert.That(actual[0], Is.EqualTo(100), "Uncovered pixel retains far depth.");
                    }
                }
            }
            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(cube); UnityEngine.Object.Destroy(resources); }
        }

        [UnityTest] public IEnumerator WorldProjectionPreservesDoorwaysNearCrossingsAndBothProjectionTypes()
        {
            var resources = LoadResources();
            try
            {
                using (var registry = new OcclusionWorld())
                using (var pipeline = new OcclusionGpuPipeline(registry, resources, new int2(101,59), 100))
                {
                    var rows = new[] {
                        new WorldCandidate { Center = new float3(0,0,-5), Extents = new float3(.2f), Valid = 1 },
                        new WorldCandidate { Center = new float3(0,0,-.11f), Extents = new float3(.2f), Valid = 1 },
                        new WorldCandidate { Center = 0, Extents = new float3(1), Valid = 1 },
                        new WorldCandidate { Center = new float3(-1,0,-5), Extents = new float3(.2f), Valid = 1 },
                        new WorldCandidate { Center = new float3(float.NaN,0,-5), Extents = new float3(.2f), Valid = 1 } };
                    var size = pipeline.Depth.Size;
                    using (var candidates = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 32))
                    using (var visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, rows.Length, 4))
                    {
                        candidates.SetData(rows);
                        foreach (bool orthographic in new[] { false, true })
                        foreach (bool opening in new[] { false, true })
                        {
                            var pixels = new float[size.x * size.y]; for (int i = 0; i < pixels.Length; i++) pixels[i] = 2;
                            if (opening) pixels[(size.y / 2) * size.x + size.x / 2] = 100;
                            var texture = new Texture2D(size.x, size.y, TextureFormat.RFloat, false, true);
                            using (var depth = new NativeArray<float>(pixels, Allocator.Persistent))
                            try
                            {
                                texture.SetPixelData(pixels, 0); texture.Apply(false);
                                var projection = GL.GetGPUProjectionMatrix(orthographic ? Matrix4x4.Ortho(-4,4,-3,3,.1f,100) : Matrix4x4.Perspective(60,101f/59,.1f,100), false);
                                using (var command = new CommandBuffer())
                                {
                                    command.CopyTexture(texture, pipeline.Depth.Levels[0]); pipeline.RecordHierarchy(command);
                                    pipeline.RecordWorldTests(command, candidates, visible, rows.Length, Matrix4x4.identity, projection, .1f);
                                    Graphics.ExecuteCommandBuffer(command);
                                }
                                var request = AsyncGPUReadback.Request(visible); while (!request.done) yield return null;
                                Assert.That(request.hasError, Is.False); var actual = request.GetData<uint>();
                                var rasterProjection = projection;
                                if (SystemInfo.graphicsUVStartsAtTop) for (int c = 0; c < 4; c++) rasterProjection[1,c] *= -1;
                                var image = new ReferenceDepthImage(depth, size, 100);
                                for (int i = 0; i < rows.Length; i++)
                                {
                                    var bounds = new Bounds(rows[i].Center, rows[i].Extents * 2);
                                    var projected = OcclusionReference.Project(bounds, Matrix4x4.identity, rasterProjection, size, .1f);
                                    if (OcclusionReference.Test(projected, image) != OcclusionVisibility.Occluded)
                                        Assert.That(actual[i], Is.EqualTo(1), "Lost projected reference " + i);
                                }
                                Assert.That(actual[0], Is.EqualTo(opening ? 1 : 0), "Actual wall/opening classification, orthographic=" + orthographic);
                            }
                            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(texture); }
                        }
                    }
                }
            }
            finally { UnityEngine.Object.Destroy(resources); }
        }

        [UnityTest] public IEnumerator WorldFrustumRejectsOnlyFullyOutsideKnownBounds()
        {
            var resources=LoadResources();
            try
            {
                using (var registry=new OcclusionWorld())
                using (var pipeline=new OcclusionGpuPipeline(registry,resources,new int2(33,19),100))
                {
                    var rows=new[] {
                        new WorldCandidate { Center=new float3(1000,0,-5), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(0,1000,-5), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(0,0,5), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(0,0,-200), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(0,0,-.02f), Extents=new float3(.001f), Valid=1 },
                        new WorldCandidate { Center=new float3(0,0,-.1f), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=0, Extents=new float3(1), Valid=1 },
                        new WorldCandidate { Center=new float3(0,0,-5), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(float.NaN,0,-5), Extents=new float3(.2f), Valid=1 },
                        new WorldCandidate { Center=new float3(1000,0,-5), Extents=new float3(.2f), Valid=0 } };
                    using (var candidates=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Length,32))
                    using (var visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Length,4))
                    {
                        candidates.SetData(rows);
                        foreach (bool orthographic in new[] { false,true })
                        foreach (bool reversed in new[] { false,true })
                        {
                            var projection=GL.GetGPUProjectionMatrix(orthographic?Matrix4x4.Ortho(-4,4,-3,3,.1f,100):Matrix4x4.Perspective(60,33f/19,.1f,100),false);
                            if (reversed) for (int c=0;c<4;c++) projection[2,c]=projection[3,c]-projection[2,c];
                            using (var command=new CommandBuffer())
                            {
                                command.SetRenderTarget(pipeline.Depth.Levels[0]); command.ClearRenderTarget(false,true,new Color(100,0,0,0));
                                pipeline.RecordHierarchy(command); pipeline.RecordWorldTests(command,candidates,visible,rows.Length,Matrix4x4.identity,projection,.1f);
                                Graphics.ExecuteCommandBuffer(command);
                            }
                            var request=AsyncGPUReadback.Request(visible); while (!request.done) yield return null;
                            Assert.That(request.hasError,Is.False); var actual=request.GetData<uint>();
                            for (int i=0;i<rows.Length;i++) Assert.That(actual[i],Is.EqualTo(i<5?0u:1u),"Conservative frustum row "+i+", ortho="+orthographic+", reversed="+reversed);
                        }
                    }
                }
            }
            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(resources); }
        }
        [Test] public void FailedPartialDepthAllocationReleasesTexturesAndBudget()
        {
            using (var registry = new OcclusionWorld())
            {
                RenderTexture first = null; int count = 0;
                Assert.Throws<InvalidOperationException>(() => new OcclusionDepthResources(registry, new int2(32), descriptor =>
                {
                    if (++count == 2) throw new InvalidOperationException("Injected texture allocation failure");
                    first = new RenderTexture(descriptor); return first;
                }));
                Assert.That(first == null || !first.IsCreated(), Is.True);
                Assert.That(registry.AccountedResourceBytes, Is.Zero);
            }
        }
        [Test] public void DisposedWorldCannotAcquireNewDepthResources()
        {
            var registry = new OcclusionWorld(); registry.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { using (var depth = new OcclusionDepthResources(registry, new int2(4))) { } });
        }
        [UnityTest] public IEnumerator UnuploadedGeometryCannotPopulateDepthDespiteCpuPreparation()
        {
            var resources = LoadResources(); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            try
            {
                using (var registry = new OcclusionWorld())
                using (var cache = new OcclusionGpuGeometryCache(registry))
                using (var pipeline = new OcclusionGpuPipeline(registry,resources,new int2(33,19),100))
                {
                    var geometry = registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry=geometry, State=new OcclusionInstanceState {
                        LocalToWorld=float4x4.Translate(new float3(0,0,-3)), WorldBounds=new Bounds(new Vector3(0,0,-3),Vector3.one), Roles=OcclusionRole.Occluder, Opaque=true } });
                    while (registry.PendingUpdateCount>0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                    using (var snapshot=registry.AcquireSnapshot())
                    foreach (bool ready in new[] { false,true })
                    {
                        var budget=new OcclusionGpuUploadBudget(ready?262144:16); budget.BeginFrame(ready?2ul:1ul); cache.Process(snapshot,budget);
                        using (var meshes=cache.Acquire(snapshot))
                        {
                            using (var command=new CommandBuffer())
                            { pipeline.RecordDepth(command,snapshot,Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-2,2,-2,2,.1f,100),false),meshes); Graphics.ExecuteCommandBuffer(command); }
                            var request=AsyncGPUReadback.Request(pipeline.Depth.Levels[0],0); while (!request.done) yield return null;
                            Assert.That(request.hasError,Is.False);
                            Assert.That(request.GetData<float>()[9*33+16],Is.EqualTo(ready?2.5f:100).Within(.001f),"Actual GPU upload readiness must gate the depth source.");
                        }
                    }
                }
            }
            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(resources); UnityEngine.Object.Destroy(cube); }
        }
        [UnityTest] public IEnumerator StaleGpuGeometryCannotPopulateCurrentDepth()
        {
            var resources=LoadResources(); var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false); Mesh replacement=null;
            try
            {
                using (var registry=new OcclusionWorld())
                using (var cache=new OcclusionGpuGeometryCache(registry))
                using (var pipeline=new OcclusionGpuPipeline(registry,resources,new int2(33,19),100))
                {
                    var geometry=registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry=geometry, State=new OcclusionInstanceState {
                        LocalToWorld=float4x4.Translate(new float3(0,0,-3)), WorldBounds=new Bounds(new Vector3(0,0,-3),Vector3.one), Roles=OcclusionRole.Occluder, Opaque=true } });
                    while (registry.PendingUpdateCount>0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                    using (var oldSnapshot=registry.AcquireSnapshot())
                    {
                        var budget=new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1); cache.Process(oldSnapshot,budget);
                        using (var oldMeshes=cache.Acquire(oldSnapshot))
                        {
                            replacement=UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
                            var vertices=replacement.vertices; for (int i=0;i<vertices.Length;i++) vertices[i]+=Vector3.right*20; replacement.vertices=vertices;
                            registry.ReplaceGeometry(geometry,replacement);
                            while (registry.PendingUpdateCount>0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                            using (var current=registry.AcquireSnapshot())
                            {
                                Assert.That(current.Instances[0].Prepared,Is.EqualTo(1));
                                using (var command=new CommandBuffer())
                                { pipeline.RecordDepth(command,current,Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-2,2,-2,2,.1f,100),false),oldMeshes); Graphics.ExecuteCommandBuffer(command); }
                                var request=AsyncGPUReadback.Request(pipeline.Depth.Levels[0],0); while (!request.done) yield return null;
                                Assert.That(request.hasError,Is.False); Assert.That(request.GetData<float>()[9*33+16],Is.EqualTo(100));
                            }
                        }
                    }
                }
            }
            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(replacement); UnityEngine.Object.Destroy(resources); UnityEngine.Object.Destroy(cube); }
        }
        [UnityTest] public IEnumerator MirroredOccludersAreOmittedUntilTheirRasterContractIsSupported()
        {
            var resources = LoadResources(); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            try
            {
                using (var registry = new OcclusionWorld())
                using (var cache = new OcclusionGpuGeometryCache(registry))
                using (var pipeline = new OcclusionGpuPipeline(registry, resources, new int2(33,19), 100))
                {
                    var geometry = registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = new OcclusionInstanceState {
                        LocalToWorld = float4x4.TRS(new float3(0,0,-3), quaternion.identity, new float3(-1,1,1)),
                        WorldBounds = new Bounds(new Vector3(0,0,-3), Vector3.one), Roles = OcclusionRole.Occluder, Opaque = true } });
                    while (registry.PendingUpdateCount > 0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                    using (var snapshot = registry.AcquireSnapshot())
                    using (var meshes = PrepareMeshes(cache,snapshot))
                    {
                        using (var command = new CommandBuffer())
                        { pipeline.RecordDepth(command, snapshot, Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-2,2,-2,2,.1f,100),false),meshes); Graphics.ExecuteCommandBuffer(command); }
                        var request = AsyncGPUReadback.Request(pipeline.Depth.Levels[0],0); while (!request.done) yield return null;
                        Assert.That(request.hasError, Is.False); Assert.That(request.GetData<float>()[9*33+16], Is.EqualTo(100));
                    }
                }
            }
            finally { AsyncGPUReadback.WaitAllRequests(); UnityEngine.Object.Destroy(resources); UnityEngine.Object.Destroy(cube); }
        }
        [UnityTest] public IEnumerator CancelledSubmissionRetainsWorldGeometryUntilTheFinalGpuFence()
        {
            var registry = new OcclusionWorld(); var resources = LoadResources();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            OcclusionGpuPipeline pipeline = null; OcclusionGpuSubmission submission = null; OcclusionSnapshotLease snapshot = null;
            OcclusionGpuGeometryCache cache = null; OcclusionGpuMeshSet meshes = null;
            using (var command = new CommandBuffer())
            try
            {
                var geometry = registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                registry.RegisterInstance(new OcclusionInstanceDescriptor { Geometry = geometry, State = new OcclusionInstanceState {
                    LocalToWorld = float4x4.Translate(new float3(0, 0, -3)), WorldBounds = new Bounds(new Vector3(0,0,-3), Vector3.one),
                    Roles = OcclusionRole.Occluder, Opaque = true } });
                while (registry.PendingUpdateCount > 0) registry.ProcessUpdates(new OcclusionUpdateBudget(262144));
                snapshot = registry.AcquireSnapshot(); pipeline = new OcclusionGpuPipeline(registry, resources, new int2(33,19), 100);
                cache = new OcclusionGpuGeometryCache(registry); meshes = PrepareMeshes(cache,snapshot);
                var texture = pipeline.Depth.Levels[0]; submission = new OcclusionGpuSubmission(snapshot, new DepthReaders(pipeline,meshes));
                pipeline.RecordDepth(command, snapshot, Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 33f/19, .1f, 100), false),meshes);
                cache.Dispose();
                int pendingBefore = OcclusionGpuRetirement.PendingCount;
                submission.Dispose(); registry.Dispose();
                Assert.That(OcclusionGpuRetirement.PendingCount, Is.GreaterThan(pendingBefore), "Cancelled work must remain owned independently of the destroyed World.");
                Assert.That(submission.Released, Is.False, "An unsealed future graphics consumer still owns this submission.");
                Assert.That(texture.IsCreated(), Is.True);
                Assert.DoesNotThrow(() => snapshot.GetGeometry(geometry));
                Graphics.ExecuteCommandBuffer(command); submission.SealAfterLastConsumer();
                int frames = 0;
                while (!submission.Released && frames++ < 120) yield return null;
                Assert.That(submission.Released, Is.True); Assert.That(registry.AccountedResourceBytes, Is.Zero);
                Assert.That(texture == null || !texture.IsCreated(), Is.True);
                Assert.Throws<ObjectDisposedException>(() => snapshot.GetGeometry(geometry));
            }
            finally
            {
                if (submission != null && !submission.Released)
                {
                    submission.SealAfterLastConsumer(); submission.Dispose();
                    // Teardown can block for assertions only; production retirement is polled.
                    if (!submission.Released)
                    {
                        var drain = AsyncGPUReadback.Request(pipeline.Depth.Levels[0], 0);
                        drain.WaitForCompletion(); submission.Poll();
                    }
                }
                else if (submission == null) { pipeline?.Dispose(); meshes?.Dispose(); snapshot?.Dispose(); }
                cache?.Dispose();
                registry.Dispose(); UnityEngine.Object.Destroy(cube); UnityEngine.Object.Destroy(resources);
            }
        }
    }
}
