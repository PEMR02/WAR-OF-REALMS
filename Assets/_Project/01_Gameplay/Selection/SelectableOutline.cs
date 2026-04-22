using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Buildings;
using Project.Gameplay.Resources;
using Project.Gameplay.Units;
using Project.Gameplay.Faction;

namespace Project.Gameplay
{
    /// <summary>
    /// Muestra un borde/outline alrededor del objeto: selección (más marcado) o hover (más suave).
    /// Crea en runtime copias del mesh escaladas con material Cull Front.
    /// Lee el bloque correspondiente del SelectionOutlineConfig según tipo (unidad, edificio, recurso).
    /// </summary>
    public class SelectableOutline : MonoBehaviour
    {
        public enum OutlineState { Off, Hover, Selected }

        [Header("Apariencia (override si no hay config; si hay config se usa por tipo: edificio/recurso)")]
        [Tooltip("Color del borde al seleccionar (más visible).")]
        public Color selectionColor = new Color(0.15f, 0.85f, 0.35f, 0.98f);
        [Tooltip("Color del borde al hacer hover (más suave).")]
        public Color hoverColor = new Color(0.4f, 0.75f, 0.4f, 0.8f);
        [Tooltip("Grosor del borde (escala del mesh).")]
        [Range(1.02f, 1.25f)] public float outlineScale = 1.02f;

        private GameObject[] _outlineObjects;
        private MeshFilter[] _outlineMeshFilters;
        private SkinnedMeshRenderer[] _outlineSkinnedSources;
        private Mesh[] _outlineBakedMeshes;
        private Material _outlineMaterial;
        private Material _outlineLeafMaterial;
        private OutlineState _state;
        private bool _isUnit;
        /// <summary>Comida animada (vaca, ciervo): mismo criterio de borde que unidades (paleta + BakeMesh alineado).</summary>
        bool _movingAnimalFoodUnitOutline;
        bool _hostileToPlayer;
        /// <summary>Evita reintentar CreateOutline cada frame si no hay mallas válidas o falló el shader.</summary>
        bool _outlineCreationAttempted;

        void Awake()
        {
            _movingAnimalFoodUnitOutline = IsMovingAnimalFoodUnitStyleOutline();
            _hostileToPlayer = GetComponent<UnitSelectable>() != null && FactionMember.IsHostileToPlayer(gameObject);
            ApplyPaletteFromMode();
            TryCreateOutlineRenderers();
        }

        void Start()
        {
            bool h = GetComponent<UnitSelectable>() != null && FactionMember.IsHostileToPlayer(gameObject);
            if (h == _hostileToPlayer) return;
            _hostileToPlayer = h;
            ApplyPaletteFromMode();
            if (_state != OutlineState.Off) SetState(_state);
        }

        void ApplyPaletteFromMode()
        {
            if (GetComponent<UnitSelectable>() != null)
            {
                if (SelectionOutlineConfig.Global != null)
                {
                    var u = _hostileToPlayer ? SelectionOutlineConfig.Global.enemyUnits : SelectionOutlineConfig.Global.units;
                    selectionColor = u.selectionColor;
                    hoverColor = u.hoverColor;
                    outlineScale = u.outlineScale;
                }
                else if (_hostileToPlayer)
                {
                    selectionColor = new Color(0.62f, 0.14f, 0.12f, 0.98f);
                    hoverColor = new Color(1f, 0.52f, 0.48f, 0.85f);
                }
                return;
            }

            if (_movingAnimalFoodUnitOutline && SelectionOutlineConfig.Global != null)
            {
                var u = SelectionOutlineConfig.Global.units;
                selectionColor = u.selectionColor;
                hoverColor = u.hoverColor;
                outlineScale = u.outlineScale;
                return;
            }

            if (SelectionOutlineConfig.Global != null)
            {
                OutlineAppearance app = GetAppearanceFromConfig();
                selectionColor = app.selectionColor;
                hoverColor = app.hoverColor;
                outlineScale = app.outlineScale;
            }
        }

        OutlineAppearance GetAppearanceFromConfig()
        {
            if (GetComponent<UnitSelectable>() != null)
            {
                var u = SelectionOutlineConfig.Global.units;
                return new OutlineAppearance { selectionColor = u.selectionColor, hoverColor = u.hoverColor, outlineScale = u.outlineScale };
            }
            // Priorizar ResourceSelectable sobre BuildingSelectable: algunos prefabs pueden reutilizar componentes
            // y si ambos existen queremos que el color sea el del bloque "Resources".
            if (GetComponent<ResourceSelectable>() != null) return SelectionOutlineConfig.Global.resources;
            if (GetComponent<BuildingSelectable>() != null) return SelectionOutlineConfig.Global.buildings;
            return SelectionOutlineConfig.Global.buildings;
        }

