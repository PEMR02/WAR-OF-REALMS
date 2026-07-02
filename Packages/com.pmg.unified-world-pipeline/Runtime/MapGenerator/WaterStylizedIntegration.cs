using System;
using System.Reflection;
using UnityEngine;

namespace Project.Gameplay.Map.Generator
{
    public static class WaterStylizedIntegration
    {
        const float BoundsPaddingY = 8f;
        static Type s_waterObjectType;
        static float s_riverVertexAlpha = 1f;

        public static bool IsStylizedWaterMaterial(Material mat)
        {
            if (mat == null || mat.shader == null)
                return false;

            string shaderName = mat.shader.name;
            string materialName = mat.name;
            return
                (!string.IsNullOrEmpty(shaderName) && shaderName.Contains("Stylized Water")) ||
                (!string.IsNullOrEmpty(shaderName) && shaderName.Contains("StylizedWater")) ||
                (!string.IsNullOrEmpty(shaderName) && shaderName.Contains("UberStylized")) ||
                (!string.IsNullOrEmpty(shaderName) && shaderName.Contains("Stylized")) ||
                (!string.IsNullOrEmpty(materialName) && materialName.Contains("Stylized Water")) ||
                (!string.IsNullOrEmpty(materialName) && materialName.Contains("StylizedWater")) ||
                (!string.IsNullOrEmpty(materialName) && materialName.Contains("PM_Stylized")) ||
                (!string.IsNullOrEmpty(materialName) && materialName.Contains("Stylized_Lake"));
        }

        static float OpacityFromTransparencyScale(float baseAlpha, float transparencyScale)
        {
            float transparency = (1f - Mathf.Clamp01(baseAlpha)) * transparencyScale;
            return Mathf.Clamp01(1f - transparency);
        }

        static bool IsWorRiverWaterMaterial(Material mat)
        {
            return mat != null &&
                mat.shader != null &&
                !string.IsNullOrEmpty(mat.shader.name) &&
                mat.shader.name.Contains("WOR River Water");
        }

        static void ApplyWorRiverWaterRuntime(Material mat, MapGenConfig config, WaterMaterialRuntimeMode mode)
        {
            if (!IsWorRiverWaterMaterial(mat))
                return;

            if (mode == WaterMaterialRuntimeMode.DirectAsset)
            {
                EnableStylizedWaterTransparentQueue(mat);
                return;
            }

            if (config == null)
                return;

            float alpha = Mathf.Clamp01(config.riverWaterAlpha);
            Color shallow = config.riverWaterShallowColor;
            Color deep = config.riverWaterDeepColor;
            shallow.a = Mathf.Lerp(alpha, 1f, 0.22f);
            deep.a = alpha;

            if (mat.HasProperty("_ShoreColor"))
                mat.SetColor("_ShoreColor", shallow);
            if (mat.HasProperty("_DeepColor"))
                mat.SetColor("_DeepColor", deep);
            if (mat.HasProperty("_Color_Shallow"))
                mat.SetColor("_Color_Shallow", shallow);
            if (mat.HasProperty("_Color_Deep"))
                mat.SetColor("_Color_Deep", deep);
            if (mat.HasProperty("_Alpha"))
                mat.SetFloat("_Alpha", alpha);
            if (mat.HasProperty("_ShoreBand"))
                mat.SetFloat("_ShoreBand", Mathf.Clamp(config.riverBankBlendStrength * 1.8f, 0.08f, 0.45f));
            if (mat.HasProperty("_FoamWidth"))
                mat.SetFloat("_FoamWidth", Mathf.Clamp(config.riverBankBlendStrength * 0.34f, 0.04f, 0.28f));
            if (mat.HasProperty("_FlowStreakStrength"))
                mat.SetFloat("_FlowStreakStrength", Mathf.Clamp(config.waterDepthColorStrength * 0.07f, 0.025f, 0.11f));
            if (mat.HasProperty("_WorldSpaceUV"))
                mat.SetFloat("_WorldSpaceUV", 0f);
            if (mat.HasProperty("_FlowDirectionBlend"))
                mat.SetFloat("_FlowDirectionBlend", 0f);
            if (mat.HasProperty("_CapFoamFadeDistance"))
                mat.SetFloat("_CapFoamFadeDistance", 0.08f);
            if (mat.HasProperty("_ObjectFoamWidth"))
                mat.SetFloat("_ObjectFoamWidth", 0.42f);
            if (mat.HasProperty("_ObjectFoamStrength"))
                mat.SetFloat("_ObjectFoamStrength", 0.58f);

            EnableStylizedWaterTransparentQueue(mat);
        }

