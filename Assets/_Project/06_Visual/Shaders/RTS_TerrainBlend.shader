Shader "Project/RTS Terrain Blend"
{
    // URP terrain-style shader: blends Grass (flat), Dirt (mid), Rock (steep) by slope and world height.
    // Use on mesh terrain or assign to a terrain material; for Unity Terrain use with a mesh export or custom setup.
    Properties
    {
        [Header(Textures)]
        _GrassTex      ("Grass", 2D) = "green" {}
        _DirtTex       ("Dirt", 2D) = "brown" {}
        _RockTex      ("Rock", 2D) = "gray" {}

        [Header(Blend)]
        _SlopeThreshold ("Slope threshold (degrees)", Range(5, 60)) = 35
        _BlendSharpness ("Blend sharpness", Range(0.5, 20)) = 4
        _HeightBlend   ("Height influence (0=slope only)", Range(0, 1)) = 0.2
        _HeightLow     ("Height low (world Y)", Float) = 0
        _HeightHigh    ("Height high (world Y)", Float) = 50
        _Tiling        ("Texture tiling (XZ)", Float) = 8

        [Header(Lighting)]
        _Smoothness    ("Smoothness", Range(0, 1)) = 0.1
        _Metallic      ("Metallic", Range(0, 1)) = 0
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
                float3 normalOS  : NORMAL;
                float2 uv        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS  : TEXCOORD2;
                float  fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_DirtTex);  SAMPLER(sampler_DirtTex);
            TEXTURE2D(_RockTex);  SAMPLER(sampler_RockTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassTex_ST;
                float4 _DirtTex_ST;
                float4 _RockTex_ST;
                float _SlopeThreshold;
                float _BlendSharpness;
                float _HeightBlend;
                float _HeightLow;
                float _HeightHigh;
                float _Tiling;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                o.uv         = IN.uv;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float slopeAngle = acos(saturate(N.y)); // 0 = flat, PI/2 = vertical
                float slopeDeg = degrees(slopeAngle);

                // Weights: grass = flat, dirt = mid, rock = steep
                float slopeNorm = saturate((slopeDeg - _SlopeThreshold * 0.5) / max(1, _SlopeThreshold));
                float wRock = saturate(pow(slopeNorm, _BlendSharpness));
                float wGrass = saturate(1.0 - pow(slopeNorm + 0.05, 1.0 / _BlendSharpness));
                float wDirt = saturate(1.0 - wRock - wGrass);

                // Optional height blend: more rock at high altitude
                float heightNorm = saturate((IN.positionWS.y - _HeightLow) / max(0.001, _HeightHigh - _HeightLow));
                float heightRock = heightNorm * _HeightBlend;
                wRock = saturate(wRock + heightRock);
                float sum = wGrass + wDirt + wRock;
                if (sum > 0.0001)
                {
                    wGrass /= sum; wDirt /= sum; wRock /= sum;
                }

                float2 uvXZ = IN.positionWS.xz * (_Tiling * 0.01);
                half4 colG = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uvXZ);
                half4 colD = SAMPLE_TEXTURE2D(_DirtTex, sampler_DirtTex, uvXZ);
                half4 colR = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, uvXZ);
                half4 col = colG * wGrass + colD * wDirt + colR * wRock;

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                half3 diffuse = mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 ambient = half3(0.2, 0.2, 0.2);
                col.rgb *= (ambient + diffuse);
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
