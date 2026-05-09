Shader "Project/RTS River Water"
{
    Properties
    {
        _MainTex ("Water Texture", 2D) = "white" {}
        _ShallowColor ("Shallow", Color) = (0.35, 0.65, 0.85, 1)
        _DeepColor ("Deep", Color) = (0.1, 0.25, 0.45, 1)
        _FlowSpeed ("Flow UV / sec", Vector) = (0.06, 0.02, 0, 0)
        _FlowDirection ("Flow Direction XY", Vector) = (1, 0.35, 0, 0)
        _WaterFlowSpeed ("Water Flow Speed", Range(0, 3)) = 1
        _DistortStrength ("Distort Strength", Range(0, 0.08)) = 0.02
        _DetailBlend ("Detail Blend", Range(0, 1)) = 0.5
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
                float4 _MainTex_ST;
                half4 _ShallowColor;
                half4 _DeepColor;
                float4 _FlowSpeed;
                float4 _FlowDirection;
                half _WaterFlowSpeed;
                half _DistortStrength;
                half _DetailBlend;
                half _BankSoft;
                half _Alpha;
            CBUFFER_END
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                float2 baseUv = IN.uv;
                float2 flowDir = _FlowDirection.xy;
                if (dot(flowDir, flowDir) < 1e-5)
                    flowDir = _FlowSpeed.xy;
                float len = max(1e-4, length(flowDir));
                flowDir /= len;
                float flowMag = max(length(_FlowSpeed.xy), 0.0001);
                float flow = flowMag * _WaterFlowSpeed;

                // Distorsion procedural leve para romper patrones de repeticion.
                float n1 = frac(sin(dot(baseUv * 31.7 + _Time.yy * 0.27, float2(12.9898, 78.233))) * 43758.5453);
                float n2 = frac(sin(dot(baseUv.yx * 27.1 + _Time.yy * 0.19, float2(39.3468, 11.135))) * 24634.6345);
                float2 distort = (float2(n1, n2) - 0.5) * (_DistortStrength * 2.0);

                float2 u = baseUv + flowDir * (flow * _Time.y) + distort;
                float2 u2 = baseUv * 1.37 + float2(0.173, 0.287) - flowDir.yx * (flow * 0.63 * _Time.y) + distort * 0.6;
                half3 texA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, u).rgb;
                half3 texB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, u2).rgb;
                half texMix = lerp(0.35h, 0.65h, _DetailBlend);
                half texLum = dot(lerp(texA, texB, texMix), half3(0.333h, 0.333h, 0.333h));
                float distFromCenter = abs(u.y - 0.5) * 2.0;
                float bankW = max(0.06, _BankSoft);
                float deepMix = 1.0 - smoothstep(0.0, bankW, distFromCenter);
                half4 col = lerp(_ShallowColor, _DeepColor, deepMix);
                col.rgb *= lerp(0.82h, 1.18h, texLum);
                col.a *= _Alpha;
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
