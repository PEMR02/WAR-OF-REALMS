Shader "Project/RTS Tree Stylized Lit"
{
    // URP forward + sombras. Gradiente 3 pasos, wrap diffuse, shadow floor, fake fill light, ambiente escalado.
    // GradientMid: altura normalizada pie-copa donde termina Bottom-Mid y empieza Mid-Top.
    // ShadowFloor: suelo de mascara directa. MinimumLight + MinimumNeutral: piso legible aun con albedo muy oscuro.
    Properties
    {
        [Header(Base)]
        _BaseMap("Albedo", 2D) = "white" {}
        [HDR] _BaseColor("Tint", Color) = (1, 1, 1, 1)

        [Header(Gradient vertical)]
        _BottomColor("Bottom Color", Color) = (0.32, 0.28, 0.22, 1)
        _MidColor("Mid Tone Color", Color) = (0.36, 0.42, 0.24, 1)
        _TopColor("Top Color", Color) = (0.62, 0.68, 0.42, 1)
        _GradientMid("Mid blend height", Range(0.05, 0.95)) = 0.45
        _GradientStrength("Gradient Strength", Range(0, 1)) = 0.78
        _GradientHeight("Gradient Height (m)", Float) = 7

        [Header(Fake light)]
        _FakeLightDirection("Fake Light Direction", Vector) = (0.65, 0.3, 0.7, 0)
        [HDR] _FakeLightTint("Fake Light Tint", Color) = (1, 0.95, 0.85, 1)
        _FakeLightStrength("Fake Light Strength", Range(0, 1)) = 0.2

        [Header(Lighting tune)]
        _ShadowFloor("Shadow Floor / Min Light", Range(0, 0.55)) = 0.2
        _LightWrap("Light wrap softness", Range(0, 1)) = 0.4
        _AmbientScale("Ambient scale", Range(0.5, 2)) = 1.2
        _MinAlbedo("Albedo value floor", Range(0, 0.25)) = 0.05
        _MinimumLight("Minimum Light", Range(0, 0.5)) = 0.3
        _MinimumNeutral("Minimum neutral fill", Color) = (0.34, 0.41, 0.28, 1)

        [Header(Variacion de color)]
        _NoiseScale("Noise Scale", Float) = 0.22
        _NoiseStrength("Noise Strength", Range(0, 0.35)) = 0.07

        [Header(Superficie mate)]
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0

        [Header(Alpha clip foliage)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;

            half4 _BaseColor;
            half4 _BottomColor;
            half4 _MidColor;
            half4 _TopColor;
            half  _GradientMid;
            half  _GradientStrength;
            float _GradientHeight;
            float4 _FakeLightDirection;
            half4 _FakeLightTint;
            half  _FakeLightStrength;
            half  _ShadowFloor;
            half  _LightWrap;
            half  _AmbientScale;
            half  _MinAlbedo;
            half  _MinimumLight;
            half4 _MinimumNeutral;
            float _NoiseScale;
            half  _NoiseStrength;
            half  _Metallic;
            half  _Smoothness;
            half  _Cutoff;

            float SoftNoise3(float3 p)
            {
                float3 f = frac(p);
                float3 i = floor(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = frac(sin(dot(i + float3(0, 0, 0), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n100 = frac(sin(dot(i + float3(1, 0, 0), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n010 = frac(sin(dot(i + float3(0, 1, 0), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n110 = frac(sin(dot(i + float3(1, 1, 0), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n001 = frac(sin(dot(i + float3(0, 0, 1), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n101 = frac(sin(dot(i + float3(1, 0, 1), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n011 = frac(sin(dot(i + float3(0, 1, 1), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float n111 = frac(sin(dot(i + float3(1, 1, 1), float3(127.1, 311.7, 74.7))) * 43758.5453);
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                VertexPositionInputs posIn = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = posIn.positionCS;
                o.positionWS = posIn.positionWS;
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                #if defined(_ALPHATEST_ON)
                clip(tex.a - _Cutoff);
                #endif

                float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                float h = max(_GradientHeight, 0.001);
                float gradT = saturate((IN.positionWS.y - rootWS.y) / h);
                half mid = saturate(_GradientMid);
                half invM = 1.0h / max(mid, 0.001h);
                half inv1M = 1.0h / max(1.0h - mid, 0.001h);
                half tLow = saturate(gradT * invM);
                half tHigh = saturate((gradT - mid) * inv1M);
                half3 gradLowSeg = lerp(_BottomColor.rgb, _MidColor.rgb, tLow);
                half3 gradHighSeg = lerp(_MidColor.rgb, _TopColor.rgb, tHigh);
                half useHigh = step(mid, gradT);
                half3 gradTint = lerp(gradLowSeg, gradHighSeg, useHigh);
                half3 albedo = lerp(tex.rgb, tex.rgb * gradTint, _GradientStrength);

                float3 np = IN.positionWS * max(_NoiseScale, 0.001);
                float n = SoftNoise3(np) * 2.0 - 1.0;
                albedo *= 1.0 + n * _NoiseStrength;
                half floorV = saturate(_MinAlbedo);
                albedo = max(albedo, half3(floorV, floorV, floorV));

                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half nd = dot(N, mainLight.direction);
                half wrap = saturate(_LightWrap);
                half nl = saturate((nd + wrap) / (1.0h + wrap));
                half atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half directMask = nl * atten;
                directMask = max(directMask, saturate(_ShadowFloor));
                half3 diffuse = mainLight.color * directMask;

                half3 ambient = SampleSH(N) * _AmbientScale;

                float3 fDir = _FakeLightDirection.xyz;
                float fLen = length(fDir);
                float3 fakeDir = fLen > 1e-4 ? fDir * (1.0 / fLen) : float3(0.65, 0.3, 0.7);
                half fakeTerm = saturate(dot(N, fakeDir) * 0.5h + 0.5h);
                half3 fakeAdd = albedo * (_FakeLightTint.rgb * (_FakeLightStrength * fakeTerm));

                half3 radiance = diffuse + ambient;
                half3 diffuseColor = albedo * (half3(1, 1, 1) - _Metallic * half3(1, 1, 1));
                half3 color = diffuseColor * radiance + fakeAdd;

                half smooth = _Smoothness;
                if (smooth > 0.02h)
                {
                    half3 V = normalize(half3(_WorldSpaceCameraPos.xyz - IN.positionWS));
                    half3 H = normalize(mainLight.direction + V);
                    half spec = pow(saturate(dot(N, H)), (1.0h - smooth) * 128.0h + 4.0h);
                    color += (spec * smooth) * mainLight.color * atten * lerp(half3(0.04, 0.04, 0.04), albedo, _Metallic);
                }

                half minLight = saturate(_MinimumLight);
                half3 byAlbedo = albedo * minLight;
                half3 byNeutral = _MinimumNeutral.rgb * minLight;
                half3 colorFloor = max(byAlbedo, byNeutral);
                color = max(color, colorFloor);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Mismos uniforms que URP rellena en el pase ShadowCaster (no usar _MainLightPosition aquí).
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;

            float4 GetShadowPositionHClip(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                return positionCS;
            }

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings o;
                o.positionCS = GetShadowPositionHClip(IN);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
