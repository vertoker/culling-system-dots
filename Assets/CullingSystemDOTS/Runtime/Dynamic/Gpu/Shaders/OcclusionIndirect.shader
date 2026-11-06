Shader "CullingSystemDOTS/Dynamic Indirect Unlit"
{
    Properties { _BaseColor("Color",Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "CullingSystemDOTSIndirect"="1" }
        HLSLINCLUDE
        #include "UnityCG.cginc"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #include "UnityIndirect.cginc"
        struct InstanceRecord { float4x4 objectToWorld; float4 color; };
        StructuredBuffer<InstanceRecord> _OcclusionInstances;
        StructuredBuffer<uint> _OcclusionInstanceIds;
        InstanceRecord LoadInstance(uint instanceID)
        {
            InitIndirectDrawArgs(0);
            return _OcclusionInstances[_OcclusionInstanceIds[GetIndirectInstanceID_Base(instanceID)]];
        }
        ENDHLSL
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4 _BaseColor;
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:TEXCOORD0; };
            Varyings Vert(float4 positionOS:POSITION,uint instanceID:SV_InstanceID)
            {
                InstanceRecord instance=LoadInstance(instanceID); Varyings output;
                output.positionCS=mul(UNITY_MATRIX_VP,mul(instance.objectToWorld,positionOS)); output.color=instance.color*_BaseColor; return output;
            }
            float4 Frag(Varyings input):SV_Target { return float4(input.color.rgb,1); }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float4 _ShadowBias;
            float3 _LightDirection;
            float3 _LightPosition;
            float4 ShadowVert(float4 positionOS:POSITION,float3 normalOS:NORMAL,uint instanceID:SV_InstanceID):SV_POSITION
            {
                InstanceRecord instance=LoadInstance(instanceID);
                float3 positionWS=mul(instance.objectToWorld,positionOS).xyz;
                // Cofactors transform normals for the accepted positive-determinant matrices,
                // including nonuniform scale, without requiring a second instance matrix.
                float3x3 m=(float3x3)instance.objectToWorld;
                float3x3 cofactors=float3x3(cross(m[1],m[2]),cross(m[2],m[0]),cross(m[0],m[1]));
                float3 normalWS=mul(cofactors,normalOS);
                normalWS=dot(normalWS,normalWS)>1e-20?normalize(normalWS):float3(0,1,0);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS=normalize(_LightPosition-positionWS);
                #else
                    float3 lightDirectionWS=_LightDirection;
                #endif
                positionWS+=lightDirectionWS*_ShadowBias.x;
                positionWS+=normalWS*((1-saturate(dot(lightDirectionWS,normalWS)))*_ShadowBias.y);
                float4 positionCS=mul(UNITY_MATRIX_VP,float4(positionWS,1));
                if (round(_ShadowBias.z)==1)
                {
                    #if defined(UNITY_REVERSED_Z)
                        positionCS.z=min(positionCS.z,positionCS.w*UNITY_NEAR_CLIP_VALUE);
                    #else
                        positionCS.z=max(positionCS.z,positionCS.w*UNITY_NEAR_CLIP_VALUE);
                    #endif
                }
                return positionCS;
            }
            float4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