        static void ApplyRiverWaterTint(Material mat)
        {
            if (!mat.HasProperty("_BaseColor") || !mat.HasProperty("_ShallowColor"))
                return;

            Color baseC = mat.GetColor("_BaseColor");
            baseC.g *= 0.76f;
            baseC.b *= 0.8f;
            mat.SetColor("_BaseColor", baseC);

            Color shallow = mat.GetColor("_ShallowColor");
            shallow = Color.Lerp(baseC, shallow, 0.45f);
            shallow.g *= 0.86f;
            shallow.b *= 0.88f;
            mat.SetColor("_ShallowColor", shallow);
        }

        static void ApplyLakeWaterTint(Material mat)
        {
            if (!mat.HasProperty("_BaseColor") || !mat.HasProperty("_ShallowColor"))
                return;

            Color baseC = new Color(0.015f, 0.155f, 0.42f, mat.GetColor("_BaseColor").a);
            Color shallow = new Color(0.045f, 0.30f, 0.40f, 0.13f);
            mat.SetColor("_BaseColor", baseC);
            mat.SetColor("_ShallowColor", shallow);
        }

        static void ApplyStylizedWaterAlpha(Material mat, float alpha, float shallowAlphaLerp = 0.35f)
        {
            // El shader lee alpha desde _BaseColor/_ShallowColor (ForwardPass.hlsl), no _WaterColor.
            if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = alpha;
                mat.SetColor("_BaseColor", c);
            }

            if (mat.HasProperty("_ShallowColor"))
            {
                Color c = mat.GetColor("_ShallowColor");
                c.a = Mathf.Lerp(alpha, 1f, shallowAlphaLerp);
                mat.SetColor("_ShallowColor", c);
            }

            if (mat.HasProperty("_WaterColor"))
            {
                Color c = mat.GetColor("_WaterColor");
                c.a = alpha;
                mat.SetColor("_WaterColor", c);
            }

            if (mat.HasProperty("_WaterShallowColor"))
            {
                Color c = mat.GetColor("_WaterShallowColor");
                c.a = Mathf.Lerp(alpha, 1f, shallowAlphaLerp);
                mat.SetColor("_WaterShallowColor", c);
            }
        }

