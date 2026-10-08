Shader "Custom/Fish"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _HeadX("Head X", Float) = 0
        _TailX("Tail X", Float) = 0
        _WaveLength("Wave Length", Float) = 0
        _Speed("Speed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _HeadX;
                float _TailX;
                float _WaveLength;
                float _Speed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                float3 pos = IN.positionOS.xyz;
                float len = abs(_TailX - _HeadX);
                float t = saturate((pos.x - _HeadX) / (_TailX - _HeadX));
                float a = len * (0.05 - 0.13 * t + 0.28 * t * t); // 論文の式
                float k = 2.0 * PI / _WaveLength;            // _WaveLength も体長比（1 前後）
                pos.z += a * sin(k * t - _Time.y * _Speed);
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(pos);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}
