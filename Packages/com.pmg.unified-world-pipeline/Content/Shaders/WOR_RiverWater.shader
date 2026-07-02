Shader "Project/WOR River Water"
{
    Properties
    {
        _DeepColor       ("Deep Color",         Color)             = (0.10, 0.40, 0.70, 0.95)
        _ShoreColor      ("Shallow Color",      Color)             = (0.15, 0.48, 0.74, 0.88)
        _FoamColor       ("Bank Foam Color",    Color)             = (0.78, 0.92, 1.00, 0.42)

        _FoamTex         ("Foam Texture",       2D) = "black" {}

        [Header(River Flow)]
        _Direction       ("Animation Direction (SW2)", Vector) = (0, -1, 0, 0)
        _Speed           ("Animation Speed (SW2)", Range(0.05, 3.00)) = 1.00
        _WorldSpaceUV    ("World Space UV",     Float) = 0
        _FlowSpeed       ("Flow Speed",         Range(0.05, 2.00)) = 0.38
        _FlowDirectionBlend ("Direction Blend", Range(0.00, 1.00)) = 0.85
        _FlowStreakStrength ("Flow Streaks",    Range(0.00, 0.60)) = 0.06
        _FlowRippleScale ("Flow Ripple Scale",  Range(0.10, 8.00)) = 1.35
        _ShoreBand       ("Shallow Shore Band", Range(0.04, 0.45)) = 0.24
        _CenterSoftness  ("Center Softness",   Range(0.35, 3.00)) = 2.60
        _CenterDarkStrength ("Center Dark Strength", Range(0.00, 0.50)) = 0.07
        _FoamWidth       ("Bank Foam Width",    Range(0.02, 0.70)) = 0.32
        _FoamAmount      ("Foam Amount",        Range(0.00, 1.00)) = 0.24
        _FoamScale       ("Foam Scale (X)",     Range(0.50, 8.00)) = 1.25
        _CapFoamFadeDistance ("Cap Foam Fade",  Range(0.00, 0.50)) = 0.08
        _CurveFoamStrength ("Curve Foam",      Range(0.00, 1.00)) = 0.18
        _ObjectFoamWidth ("Object Foam Width",  Range(0.00, 2.00)) = 0.35
        _ObjectFoamStrength ("Object Foam Strength", Range(0.00, 1.00)) = 0.55
        _MouthFoamStrength ("Mouth Blend Foam", Range(0.00, 1.00)) = 0.10
        _MouthFadeStrength ("Mouth Alpha Fade", Range(0.00, 1.00)) = 0.12
        _ConfluenceFoamStrength ("Confluence Foam", Range(0.00, 1.00)) = 0.08
        _Alpha           ("Alpha",              Range(0.50, 1.00)) = 0.98

        [Header(Uber Stylized Compat)]
        [HDR]_Color_Shallow ("Color_Shallow", Color) = (0.15, 0.48, 0.74, 0.88)
        [HDR]_Color_Deep    ("Color_Deep",    Color) = (0.10, 0.40, 0.70, 0.95)
        _Water_Depth        ("Water_Depth",   Float) = 0.39
        _SurfFoam_Color     ("SurfFoam_Color", Color) = (0.78, 0.92, 1.00, 0.42)
        [NoScaleOffset]_SurfFoam_Map ("SurfFoam_Map", 2D) = "black" {}
        _SurfFoam_Pan       ("SurfFoam_Pan", Vector) = (0.01, 0.01, 0, 0)
        _SurfFoam_Scale     ("SurfFoam_Scale", Float) = 3
        _SurfFoam_Tile      ("SurfFoam_Tile", Vector) = (1, 1, 0, 0)
        _SurfFoam_Edge      ("SurfFoam_Edge", Range(0, 1)) = 0.35
        _SurfFoam_EdgeSmooth ("SurfFoam_EdgeSmooth", Range(0.001, 1)) = 0.18
        _SurfFoam_AlphaBlend ("SurfFoam_AlphaBlend", Range(0, 1)) = 0.65
        _Invert_SurfFoam    ("Invert_SurfFoam", Float) = 0
        [NoScaleOffset]_SurfaceDistortion_Map ("SurfaceDistortion_Map", 2D) = "gray" {}
        _SurfaceDistortion_Scale ("SurfaceDistortion_Scale", Float) = 1
        _SurfaceDistortion_Strength ("SurfaceDistortion_Strength", Range(0, 1)) = 0.08
        _SurfaceDistortion_Pan ("SurfaceDistortion_Pan", Vector) = (0.08, 0.04, 0, 0)
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
        Offset -1, -1
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardRiver"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_SurfFoam_Map); SAMPLER(sampler_SurfFoam_Map);
            TEXTURE2D(_SurfaceDistortion_Map); SAMPLER(sampler_SurfaceDistortion_Map);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
                float4 vtxColor   : TEXCOORD3;
                float4 screenPos  : TEXCOORD4;
                float  waterEyeDepth : TEXCOORD5;
                float  fogFactor  : TEXCOORD6;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShoreColor;
                half4 _FoamColor;
                half4 _Color_Shallow;
                half4 _Color_Deep;
                half4 _SurfFoam_Color;
                float4 _Direction;
                float4 _SurfFoam_Pan;
                float4 _SurfFoam_Tile;
                float4 _SurfaceDistortion_Pan;
                half  _FlowSpeed;
                half  _Speed;
                half  _WorldSpaceUV;
                half  _FlowDirectionBlend;
                half  _FlowRippleScale;
                half  _FlowStreakStrength;
                half  _ShoreBand;
                half  _CenterSoftness;
                half  _CenterDarkStrength;
                half  _FoamWidth;
                half  _FoamAmount;
                half  _FoamScale;
                half  _CapFoamFadeDistance;
                half  _CurveFoamStrength;
                half  _ObjectFoamWidth;
                half  _ObjectFoamStrength;
                half  _MouthFoamStrength;
                half  _MouthFadeStrength;
                half  _ConfluenceFoamStrength;
                half  _Alpha;
                half  _Water_Depth;
                half  _SurfFoam_Scale;
                half  _SurfFoam_Edge;
                half  _SurfFoam_EdgeSmooth;
                half  _SurfFoam_AlphaBlend;
                half  _Invert_SurfFoam;
                half  _SurfaceDistortion_Scale;
                half  _SurfaceDistortion_Strength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.positionWS = posWS;
                o.uv         = IN.uv;
                o.uv2        = IN.uv2;
                o.vtxColor   = IN.color;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.waterEyeDepth = -TransformWorldToView(posWS).z;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t   = _Time.y;
                float2 uv = IN.uv;
                float2 wp = IN.positionWS.xz;
                float endpointAlpha = saturate(IN.uv2.x);
                float2 sw2Dir = _Direction.xy;
                if (dot(sw2Dir, sw2Dir) < 1e-5)
                    sw2Dir = float2(0.0, -1.0);
                sw2Dir = normalize(sw2Dir);
                // StylizedWater2 scrolls with -Direction, so use the same convention for material tuning.
                float2 downstreamDir = -sw2Dir;

                float worMode = 1.0 - step(0.75, IN.vtxColor.a);
                float curveFoamVtx = saturate(IN.vtxColor.b);
                float mouthFoamVtx = saturate(IN.vtxColor.g);
                float confluenceFoamVtx = saturate(IN.vtxColor.a / 0.28);

                float cross = abs(uv.x - 0.5) * 2.0;
                float crossAA = fwidth(cross) * 2.5;
                float waterDepthControl = saturate((float)_Water_Depth);
                float shoreBand = max((float)_ShoreBand, 0.07);
                float shallow01 = smoothstep(1.0 - shoreBand - crossAA, 1.0 + crossAA, cross);
                shallow01 = pow(saturate(shallow01), 1.45);
                float center01 = 1.0 - smoothstep(0.18, 0.92, cross);
                float depth01 = pow(saturate(center01), max(lerp(0.35, 2.4, waterDepthControl) * (float)_CenterSoftness * 0.42, 0.32));

                float flowCoord = uv.y;
                float widthCoord = uv.x;
                float2 worldFlowUV = float2(dot(wp, downstreamDir), dot(wp, float2(-downstreamDir.y, downstreamDir.x)));
                float2 flowSpace = lerp(float2(flowCoord, widthCoord), worldFlowUV * 0.08, saturate((float)_WorldSpaceUV));
                float flowRate = (float)_FlowSpeed * max((float)_Speed, 0.05);
                float flowScroll = flowSpace.x * (float)_FlowRippleScale
                    + dot(wp, downstreamDir) * 0.028 * saturate((float)_FlowDirectionBlend)
                    - t * flowRate * 0.42;

                float junctionSoft = saturate(max(mouthFoamVtx, confluenceFoamVtx));
                float centerDark = (float)_CenterDarkStrength * (1.0 - junctionSoft * 0.65) * 0.55;
                half4 deepColor = lerp(_DeepColor, _Color_Deep, waterDepthControl);
                half4 shoreColor = lerp(_ShoreColor, _Color_Shallow, 0.35h);
                half4 foamColor = lerp(_FoamColor, _SurfFoam_Color, 0.35h);
                half4 midColor = lerp(deepColor, shoreColor, 0.22h);
                half4 waterCol = lerp(midColor, shoreColor, (half)(shallow01 * 0.72));
                waterCol = lerp(waterCol, deepColor, (half)(depth01 * 0.38));
                waterCol = lerp(deepColor, waterCol, (half)(1.0 - depth01 * centerDark));
                waterCol.a = lerp(_DeepColor.a, _ShoreColor.a, (half)shallow01);

                float edgeDist = min(widthCoord, 1.0 - widthCoord);
                float edgeAA = fwidth(edgeDist) * 2.2;
                float bankMask = 1.0 - smoothstep((float)_FoamWidth - edgeAA, (float)_FoamWidth + edgeAA, edgeDist);
                bankMask = pow(saturate(bankMask), 1.28);
                float capFadeDistance = max((float)_CapFoamFadeDistance, 0.0001);
                float capMask = saturate(min(IN.uv2.y, 1.0 - IN.uv2.y) / capFadeDistance);
                bankMask *= capMask * endpointAlpha;

                float curveRate = saturate(fwidth(cross) * 13.0);
                float curveFoam = saturate(curveRate * (float)_CurveFoamStrength);
                if (worMode > 0.5)
                    curveFoam = max(curveFoam, curveFoamVtx * (float)_CurveFoamStrength);

                float flowStreak = sin(flowScroll * 6.283 + widthCoord * 2.4) * 0.5 + 0.5;
                flowStreak = pow(flowStreak, 3.0) * depth01 * (1.0 - bankMask * 0.9) * (float)_FlowStreakStrength;

                float foamScale = max((float)_FoamScale, (float)_SurfFoam_Scale * 0.55);
                float2 foamPan = float2(0.0, _SurfFoam_Pan.y) * t;
                float2 foamBaseUV = float2(widthCoord, flowScroll);
                float2 distortionPan = float2(0.0, _SurfaceDistortion_Pan.y) * t;
                float2 distortionUV = foamBaseUV * max((float)_SurfaceDistortion_Scale, 0.001) + distortionPan;
                float2 distortion = (SAMPLE_TEXTURE2D(_SurfaceDistortion_Map, sampler_SurfaceDistortion_Map, distortionUV).rg * 2.0 - 1.0)
                    * (float)_SurfaceDistortion_Strength;
                distortion.x *= 0.18;
                float2 fuv1 = float2(widthCoord * foamScale, flowScroll * 1.8) + foamPan + distortion;
                float2 fuv2 = float2(widthCoord * foamScale * 0.82 + 0.53, flowScroll * 1.95 - 0.11) + float2(0.0, -foamPan.y) - distortion * 0.65;
                half foam1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv1).r;
                half foam2 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv2).r;
                float2 surfFoamUV = (foamBaseUV * max((float)_SurfFoam_Scale, 0.001) * max(abs(_SurfFoam_Tile.xy), 0.001)) + foamPan + distortion;
                half surfFoamRaw = SAMPLE_TEXTURE2D(_SurfFoam_Map, sampler_SurfFoam_Map, surfFoamUV).r;
                surfFoamRaw = lerp(surfFoamRaw, 1.0h - surfFoamRaw, saturate(_Invert_SurfFoam));
                half surfFoam = smoothstep(_SurfFoam_Edge, saturate(_SurfFoam_Edge + _SurfFoam_EdgeSmooth), surfFoamRaw);
                half foamTex = saturate(lerp(foam1 * 0.58h + foam2 * 0.42h, surfFoam, _SurfFoam_AlphaBlend));

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float depthDiff = max(sceneEyeDepth - IN.waterEyeDepth, 0.0);
                float objectFoam = 0.0;
                if (_ObjectFoamWidth > 0.001h)
                {
                    objectFoam = 1.0 - smoothstep(0.015, max((float)_ObjectFoamWidth, 0.02), depthDiff);
                    objectFoam *= step(0.0001, depthDiff) * (1.0 - bankMask * 0.35) * (float)_ObjectFoamStrength;
                }

                float foamThresh = 1.0 - (float)_FoamAmount;
                float bankFoam = bankMask * smoothstep(foamThresh - 0.10, foamThresh + 0.10, (float)foamTex);
                float mouthFoam = mouthFoamVtx * (float)_MouthFoamStrength * (0.4 + bankMask * 0.35);
                float confFoam = confluenceFoamVtx * (float)_ConfluenceFoamStrength * (0.35 + bankMask * 0.35);
                float foamMask = saturate(bankFoam + curveFoam * bankMask * 0.85 + mouthFoam + confFoam + objectFoam) * endpointAlpha;

                half4 result = waterCol;
                result.rgb += (half)(flowStreak * 0.20h);
                result.rgb = lerp(result.rgb, foamColor.rgb, (half)foamMask * foamColor.a);
                result.a = saturate(waterCol.a * _Alpha);
                result.a *= (half)(1.0 - mouthFoamVtx * (float)_MouthFadeStrength);
                result.a = max(result.a, (half)foamMask * foamColor.a * 0.55h);
                result.a *= endpointAlpha;
                result.rgb = MixFog(result.rgb, IN.fogFactor);
                return result;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
