Shader "Custom/TerrainSkirt"
{
    // ─── Fallback procedural (bandas de color). Preferir MAT_TerrainSkirt_SoilLayers + textura. ───
    // UV-V en modo procedural: 0 = fondo del skirt; 1 = superficie del terreno en cada columna (TerrainSkirtBuilder).
    // Con depth=30 y terrainHeight=50 → totalRange=80.
    // Un borde a nivel del agua (Y≈12.5) tiene V_surface ≈ (12.5+30)/80 ≈ 0.53
    // Las bandas están calibradas para que sean visibles en ese rango [0..0.55].
    Properties
    {
        // ── Colores de capas (de abajo hacia arriba) ──
        _ColorBedrock   ("Capa 1 Roca/Base",        Color) = (0.62, 0.52, 0.38, 1)
        _ColorSand      ("Capa 2 Arena/Sustrato",   Color) = (0.72, 0.48, 0.22, 1)
        _ColorSubsoil   ("Capa 3 Subsuelo rojizo",  Color) = (0.52, 0.22, 0.05, 1)
        _ColorClay      ("Capa 4 Arcilla marron",   Color) = (0.38, 0.18, 0.04, 1)
        _ColorTopsoil   ("Capa 5 Tierra organica",  Color) = (0.22, 0.11, 0.03, 1)

        // ── Posiciones V de cada transición (0-1) ──
        // Calibradas para depth=30, terrainH=50:
        // rango visible en borde de agua ≈ [0 .. 0.53]
        _T01  ("Transicion Bedrock→Arena",    Range(0,1)) = 0.08
        _T12  ("Transicion Arena→Subsuelo",   Range(0,1)) = 0.18
        _T23  ("Transicion Subsuelo→Arcilla", Range(0,1)) = 0.30
        _T34  ("Transicion Arcilla→Topsoil",  Range(0,1)) = 0.46
        _Soft ("Suavidad de transiciones",    Range(0.002,0.06)) = 0.025

        // ── Ruido horizontal para ondular las bandas ──
        _NoiseScale   ("Escala del ruido",    Range(0,30)) = 10.0
        _NoiseAmp     ("Amplitud del ruido",  Range(0,0.06)) = 0.025

        // ── Iluminación ──
        _AmbientMin   ("Luz ambiente minima", Range(0,1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 150
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 posOS   : POSITION;
                float3 normOS  : NORMAL;
                float2 uv      : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posHCS  : SV_POSITION;
                float2 uv      : TEXCOORD0;
                float3 normWS  : TEXCOORD1;
                float  fog     : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorBedrock;
                half4 _ColorSand;
                half4 _ColorSubsoil;
                half4 _ColorClay;
                half4 _ColorTopsoil;
                float _T01, _T12, _T23, _T34, _Soft;
                float _NoiseScale, _NoiseAmp;
                float _AmbientMin;
            CBUFFER_END

            // ── Hash / Value Noise 2D (sin textura) ──
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash2(i).x;
                float b = hash2(i + float2(1,0)).x;
                float c = hash2(i + float2(0,1)).x;
                float d = hash2(i + float2(1,1)).x;
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }

            // ── Mezcla de 5 capas ──
            half4 sampleLayers(float v)
            {
                float s = _Soft;
                float w01 = smoothstep(_T01 - s, _T01 + s, v);
                float w12 = smoothstep(_T12 - s, _T12 + s, v);
                float w23 = smoothstep(_T23 - s, _T23 + s, v);
                float w34 = smoothstep(_T34 - s, _T34 + s, v);

                half4 c = _ColorBedrock;
                c = lerp(c, _ColorSand,    w01);
                c = lerp(c, _ColorSubsoil, w12);
                c = lerp(c, _ColorClay,    w23);
                c = lerp(c, _ColorTopsoil, w34);
                return c;
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                o.uv     = IN.uv;
                o.normWS = TransformObjectToWorldNormal(IN.normOS);
                o.fog    = ComputeFogFactor(o.posHCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float v = IN.uv.y;
                float u = IN.uv.x;

                // Ondulación horizontal suave de las bandas
                float n = valueNoise(float2(u * _NoiseScale, v * _NoiseScale * 0.25));
                v = saturate(v + (n - 0.5) * _NoiseAmp);

                half4 col = sampleLayers(v);

                // Lambert difuso con mínimo de ambiente elevado (evita caras negras/amarillas)
                Light L = GetMainLight();
                float NdotL = saturate(dot(normalize(IN.normWS), L.direction));
                float light = _AmbientMin + (1.0 - _AmbientMin) * NdotL;
                col.rgb *= light * half3(L.color.r, L.color.g, L.color.b);

                col.rgb = MixFog(col.rgb, IN.fog);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