        /// <summary>Comida con desplazamiento (pastor o agente): mismo pipeline de outline que <see cref="UnitSelectable"/>.</summary>
        bool IsMovingAnimalFoodUnitStyleOutline()
        {
            if (GetComponent<ResourceSelectable>() == null) return false;
            var node = GetComponent<ResourceNode>();
            if (node == null || node.kind != ResourceKind.Food) return false;
            if (GetComponentInChildren<AnimalPastureBehaviour>(true) != null) return true;
            if (GetComponentInChildren<NavMeshAgent>(true) != null) return true;
            return false;
        }

        /// <summary>El SMR con mayor volumen de bounds en mundo (suele ser el cuerpo; descarta collares/LOD minúsculos o duplicados).</summary>
        static int FindPrimarySkinnedMeshIndexForMovingFood(SkinnedMeshRenderer[] skinned)
        {
            int best = -1;
            float bestVol = -1f;
            for (int i = 0; i < skinned.Length; i++)
            {
                var smr = skinned[i];
                if (smr == null || smr.sharedMesh == null) continue;
                if (BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(smr)) continue;
                Vector3 s = smr.bounds.size;
                float vol = s.x * s.y * s.z;
                if (vol > bestVol)
                {
                    bestVol = vol;
                    best = i;
                }
            }
            return best;
        }

        static bool IsLikelyFoliageMaterial(Material mat)
        {
            if (mat == null) return false;
            string n = mat.name.ToLowerInvariant();
            return n.Contains("leaf") || n.Contains("leaves") || n.Contains("foliage") ||
                   n.Contains("hoja") || n.Contains("copa") || n.Contains("canopy") || n.Contains("fronda");
        }

        static Texture ResolveAlphaTexture(Material source)
        {
            if (source == null) return null;
            if (source.HasProperty("_BaseMap"))
            {
                var t = source.GetTexture("_BaseMap");
                if (t != null) return t;
            }
            if (source.HasProperty("_MainTex"))
            {
                var t = source.GetTexture("_MainTex");
                if (t != null) return t;
            }
            return null;
        }

        static float ResolveAlphaCutoff(Material source)
        {
            if (source == null) return 0.33f;
            if (source.HasProperty("_Cutoff")) return Mathf.Clamp01(source.GetFloat("_Cutoff"));
            if (source.HasProperty("_AlphaClipThreshold")) return Mathf.Clamp01(source.GetFloat("_AlphaClipThreshold"));
            return 0.33f;
        }

        void TryCreateOutlineRenderers()
        {
            if (_outlineObjects != null) return;
            CreateOutlineRenderers();
        }

