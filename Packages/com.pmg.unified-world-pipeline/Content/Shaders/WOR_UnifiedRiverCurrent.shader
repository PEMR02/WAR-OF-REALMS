Shader "Project/WOR Unified River Current"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.78, 0.94, 1.0, 0.32)
        _Speed ("Speed", Float) = 1.35
        _StripeDensity ("Stripe Density", Float) = 1.0
        _Softness ("Softness", Range(0.02, 0.45)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+100"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Speed;
                half _StripeDensity;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half phase = frac(input.uv.x * max(_StripeDensity, 0.001h) - _Time.y * _Speed + input.color.r);
                half dash = 1.0h - smoothstep(0.0h, max(_Softness, 0.001h), abs(phase - 0.5h));
                half sideFade = smoothstep(0.0h, 0.28h, input.uv.y) * (1.0h - smoothstep(0.72h, 1.0h, input.uv.y));
                half alpha = _BaseColor.a * input.color.a * dash * sideFade;
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
