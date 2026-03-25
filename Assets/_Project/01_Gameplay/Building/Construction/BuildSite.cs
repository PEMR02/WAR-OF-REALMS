using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Project.Gameplay.Map;
using Project.Gameplay.Combat;
using Project.Gameplay.Players;
using Project.Gameplay.Resources;
using Project.Gameplay.Units;

namespace Project.Gameplay.Buildings
{
    public class BuildSite : MonoBehaviour
    {
        [Header("Config")]
        public BuildingSO buildingSO;
        public GameObject finalPrefab;
        public float buildTime = 10f;     // segundos con 1 aldeano
        public float refundOnCancel = 0.75f;

        [Header("Path (muros/cercas orgánicos)")]
        [Tooltip("Puntos del recorrido del muro. Si buildingSO.compoundPathMode y hay >= 2 puntos, los segmentos siguen este path.")]
        [SerializeField] List<Vector3> pathPoints = new List<Vector3>();
        [Tooltip("Si true, al cancelar se libera pathOccupiedMin/Size en lugar del footprint estándar.")]
        [SerializeField] bool usePathOccupiedRect;
        [SerializeField] Vector2Int pathOccupiedMin;
        [SerializeField] Vector2Int pathOccupiedSize;

        public void SetPathOccupiedRect(Vector2Int min, Vector2Int size)
        {
            usePathOccupiedRect = true;
            pathOccupiedMin = min;
            pathOccupiedSize = size;
        }

        [Header("Owner (asignado al colocar)")]
        [Tooltip("Jugador que colocó el edificio; se usa para reembolso al cancelar.")]
        public PlayerResources owner;

        [Header("Runtime")]
        [Range(0f, 1f)] public float progress01;
        [Tooltip("Altura Y de la base del edificio (placementY del footprint). Se usa al completar para que el edificio no quede volando.")]
        public float targetBaseY = float.MinValue;

        [Header("Debug")]
        public bool debugLogs = false;

        readonly HashSet<Project.Gameplay.Units.Builder> _builders = new();
        readonly List<Project.Gameplay.Units.Builder> _buildersSnapshot = new List<Project.Gameplay.Units.Builder>(16);
        static readonly Collider[] ResourceOverlapBuffer = new Collider[32];
        bool _completed;
        bool _cellsOccupied;
        Image _progressFill;
        const float ProgressBarHeight = 2.5f;

        /// <summary>Construcción segmento a segmento (muro): lista de posiciones a construir en orden.</summary>
        struct SegmentSlot { public Vector3 pos; public Quaternion rot; public bool isCorner; public bool isGate; }
        List<SegmentSlot> _compoundSegments;
        GameObject _compoundRoot;
        float _compoundSegmentBuildTime;
        int _compoundHpShareDenominator = 1;
        float[] _slotProgress;
        bool[] _slotRemoved;
        readonly Dictionary<int, GameObject> _phasedSegmentBySlot = new Dictionary<int, GameObject>();
        readonly Dictionary<Builder, int> _builderToSlot = new Dictionary<Builder, int>();

        public bool IsCompleted => progress01 >= 1f;
        public bool IsCompoundPathBuilding => _compoundSegments != null && _compoundSegments.Count > 0;

        /// <summary>Ruta paralela al muro: un waypoint por segmento, mismo lado siempre, para que el aldeano recorra y construya sin cortes.</summary>
        List<Vector3> _approachWaypoints;

        public Vector3 GetCurrentWorkPosition()
        {
            if (!IsCompoundPathBuilding || _compoundSegments == null || _slotProgress == null) return transform.position;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (_slotRemoved != null && _slotRemoved[i]) continue;
                if (_slotProgress[i] < 1f) return _compoundSegments[i].pos;
            }
            return transform.position;
        }

        public Vector3 GetCurrentApproachPoint()
        {
            int idx = FirstIncompleteSlotIndex();
            if (_approachWaypoints != null && idx >= 0 && idx < _approachWaypoints.Count)
                return _approachWaypoints[idx];
            return GetCurrentWorkPosition();
        }

