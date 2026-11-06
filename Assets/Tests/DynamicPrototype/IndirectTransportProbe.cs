using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.DynamicPrototype.Tests
{
    internal sealed class IndirectTransportProbe : IDisposable
    {
        public readonly bool[] Hidden;
        public bool CastShadows;
        public bool ResourcesReleased { get; private set; }
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly Camera[] cameras;
        private readonly GraphicsBuffer[] arguments;
        private readonly GraphicsBuffer allArguments;
        private readonly ComputeShader compute;
        private readonly MaterialPropertyBlock properties;
        private readonly Bounds bounds;
        private GraphicsFence fence;
        private bool hasFence, awaitingContextEnd, disposeRequested;

        public IndirectTransportProbe(Mesh mesh, Material material, Matrix4x4 transform, Camera[] cameras,
            Func<GraphicsBuffer> createArguments = null)
        {
            if (mesh == null || material == null || cameras == null || cameras.Length == 0) throw new ArgumentException("Complete transport inputs are required.");
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsGraphicsFence || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
                throw new NotSupportedException("This transport proof is scoped to DX12 compute/fence support.");
            #if UNITY_EDITOR
            compute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Tests/DynamicPrototype/IndirectTransport.compute");
            #endif
            if (compute == null) throw new InvalidOperationException("Missing indirect transport compute fixture.");
            this.mesh = mesh; this.material = material; this.cameras = (Camera[])cameras.Clone();
            Hidden = new bool[cameras.Length];
            arguments = new GraphicsBuffer[cameras.Length];
            properties = new MaterialPropertyBlock(); properties.SetMatrix("_ProbeObjectToWorld", transform);
            properties.SetMatrix("_ProbeWorldToObject", transform.inverse);
            bounds = new Bounds(transform.MultiplyPoint3x4(mesh.bounds.center), Vector3.one * 100);
            var data = new[] { new GraphicsBuffer.IndirectDrawIndexedArgs {
                indexCountPerInstance = mesh.GetIndexCount(0), instanceCount = 1,
                startIndex = mesh.GetIndexStart(0), baseVertexIndex = mesh.GetBaseVertex(0), startInstance = 0 } };
            try
            {
                allArguments = createArguments != null ? createArguments() : CreateArguments();
                allArguments.SetData(data);
                for (int i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = createArguments != null ? createArguments() : CreateArguments();
                    arguments[i].SetData(data);
                }
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endContextRendering += EndContext;
            }
            catch { Release(); throw; }
        }

        private static GraphicsBuffer CreateArguments() => new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);

        private void Begin(ScriptableRenderContext context, Camera camera)
        {
            int view = Array.IndexOf(cameras, camera); if (view < 0 || disposeRequested) return;
            using (var command = new CommandBuffer { name = "Owned GPU indirect transport count" })
            {
                command.SetComputeIntParam(compute, "Hidden", Hidden[view] ? 1 : 0);
                command.SetComputeBufferParam(compute, 0, "Arguments", arguments[view]);
                command.DispatchCompute(compute, 0, 1, 1, 1);
                Graphics.ExecuteCommandBuffer(command);
            }
            var parameters = new RenderParams(material) { camera = camera, layer = 31, worldBounds = bounds,
                matProps = properties, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshIndirect(parameters, mesh, arguments[view]);
            if (CastShadows)
            {
                parameters.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                Graphics.RenderMeshIndirect(parameters, mesh, allArguments);
            }
            awaitingContextEnd = true;
        }

        private void EndContext(ScriptableRenderContext context, List<Camera> renderedCameras)
        {
            if (awaitingContextEnd)
            {
                // Installed URP submits its camera RenderGraph before this public context-end event.
                // This fence follows the color AND shadow consumers, not merely the count dispatch.
                fence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
                hasFence = true; awaitingContextEnd = false;
            }
            if (disposeRequested) BeginRetirement();
        }

        public void Dispose()
        {
            if (disposeRequested) return; disposeRequested = true;
            RenderPipelineManager.beginCameraRendering -= Begin;
            if (!awaitingContextEnd) BeginRetirement();
        }

        private void BeginRetirement()
        {
            RenderPipelineManager.endContextRendering -= EndContext;
            if (!hasFence || fence.passed) Release();
            #if UNITY_EDITOR
            else UnityEditor.EditorApplication.update += PollRetirement;
            #endif
        }

        private void PollRetirement()
        {
            if (!fence.passed) return;
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PollRetirement;
            #endif
            Release();
        }

        private void Release()
        {
            if (ResourcesReleased) return;
            if (arguments != null) foreach (var buffer in arguments) buffer?.Dispose();
            allArguments?.Dispose(); ResourcesReleased = true;
        }
    }
}
