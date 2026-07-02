Shader "Project/WOR Lake Water"
{
    Properties
    {
        _DeepColor       ("Deep Color",         Color)             = (0.05, 0.38, 0.82, 0.96)
        _MidColor        ("Mid Depth Color",    Color)             = (0.08, 0.48, 0.86, 0.92)
        _ShoreColor      ("Shallow Color",      Color)             = (0.22, 0.62, 0.88, 0.72)
        _FoamColor       ("Foam Color",         Color)             = (0.82, 0.94, 1.00, 0.58)
        _IntersectionColor ("Shore Line Color", Color)             = (0.70, 0.90, 1.00, 0.36)

        _FoamTex         ("Foam Texture",       2D) = "black" {}
        _WaveTex         ("Wave Normal",        2D) = "bump"  {}
        _IntersectionNoise ("Shore Noise",      2D) = "gray" {}

        _DepthCurve      ("Depth Curve",        Range(0.55, 1.35)) = 0.92
        _DeepCoreStart   ("Deep Core Start",    Range(0.35, 0.90)) = 0.68
        _DeepCoreEnd     ("Deep Core End",      Range(0.70, 1.00)) = 0.96
        _DeepCoreStrength ("Deep Core Strength", Range(0.00, 1.00)) = 0.14
        _MouthFoamStrength ("Mouth Foam",       Range(0.00, 1.00)) = 0.18

        _FoamAmount      ("Rim Foam Amount",    Range(0.00, 1.00)) = 0.24
        _FoamEdgePower   ("Rim Foam Width",     Range(1.00, 12.0)) = 6.20
        _ShoreBandPower  ("Shore Tint Width",   Range(1.00, 14.0)) = 3.80
        _DepthSoftness   ("Depth Edge Softness", Range(0.00, 0.25)) = 0.10
        _ShoreLineStrength ("Shore Line Strength", Range(0.00, 1.00)) = 0.18
        _FoamScale       ("Foam Scale",         Range(0.02, 0.50)) = 0.14
        _FoamSpeed       ("Foam Speed",         Range(0.00, 1.00)) = 0.08

        _IntersectionTiling    ("Shore Noise Tiling",   Range(0.02, 0.80)) = 0.16
        _IntersectionSpeed     ("Shore Line Speed",     Range(0.00, 0.30)) = 0.05
        _IntersectionClipping  ("Shore Line Clip",      Range(0.00, 1.00)) = 0.62
        _IntersectionFalloff   ("Shore Line Falloff",   Range(0.01, 1.00)) = 0.28

        _WaveScale       ("Wave Scale",         Range(0.02, 0.60)) = 0.22
        _WaveSpeed       ("Wave Speed",         Range(0.00, 1.00)) = 0.06
        _WaveStrength    ("Wave Strength",      Range(0.00, 0.60)) = 0.08
        _SparkleStrength ("Sparkle Strength",   Range(0.00, 2.00)) = 0.14
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardLake"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FoamTex);              SAMPLER(sampler_FoamTex);
            TEXTURE2D(_WaveTex);              SAMPLER(sampler_WaveTex);
            TEXTURE2D(_IntersectionNoise);    SAMPLER(sampler_IntersectionNoise);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 vtxColor   : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MidColor;
                half4 _ShoreColor;
                half4 _FoamColor;
                half4 _IntersectionColor;
                half  _DepthSoftness;
                half  _DepthCurve;
                half  _DeepCoreStart;
                half  _DeepCoreEnd;
                half  _DeepCoreStrength;
                half  _MouthFoamStrength;
                half  _FoamAmount;
                half  _FoamEdgePower;
                half  _ShoreBandPower;
                half  _ShoreLineStrength;
                half  _FoamScale;
                half  _FoamSpeed;
                half  _IntersectionTiling;
                half  _IntersectionSpeed;
                half  _IntersectionClipping;
                half  _IntersectionFalloff;
                half  _WaveScale;
                half  _WaveSpeed;
                half  _WaveStrength;
                half  _SparkleStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.positionWS = posWS;
                o.uv         = IN.uv;
                o.vtxColor   = IN.color;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float SampleWorIntersection(float2 wp, float coastMask, float t)
            {
                if (coastMask < 0.001)
                    return 0.0;

                float2 time = float2(t * (float)_IntersectionSpeed * 0.3, t * (float)_IntersectionSpeed * 0.18);
                float2 nUV = wp * (float)_IntersectionTiling;
                float noise = SAMPLE_TEXTURE2D(_IntersectionNoise, sampler_IntersectionNoise, nUV + time).r;
                float noise2 = SAMPLE_TEXTURE2D(_IntersectionNoise, sampler_IntersectionNoise, nUV * 1.38 - time).r;
                float mask = saturate((noise * 0.55 + noise2 * 0.45) * coastMask);
                return smoothstep((float)_IntersectionClipping - 0.12, (float)_IntersectionClipping + 0.08, mask) * coastMask;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t   = _Time.y;
                float2 wp = IN.positionWS.xz;

                float worMode = 1.0 - step(0.5, IN.vtxColor.a);
                float depthVtx = saturate(IN.vtxColor.r);
                float depthUv  = saturate(IN.uv.y);
                float depth01  = lerp(depthUv, depthVtx, worMode);
                float depthAA = fwidth(depth01) * 1.8 + (float)_DepthSoftness;
                depth01 = smoothstep(0.04 - depthAA, 0.96 + depthAA, depth01);
                depth01 = pow(saturate(depth01), (float)_DepthCurve);

                float shore = pow(saturate(1.0 - depth01), (float)_ShoreBandPower);
                float shoreTintBand = smoothstep(0.82, 0.99, shore);
                float coastProx = saturate(1.0 - depth01);
                float coastMask = smoothstep(0.90, 0.998, coastProx);
                float mouthFoam = saturate(IN.vtxColor.g) * (float)_MouthFoamStrength;

                float2 wuv1 = wp * (float)_WaveScale
                            + float2( t * (float)_WaveSpeed * 0.22, t * (float)_WaveSpeed * 0.16);
                float2 wuv2 = wp * (float)_WaveScale * 0.71
                            + float2(-t * (float)_WaveSpeed * 0.31, t * (float)_WaveSpeed * 0.52);
                float n1 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, wuv1).r;
                float n2 = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, wuv2).r;
                float waveMix = n1 * 0.58 + n2 * 0.42;
                float interiorMask = smoothstep(0.42, 0.82, depth01);
                float waveLum = 1.0 + (waveMix - 0.5) * (float)_WaveStrength * 0.18 * (1.0 - interiorMask * 0.82);

                float deepCore = smoothstep((float)_DeepCoreStart, (float)_DeepCoreEnd, depth01) * (float)_DeepCoreStrength;
                half4 waterCol = lerp(_ShoreColor, _MidColor, (half)saturate(shoreTintBand * 0.42));
                waterCol = lerp(waterCol, _DeepColor, (half)(deepCore * 0.55h + interiorMask * 0.28h));
                waterCol.rgb *= (half)waveLum;
                waterCol.a = lerp(_ShoreColor.a, _DeepColor.a, (half)(interiorMask * 0.5h + deepCore * 0.2h));

                float sparkle = pow(saturate((n1 - 0.52) * (n2 - 0.52) * 6.0), 6.0);
                waterCol.rgb += (half)(sparkle * (float)_SparkleStrength * coastMask * 0.05);

                float intersection = SampleWorIntersection(wp, coastMask, t);
                float coastLine = coastMask * (float)_ShoreLineStrength;

                float2 fuv1 = wp * (float)_FoamScale
                            + float2( t * (float)_FoamSpeed * 0.18, t * (float)_FoamSpeed * 0.10);
                half foamTex = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv1).r;
                float foamRim = pow(saturate(shore), (float)_FoamEdgePower)
                              * smoothstep(1.0 - (float)_FoamAmount - 0.08, 1.0 - (float)_FoamAmount + 0.08, (float)foamTex)
                              * coastMask * 0.18;

                float mouthLine = mouthFoam * (0.42 + intersection * 0.22);
                float shoreWhite = saturate(max(mouthLine, max(coastLine * 0.65, foamRim)));

                half4 result = waterCol;
                result.rgb = lerp(result.rgb, _IntersectionColor.rgb, (half)(shoreWhite * _IntersectionColor.a));
                result.rgb = lerp(result.rgb, _FoamColor.rgb, (half)(shoreWhite * _FoamColor.a * 0.55h));
                result.a   = max(waterCol.a, (half)(shoreWhite * 0.38h));
                result.rgb = MixFog(result.rgb, IN.fogFactor);
                return result;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
