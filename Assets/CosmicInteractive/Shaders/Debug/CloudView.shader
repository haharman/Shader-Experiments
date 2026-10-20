Shader "Debug/CloudView"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _DepthNear ("Depth Near (z)", Float) = 0.42
        _DepthFar ("Depth Far (z)", Float) = 0.58
        _DensityMax ("Density Max", Float) = 1
        _Hue ("Hue (0-1)", Range(0, 1)) = 0.08
        _BrightnessFar ("Brightness Far", Range(0, 1)) = 0.2
        _BrightnessNear ("Brightness Near", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _DepthNear;
            float _DepthFar;
            float _DensityMax;
            float _Hue;
            float _BrightnessFar;
            float _BrightnessNear;

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

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                float2 cloud = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rg;
                float near = saturate((_DepthFar - cloud.r) / (_DepthFar - _DepthNear));
                float saturation = saturate(cloud.g / _DensityMax);
                float brightness = lerp(_BrightnessFar, _BrightnessNear, near);
                float3 pureHue = saturate(abs(frac(_Hue + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1);
                float3 rgb = brightness * lerp(float3(1, 1, 1), pureHue, saturation);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = SRGBToLinear(rgb);
            #endif
                return float4(rgb, 1);
            }
            ENDHLSL
        }
    }
}