        static void EnableStylizedWaterTransparentQueue(Material mat)
        {
            mat.renderQueue = 3000;
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        static void ApplyRiverShorelineIntersection(Material mat)
        {
            mat.EnableKeyword("_SHARP_INERSECTION");
            // 1 = vertexColor.r: orillas fiables en ribbon plano (depth RTS suele no marcar orilla).
            if (mat.HasProperty("_IntersectionSource"))
                mat.SetFloat("_IntersectionSource", 1f);
            if (mat.HasProperty("_IntersectionLength"))
                mat.SetFloat("_IntersectionLength", 2.5f);
            if (mat.HasProperty("_IntersectionFalloff"))
                mat.SetFloat("_IntersectionFalloff", 0.56f);
            if (mat.HasProperty("_IntersectionTiling"))
                mat.SetFloat("_IntersectionTiling", 0.3f);
            if (mat.HasProperty("_IntersectionSpeed"))
                mat.SetFloat("_IntersectionSpeed", -0.05f);
            if (mat.HasProperty("_IntersectionDistortion"))
                mat.SetFloat("_IntersectionDistortion", 0.16f);
            if (mat.HasProperty("_IntersectionRippleStrength"))
                mat.SetFloat("_IntersectionRippleStrength", 0.19f);
            if (mat.HasProperty("_IntersectionRippleDist"))
                mat.SetFloat("_IntersectionRippleDist", 40f);
            if (mat.HasProperty("_IntersectionClipping"))
                mat.SetFloat("_IntersectionClipping", 0.92f);
            if (mat.HasProperty("_IntersectionColor"))
                mat.SetColor("_IntersectionColor", new Color(1f, 1f, 1f, 0.62f));
        }

        static void ApplyLakeShorelineIntersection(Material mat)
        {
            // Lago MS: vertex R da una orilla estable; depth en MS plano puede crear rejilla o desaparecer.
            mat.EnableKeyword("_SHARP_INERSECTION");
            mat.DisableKeyword("_SMOOTH_INTERSECTION");
            if (mat.HasProperty("_IntersectionSource"))
                mat.SetFloat("_IntersectionSource", 1f);
            if (mat.HasProperty("_IntersectionLength"))
                mat.SetFloat("_IntersectionLength", 0.8f);
            if (mat.HasProperty("_IntersectionFalloff"))
                mat.SetFloat("_IntersectionFalloff", 0.32f);
            if (mat.HasProperty("_IntersectionTiling"))
                mat.SetFloat("_IntersectionTiling", 0.08f);
            if (mat.HasProperty("_IntersectionSpeed"))
                mat.SetFloat("_IntersectionSpeed", 0.02f);
            if (mat.HasProperty("_IntersectionDistortion"))
                mat.SetFloat("_IntersectionDistortion", 0.04f);
            if (mat.HasProperty("_IntersectionRippleStrength"))
                mat.SetFloat("_IntersectionRippleStrength", 0.03f);
            if (mat.HasProperty("_IntersectionRippleDist"))
                mat.SetFloat("_IntersectionRippleDist", 20f);
            if (mat.HasProperty("_IntersectionClipping"))
                mat.SetFloat("_IntersectionClipping", 0.76f);
            if (mat.HasProperty("_IntersectionColor"))
                mat.SetColor("_IntersectionColor", new Color(0.92f, 0.98f, 1f, 0.42f));
        }

        static void ApplyLakeSurfaceFoam(Material mat)
        {
            mat.EnableKeyword("_FOAM");
            if (mat.HasProperty("_FoamOn"))
                mat.SetFloat("_FoamOn", 1f);
            if (mat.HasProperty("_FoamBaseAmount"))
                mat.SetFloat("_FoamBaseAmount", 0f);
            if (mat.HasProperty("_FoamWaveAmount"))
                mat.SetFloat("_FoamWaveAmount", 0.18f);
            if (mat.HasProperty("_FoamWaveMask"))
                mat.SetFloat("_FoamWaveMask", 0.52f);
            if (mat.HasProperty("_FoamOpacity"))
                mat.SetFloat("_FoamOpacity", 0.16f);
            if (mat.HasProperty("_FoamClipping"))
                mat.SetFloat("_FoamClipping", 0.94f);
            SetFloatOrVectorTiling(mat, "_FoamTiling", 0.28f);
            if (mat.HasProperty("_FoamSubTiling"))
                mat.SetFloat("_FoamSubTiling", 0.62f);
            if (mat.HasProperty("_FoamSpeed"))
                mat.SetFloat("_FoamSpeed", 0.08f);
            if (mat.HasProperty("_VertexColorFoam"))
                mat.SetFloat("_VertexColorFoam", 0f);
        }

        static void ApplyLakeCalmSurface(Material mat)
        {
            mat.EnableKeyword("_WAVES");
            if (mat.HasProperty("_WavesOn"))
                mat.SetFloat("_WavesOn", 1f);
            if (mat.HasProperty("_WaveHeight"))
                mat.SetFloat("_WaveHeight", 0.13f);
            if (mat.HasProperty("_WaveSpeed"))
                mat.SetFloat("_WaveSpeed", 0.55f);
            if (mat.HasProperty("_WaveDistance"))
                mat.SetFloat("_WaveDistance", 0.48f);
            if (mat.HasProperty("_WaveCount"))
                mat.SetFloat("_WaveCount", 3f);
            if (mat.HasProperty("_NormalSpeed"))
                mat.SetFloat("_NormalSpeed", 0.42f);
            if (mat.HasProperty("_NormalStrength"))
                mat.SetFloat("_NormalStrength", 0.22f);
            SetFloatOrVectorTiling(mat, "_NormalTiling", 0.34f);
            if (mat.HasProperty("_FlowSpeed"))
                mat.SetFloat("_FlowSpeed", 0.25f);
            if (mat.HasProperty("_ReflectionStrength"))
                mat.SetFloat("_ReflectionStrength", 0.9f);
            if (mat.HasProperty("_WaveTint"))
                mat.SetFloat("_WaveTint", 0.06f);
            if (mat.HasProperty("_SlopeFoam"))
                mat.SetFloat("_SlopeFoam", 0f);
        }

        public static void ApplyStylizedRiverMaterialRuntime(Material mat, MapGenConfig config)
        {
            ApplyStylizedRiverMaterialRuntime(mat, config, WaterMaterialRuntimeMode.SW2ProceduralTranslator);
        }

        public static void ApplyStylizedRiverMaterialRuntime(Material mat, MapGenConfig config, WaterMaterialRuntimeMode mode)
        {
            ApplyWorRiverWaterRuntime(mat, config, mode);

            if (!IsStylizedWaterMaterial(mat) || config == null)
                return;
            if (mode != WaterMaterialRuntimeMode.SW2ProceduralTranslator)
                return;

            // Velocidad y tiling: solo en el asset StylizedWater2_1_Toon_River (no tocar por código).
            float alpha = OpacityFromTransparencyScale(config.riverWaterAlpha, 0.28f);
            alpha = Mathf.Clamp(alpha, 0.86f, 0.96f);
            s_riverVertexAlpha = alpha;
            ApplyStylizedWaterAlpha(mat, alpha, 0.22f);
            ApplyRiverWaterTint(mat);

            ApplyRiverShorelineIntersection(mat);

            if (mat.HasProperty("_FoamBaseAmount"))
                mat.SetFloat("_FoamBaseAmount", 0.34f);
            SetFloatOrVectorTiling(mat, "_FoamTiling", 0.92f);
            if (mat.HasProperty("_FoamSubTiling"))
                mat.SetFloat("_FoamSubTiling", 0.62f);
            if (mat.HasProperty("_FoamOpacity"))
                mat.SetFloat("_FoamOpacity", 0.11f);
            if (mat.HasProperty("_FoamClipping"))
                mat.SetFloat("_FoamClipping", 0.976f);

            if (mat.HasProperty("_VertexColorDepth"))
                mat.SetFloat("_VertexColorDepth", alpha < 0.98f ? 1f : 0f);

            if (alpha < 0.98f)
                EnableStylizedWaterTransparentQueue(mat);
        }

        static void ApplyProceduralMsLakeCompat(Material mat)
        {
            // MS plano + depth buffer de escena = todo shallow + rejilla blanca. Foam/orilla quedan en el asset.
            mat.DisableKeyword("_CAUSTICS");
            mat.DisableKeyword("_REFRACTION");
            if (mat.HasProperty("_CausticsOn"))
                mat.SetFloat("_CausticsOn", 0f);
            if (mat.HasProperty("_RefractionOn"))
                mat.SetFloat("_RefractionOn", 0f);

            if (mat.HasProperty("_CrossPan_IntersectionOn"))
                mat.SetFloat("_CrossPan_IntersectionOn", 0f);
            if (mat.HasProperty("_Texture_IntersectionOn"))
                mat.SetFloat("_Texture_IntersectionOn", 0f);

            mat.EnableKeyword("_UNLIT");
            mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (mat.HasProperty("_EnvironmentReflectionsOn"))
                mat.SetFloat("_EnvironmentReflectionsOn", 1f);
            if (mat.HasProperty("_LightingOn"))
                mat.SetFloat("_LightingOn", 0f);

            // Runtime Codex lake preset: disable SW2 scene depth for procedural MS lakes.
            mat.EnableKeyword("_DISABLE_DEPTH_TEX");
            if (mat.HasProperty("_DisableDepthTexture"))
                mat.SetFloat("_DisableDepthTexture", 1f);
            if (mat.HasProperty("_DepthVertical"))
                mat.SetFloat("_DepthVertical", 5.5f);
            if (mat.HasProperty("_DepthHorizontal"))
                mat.SetFloat("_DepthHorizontal", 0.22f);
        }

        public static void ApplyStylizedLakeMaterialRuntime(Material mat, MapGenConfig config)
        {
            ApplyStylizedLakeMaterialRuntime(mat, config, WaterMaterialRuntimeMode.SW2ProceduralTranslator);
        }

        public static void ApplyStylizedLakeMaterialRuntime(Material mat, MapGenConfig config, WaterMaterialRuntimeMode mode)
        {
            if (!IsStylizedWaterMaterial(mat) || config == null)
                return;
            if (mode != WaterMaterialRuntimeMode.SW2ProceduralTranslator)
                return;

            ApplyProceduralMsLakeCompat(mat);

            float alpha = OpacityFromTransparencyScale(config.lakeWaterAlpha, 0.72f);
            alpha = Mathf.Clamp(alpha, 0.62f, 0.80f);
            s_lakeSurfaceAlpha = alpha;
            ApplyStylizedWaterAlpha(mat, alpha, 0.22f);
            ApplyLakeWaterTint(mat);

            ApplyLakeShorelineIntersection(mat);
            ApplyLakeSurfaceFoam(mat);
            ApplyLakeCalmSurface(mat);

            // Profundidad por vertex color G: centro profundo, orillas shallow (sin depth buffer de escena).
            if (mat.HasProperty("_VertexColorDepth"))
                mat.SetFloat("_VertexColorDepth", 1f);

            if (alpha < 0.98f)
                EnableStylizedWaterTransparentQueue(mat);
        }

        static float s_lakeSurfaceAlpha = 1f;

        public static Color GetLakeVertexColor(float depth01)
        {
            depth01 = Mathf.Clamp01(depth01);
            float shore = Mathf.Clamp01(1f - depth01);
            float shoreMask = Mathf.Pow(shore, 5.5f) * 0.58f;

            // SW2 subtracts vertex G from water density on non-river materials.
            // Use a strong shore mask so procedural MS lakes get visible shallow/deep contrast without scene depth.
            float shallowMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.72f, shore));
            float depthMask = Mathf.Clamp01(shallowMask * 0.86f);
            return new Color(shoreMask, depthMask, 0f, 0f);
        }

        public static void ApplyRiverFlowDirection(Material mat, Vector2 downstreamDir, float speedScale)
        {
            if (mat == null)
                return;

            if (downstreamDir.sqrMagnitude < 1e-4f)
                return;

            downstreamDir.Normalize();

            bool isWorRiver = IsWorRiverWaterMaterial(mat);
            string shaderName = mat.shader != null ? mat.shader.name : "";
            bool isRtsRiver = !string.IsNullOrEmpty(shaderName) && shaderName.Contains("RTS River Water");
            bool isSw2River = IsStylizedWaterMaterial(mat) || isWorRiver ||
                (!string.IsNullOrEmpty(shaderName) && shaderName.Contains("Stylized Water"));

            // SW2/WOR: TIME macro usa -_Direction.xy → _Direction debe apuntar aguas arriba.
            Vector2 directionForMaterial = isSw2River ? -downstreamDir : downstreamDir;

            if (mat.HasProperty("_Direction"))
                mat.SetVector("_Direction", new Vector4(directionForMaterial.x, directionForMaterial.y, 0f, 0f));
            if (mat.HasProperty("_Speed"))
                mat.SetFloat("_Speed", Mathf.Clamp(speedScale, 0.25f, 2.5f));

            if (isWorRiver)
            {
                if (mat.HasProperty("_FlowSpeed"))
                    mat.SetFloat("_FlowSpeed", Mathf.Clamp(speedScale * 0.35f, 0.08f, 1.2f));
                return;
            }

            if (isRtsRiver)
            {
                if (mat.HasProperty("_FlowDirection"))
                    mat.SetVector("_FlowDirection", new Vector4(downstreamDir.x, downstreamDir.y, 0f, 0f));
                if (mat.HasProperty("_WaterFlowSpeed"))
                    mat.SetFloat("_WaterFlowSpeed", Mathf.Clamp(speedScale, 0.25f, 2.5f));
            }
            else if (IsStylizedWaterMaterial(mat) && mat.HasProperty("_FlowSpeed"))
                mat.SetFloat("_FlowSpeed", Mathf.Clamp(speedScale * 0.35f, 0.08f, 1.2f));
        }

        /// <summary>Refuerzo de orilla en curvas (0 recto, 1 curva fuerte). Multiplica máscara transversal.</summary>
        public static float ComputeRiverCurveShoreFoam01(float turnAngleDeg)
        {
            const float straightDeg = 8f;
            const float curveDeg = 44f;
            if (turnAngleDeg <= straightDeg)
                return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(straightDeg, curveDeg, turnAngleDeg));
        }

        /// <summary>Máscara transversal: orillas + leve centro (5 verts); reparte sin bloque blanco.</summary>
        public static float GetRiverShoreAcrossWidth01(int vertexIndex, int vertsPerCrossSection)
        {
            if (vertsPerCrossSection == CrossSectionVertexCountProfile)
            {
                return vertexIndex switch
                {
                    0 => 0.74f,
                    1 => 0.24f,
                    2 => 0.07f,
                    3 => 0.24f,
                    4 => 0.74f,
                    _ => 0f
                };
            }

            if (vertsPerCrossSection <= 1)
                return 0f;
            float u = vertexIndex / (float)(vertsPerCrossSection - 1);
            float edge01 = Mathf.Abs(u - 0.5f) * 2f;
            return Mathf.SmoothStep(0.58f, 0.94f, edge01);
        }

        const int CrossSectionVertexCountProfile = 5;

        public static Color GetRiverVertexColor(Material mat, float curveShoreFoam01, float shoreAcrossWidth01)
        {
            if (!IsStylizedWaterMaterial(mat))
                return Color.white;

            float g = (1f - s_riverVertexAlpha) * 0.35f;
            float alongMul = Mathf.Lerp(0.97f, 1.05f, Mathf.Clamp01(curveShoreFoam01));
            float r = Mathf.Clamp01(shoreAcrossWidth01 * alongMul * 0.62f);
            return new Color(r, g, 0f, 1f);
        }

        public static void PrepareMesh(Mesh mesh, Material mat)
        {
            if (mesh == null || !IsStylizedWaterMaterial(mat))
                return;

            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != mesh.vertexCount)
            {
                normals = new Vector3[mesh.vertexCount];
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = Vector3.up;
                mesh.normals = normals;
            }

            Vector4[] tangents = mesh.tangents;
            if (tangents == null || tangents.Length != mesh.vertexCount)
            {
                tangents = new Vector4[mesh.vertexCount];
                for (int i = 0; i < tangents.Length; i++)
                    tangents[i] = new Vector4(-1f, 0f, 0f, -1f);
                mesh.tangents = tangents;
            }

            if (mesh.uv2 == null || mesh.uv2.Length != mesh.vertexCount)
                mesh.uv2 = mesh.uv;

            Bounds b = mesh.bounds;
            if (b.size.y < BoundsPaddingY)
            {
                b.Expand(new Vector3(0f, BoundsPaddingY - b.size.y, 0f));
                mesh.bounds = b;
            }
        }

        public static void AttachWaterObject(GameObject go, MeshFilter mf, MeshRenderer mr, Material mat)
        {
            if (go == null || mf == null || mr == null || !IsStylizedWaterMaterial(mat))
                return;

            Type waterObjectType = GetWaterObjectType();
            if (waterObjectType == null)
                return;

            Component waterObject = go.GetComponent(waterObjectType);
            if (waterObject == null)
                waterObject = go.AddComponent(waterObjectType);

            SetField(waterObject, "material", mat);
            SetField(waterObject, "meshFilter", mf);
            SetField(waterObject, "meshRenderer", mr);
        }

        static Type GetWaterObjectType()
        {
            if (s_waterObjectType != null)
                return s_waterObjectType;

            s_waterObjectType = Type.GetType("StylizedWater2.WaterObject, sc.stylizedwater2.runtime");
            if (s_waterObjectType != null)
                return s_waterObjectType;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                s_waterObjectType = assembly.GetType("StylizedWater2.WaterObject");
                if (s_waterObjectType != null)
                    return s_waterObjectType;
            }

            return null;
        }

        static void SetField(Component component, string fieldName, object value)
        {
            FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
                field.SetValue(component, value);
        }
    

static void SetFloatOrVectorTiling(Material mat, string property, float value)
        {
            // Stylized Water variants declare these tiling properties with different backing types.
            // For authoring/procedural generation, preserving the material-authored value is safer
            // than forcing a runtime value and triggering Unity's property sheet type conflict.
        }
}
}