        void CreateOutlineRenderers()
        {
            if (_outlineCreationAttempted) return;
            _outlineCreationAttempted = true;

            var meshFilters = GetComponentsInChildren<MeshFilter>(true);
            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int nMesh = meshFilters != null ? meshFilters.Length : 0;
            int nSkinned = skinned != null ? skinned.Length : 0;
            if (nMesh + nSkinned == 0)
            {
                _outlineObjects = System.Array.Empty<GameObject>();
                _outlineMeshFilters = System.Array.Empty<MeshFilter>();
                _outlineSkinnedSources = System.Array.Empty<SkinnedMeshRenderer>();
                _outlineBakedMeshes = System.Array.Empty<Mesh>();
                return;
            }

            var shader = Shader.Find("Unlit/OutlineCullFront");
            if (shader == null)
            {
                _outlineObjects = System.Array.Empty<GameObject>();
                _outlineMeshFilters = System.Array.Empty<MeshFilter>();
                _outlineSkinnedSources = System.Array.Empty<SkinnedMeshRenderer>();
                _outlineBakedMeshes = System.Array.Empty<Mesh>();
                return;
            }

            _isUnit = GetComponent<UnitSelectable>() != null;
            _hostileToPlayer = _isUnit && FactionMember.IsHostileToPlayer(gameObject);
            ApplyPaletteFromMode();
            // Unidades y recursos estáticos: escalar alrededor del centro del bounds local para evitar "engordar"
            // el tronco por pivote en la base y mejorar ajuste en copas.
            bool centerStaticMeshByBounds = _isUnit || GetComponent<ResourceNode>() != null;

            _outlineMaterial = new Material(shader);
            _outlineObjects = new GameObject[nMesh + nSkinned];
            _outlineMeshFilters = new MeshFilter[nMesh + nSkinned];
            _outlineSkinnedSources = new SkinnedMeshRenderer[nMesh + nSkinned];
            _outlineBakedMeshes = new Mesh[nMesh + nSkinned];
            int idx = 0;
            if (meshFilters != null)
            {
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    if (_movingAnimalFoodUnitOutline)
                        continue;
                    var mf = meshFilters[i];
                    if (mf == null || BuildingTerrainAlignment.ShouldExcludeMeshFilterForOutline(mf)) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    _outlineObjects[idx] = CreateOutlineObject(mf.sharedMesh, mf.transform, centerStaticMeshByBounds, null, mr);
                    _outlineMeshFilters[idx] = _outlineObjects[idx] != null ? _outlineObjects[idx].GetComponent<MeshFilter>() : null;
                    _outlineSkinnedSources[idx] = null;
                    _outlineBakedMeshes[idx] = null;
                    idx++;
                }
            }
            if (skinned != null)
            {
                // Comida animada (vaca FBX): varios SMR duplican siluetas o uno tiene bounds raros → un solo outline en el SMR de mayor volumen visible.
                if (_movingAnimalFoodUnitOutline)
                {
                    int pick = FindPrimarySkinnedMeshIndexForMovingFood(skinned);
                    if (pick >= 0)
                    {
                        var smr = skinned[pick];
                        var bakedMesh = new Mesh();
                        bakedMesh.name = "OutlineBaked";
                        _outlineObjects[idx] = CreateOutlineObject(bakedMesh, smr.transform, centerStaticMeshByBounds, smr, smr);
                        _outlineMeshFilters[idx] = _outlineObjects[idx] != null ? _outlineObjects[idx].GetComponent<MeshFilter>() : null;
                        _outlineSkinnedSources[idx] = smr;
                        _outlineBakedMeshes[idx] = bakedMesh;
                        idx++;
                    }
                }
                else
                {
                    for (int i = 0; i < skinned.Length; i++)
                    {
                        var smr = skinned[i];
                        if (smr == null || smr.sharedMesh == null) continue;
                        if (BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(smr)) continue;
                        var bakedMesh = new Mesh();
                        bakedMesh.name = "OutlineBaked";
                        _outlineObjects[idx] = CreateOutlineObject(bakedMesh, smr.transform, centerStaticMeshByBounds, smr, smr);
                        _outlineMeshFilters[idx] = _outlineObjects[idx] != null ? _outlineObjects[idx].GetComponent<MeshFilter>() : null;
                        _outlineSkinnedSources[idx] = smr;
                        _outlineBakedMeshes[idx] = bakedMesh;
                        idx++;
                    }
                }
            }
            if (idx < _outlineObjects.Length)
            {
                var trimmed = new GameObject[idx];
                var trimmedFilters = new MeshFilter[idx];
                var trimmedSrc = new SkinnedMeshRenderer[idx];
                var trimmedMesh = new Mesh[idx];
                for (int i = 0; i < idx; i++) { trimmed[i] = _outlineObjects[i]; trimmedFilters[i] = _outlineMeshFilters[i]; trimmedSrc[i] = _outlineSkinnedSources[i]; trimmedMesh[i] = _outlineBakedMeshes[i]; }
                _outlineObjects = trimmed;
                _outlineMeshFilters = trimmedFilters;
                _outlineSkinnedSources = trimmedSrc;
                _outlineBakedMeshes = trimmedMesh;
            }

            if (idx == 0)
            {
                if (_outlineMaterial != null)
                {
                    Destroy(_outlineMaterial);
                    _outlineMaterial = null;
                }
                if (_outlineLeafMaterial != null)
                {
                    Destroy(_outlineLeafMaterial);
                    _outlineLeafMaterial = null;
                }
                _outlineObjects = System.Array.Empty<GameObject>();
                _outlineMeshFilters = System.Array.Empty<MeshFilter>();
                _outlineSkinnedSources = System.Array.Empty<SkinnedMeshRenderer>();
                _outlineBakedMeshes = System.Array.Empty<Mesh>();
            }
        }

