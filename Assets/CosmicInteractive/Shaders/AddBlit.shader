Shader "Custom/AddBlit"
{
    Properties
    {
        _MainTex("MainTexture", 2D) = "black"{}
        _ScaleOffsetUV("Scale Offset UV", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline"}
        ZTest Always
        Cull Off
        ZWrite Off
        Blend One One
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _ScaleOffsetUV;
            
            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _ScaleOffsetUV.xy + _ScaleOffsetUV.zw; // ScaleとTranslation（Offset）
                return output;
            }
            
            float4 frag (Varyings input) : SV_Target
            {
                if (any(input.uv < 0.0) || any(input.uv > 1.0)) return 0;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            }
            ENDHLSL
        }
    }
}