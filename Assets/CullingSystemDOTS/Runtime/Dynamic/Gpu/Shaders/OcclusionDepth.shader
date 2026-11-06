Shader "Hidden/CullingSystemDOTS/DynamicLinearDepth"
{
    SubShader
    {
        Pass
        {
            Cull Back ZWrite Off ZTest Always Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4x4 _OcclusionObjectToWorld, _OcclusionView, _OcclusionViewProjection;
            float _FarDepth;
            struct Varyings { float4 positionCS : SV_POSITION; float depth : TEXCOORD0; };
            Varyings Vert(float4 positionOS : POSITION)
            {
                float4 world = mul(_OcclusionObjectToWorld, positionOS);
                Varyings output; output.positionCS = mul(_OcclusionViewProjection, world);
                output.depth = -mul(_OcclusionView, world).z; return output;
            }
            float Frag(Varyings input) : SV_Target
            {
                return isfinite(input.depth) && input.depth > 0 && input.depth < _FarDepth ? input.depth : _FarDepth;
            }
            ENDHLSL
        }
    }
}