        GameObject CreateOutlineObject(Mesh mesh, Transform parentForOutline, bool centerByBounds, SkinnedMeshRenderer skinnedSource, Renderer sourceRenderer)
        {
            var go = new GameObject("Outline");
            go.transform.SetParent(parentForOutline != null ? parentForOutline : transform, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * outlineScale;
            if (centerByBounds && skinnedSource == null)
            {
                var bounds = mesh.bounds;
                Vector3 ls = go.transform.localScale;
                go.transform.localPosition = new Vector3(
                    bounds.center.x * (1f - ls.x),
                    bounds.center.y * (1f - ls.y),
                    bounds.center.z * (1f - ls.z));
            }
            else
                go.transform.localPosition = Vector3.zero;

            var outlineMf = go.AddComponent<MeshFilter>();
            outlineMf.sharedMesh = mesh;

            var outlineMr = go.AddComponent<MeshRenderer>();
            if (mesh != null && mesh.subMeshCount > 1)
            {
                var mats = new Material[mesh.subMeshCount];
                Material[] src = sourceRenderer != null ? sourceRenderer.sharedMaterials : null;
                for (int i = 0; i < mats.Length; i++)
                {
                    bool foliage = src != null && i < src.Length && IsLikelyFoliageMaterial(src[i]);
                    if (foliage)
                    {
                        if (_outlineLeafMaterial == null)
                        {
                            _outlineLeafMaterial = new Material(_outlineMaterial);
                            Color c = _outlineLeafMaterial.color;
                            c.a *= 0.35f;
                            _outlineLeafMaterial.color = c;
                            if (_outlineLeafMaterial.HasProperty("_BaseColor"))
                                _outlineLeafMaterial.SetColor("_BaseColor", c);
                            _outlineLeafMaterial.SetFloat("_UseTextureAlpha", 1f);
                        }
                        var srcMat = src[i];
                        var alphaTex = ResolveAlphaTexture(srcMat);
                        if (alphaTex != null)
                            _outlineLeafMaterial.SetTexture("_MainTex", alphaTex);
                        _outlineLeafMaterial.SetFloat("_Cutoff", ResolveAlphaCutoff(srcMat));
                        mats[i] = _outlineLeafMaterial;
                    }
                    else
                        mats[i] = _outlineMaterial;
                }
                outlineMr.sharedMaterials = mats;
            }
            else
                outlineMr.sharedMaterial = _outlineMaterial;
            outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineMr.receiveShadows = false;
            if (_outlineMaterial != null && _outlineMaterial.renderQueue < 2500)
                _outlineMaterial.renderQueue = 2500;

            int outlineVisLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (outlineVisLayer >= 0)
                go.layer = outlineVisLayer;

            go.SetActive(false);
            return go;
        }

        void LateUpdate()
        {
            if (_state == OutlineState.Off || _outlineObjects == null) return;
            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                var smr = _outlineSkinnedSources[i];
                var baked = _outlineBakedMeshes[i];
                if (smr == null || baked == null || _outlineObjects[i] == null) continue;
                smr.BakeMesh(baked);
                var mf = _outlineMeshFilters != null && i < _outlineMeshFilters.Length ? _outlineMeshFilters[i] : null;
                if (mf != null) mf.sharedMesh = baked;
                // Unidades y comida animada: misma corrección de bake que <see cref="UnitSelectable"/>.
                _outlineObjects[i].transform.localPosition = (_isUnit || _movingAnimalFoodUnitOutline) ? -baked.bounds.center : Vector3.zero;
            }
        }

        /// <summary>Activa borde de selección (más marcado).</summary>
        public void SetSelectionOutline(bool on)
        {
            if (on) SetState(OutlineState.Selected);
            else if (_state == OutlineState.Selected) SetState(OutlineState.Off);
        }

        /// <summary>Activa borde de hover (más suave). Solo se muestra si no hay selección.</summary>
        public void SetHoverOutline(bool on)
        {
            if (on && _state != OutlineState.Selected) SetState(OutlineState.Hover);
            else if (on == false && _state == OutlineState.Hover) SetState(OutlineState.Off);
        }

        void SetState(OutlineState state)
        {
            _state = state;
            if (state != OutlineState.Off) TryCreateOutlineRenderers();
            if (_outlineObjects == null || _outlineObjects.Length == 0) return;

            ApplyPaletteFromMode();

            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                if (_outlineObjects[i] != null)
                    _outlineObjects[i].transform.localScale = Vector3.one * outlineScale;
            }

            bool active = state != OutlineState.Off;
            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                if (_outlineObjects[i] != null)
                    _outlineObjects[i].SetActive(active);
            }

            if (_outlineMaterial != null)
            {
                Color c = state == OutlineState.Selected ? selectionColor : hoverColor;
                _outlineMaterial.color = c;
                if (_outlineMaterial.HasProperty("_BaseColor"))
                    _outlineMaterial.SetColor("_BaseColor", c);
                if (_outlineLeafMaterial != null)
                {
                    Color lc = c;
                    lc.a *= 0.35f;
                    _outlineLeafMaterial.color = lc;
                    if (_outlineLeafMaterial.HasProperty("_BaseColor"))
                        _outlineLeafMaterial.SetColor("_BaseColor", lc);
                }
            }
        }
    }
}
