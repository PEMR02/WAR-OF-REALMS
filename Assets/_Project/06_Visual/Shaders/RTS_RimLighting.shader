Shader "Project/RTS Rim Lighting"
{
    // Fresnel rim lighting for units and buildings — improves readability from RTS camera distance.
    Properties
    {
        [Header(Base)]
        _BaseColor    ("Base Color", Color) = (1,1,1,1)
        _BaseMap      ("Base Map", 2D) = "white" {}

        [Header(Rim)]
        _RimColor     ("Rim Color", Color) = (1, 0.9, 0.75, 1)
        _RimIntensity ("Rim Intensity", Range(0, 1)) = 0.2
        _RimPower     ("Rim Power", Range(1, 8)) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

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
                float3 viewDirWS  : TEXCOORD2;
                float  fog        : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _RimColor;
            half _RimIntensity;
            half _RimPower;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                o.viewDirWS = _WorldSpaceCameraPos.xyz - positionWS;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float NdotV = saturate(dot(N, V));
                float fresnel = pow(1.0 - NdotV, _RimPower);
                half3 rim = _RimColor.rgb * (_RimIntensity * fresnel);

                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                half3 diffuse = mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 ambient = half3(0.2, 0.2, 0.2);
                base.rgb *= (ambient + diffuse);
                base.rgb += rim;
                base.rgb = MixFog(base.rgb, IN.fog);
                return base;
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
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
