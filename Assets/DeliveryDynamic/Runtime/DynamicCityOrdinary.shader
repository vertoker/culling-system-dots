Shader "CullingSystemDOTS/Samples/Dynamic City Ordinary"
{
    Properties { _BaseColor("Linear color",Vector)=(1,1,1,1) }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        CBUFFER_END
        #if defined(UNITY_DOTS_INSTANCING_ENABLED)
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4,_BaseColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4,_BaseColor)
        #endif
        struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
        ENDHLSL
        Pass
        {
            Name "Forward" Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            struct Varyings {float4 positionCS:SV_POSITION;float4 color:TEXCOORD0;};
            Varyings Vert(Attributes input)
            {UNITY_SETUP_INSTANCE_ID(input);Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.color=_BaseColor;return output;}
            float4 Frag(Varyings input):SV_Target {return float4(input.color.rgb,1);}
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection,_LightPosition;
            float4 VertShadow(Attributes input):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS=TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-positionWS);
                #else
                    float3 direction=_LightDirection;
                #endif
                float4 clip=TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,direction));
                if(round(_ShadowBias.z)==1)
                {
                    #if UNITY_REVERSED_Z
                        clip.z=min(clip.z,clip.w*UNITY_NEAR_CLIP_VALUE);
                    #else
                        clip.z=max(clip.z,clip.w*UNITY_NEAR_CLIP_VALUE);
                    #endif
                }
                return clip;
            }
            float4 FragShadow():SV_Target {return 0;}
            ENDHLSL
        }
    }
}
