Shader "Project/RTS Ground Decal"
{
    // Quad under buildings: dirt texture with alpha fade at edges. Unlit, no shadows.
    Properties
    {
        _MainTex    ("Dirt Texture", 2D) = "brown" {}
        _Color      ("Tint", Color) = (1,1,1,1)
        _EdgeFade   ("Edge fade (0=sharp, 0.5=soft)", Range(0, 0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+1" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Offset -1, -1

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv        : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float  fog       : TEXCOORD1;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _EdgeFade;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                float2 d = abs(IN.uv - 0.5) * 2.0; // 0 at center, 1 at edge
                float edge = max(d.x, d.y);
                float a = 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, edge);
                col.a *= a;
                col.rgb = MixFog(col.rgb, IN.fog);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
