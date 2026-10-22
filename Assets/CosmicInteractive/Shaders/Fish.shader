Shader "Custom/Fish"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0, 0, 0, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _HeadX("Head X", Float) = 0
        _TailX("Tail X", Float) = 0
        _WaveLength("Wave Length", Float) = 0
        _Phase("Wave Phase", Float) = 0
        _AmplitudeScale("Amplitude Scale", Float) = 1
        _CenterAlpha("Center Alpha", Range(0, 1)) = 0.1
        _EdgeAlpha("Edge Alpha", Range(0, 1)) = 0.9
        _FresnelPower("Fresnel Power", Range(0.1, 8)) = 2
        _HighlightCenter("Highlight Center", Range(0, 1)) = 0.5
        _HighlightWidth("Highlight Width", Range(0.01, 0.5)) = 0.04
        _HighlightStrength("Highlight Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // frag が「A 掛け済みの rgb」と「A」を出す。Compose は color * (1 - fish.a) + fish.rgb で読む
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
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
                float _AmplitudeScale;                       // 波の振幅の倍率。Boid が個体ごとに渡す
                float _Phase;                                // 波の位相。Boid が速さを積分して、個体ごとに渡す
                float _CenterAlpha;
                float _EdgeAlpha;
                float _FresnelPower;
                float _HighlightCenter;
                float _HighlightWidth;
                float _HighlightStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                float3 pos = IN.positionOS.xyz;
                float span = _TailX - _HeadX;                // 頭から尾へのベクトル（符号つき）
                float len = abs(span);                       // 体長
                float t = saturate((pos.x - _HeadX) / span); // 体軸上の位置（頭 0、尾 1）
                float a = len * (0.05 - 0.13 * t + 0.28 * t * t) * _AmplitudeScale; // 論文の式（振幅）
                float k = 2.0 * PI / _WaveLength;            // _WaveLength は体長比（1 前後）
                float phase = k * t - _Phase;
                pos.z += a * sin(phase);

                // 変形後の傾き dz/dx。z = a(t) * sin(phase) を t で微分して、dt/dx = 1 / span で x の微分にする
                float dadt = len * (-0.13 + 0.56 * t) * _AmplitudeScale;
                float dzdt = dadt * sin(phase) + a * k * cos(phase);
                float slope = dzdt / span;

                // 法線を xz 平面で、傾きの角度（atan(slope)）だけ回す。cos と sin は三角関数を使わずに求める
                float c = rsqrt(1.0 + slope * slope);
                float s = slope * c;
                float3 n = IN.normalOS;
                float3 nDeformed = float3(n.x * c - n.z * s, n.y, n.x * s + n.z * c);

                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(pos);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(nDeformed);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);                       // 補間で長さが縮むので正規化し直す
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS); // 面からカメラへの向き
                float d = saturate(dot(N, V));                           // 1 = 正対、0 = 縁

                // 縁ほど濃い A（中心 _CenterAlpha から縁 _EdgeAlpha へ）
                float edge = pow(1.0 - d, _FresnelPower);
                float alphaBase = lerp(_CenterAlpha, _EdgeAlpha, edge);

                // d が _HighlightCenter の前後の細い帯だけ 1 になる（_HighlightWidth が帯の半幅）
                float h = (1.0 - smoothstep(0.0, _HighlightWidth, abs(d - _HighlightCenter))) * _HighlightStrength;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                // 色は A 掛け済み。帯の白は A に入れず rgb に足す（背景は (1 - alphaBase) のまま見える）
                half3 color = albedo * alphaBase + h * half3(1, 1, 1);
                return half4(color, alphaBase);
            }
            ENDHLSL
        }
    }
}
