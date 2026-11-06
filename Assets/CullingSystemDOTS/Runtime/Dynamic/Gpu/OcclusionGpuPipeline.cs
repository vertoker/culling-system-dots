using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionGpuPipeline : IDisposable
    {
        internal readonly OcclusionDepthResources Depth;
        private readonly ComputeShader pyramid, bounds;
        private readonly ulong worldIdentity;
        private readonly Material depthMaterial;
        internal readonly float FarDepth;
        private bool disposed;
        internal static bool IsSupported => SystemInfo.supportsComputeShaders && SystemInfo.supportsGraphicsFence &&
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Blend | GraphicsFormatUsage.LoadStore | GraphicsFormatUsage.Sample);
        internal OcclusionGpuPipeline(OcclusionWorld registry, OcclusionShaderResources resources, Unity.Mathematics.int2 size, float farDepth)
        {
            if (!IsSupported) throw new NotSupportedException("Runtime occlusion requires accepted DX12 compute/fence and R32 render/blend/load-store support.");
            if (resources == null || resources.Depth == null || resources.Pyramid == null || resources.Bounds == null || !resources.Depth.isSupported)
                throw new ArgumentException("Complete supported depth resources are required.");
            if (float.IsNaN(farDepth) || float.IsInfinity(farDepth) || farDepth <= 0) throw new ArgumentOutOfRangeException(nameof(farDepth));
            pyramid = resources.Pyramid; bounds = resources.Bounds; FarDepth = farDepth; worldIdentity = registry?.Identity ?? 0;
            try
            {
                depthMaterial = new Material(resources.Depth) { hideFlags = HideFlags.HideAndDontSave };
                Depth = new OcclusionDepthResources(registry, size);
            }
            catch { Dispose(); throw; }
        }
        internal void RecordDepth(CommandBuffer command, OcclusionSnapshotLease snapshot, Matrix4x4 view, Matrix4x4 gpuProjection)
        {
            CheckAlive();
            if (command == null || snapshot == null || snapshot.WorldIdentity != worldIdentity ||
                !OcclusionValidity.MatrixFinite(view) || !OcclusionValidity.MatrixFinite(gpuProjection)) throw new ArgumentException("Current finite view and matching snapshot are required.");
            command.SetRenderTarget(Depth.Levels[0]); command.SetViewport(new Rect(0, 0, Depth.Size.x, Depth.Size.y));
            command.ClearRenderTarget(false, true, new Color(FarDepth, 0, 0, 0));
            var properties = new MaterialPropertyBlock();
            properties.SetMatrix("_OcclusionView", view); properties.SetMatrix("_OcclusionViewProjection", gpuProjection * view);
            properties.SetFloat("_FarDepth", FarDepth);
            foreach (var instance in snapshot.Instances)
            {
                if ((instance.Roles & OcclusionRole.Occluder) == 0 || instance.Opaque == 0 || instance.Prepared == 0) continue;
                float determinant = math.determinant(new float3x3(instance.LocalToWorld.c0.xyz, instance.LocalToWorld.c1.xyz, instance.LocalToWorld.c2.xyz));
                // Per-instance mirrored culling has no accepted raster contract yet. Omission
                // reduces occlusion; rendering the wrong winding could invent opaque coverage.
                if (!OcclusionValidity.Finite(determinant) || determinant <= 0) continue;
                properties.SetMatrix("_OcclusionObjectToWorld", instance.LocalToWorld);
                command.DrawMesh(snapshot.GetMesh(instance.Geometry), Matrix4x4.identity, depthMaterial, instance.SubMesh, 0, properties);
            }
        }
        internal void RecordHierarchy(CommandBuffer command)
        {
            CheckAlive();
            var shader = pyramid; int kernel = shader.FindKernel("Reduce");
            command.SetComputeFloatParam(shader, "FarDepth", FarDepth);
            for (int i = 1; i < Depth.Levels.Length; i++)
            {
                var source = Depth.Levels[i - 1]; var target = Depth.Levels[i];
                command.SetComputeIntParams(shader, "SourceSize", source.width, source.height);
                command.SetComputeIntParams(shader, "DestinationSize", target.width, target.height);
                command.SetComputeTextureParam(shader, kernel, "Source", source);
                command.SetComputeTextureParam(shader, kernel, "Destination", target);
                command.DispatchCompute(shader, kernel, (target.width + 7) / 8, (target.height + 7) / 8, 1);
            }
        }
        internal void RecordProjectedTests(CommandBuffer command, GraphicsBuffer candidates, GraphicsBuffer visible, int count)
        {
            CheckBuffers(command, candidates, visible, count, 24); if (count == 0) return;
            var shader = bounds; int kernel = shader.FindKernel("TestProjected");
            ConfigureTests(command, kernel, visible, count);
            command.SetComputeBufferParam(shader, kernel, "Candidates", candidates);
            command.DispatchCompute(shader, kernel, (count + 63) / 64, 1, 1);
        }
        internal void RecordWorldTests(CommandBuffer command, GraphicsBuffer candidates, GraphicsBuffer visible, int count,
            Matrix4x4 view, Matrix4x4 gpuProjection, float nearPlane)
        {
            CheckBuffers(command, candidates, visible, count, 32); if (count == 0) return;
            if (!OcclusionValidity.MatrixFinite(view) || !OcclusionValidity.MatrixFinite(gpuProjection) ||
                !OcclusionValidity.Finite(nearPlane) || nearPlane <= 0) throw new ArgumentException("A finite current projection and positive near plane are required.");
            var shader = bounds; int kernel = shader.FindKernel("TestWorld");
            ConfigureTests(command, kernel, visible, count);
            command.SetComputeMatrixParam(shader, "CameraView", view); command.SetComputeMatrixParam(shader, "CameraProjection", gpuProjection);
            command.SetComputeFloatParam(shader, "NearPlane", nearPlane);
            command.SetComputeFloatParam(shader, "RasterYSign", SystemInfo.graphicsUVStartsAtTop ? -1 : 1);
            command.SetComputeBufferParam(shader, kernel, "WorldCandidates", candidates);
            command.DispatchCompute(shader, kernel, (count + 63) / 64, 1, 1);
        }
        private void ConfigureTests(CommandBuffer command, int kernel, GraphicsBuffer visible, int count)
        {
            for (int i = 0; i < 16; i++) command.SetComputeTextureParam(bounds, kernel, "Depth" + i, Depth.Levels[Math.Min(i, Depth.Levels.Length - 1)]);
            command.SetComputeIntParams(bounds, "ReferenceSize", Depth.Size.x, Depth.Size.y);
            command.SetComputeIntParam(bounds, "LevelCount", Depth.Levels.Length); command.SetComputeIntParam(bounds, "CandidateCount", count);
            command.SetComputeFloatParam(bounds, "FarDepth", FarDepth); command.SetComputeFloatParam(bounds, "DepthBias", .001f);
            command.SetComputeBufferParam(bounds, kernel, "Visible", visible);
        }
        private void CheckBuffers(CommandBuffer command, GraphicsBuffer candidates, GraphicsBuffer visible, int count, int stride)
        {
            CheckAlive(); if (command == null || count < 0) throw new ArgumentException("A command and nonnegative candidate count are required.");
            if (count == 0) return;
            if (candidates == null || visible == null || !candidates.IsValid() || !visible.IsValid() || candidates.count < count || visible.count < count ||
                candidates.stride != stride || visible.stride != 4) throw new ArgumentException("Current correctly sized GPU buffers are required.");
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Depth?.Dispose();
            if (depthMaterial != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(depthMaterial); else UnityEngine.Object.DestroyImmediate(depthMaterial); }
        }
        private void CheckAlive() { if (disposed) throw new ObjectDisposedException(nameof(OcclusionGpuPipeline)); }
    }
}
