Shader "Project/River Water Simple"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.25, 0.55, 0.88, 0.82)
        _Alpha ("Alpha", Range(0.2, 1)) = 0.78
        _ScrollV ("Scroll V (world-ish)", Range(0, 2)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Alpha;
                half _ScrollV;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float edgeDist = min(IN.uv.x, 1.0 - IN.uv.x);
                half edgeFoam = 1.0h - smoothstep(0.0h, 0.18h, (half)edgeDist);
                edgeFoam = saturate(edgeFoam);
                float v = IN.uv.y + _Time.y * _ScrollV;
                half3 col = _BaseColor.rgb * (0.92h + 0.08h * frac(v * 0.37h));
                col += edgeFoam * half3(0.12h, 0.14h, 0.16h);
                half4 outC = half4(col, _BaseColor.a * _Alpha);
                outC.rgb = MixFog(outC.rgb, IN.fogFactor);
                return outC;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
