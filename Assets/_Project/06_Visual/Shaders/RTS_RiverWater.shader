Shader "Project/RTS River Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.35, 0.65, 0.85, 1)
        _DeepColor ("Deep", Color) = (0.1, 0.25, 0.45, 1)
        _FlowSpeed ("Flow UV / sec", Vector) = (0.06, 0.02, 0, 0)
        _BankSoft ("Bank blend", Range(0.05, 0.5)) = 0.22
        _Alpha ("Alpha", Range(0.3, 1)) = 0.88
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
                half4 _ShallowColor;
                half4 _DeepColor;
                float4 _FlowSpeed;
                half _BankSoft;
                half _Alpha;
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
                float2 u = IN.uv + _FlowSpeed.xy * _Time.y;
                float distFromCenter = abs(u.y - 0.5) * 2.0;
                float bankW = max(0.06, _BankSoft);
                float deepMix = 1.0 - smoothstep(0.0, bankW, distFromCenter);
                half4 col = lerp(_ShallowColor, _DeepColor, deepMix);
                col.a *= _Alpha;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
