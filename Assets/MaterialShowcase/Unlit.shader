Shader "Custom/Unlit"
{
    Properties
    {
        [MainColor] _Color("Color", Color) = (1, 1, 1, 1)
        [MainTexture] _Texture("Texture", 2D) = "white" { }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline" }
        
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag 
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes // =appdata
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            TEXTURE2D(_Texture);
            SAMPLER(sampler_Texture);
            
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Texture_ST;
            CBUFFER_END
            
            struct Varyings // = v2f
            {
                float4 position : SV_POSITION;
                float uv : TEXCOORD0;
            };
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.position = TransformObjectToHClip(IN.position.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _Texture);
                return OUT;
            }
            
            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_Texture, sampler_Texture, IN.uv) * _Color;
                return color;
            }
            ENDHLSL
        }
    }
}