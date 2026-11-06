Shader "Hidden/CullingSystemDOTS/IndirectTransport"
{
    Properties { _BaseColor("Color", Color) = (1,0,0,1) }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
        #include "UnityIndirect.cginc"
        float4x4 _ProbeObjectToWorld;
        float4x4 _ProbeWorldToObject;
        float4 _BaseColor;
        float3 _LightDirection, _LightPosition;
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; };
        Varyings Vert(Attributes input, uint instanceID : SV_InstanceID)
        {
            InitIndirectDrawArgs(0);
            Varyings output;
            output.positionCS = TransformWorldToHClip(mul(_ProbeObjectToWorld, input.positionOS).xyz);
            return output;
        }
        Varyings ShadowVert(Attributes input, uint instanceID : SV_InstanceID)
        {
            InitIndirectDrawArgs(0);
            float3 positionWS = mul(_ProbeObjectToWorld, input.positionOS).xyz;
            float3 normalWS = normalize(mul(input.normalOS, (float3x3)_ProbeWorldToObject));
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 direction = normalize(_LightPosition - positionWS);
            #else
                float3 direction = _LightDirection;
            #endif
            Varyings output;
            output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, direction)));
            return output;
        }
        float4 Frag(Varyings input) : SV_Target { return _BaseColor; }
        float4 ShadowFrag(Varyings input) : SV_Target { return 0; }
        ENDHLSL
        Pass
        {
            Name "Forward" Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