        int FirstIncompleteSlotIndex()
        {
            if (_slotProgress == null || _compoundSegments == null) return -1;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (_slotRemoved != null && _slotRemoved[i]) continue;
                if (_slotProgress[i] < 1f) return i;
            }
            return -1;
        }

        public Vector3 GetWorkPositionForBuilder(Builder b)
        {
            if (!IsCompoundPathBuilding || _compoundSegments == null) return transform.position;
            if (b != null && _builderToSlot.TryGetValue(b, out int idx) && idx >= 0 && idx < _compoundSegments.Count)
            {
                if (!(_slotRemoved != null && _slotRemoved[idx]) && _slotProgress != null && idx < _slotProgress.Length && _slotProgress[idx] < 1f - 1e-4f)
                    return _compoundSegments[idx].pos;
            }
            int j = FindNearestIncompleteSlotIndex(b != null ? b.transform.position : transform.position);
            return j >= 0 ? _compoundSegments[j].pos : transform.position;
        }

        public Vector3 GetApproachPointForBuilder(Builder b)
        {
            if (!IsCompoundPathBuilding || _approachWaypoints == null || _compoundSegments == null) return transform.position;
            if (b != null && _builderToSlot.TryGetValue(b, out int idx) && idx >= 0 && idx < _approachWaypoints.Count)
            {
                if (!(_slotRemoved != null && _slotRemoved[idx]) && _slotProgress != null && idx < _slotProgress.Length && _slotProgress[idx] < 1f - 1e-4f)
                    return _approachWaypoints[idx];
            }
            int j = FindNearestIncompleteSlotIndex(b != null ? b.transform.position : transform.position);
            if (j >= 0 && j < _approachWaypoints.Count) return _approachWaypoints[j];
            return _approachWaypoints.Count > 0 ? _approachWaypoints[0] : transform.position;
        }

        int FindNearestIncompleteSlotIndex(Vector3 position)
        {
            if (_compoundSegments == null || _slotProgress == null) return -1;
            float best = float.MaxValue;
            int found = -1;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (_slotRemoved != null && _slotRemoved[i]) continue;
                if (_slotProgress[i] >= 1f) continue;
                float d = (position - _compoundSegments[i].pos).sqrMagnitude;
                if (d < best) { best = d; found = i; }
            }
            return found;
        }

        /// <summary>Path del muro/cerca (varios puntos). Usado cuando buildingSO.compoundPathMode = true.</summary>
        public IReadOnlyList<Vector3> PathPoints => pathPoints;
        List<int> _pathGatePointIndices;

        public void SetPathPoints(IEnumerable<Vector3> points)
        {
            pathPoints.Clear();
            if (points != null) pathPoints.AddRange(points);
        }

        /// <summary>Índices de path points donde el primer segmento del tramo será una puerta (compoundGatePrefab).</summary>
        public void SetPathPointGates(IEnumerable<int> gatePointIndices)
        {
            _pathGatePointIndices = gatePointIndices != null ? new List<int>(gatePointIndices) : new List<int>();
        }

        public void Register(Builder b) => _builders.Add(b);

        public void Unregister(Builder b)
        {
            _builders.Remove(b);
            if (b != null) _builderToSlot.Remove(b);
        }

        /// <summary>Ranuras de grid marcadas como reemplazadas por puerta a mitad de obra.</summary>
        public void NotifyGateReplacedSlots(int startSlotIndex, int count)
        {
            if (_slotRemoved == null || startSlotIndex < 0 || count <= 0) return;
            for (int k = 0; k < count; k++)
            {
                int i = startSlotIndex + k;
                if (i >= 0 && i < _slotRemoved.Length)
                    _slotRemoved[i] = true;
            }
            var toClear = new List<Builder>();
            foreach (var kv in _builderToSlot)
            {
                int si = kv.Value;
                if (si >= startSlotIndex && si < startSlotIndex + count)
                    toClear.Add(kv.Key);
            }
            foreach (var br in toClear)
                _builderToSlot.Remove(br);
            RecalculateCompoundProgress01();
        }

        void RecalculateCompoundProgress01()
        {
            if (_compoundSegments == null || _slotProgress == null)
            {
                progress01 = 0f;
                return;
            }
            int n = _compoundSegments.Count;
            if (n == 0)
            {
                progress01 = 1f;
                return;
            }
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                if (_slotRemoved != null && _slotRemoved[i])
                    sum += 1f;
                else
                    sum += Mathf.Clamp01(_slotProgress[i]);
            }
            progress01 = sum / n;
        }

        bool IsCompoundPathFullyBuilt()
        {
            if (_compoundSegments == null || _slotProgress == null) return false;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (_slotRemoved != null && _slotRemoved[i]) continue;
                if (_slotProgress[i] < 1f - 1e-4f) return false;
            }
            return true;
        }

        void TryFinishCompoundPathIfComplete()
        {
            if (!IsCompoundPathFullyBuilt() || _compoundRoot == null) return;
            float baseYpath = pathPoints != null && pathPoints.Count > 0 ? pathPoints[0].y : _compoundRoot.transform.position.y;
            Vector2Int? pathMin = null, pathSize = null;
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady && pathPoints != null && pathPoints.Count > 0)
            {
                ComputePathOccupiedRect(pathPoints, out Vector2Int min, out Vector2Int size);
                pathMin = min;
                pathSize = size;
            }
            FinishCompoundRoot(_compoundRoot, buildingSO, baseYpath, pathMin, pathSize);
            FinishConstructionAndDestroy();
        }

        bool IsSlotReservedByOtherBuilder(int slot, Builder self)
        {
            if (slot < 0 || _compoundSegments == null) return false;
            foreach (var kv in _builderToSlot)
            {
                if (kv.Key == null || kv.Key == self) continue;
                if (kv.Value != slot) continue;
                if (!_builders.Contains(kv.Key)) continue;
                if (_slotProgress != null && slot < _slotProgress.Length && _slotProgress[slot] < 1f - 1e-4f)
                    return true;
            }
            return false;
        }

        /// <summary>Asigna o muestra el índice de tramo donde trabaja el aldeano (evita solapar mientras quede otro tramo libre).</summary>
        int ResolveWorkSlot(Builder builder)
        {
            int n = _compoundSegments != null ? _compoundSegments.Count : 0;
            if (n == 0 || _slotProgress == null) return -1;

            if (builder != null && _builderToSlot.TryGetValue(builder, out int assigned))
            {
                if (assigned >= 0 && assigned < n
                    && !(_slotRemoved != null && _slotRemoved[assigned])
                    && _slotProgress[assigned] < 1f - 1e-4f)
                    return assigned;
                _builderToSlot.Remove(builder);
            }

            Vector3 pos = builder != null ? builder.transform.position : transform.position;
            int best = -1;
            float bestD = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (_slotRemoved != null && _slotRemoved[i]) continue;
                    if (_slotProgress[i] >= 1f - 1e-4f) continue;
                    if (pass == 0 && IsSlotReservedByOtherBuilder(i, builder)) continue;
                    float d = (_compoundSegments[i].pos - pos).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (best >= 0) break;
            }

            if (builder != null && best >= 0)
                _builderToSlot[builder] = best;
            return best;
        }

        int PerSegmentHpShare()
        {
            if (buildingSO == null) return 1;
            return Mathf.Max(1, Mathf.RoundToInt((float)buildingSO.maxHP / Mathf.Max(1, _compoundHpShareDenominator)));
        }

        void ApplyHealthAndMarkerToSegment(GameObject seg, int slotIndex, SegmentSlot slot)
        {
            if (seg == null) return;
            var marker = seg.GetComponent<CompoundWallSegmentMarker>();
            if (marker == null) marker = seg.AddComponent<CompoundWallSegmentMarker>();
            marker.slotIndex = slotIndex;
            marker.isCornerPiece = slot.isCorner;
            marker.isGatePiece = slot.isGate;
            var h = seg.GetComponent<Health>();
            if (h == null) h = seg.AddComponent<Health>();
            h.InitFromMax(PerSegmentHpShare());
        }

        void FinalizeCompoundSlot(int slotIndex)
        {
            if (_compoundSegments == null || _compoundRoot == null || buildingSO == null) return;
            if (slotIndex < 0 || slotIndex >= _compoundSegments.Count) return;
            if (_slotRemoved != null && _slotRemoved[slotIndex]) return;

            var slot = _compoundSegments[slotIndex];

            if (slot.isCorner)
            {
                GameObject prefab = buildingSO.compoundCornerPrefab;
                if (prefab != null)
                {
                    GameObject seg = Instantiate(prefab, slot.pos, slot.rot, _compoundRoot.transform);
                    seg.transform.localScale = Vector3.one;
                    seg.layer = _compoundRoot.layer;
                    SetLayerRecursive(seg.transform, _compoundRoot.layer);
                    EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength);
                    ApplyHealthAndMarkerToSegment(seg, slotIndex, slot);
                }
            }
            else if (slot.isGate && buildingSO.compoundGatePrefab != null)
            {
                Quaternion gateRot = slot.rot * Quaternion.Euler(buildingSO.compoundGateRotationOffset);
                GameObject seg = Instantiate(buildingSO.compoundGatePrefab, slot.pos, gateRot, _compoundRoot.transform);
                seg.transform.localScale = Vector3.one;
                seg.layer = _compoundRoot.layer;
                SetLayerRecursive(seg.transform, _compoundRoot.layer);
                if (seg.GetComponentInChildren<GateController>(true) == null)
                    seg.AddComponent<GateController>();
                EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength);
                ApplyHealthAndMarkerToSegment(seg, slotIndex, slot);
            }
            else
            {
                if (_phasedSegmentBySlot.TryGetValue(slotIndex, out GameObject wip) && wip != null)
                {
                    var phased = wip.GetComponent<PhasedBuildSegment>();
                    if (phased != null) phased.SetPhase(1f);
                    EnsureSegmentBlocksPassage(wip, buildingSO.compoundSegmentLength);
                    ApplyHealthAndMarkerToSegment(wip, slotIndex, slot);
                }
                else
                {
                    GameObject prefab = buildingSO.compoundSegmentPrefab;
                    if (prefab != null)
                    {
                        GameObject seg = Instantiate(prefab, slot.pos, slot.rot, _compoundRoot.transform);
                        seg.transform.localScale = Vector3.one;
                        seg.layer = _compoundRoot.layer;
                        SetLayerRecursive(seg.transform, _compoundRoot.layer);
                        EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength);
                        ApplyHealthAndMarkerToSegment(seg, slotIndex, slot);
                    }
                }
            }
        }

        /// <summary>
        /// Colocación de Muro_Puerta a mitad de obra: requiere 3 tramos rectos ya construidos consecutivos que incluyan el segmento clicado.
        /// </summary>
        public bool TryReplaceCompoundPathSegmentsWithGate(RaycastHit hit, BuildingSO gateSo, GameObject gatePrefab, Vector3 gateReplacementRotationOffset)
        {
            if (!IsCompoundPathBuilding || _compoundRoot == null || _compoundSegments == null || gatePrefab == null || gateSo == null)
                return false;
            if (_slotRemoved == null || _slotProgress == null) return false;
            if (!TryGetCompoundSlotIndexFromHit(hit, out int clickedSlot)) return false;
            if (!TryFindBuiltStraightRunOfThree(clickedSlot, out int startSlot)) return false;

            for (int k = 0; k < 3; k++)
            {
                int si = startSlot + k;
                if (_phasedSegmentBySlot.TryGetValue(si, out GameObject wip) && wip != null)
                {
                    Destroy(wip);
                    _phasedSegmentBySlot.Remove(si);
                }
            }

            for (int c = _compoundRoot.transform.childCount - 1; c >= 0; c--)
            {
                Transform ch = _compoundRoot.transform.GetChild(c);
                var marker = ch.GetComponentInChildren<CompoundWallSegmentMarker>(true);
                if (marker == null) continue;
                if (marker.slotIndex >= startSlot && marker.slotIndex <= startSlot + 2)
                    Destroy(ch.gameObject);
            }

            for (int c = transform.childCount - 1; c >= 0; c--)
            {
                Transform ch = transform.GetChild(c);
                if (!ch.name.StartsWith("Foundation_", System.StringComparison.Ordinal)) continue;
                string suffix = ch.name.Substring("Foundation_".Length);
                if (!int.TryParse(suffix, out int fi)) continue;
                if (fi >= startSlot && fi <= startSlot + 2)
                    Destroy(ch.gameObject);
            }

            Vector3 center = Vector3.zero;
            for (int k = 0; k < 3; k++)
                center += _compoundSegments[startSlot + k].pos;
            center /= 3f;
            Quaternion rot = _compoundSegments[startSlot + 1].rot * Quaternion.Euler(gateReplacementRotationOffset);
            int layer = _compoundRoot.layer;
            GameObject gate = Instantiate(gatePrefab, center, rot, _compoundRoot.transform);
            gate.transform.localScale = Vector3.one;
            gate.layer = layer;
            SetLayerRecursive(gate.transform, layer);
            if (gate.GetComponentInChildren<GateController>(true) == null)
                gate.AddComponent<GateController>();
            var gMarker = gate.GetComponent<CompoundWallSegmentMarker>();
            if (gMarker == null) gMarker = gate.AddComponent<CompoundWallSegmentMarker>();
            gMarker.slotIndex = startSlot + 1;
            gMarker.isGatePiece = true;
            gMarker.isCornerPiece = false;
            var gh = gate.GetComponent<Health>();
            if (gh == null) gh = gate.AddComponent<Health>();
            gh.InitFromMax(gateSo.maxHP);

            var rootObs = _compoundRoot.GetComponent<NavMeshObstacle>();
            if (rootObs != null) Destroy(rootObs);

            NotifyGateReplacedSlots(startSlot, 3);
            TryFinishCompoundPathIfComplete();
            return true;
        }

        bool TryGetCompoundSlotIndexFromHit(RaycastHit hit, out int slotIndex)
        {
            slotIndex = -1;
            if (_compoundSegments == null) return false;

            var marker = hit.collider.GetComponentInParent<CompoundWallSegmentMarker>();
            if (marker != null && marker.slotIndex >= 0 && marker.slotIndex < _compoundSegments.Count)
            {
                slotIndex = marker.slotIndex;
                return true;
            }

            Transform t = hit.collider.transform;
            while (t != null)
            {
                if (t.parent == transform && t.name.StartsWith("Foundation_", System.StringComparison.Ordinal))
                {
                    string suffix = t.name.Substring("Foundation_".Length);
                    if (int.TryParse(suffix, out int idx) && idx >= 0 && idx < _compoundSegments.Count)
                    {
                        slotIndex = idx;
                        return true;
                    }
                }
                t = t.parent;
            }

            if (_compoundRoot != null && hit.collider.transform.IsChildOf(_compoundRoot.transform))
            {
                slotIndex = NearestCompoundSlotToPoint(hit.point);
                return slotIndex >= 0;
            }

            if (hit.collider.transform.IsChildOf(transform))
            {
                slotIndex = NearestCompoundSlotToPoint(hit.point);
                return slotIndex >= 0;
            }

            if (hit.collider.GetComponentInParent<BuildSite>() == this)
            {
                slotIndex = NearestCompoundSlotToPoint(hit.point);
                return slotIndex >= 0;
            }

            return false;
        }

        int NearestCompoundSlotToPoint(Vector3 world)
        {
            if (_compoundSegments == null) return -1;
            Vector3 flat = world; flat.y = 0f;
            float best = float.MaxValue;
            int found = -1;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                Vector3 p = _compoundSegments[i].pos;
                p.y = 0f;
                float d = (p - flat).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    found = i;
                }
            }
            return found;
        }

        bool TryFindBuiltStraightRunOfThree(int clickedSlot, out int startSlot)
        {
            startSlot = -1;
            if (_compoundSegments == null || _slotProgress == null || _slotRemoved == null) return false;
            const int need = 3;
            for (int s = clickedSlot - (need - 1); s <= clickedSlot; s++)
            {
                if (s < 0 || s + need - 1 >= _compoundSegments.Count) continue;
                bool ok = true;
                for (int k = 0; k < need; k++)
                {
                    int i = s + k;
                    var sl = _compoundSegments[i];
                    if (sl.isCorner || sl.isGate) { ok = false; break; }
                    if (_slotRemoved[i]) { ok = false; break; }
                    if (_slotProgress[i] < 1f - 1e-4f) { ok = false; break; }
                }
                if (!ok) continue;
                if (clickedSlot < s || clickedSlot > s + need - 1) continue;
                startSlot = s;
                return true;
            }
            return false;
        }

		void Start()
		{
			// Asegurar que el solar sea seleccionable (click para ver panel "Cancelar construcción")
			if (GetComponent<BuildingSelectable>() == null)
				gameObject.AddComponent<BuildingSelectable>();

			StartCoroutine(ValidateAndInitCompoundPath());
			_cellsOccupied = true;
			EnsureBuildProgressBar();
		}

		void LateUpdate()
		{
			if (_progressFill != null)
				_progressFill.fillAmount = progress01;
		}

		void EnsureBuildProgressBar()
		{
			if (_progressFill != null) return;

			var root = new GameObject("BuildProgressBar");
			root.transform.SetParent(transform);
			root.transform.localPosition = new Vector3(0f, ProgressBarHeight, 0f);
			root.transform.localRotation = Quaternion.identity;
			root.transform.localScale = Vector3.one;

			var canvas = root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.worldCamera = Camera.main;
			var rt = root.GetComponent<RectTransform>();
			if (rt == null) rt = root.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(2f, 0.2f);
			rt.localScale = Vector3.one * 0.01f;

			var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			bgGo.transform.SetParent(root.transform, false);
			var bgRt = bgGo.GetComponent<RectTransform>();
			bgRt.anchorMin = Vector2.zero;
			bgRt.anchorMax = Vector2.one;
			bgRt.offsetMin = Vector2.zero;
			bgRt.offsetMax = Vector2.zero;
			bgGo.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
			bgGo.GetComponent<Image>().raycastTarget = false;

			var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			fillGo.transform.SetParent(root.transform, false);
			var fillRt = fillGo.GetComponent<RectTransform>();
			fillRt.anchorMin = Vector2.zero;
			fillRt.anchorMax = Vector2.one;
			fillRt.offsetMin = Vector2.zero;
			fillRt.offsetMax = Vector2.zero;
			_progressFill = fillGo.GetComponent<Image>();
			_progressFill.color = new Color(0.2f, 0.75f, 0.25f, 0.95f);
			_progressFill.raycastTarget = false;
			_progressFill.type = Image.Type.Filled;
			_progressFill.fillMethod = Image.FillMethod.Horizontal;
			_progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
			_progressFill.fillAmount = progress01;
		}

		IEnumerator ValidateAndInitCompoundPath()
		{
			yield return null;

			bool valid = buildingSO != null && (
				finalPrefab != null ||
				(buildingSO.isCompound && buildingSO.compoundSegmentPrefab != null)
			);
			if (!valid)
			{
				Debug.LogWarning($"BuildSite inválido destruido: {name} (buildingSO o prefab/compoundSegmentPrefab no configurado)");
				Destroy(gameObject);
				yield break;
			}

			bool usePath = buildingSO.isCompound && buildingSO.compoundPathMode && pathPoints != null && pathPoints.Count >= 2;
			if (usePath)
			{
				_compoundSegments = ComputeCompoundSegmentList(buildingSO);
				if (_compoundSegments == null || _compoundSegments.Count == 0)
				{
					if (debugLogs) Debug.LogWarning("BuildSite: path sin segmentos (¿todo en recursos?).");
					_compoundSegments = null;
				}
				else
				{
					_compoundRoot = new GameObject($"{buildingSO.id}_Compound");
					_compoundRoot.transform.SetParent(transform, worldPositionStays: true);
					_compoundRoot.transform.position = pathPoints[0];
					_compoundRoot.transform.rotation = Quaternion.identity;
					_compoundRoot.layer = gameObject.layer;
					_compoundSegmentBuildTime = buildTime / Mathf.Max(1, _compoundSegments.Count);
					_compoundHpShareDenominator = Mathf.Max(1, _compoundSegments.Count);
					_slotProgress = new float[_compoundSegments.Count];
					_slotRemoved = new bool[_compoundSegments.Count];
					for (int si = 0; si < _compoundSegments.Count; si++)
					{
						_slotProgress[si] = 0f;
						_slotRemoved[si] = false;
					}
					_phasedSegmentBySlot.Clear();
					_builderToSlot.Clear();
					var under = _compoundRoot.AddComponent<CompoundWallUnderConstruction>();
					under.Initialize(this);
					progress01 = 0f;
					ComputeApproachWaypointsParallel();
					SpawnFoundationVisualsAlongPath();
				}
			}
		}

		/// <summary>Calcula la ruta paralela al muro (un waypoint por segmento al mismo lado) para que el aldeano siga un recorrido fijo.</summary>
		void ComputeApproachWaypointsParallel()
		{
			if (_compoundSegments == null || _compoundSegments.Count == 0) return;
			float cellSize = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
			float offsetDist = cellSize * 1.5f;
			// Lado del muro: usar la "derecha" del primer segmento para todos (mismo lado en todo el path).
			Vector3 side = _compoundSegments[0].rot * Vector3.right;
			side.y = 0f;
			if (side.sqrMagnitude < 0.01f) side = Vector3.right;
			else side.Normalize();
			_approachWaypoints = new List<Vector3>(_compoundSegments.Count);
			for (int i = 0; i < _compoundSegments.Count; i++)
			{
				Vector3 wp = _compoundSegments[i].pos + side * offsetDist;
				if (buildingSO != null && buildingSO.compoundPathRaycastTerrain)
					wp.y = SampleGroundHeight(wp, buildingSO.compoundPathGroundMask);
				else
					wp.y = _compoundSegments[i].pos.y;
				_approachWaypoints.Add(wp);
			}
		}

		/// <summary>Muestra la fundación (visual del BuildSite) en todo el trayecto para que al cancelar quede claro el path y se pueda volver a construir.</summary>
		void SpawnFoundationVisualsAlongPath()
		{
			if (_compoundSegments == null || _compoundSegments.Count == 0) return;
			Transform template = GetFoundationVisualTemplate();
			if (template == null) return;
			// Clic izq. (BuildingSelectable) y clic dcho. (orden construir) usan Raycast; los triggers no cuentan por defecto.
			EnsureFoundationRaycastTarget(template.gameObject);
			for (int i = 1; i < _compoundSegments.Count; i++)
			{
				var slot = _compoundSegments[i];
				GameObject clone = Instantiate(template.gameObject, slot.pos, slot.rot, transform);
				clone.name = $"Foundation_{i}";
				clone.layer = gameObject.layer;
				SetLayerRecursive(clone.transform, gameObject.layer);
				EnsureFoundationRaycastTarget(clone);
			}
		}

		/// <summary>Asegura al menos un <see cref="Collider"/> sólido (no trigger) para que la fundación sea clicable.</summary>
		static void EnsureFoundationRaycastTarget(GameObject root)
		{
			if (root == null) return;
			foreach (var c in root.GetComponentsInChildren<Collider>(true))
			{
				if (c != null && c.enabled && !c.isTrigger)
					return;
			}

			if (!TryComputeRendererBoundsIgnoringUI(root.transform, out Bounds worldBounds))
			{
				float s = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
				worldBounds = new Bounds(root.transform.position, new Vector3(s, 0.6f, s));
			}

			var box = root.AddComponent<BoxCollider>();
			box.isTrigger = false;
			box.center = root.transform.InverseTransformPoint(worldBounds.center);
			Vector3 lossy = root.transform.lossyScale;
			Vector3 ext = worldBounds.extents * 2f;
			box.size = new Vector3(
				Mathf.Max(0.35f, ext.x / Mathf.Max(1e-4f, lossy.x)),
				Mathf.Max(0.25f, ext.y / Mathf.Max(1e-4f, lossy.y)),
				Mathf.Max(0.35f, ext.z / Mathf.Max(1e-4f, lossy.z)));
		}

		static bool TryComputeRendererBoundsIgnoringUI(Transform root, out Bounds merged)
		{
			merged = default;
			bool any = false;
			var rends = root.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < rends.Length; i++)
			{
				var r = rends[i];
				if (r == null) continue;
				if (r.gameObject.GetComponent<Canvas>() != null) continue;
				if (!any)
				{
					merged = r.bounds;
					any = true;
				}
				else
					merged.Encapsulate(r.bounds);
			}
			return any;
		}

		/// <summary>Primer hijo que tenga Renderer y no sea la barra de progreso (para clonar fundación a lo largo del path).</summary>
		Transform GetFoundationVisualTemplate()
		{
			for (int i = 0; i < transform.childCount; i++)
			{
				Transform t = transform.GetChild(i);
				if (t.name.IndexOf("ProgressBar", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
				if (t.GetComponentInChildren<Renderer>(true) != null) return t;
			}
			return null;
		}

		void OnDestroy()
		{
			// 🟢 Si se destruye sin completar (cancelación), liberar celdas y destruir muro parcial
			if (!_completed && _cellsOccupied)
			{
				FreeCells();
			}
			if (!_completed && _compoundRoot != null)
			{
				Destroy(_compoundRoot);
			}
		}

		/// <summary>Cancela la construcción: reembolso parcial, libera aldeanos y celdas, destruye el solar.</summary>
		public void CancelConstruction()
		{
			if (_completed) return;

			_completed = true;
			_cellsOccupied = false;

			// Reembolso parcial al jugador
			if (owner != null && buildingSO != null && buildingSO.costs != null)
			{
				foreach (var cost in buildingSO.costs)
				{
					int refund = Mathf.RoundToInt(cost.amount * refundOnCancel);
					if (refund > 0)
						owner.Add(cost.kind, refund);
				}
			}

			FreeCells();

			// Quitar este solar como objetivo de los aldeanos que estaban construyendo (snapshot reutilizable, sin alloc)
			_buildersSnapshot.Clear();
			_buildersSnapshot.AddRange(_builders);
			_builders.Clear();
			foreach (var b in _buildersSnapshot)
			{
				if (b != null) b.ClearBuildTargetIfThis(this);
			}

			if (_compoundRoot != null) Destroy(_compoundRoot);
			Destroy(gameObject);
		}

        public void AddWorkSeconds(float workSeconds, Builder builder = null)
        {
            if (_completed) return;

            if (IsCompoundPathBuilding)
            {
                if (_compoundSegmentBuildTime <= 0.01f) _compoundSegmentBuildTime = 0.01f;
                if (_slotProgress == null || _compoundSegments == null) return;

                int slot = ResolveWorkSlot(builder);
                if (slot < 0)
                {
                    RecalculateCompoundProgress01();
                    TryFinishCompoundPathIfComplete();
                    return;
                }

                _slotProgress[slot] += workSeconds / _compoundSegmentBuildTime;

                var segSlot = _compoundSegments[slot];
                if (!segSlot.isCorner && !segSlot.isGate
                    && !(_slotRemoved != null && _slotRemoved[slot])
                    && _slotProgress[slot] > 0f && _slotProgress[slot] < 1f
                    && buildingSO.compoundSegmentPrefab != null)
                {
                    GameObject prefab = buildingSO.compoundSegmentPrefab;
                    if (prefab.GetComponent<PhasedBuildSegment>() != null)
                    {
                        if (!_phasedSegmentBySlot.TryGetValue(slot, out GameObject seg) || seg == null)
                        {
                            seg = Instantiate(prefab, segSlot.pos, segSlot.rot, _compoundRoot.transform);
                            seg.transform.localScale = Vector3.one;
                            seg.layer = _compoundRoot.layer;
                            SetLayerRecursive(seg.transform, _compoundRoot.layer);
                            _phasedSegmentBySlot[slot] = seg;
                        }
                        var phased = seg.GetComponent<PhasedBuildSegment>();
                        if (phased != null)
                            phased.SetPhase(Mathf.Clamp01(_slotProgress[slot]));
                    }
                }

                if (_slotProgress[slot] >= 1f - 1e-4f && !(_slotRemoved != null && _slotRemoved[slot]))
                {
                    FinalizeCompoundSlot(slot);
                    _phasedSegmentBySlot.Remove(slot);
                    _slotProgress[slot] = 1f;
                    if (builder != null)
                        _builderToSlot.Remove(builder);
                }

                RecalculateCompoundProgress01();
                TryFinishCompoundPathIfComplete();
                return;
            }

            if (buildTime <= 0.01f) buildTime = 0.01f;
            progress01 = Mathf.Clamp01(progress01 + (workSeconds / buildTime));
            if (progress01 < 1f) return;
            Complete();
        }

        void FinishConstructionAndDestroy()
        {
            _completed = true;
            _cellsOccupied = false;

            // El root del muro compuesto es hijo del solar; si no se desvincula, Unity lo destruye junto al BuildSite.
            if (_compoundRoot != null)
            {
                _compoundRoot.transform.SetParent(null, worldPositionStays: true);
                _compoundRoot = null;
            }

            _buildersSnapshot.Clear();
            _buildersSnapshot.AddRange(_builders);
            _builders.Clear();
            foreach (var b in _buildersSnapshot)
            {
                if (b != null) b.ClearBuildTargetIfThis(this);
            }
            if (buildingSO != null)
                Project.UI.GameplayNotifications.Show($"Edificio completado: {buildingSO.id}");
            Destroy(gameObject);
        }

        void Complete()
        {
            _completed = true;

            // 🟢 NO liberar celdas aquí, el BuildingInstance las toma
            // Las celdas quedan ocupadas, solo cambia el dueño (BuildSite → BuildingInstance)

            if (buildingSO != null && buildingSO.isCompound && buildingSO.compoundSegmentPrefab != null)
            {
                CompleteCompound();
            }
            else if (finalPrefab != null)
            {
                GameObject built = Instantiate(finalPrefab, transform.position, transform.rotation);
                // Alinear al terreno usando la Y de base calculada en el placer (placementY).
                float baseY = targetBaseY > -10000f ? targetBaseY : built.transform.position.y;
                AlignBuiltToTerrain(built, baseY);

                // Opcional: aplanar terreno bajo el footprint (estilo Anno).
                if (buildingSO != null && MapGrid.Instance != null && MapGrid.Instance.IsReady)
                {
                    BuildingTerrainFlattener.FlattenUnderBuilding(built.transform.position, buildingSO.size, baseY);
                }
                // Mantener capa de selección/colisión consistente en todo el edificio final.
                built.layer = gameObject.layer;
                SetLayerRecursive(built.transform, built.layer);
                // 🟢 Pasar info del footprint al edificio final
                var instance = built.GetComponent<BuildingInstance>();
                if (instance != null)
                {
                    instance.buildingSO = buildingSO;
                    instance.OccupyCellsOnStart();  // El edificio toma control de las celdas
                }

                // Añadir plataforma 3D baja bajo el edificio para compensar pequeñas irregularidades.
                if (built.GetComponent<BuildingBasePlatform>() == null)
                    built.AddComponent<BuildingBasePlatform>();

                // TownCenter construido por aldeanos: asegurar producción igual que TC inicial del mapa.
                if (built.GetComponent<TownCenter>() != null && built.GetComponent<ProductionBuilding>() == null)
                    built.AddComponent<ProductionBuilding>();

                ConfigureBuiltBuildingRuntime(built, buildingSO);

                var buildingCtrl = built.GetComponent<BuildingController>();
                if (buildingCtrl != null) buildingCtrl.RefreshObstacleAndCollider();

                // ✅ Aumentar límite de población si el edificio lo proporciona
                if (buildingSO != null && buildingSO.populationProvided > 0)
                {
                    var popManager = Object.FindFirstObjectByType<Project.Gameplay.Players.PopulationManager>();
                    if (popManager != null)
                    {
                        popManager.AddHousingCapacity(buildingSO.populationProvided);
                        if (debugLogs) Debug.Log($"🏠 {buildingSO.id} construido → +{buildingSO.populationProvided} población máxima");
                    }
                }
            }
            else if (!buildingSO.isCompound)
                Debug.LogWarning($"BuildSite: finalPrefab null | name={name} | id={GetInstanceID()} | so={(buildingSO?buildingSO.id:"null")}");

            // Snapshot reutilizable (evita alloc y modificar la colección durante la iteración)
            _buildersSnapshot.Clear();
            _buildersSnapshot.AddRange(_builders);
            _builders.Clear();

            for (int i = 0; i < _buildersSnapshot.Count; i++)
            {
                var b = _buildersSnapshot[i];
                if (b != null) b.ClearBuildTargetIfThis(this);
            }

            _cellsOccupied = false;  // 🟢 Ya no somos responsables de las celdas
            if (buildingSO != null)
                Project.UI.GameplayNotifications.Show($"Edificio completado: {buildingSO.id}");
            Destroy(gameObject);
        }

        void CompleteCompound()
        {
            var so = buildingSO;
            bool usePath = so.compoundPathMode && pathPoints != null && pathPoints.Count >= 2;

            if (usePath)
                CompleteCompoundAlongPath(so);
            else
                CompleteCompoundStraightLine(so);
        }

        void CompleteCompoundStraightLine(BuildingSO so)
        {
            float baseY = targetBaseY > -10000f ? targetBaseY : transform.position.y;

            GameObject root = new GameObject($"{so.id}_Compound");
            root.transform.SetPositionAndRotation(transform.position, transform.rotation);
            root.layer = gameObject.layer;

            Quaternion segmentRot = Quaternion.Euler(so.compoundSegmentRotationOffset);
            for (int i = 0; i < so.compoundSegmentCount; i++)
            {
                Vector3 localPos = new Vector3(i * so.compoundSpacing, 0f, 0f);
                GameObject seg = Instantiate(so.compoundSegmentPrefab, root.transform);
                seg.transform.localPosition = localPos;
                seg.transform.localRotation = segmentRot;
                seg.transform.localScale = Vector3.one;
                AlignBuiltToTerrain(seg, baseY);
                seg.layer = root.layer;
                SetLayerRecursive(seg.transform, root.layer);
            }

            FinishCompoundRoot(root, so, baseY, null, null);
        }

        /// <summary>Precalcula la lista de segmentos en orden de recorrido: esquina inicial → tramo → esquina en cada giro → tramo → … (cada bloque construido bloquea el paso).</summary>
        List<SegmentSlot> ComputeCompoundSegmentList(BuildingSO so)
            => ComputeCompoundSegmentListForPath(pathPoints, so, _pathGatePointIndices);

        /// <summary>Versión estática para ocupar solo el perímetro en grid (sin depender de lista en vivo del BuildSite).</summary>
        static List<SegmentSlot> ComputeCompoundSegmentListForPath(
            IReadOnlyList<Vector3> pathPts,
            BuildingSO so,
            List<int> gateLegStarts)
        {
            var list = new List<SegmentSlot>(128);
            if (pathPts == null || pathPts.Count < 2 || so == null) return list;

            float segLength = Mathf.Max(0.5f, so.compoundSegmentLength);
            float minAngleRad = so.compoundCornerMinAngleDeg * Mathf.Deg2Rad;

            for (int j = 0; j < pathPts.Count; j++)
            {
                // 1) Esquina en este punto (inicio, final o giro), si aplica
                bool placeCorner = false;
                Vector3 forwardDir = Vector3.forward;
                if (so.compoundCornerPrefab != null && pathPts.Count >= 2)
                {
                    if (j == 0 || j == pathPts.Count - 1)
                    {
                        if (so.compoundPlaceCornerAtEndpoints)
                        {
                            placeCorner = true;
                            if (j == 0 && pathPts.Count > 1)
                                forwardDir = (pathPts[1] - pathPts[0]).normalized;
                            else if (j == pathPts.Count - 1 && pathPts.Count > 1)
                                forwardDir = (pathPts[j] - pathPts[j - 1]).normalized;
                            forwardDir.y = 0f;
                        }
                    }
                    else
                    {
                        Vector3 dirIn = (pathPts[j] - pathPts[j - 1]).normalized;
                        Vector3 dirOut = (pathPts[j + 1] - pathPts[j]).normalized;
                        dirIn.y = 0f; dirOut.y = 0f;
                        float dot = Vector3.Dot(dirIn, dirOut);
                        if (Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) >= minAngleRad)
                        {
                            placeCorner = true;
                            Vector3 bisector = (dirIn + dirOut).normalized;
                            forwardDir = bisector.sqrMagnitude < 0.01f ? dirOut : bisector;
                        }
                    }
                }
                if (placeCorner)
                {
                    Vector3 pos = pathPts[j];
                    if (so.compoundPathRaycastTerrain)
                        pos.y = SampleGroundHeight(pos, so.compoundPathGroundMask);
                    if (!HasResourceAt(pos, so.compoundSegmentLength * 0.6f))
                    {
                        Quaternion cornerRot = SafeLookRotation(forwardDir) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                        list.Add(new SegmentSlot { pos = pos, rot = cornerRot, isCorner = true });
                    }
                }

                // 2) Segmentos rectos del tramo [j] → [j+1] (el primero puede ser puerta si compoundGatePrefab y punto marcado)
                if (j >= pathPts.Count - 1) continue;
                Vector3 start = pathPts[j];
                Vector3 end = pathPts[j + 1];
                Vector3 toNext = end - start;
                toNext.y = 0f;
                float distance = toNext.magnitude;
                if (distance < 0.01f) continue;
                Vector3 direction = toNext / distance;
                int numSegs = Mathf.Max(2, Mathf.CeilToInt(distance / segLength));
                float step = distance / numSegs;
                bool gateAtThisLeg = so.compoundGatePrefab != null && gateLegStarts != null && gateLegStarts.Contains(j);
                const int gateReplacesSegmentCount = 3;

                if (gateAtThisLeg && numSegs >= gateReplacesSegmentCount)
                {
                    // La puerta ocupa el espacio de 3 bloques: un slot centrado en esos 3 (promedio de posiciones 0.5, 1.5, 2.5).
                    float tCenter = ((0.5f + 1.5f + 2.5f) * step) / (3f * Mathf.Max(distance, 0.001f));
                    tCenter = Mathf.Clamp01(tCenter);
                    Vector3 worldPos = Vector3.Lerp(start, end, tCenter);
                    if (so.compoundPathRaycastTerrain)
                        worldPos.y = SampleGroundHeight(worldPos, so.compoundPathGroundMask);
                    else
                        worldPos.y = start.y;
                    if (!HasResourceAt(worldPos, segLength * 0.6f))
                    {
                        Quaternion rot = SafeLookRotation(direction) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                        list.Add(new SegmentSlot { pos = worldPos, rot = rot, isCorner = false, isGate = true });
                    }
                    for (int i = gateReplacesSegmentCount; i < numSegs; i++)
                    {
                        float t = (i * step + step * 0.5f) / Mathf.Max(distance, 0.001f);
                        worldPos = Vector3.Lerp(start, end, t);
                        if (so.compoundPathRaycastTerrain)
                            worldPos.y = SampleGroundHeight(worldPos, so.compoundPathGroundMask);
                        else
                            worldPos.y = start.y;
                        if (HasResourceAt(worldPos, segLength * 0.6f)) continue;
                        Quaternion rot = SafeLookRotation(direction) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                        list.Add(new SegmentSlot { pos = worldPos, rot = rot, isCorner = false, isGate = false });
                    }
                }
                else
                {
                    for (int i = 0; i < numSegs; i++)
                    {
                        float t = (i * step + step * 0.5f) / Mathf.Max(distance, 0.001f);
                        Vector3 worldPos = Vector3.Lerp(start, end, t);
                        if (so.compoundPathRaycastTerrain)
                            worldPos.y = SampleGroundHeight(worldPos, so.compoundPathGroundMask);
                        else
                            worldPos.y = start.y;
                        if (HasResourceAt(worldPos, segLength * 0.6f)) continue;
                        Quaternion rot = SafeLookRotation(direction) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                        bool isGate = gateAtThisLeg && i == 0;
                        list.Add(new SegmentSlot { pos = worldPos, rot = rot, isCorner = false, isGate = isGate });
                    }
                }
            }
            return list;
        }

        /// <summary>Genera segmentos a lo largo del path (estilo Fence Layout / FencePath). Cada tramo sigue la dirección del path; en diagonales se usa Ceil para evitar huecos; en giros se puede colocar prefab de torre.</summary>
        void CompleteCompoundAlongPath(BuildingSO so)
        {
            float segLength = Mathf.Max(0.5f, so.compoundSegmentLength);
            Vector3 rootPos = pathPoints[0];
            GameObject root = new GameObject($"{so.id}_Compound");
            root.transform.position = rootPos;
            root.transform.rotation = Quaternion.identity;
            root.layer = gameObject.layer;

            // Segmentos por tramo: Ceil para que no queden huecos en diagonales (ligero solape si hace falta)
            for (int p = 0; p < pathPoints.Count - 1; p++)
            {
                Vector3 start = pathPoints[p];
                Vector3 end = pathPoints[p + 1];
                Vector3 toNext = end - start;
                toNext.y = 0f;
                float distance = toNext.magnitude;
                if (distance < 0.01f) continue;
                Vector3 direction = toNext / distance;

                int numSegs = Mathf.Max(1, Mathf.CeilToInt(distance / segLength));
                float step = distance / numSegs;

                for (int i = 0; i < numSegs; i++)
                {
                    float t = (i * step + step * 0.5f) / Mathf.Max(distance, 0.001f);
                    Vector3 worldPos = Vector3.Lerp(start, end, t);
                    if (so.compoundPathRaycastTerrain)
                        worldPos.y = SampleGroundHeight(worldPos, so.compoundPathGroundMask);
                    else
                        worldPos.y = start.y;

                    if (HasResourceAt(worldPos, segLength * 0.6f))
                        continue;

                    Quaternion rot = SafeLookRotation(direction) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                    GameObject seg = Instantiate(so.compoundSegmentPrefab, worldPos, rot, root.transform);
                    seg.transform.localScale = Vector3.one;
                    seg.layer = root.layer;
                    SetLayerRecursive(seg.transform, root.layer);
                }
            }

            // Torres/corners en cambios de dirección (y opcionalmente en extremos)
            if (so.compoundCornerPrefab != null && pathPoints.Count >= 2)
            {
                float minAngleRad = so.compoundCornerMinAngleDeg * Mathf.Deg2Rad;
                for (int j = 0; j < pathPoints.Count; j++)
                {
                    bool placeCorner = false;
                    Vector3 forwardDir = Vector3.forward;

                    if (j == 0 || j == pathPoints.Count - 1)
                    {
                        if (!so.compoundPlaceCornerAtEndpoints) continue;
                        placeCorner = true;
                        if (j == 0 && pathPoints.Count > 1)
                            forwardDir = (pathPoints[1] - pathPoints[0]).normalized;
                        else if (j == pathPoints.Count - 1 && pathPoints.Count > 1)
                            forwardDir = (pathPoints[j] - pathPoints[j - 1]).normalized;
                        forwardDir.y = 0f;
                    }
                    else
                    {
                        Vector3 dirIn = (pathPoints[j] - pathPoints[j - 1]).normalized;
                        Vector3 dirOut = (pathPoints[j + 1] - pathPoints[j]).normalized;
                        dirIn.y = 0f;
                        dirOut.y = 0f;
                        float dot = Vector3.Dot(dirIn, dirOut);
                        float angleRad = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                        if (angleRad >= minAngleRad)
                        {
                            placeCorner = true;
                            Vector3 bisector = (dirIn + dirOut).normalized;
                            if (bisector.sqrMagnitude < 0.01f)
                                forwardDir = dirOut;
                            else
                                forwardDir = bisector;
                        }
                    }

                    if (!placeCorner) continue;

                    Vector3 pos = pathPoints[j];
                    if (so.compoundPathRaycastTerrain)
                        pos.y = SampleGroundHeight(pos, so.compoundPathGroundMask);
                    if (HasResourceAt(pos, so.compoundSegmentLength * 0.6f))
                        continue;
                    Quaternion cornerRot = SafeLookRotation(forwardDir) * Quaternion.Euler(so.compoundSegmentRotationOffset);
                    GameObject corner = Instantiate(so.compoundCornerPrefab, pos, cornerRot, root.transform);
                    corner.transform.localScale = Vector3.one;
                    corner.layer = root.layer;
                    SetLayerRecursive(corner.transform, root.layer);
                }
            }

            Vector2Int? pathMin = null;
            Vector2Int? pathSize = null;
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady && pathPoints.Count > 0)
            {
                ComputePathOccupiedRect(pathPoints, out Vector2Int min, out Vector2Int size);
                pathMin = min;
                pathSize = size;
            }

            float baseY = pathPoints.Count > 0 ? pathPoints[0].y : root.transform.position.y;
            FinishCompoundRoot(root, so, baseY, pathMin, pathSize);
        }

        /// <summary>Calcula el rect de celdas que cubre un path (para ocupar/liberar grid en muros).</summary>
        public static void ComputePathOccupiedRect(IReadOnlyList<Vector3> points, out Vector2Int min, out Vector2Int size)
        {
            min = Vector2Int.zero;
            size = Vector2Int.one;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || points == null || points.Count == 0) return;

            if (points.Count == 0) return;
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2Int cell = MapGrid.Instance.WorldToCell(points[i]);
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y < minZ) minZ = cell.y;
                if (cell.y > maxZ) maxZ = cell.y;
            }
            min = new Vector2Int(minX, minZ);
            size = new Vector2Int(maxX - minX + 1, maxZ - minZ + 1);
            if (size.x < 1) size.x = 1;
            if (size.y < 1) size.y = 1;
        }

        static List<Vector2Int> ComputeCompoundOccupiedCells(IReadOnlyList<SegmentSlot> segments)
        {
            var cells = new List<Vector2Int>(128);
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || segments == null) return cells;

            var unique = new HashSet<Vector2Int>();
            for (int i = 0; i < segments.Count; i++)
            {
                var slot = segments[i];
                if (slot.isGate) continue;

                Vector2Int cell = MapGrid.Instance.WorldToCell(slot.pos);
                if (unique.Add(cell))
                    cells.Add(cell);
            }

            return cells;
        }

        /// <summary>
        /// Respaldo: celdas del perímetro del path en el grid (líneas entre puntos). No rellena el interior del polígono.
        /// </summary>
        static List<Vector2Int> ComputePathOutlineGridCells(IReadOnlyList<Vector3> pathPts)
        {
            var cells = new List<Vector2Int>(64);
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady || pathPts == null || pathPts.Count < 2)
                return cells;

            var unique = new HashSet<Vector2Int>();
            for (int j = 0; j < pathPts.Count - 1; j++)
            {
                Vector2Int a = MapGrid.Instance.WorldToCell(pathPts[j]);
                Vector2Int b = MapGrid.Instance.WorldToCell(pathPts[j + 1]);
                AddGridLineCells(a, b, unique, cells);
            }

            return cells;
        }

        static void AddGridLineCells(Vector2Int c0, Vector2Int c1, HashSet<Vector2Int> unique, List<Vector2Int> outCells)
        {
            int x0 = c0.x, y0 = c0.y, x1 = c1.x, y1 = c1.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                var c = new Vector2Int(x0, y0);
                if (unique.Add(c)) outCells.Add(c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>
        /// El root del muro compuesto ocupa un rectángulo completo en el grid. Si hay puertas dentro del path,
        /// liberamos una pequeña ventana alrededor de cada gate para que A* pueda considerar ese hueco transitable.
        /// </summary>
        void ReleaseCompoundGateCellsFromGrid()
        {
            if (_compoundSegments == null || _compoundSegments.Count == 0) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!_compoundSegments[i].isGate) continue;

                Vector2Int center = MapGrid.Instance.WorldToCell(_compoundSegments[i].pos);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        MapGrid.Instance.SetOccupied(new Vector2Int(center.x + dx, center.y + dy), false);
                    }
                }

                if (debugLogs)
                    Debug.Log($"BuildSite: liberadas celdas del gate en {center} para A*.", this);
            }
        }

        static float SampleGroundHeight(Vector3 xzPos, LayerMask groundMask)
        {
            Vector3 rayStart = xzPos + Vector3.up * 50f;
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, groundMask))
                return hit.point.y;
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain != null)
                return terrain.SampleHeight(xzPos) + terrain.transform.position.y;
            return xzPos.y;
        }

        /// <summary>True si hay un recurso (árbol, piedra, etc.) en la posición; el muro debe saltar ese espacio.</summary>
        static bool HasResourceAt(Vector3 worldPos, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(worldPos, radius, ResourceOverlapBuffer);
            if (count >= ResourceOverlapBuffer.Length) return true;

            for (int i = 0; i < count; i++)
            {
                var hit = ResourceOverlapBuffer[i];
                if (hit != null && hit.GetComponentInParent<ResourceNode>() != null)
                    return true;
            }
            return false;
        }

        void FinishCompoundRoot(GameObject root, BuildingSO so, float baseY, Vector2Int? overrideMin, Vector2Int? overrideSize)
        {
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady && overrideMin == null && overrideSize == null)
                BuildingTerrainFlattener.FlattenUnderBuilding(root.transform.position, so.size, baseY);

            if (root.GetComponent<BuildingBasePlatform>() == null)
                root.AddComponent<BuildingBasePlatform>();

            bool perSegHealth = so.isCompound && so.compoundPathMode && pathPoints != null && pathPoints.Count >= 2;

            var instance = root.AddComponent<BuildingInstance>();
            instance.buildingSO = so;
            instance.perSegmentHealth = perSegHealth;

            if (!perSegHealth)
            {
                var health = root.GetComponent<Health>();
                if (health == null) health = root.AddComponent<Health>();
                health.InitFromMax(so.maxHP);
            }
            if (overrideMin.HasValue && overrideSize.HasValue)
            {
                instance.overrideOccupiedMin = overrideMin;
                instance.overrideOccupiedSize = overrideSize;
            }

            // Ocupación grid: solo celdas del muro (perímetro), nunca el rect AABB completo (inventario interior).
            List<Vector2Int> wallFootprintCells = null;
            if (so.isCompound && so.compoundPathMode && pathPoints != null && pathPoints.Count >= 2)
            {
                List<SegmentSlot> segSrc = (_compoundSegments != null && _compoundSegments.Count > 0)
                    ? _compoundSegments
                    : ComputeCompoundSegmentListForPath(pathPoints, so, _pathGatePointIndices);
                wallFootprintCells = ComputeCompoundOccupiedCells(segSrc);
                if (wallFootprintCells.Count == 0)
                    wallFootprintCells = ComputePathOutlineGridCells(pathPoints);
                if (wallFootprintCells.Count > 0)
                    instance.overrideOccupiedCells = wallFootprintCells;
            }
            else if (_compoundSegments != null && _compoundSegments.Count > 0)
            {
                wallFootprintCells = ComputeCompoundOccupiedCells(_compoundSegments);
                if (wallFootprintCells.Count > 0)
                    instance.overrideOccupiedCells = wallFootprintCells;
            }

            instance.OccupyCellsOnStart();
            ReleaseCompoundGateCellsFromGrid();

            ConfigureBuiltBuildingRuntime(root, so);

            // BuildingController requiere Collider. En compuestos (muro) el root usa trigger para no bloquear el paso por la puerta.
            if (root.GetComponent<Collider>() == null && overrideMin.HasValue && overrideSize.HasValue && MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                float cs = MapGrid.Instance.cellSize;
                Vector2Int min = overrideMin.Value;
                Vector2Int size = overrideSize.Value;
                Vector3 centerWorld = MapGrid.Instance.CellToWorld(new Vector2Int(min.x + size.x / 2, min.y + size.y / 2));
                centerWorld.y = root.transform.position.y + 1f;
                Vector3 sizeWorld = new Vector3(Mathf.Max(1f, size.x) * cs, 2f, Mathf.Max(1f, size.y) * cs);
                var box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(sizeWorld.x / Mathf.Max(0.001f, root.transform.lossyScale.x), sizeWorld.y / Mathf.Max(0.001f, root.transform.lossyScale.y), sizeWorld.z / Mathf.Max(0.001f, root.transform.lossyScale.z));
                box.center = root.transform.InverseTransformPoint(centerWorld);
                box.isTrigger = so.isCompound;
            }
            else if (root.GetComponent<Collider>() == null)
            {
                var box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(so.size.x * 2.5f, 2f, so.size.y * 2.5f);
                box.center = new Vector3(0f, 1f, 0f);
                box.isTrigger = so.isCompound;
            }

            var ctrl = root.GetComponent<BuildingController>();
            if (ctrl == null) ctrl = root.AddComponent<BuildingController>();
            if (ctrl != null)
            {
                if (so.isCompound)
                {
                    ctrl.carveNavMesh = false;
                    var rootObs = root.GetComponent<NavMeshObstacle>();
                    if (rootObs != null) Object.Destroy(rootObs);
                }
                else
                    ctrl.RefreshObstacleAndCollider();
            }

            if (root.GetComponent<BuildingSelectable>() == null)
                root.AddComponent<BuildingSelectable>();

            if (so.populationProvided > 0)
            {
                var popManager = Object.FindFirstObjectByType<Project.Gameplay.Players.PopulationManager>();
                if (popManager != null)
                    popManager.AddHousingCapacity(so.populationProvided);
            }
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        /// <summary>Cada bloque construido del muro debe bloquear el paso solo en su celda: collider y NavMeshObstacle con tamaño = 1 celda.</summary>
        static void EnsureSegmentBlocksPassage(GameObject segment, float segmentLength)
        {
            if (segment == null) return;
            float cellSize = (MapGrid.Instance != null && MapGrid.Instance.IsReady) ? MapGrid.Instance.cellSize : 2.5f;
            float sizeXZ = Mathf.Min(Mathf.Max(0.5f, segmentLength * 0.85f), cellSize) * 0.9f;

            Vector3 boxSize;
            Vector3 boxCenter;
            if (!TryGetWallSegmentLocalBoxFromRenderers(segment.transform, sizeXZ, cellSize, out boxCenter, out boxSize))
            {
                float sizeY = 2f;
                boxSize = new Vector3(sizeXZ, sizeY, sizeXZ);
                boxCenter = new Vector3(0f, sizeY * 0.5f, 0f);
            }

            // Siempre un BoxCollider en la raíz del segmento: cubre toda la altura del modelo (clic/selección isométrica).
            var box = segment.GetComponent<BoxCollider>();
            if (box == null) box = segment.AddComponent<BoxCollider>();
            box.size = boxSize;
            box.center = boxCenter;
            box.isTrigger = false;

            EnsureSegmentNavObstacle(segment, boxSize, boxCenter);
        }

        /// <summary>Bounds locales del muro a partir de los renderers; XZ limitados a la celda/tramo, Y = alto real del mesh.</summary>
        static bool TryGetWallSegmentLocalBoxFromRenderers(Transform root, float sizeXZFromSegment, float cellSize, out Vector3 localCenter, out Vector3 localSize)
        {
            localCenter = Vector3.zero;
            localSize = Vector3.one;
            if (root == null) return false;

            Bounds world = default;
            bool has = false;
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                if (r.gameObject.GetComponent<Canvas>() != null) continue;
                string n = r.gameObject.name;
                if (n.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                if (!has)
                {
                    world = r.bounds;
                    has = true;
                }
                else
                    world.Encapsulate(r.bounds);
            }
            if (!has) return false;

            Vector3 lossy = root.lossyScale;
            if (Mathf.Abs(lossy.x) < 1e-4f || Mathf.Abs(lossy.y) < 1e-4f || Mathf.Abs(lossy.z) < 1e-4f) return false;

            localCenter = root.InverseTransformPoint(world.center);
            localSize = new Vector3(
                world.size.x / Mathf.Abs(lossy.x),
                world.size.y / Mathf.Abs(lossy.y),
                world.size.z / Mathf.Abs(lossy.z));

            float capXZ = Mathf.Min(Mathf.Max(sizeXZFromSegment, 0.5f), cellSize) * 1.05f;
            localSize.x = Mathf.Clamp(Mathf.Max(localSize.x, 0.35f), 0.35f, capXZ);
            localSize.z = Mathf.Clamp(Mathf.Max(localSize.z, 0.35f), 0.35f, capXZ);
            localSize.y = Mathf.Max(localSize.y, 0.5f);
            return true;
        }

        static void EnsureSegmentNavObstacle(GameObject segment, Vector3 boxSize, Vector3 boxCenter)
        {
            if (segment == null) return;
            var obs = segment.GetComponent<NavMeshObstacle>();
            if (obs == null) obs = segment.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.size = boxSize;
            obs.center = boxCenter;
            obs.carving = true;
            obs.enabled = true;
        }

        /// <summary>LookRotation que no falla si la dirección es (casi) cero (p. ej. puntos duplicados en path).</summary>
        static Quaternion SafeLookRotation(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <param name="targetBaseY">Si tiene valor, la base del edificio se coloca en esta Y (misma que el placement). Si no, se usa SampleHeight en el pivot (fallback).</param>
        static void AlignBuiltToTerrain(GameObject go, float? targetBaseY = null)
        {
            if (go == null) return;

            float referenceY;
            if (targetBaseY.HasValue)
            {
                referenceY = targetBaseY.Value;
            }
            else
            {
                var terrain = Object.FindFirstObjectByType<Terrain>();
                if (terrain == null) return;
                Vector3 pivotPos = go.transform.position;
                referenceY = terrain.SampleHeight(pivotPos) + terrain.transform.position.y;
            }

            float bottomY = float.MaxValue;
            bool foundBounds = false;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n == "DropAnchor" || n == "SpawnPoint" || n == "BarAnchor" || r.gameObject.GetComponent<Canvas>() != null)
                    continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                foundBounds = true;
            }

            if (!foundBounds)
            {
                var colliders = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null) continue;
                    bottomY = Mathf.Min(bottomY, c.bounds.min.y);
                    foundBounds = true;
                }
            }

            float pivotY = go.transform.position.y;
            float delta = referenceY - (foundBounds ? bottomY : pivotY);
            if (Mathf.Abs(delta) < 0.0001f) return;
            Vector3 p = go.transform.position;
            p.y += delta;
            go.transform.position = p;
        }

        static void ConfigureBuiltBuildingRuntime(GameObject built, BuildingSO soOverride)
        {
            if (built == null) return;

            Transform anchor = built.transform.Find("BarAnchor");
            if (anchor == null)
            {
                var go = new GameObject("BarAnchor");
                anchor = go.transform;
                anchor.SetParent(built.transform, false);
                anchor.localPosition = new Vector3(0f, 2.5f, 0f);
            }

            var health = built.GetComponent<Health>();
            if (health == null) health = built.GetComponentInChildren<Health>(true);
            if (health != null)
                health.SetBarAnchor(anchor);

            var settings = built.GetComponent<WorldBarSettings>();
            if (settings != null)
            {
                settings.barAnchor = anchor;
                settings.autoAnchorName = "BarAnchor";
                settings.useLocalOffsetOverride = true;
                settings.localOffset = Vector3.zero;
            }

            var prod = built.GetComponent<ProductionBuilding>();
            if (prod != null)
                EnsureSafeSpawnPoint(prod, built.transform, soOverride);
        }

        static void EnsureSafeSpawnPoint(ProductionBuilding prod, Transform root, BuildingSO soOverride)
        {
            if (prod == null || root == null) return;
            float sign = soOverride != null && soOverride.unitSpawnForwardSign >= 0f ? 1f : -1f;
            if (prod.spawnPoint == null)
            {
                var spawnObj = new GameObject("SpawnPoint");
                spawnObj.transform.SetParent(root);
                float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(root.lossyScale.z));
                spawnObj.transform.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                prod.spawnPoint = spawnObj.transform;
                return;
            }

            // Si el spawnpoint está absurdamente lejos por herencia de escala/prefab, normalizar.
            if (prod.spawnPoint.parent == root)
            {
                float worldDist = Vector3.Distance(root.position, prod.spawnPoint.position);
                if (worldDist > 8f)
                {
                    float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(root.lossyScale.z));
                    prod.spawnPoint.localPosition = new Vector3(0f, 0f, (3f * sign) / safeScaleZ);
                }
            }
        }

		/// <summary>Libera las celdas ocupadas por este BuildSite (si se cancela).</summary>
		void FreeCells()
		{
			if (buildingSO == null) return;
			if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;

			Vector2Int min;
			Vector2Int size;
			if (usePathOccupiedRect && pathOccupiedSize.x > 0 && pathOccupiedSize.y > 0)
			{
				min = pathOccupiedMin;
				size = pathOccupiedSize;
			}
			else
			{
				Vector2Int center = MapGrid.Instance.WorldToCell(transform.position);
				size = new Vector2Int(
					Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.x)),
					Mathf.Max(1, Mathf.RoundToInt(buildingSO.size.y))
				);
				min = new Vector2Int(center.x - size.x / 2, center.y - size.y / 2);
			}

			MapGrid.Instance.SetOccupiedRect(min, size, false);

			if (debugLogs) Debug.Log($"🔓 Grid: Liberado {size.x}×{size.y} por cancelación de BuildSite");
		}
    }
}
