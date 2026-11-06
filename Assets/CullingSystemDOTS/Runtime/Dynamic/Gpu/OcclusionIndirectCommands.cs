using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal struct OcclusionGpuInstanceData
    {
        internal float4x4 LocalToWorld;
        internal float4 Color;
    }
    internal static class OcclusionIndirectCommands
    {
        internal static void Compact(CommandBuffer command, ComputeShader shader, GraphicsBuffer visible, GraphicsBuffer inputIds,
            GraphicsBuffer groupIds, GraphicsBuffer arguments, GraphicsBuffer outputIds, int instances, int groups)
        {
            if (instances <= 0 || groups <= 0) return;
            int reset = shader.FindKernel("ResetCounts"), compact = shader.FindKernel("Compact");
            command.SetComputeIntParam(shader,"InstanceCount",instances); command.SetComputeIntParam(shader,"GroupCount",groups);
            command.SetComputeIntParam(shader,"ArgumentStride",GraphicsBuffer.IndirectDrawIndexedArgs.size);
            command.SetComputeBufferParam(shader,reset,"Arguments",arguments);
            command.DispatchCompute(shader,reset,(groups+63)/64,1,1);
            command.SetComputeBufferParam(shader,compact,"Arguments",arguments);
            command.SetComputeBufferParam(shader,compact,"Visible",visible); command.SetComputeBufferParam(shader,compact,"InputIds",inputIds);
            command.SetComputeBufferParam(shader,compact,"GroupIds",groupIds); command.SetComputeBufferParam(shader,compact,"OutputIds",outputIds);
            command.DispatchCompute(shader,compact,(instances+63)/64,1,1);
        }
        internal static void Draw(Camera camera, Mesh mesh, Material material, Bounds bounds, int layer, GraphicsBuffer instances,
            GraphicsBuffer ids, GraphicsBuffer arguments, int commandIndex, ShadowCastingMode shadows = ShadowCastingMode.Off)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetBuffer("_OcclusionInstances",instances); properties.SetBuffer("_OcclusionInstanceIds",ids);
            var parameters = new RenderParams(material) { camera=camera, worldBounds=bounds, layer=layer, matProps=properties,
                shadowCastingMode=shadows, receiveShadows=false };
            Graphics.RenderMeshIndirect(parameters,mesh,arguments,1,commandIndex);
        }
    }
}
