using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Project.Gameplay.Map;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
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
        [Tooltip("Logs de slot, approach y distancia al asignar trabajo compuesto (AddWorkSeconds).")]
        public bool compoundConstructionDebug = false;
        [Tooltip("Al seleccionar el solar en la jerarquía: dibuja tramos, laterales ± y approach resuelto (Editor/Play).")]
        public bool compoundDebugGizmos = false;

        [Header("Muro compuesto — NavMesh")]
        [Tooltip("Cada tramo al completarse añade BoxCollider y NavMeshObstacle con carve. Si true: al terminar el muro entero se ejecuta un pase extra por si algún segmento quedó sin carve (compatibilidad).")]
        public bool blockPassageOnlyWhenWholeCompoundCompleted = false;

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

        public bool IsCompleted => progress01 >= 1f;
        public bool IsCompoundPathBuilding => _compoundSegments != null && _compoundSegments.Count > 0;

        /// <summary>Ruta paralela al muro: un waypoint por segmento (lateral elegido por tramo y navegabilidad).</summary>
        List<Vector3> _approachWaypoints;

        const float CompoundApproachNavSampleRadius = 2.75f;
        /// <summary>Prioriza tramos tocando segmento ya levantado (cierra huecos en lugar de saltar al centro del muro).</summary>
        const float CompoundSlotAdjacentBuiltScoreBonus = 900f;
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
            if (idx >= 0 && _compoundSegments != null && idx < _compoundSegments.Count)
                return GetNearestLateralApproachWorldForSlot(idx, transform.position);
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
            Vector3 pos = b != null ? b.transform.position : transform.position;
            int workSlot = ResolveWorkSlot(b);
            if (workSlot >= 0) return _compoundSegments[workSlot].pos;
            return GetDynamicWorkPosition(pos);
        }

        public Vector3 GetApproachPointForBuilder(Builder b)
        {
            if (!IsCompoundPathBuilding || _compoundSegments == null) return transform.position;
            Vector3 pos = b != null ? b.transform.position : transform.position;
            // Misma heurística que AddWorkSeconds: el aldeano camina hacia el approach del tramo donde va a aportar,
            // no solo hacia el slot “más cercano para moverse” (evita IA u orden distinto quedando en un lateral sin seguir el frente).
            int workSlot = ResolveWorkSlot(b);
            if (workSlot >= 0 && TryGetBestApproachPointForSlot(workSlot, pos, out Vector3 approach))
                return approach;
            return GetDynamicApproachPoint(pos);
        }

        /// <summary>
        /// Para comprobar rango de construcción: punto más cercano al aldeano sobre el eje del tramo activo
        /// (mismo slot que <see cref="GetWorkPositionForBuilder"/>), no solo el centro del slot.
        /// </summary>
        public Vector3 GetClosestPointOnActiveCompoundSegment(Vector3 fromWorld, Builder builder = null)
        {
            if (!IsCompoundPathBuilding || _compoundSegments == null) return fromWorld;

            // Mínimo sobre todos los tramos incompletos: el aldeano sigue "en rango" del tramo al que está pegado en el suelo,
            // aunque el score dinámico prefiera otro tramo por approach NavMesh (evita que dejen de construir a mitad de línea).
            float bestSq = float.MaxValue;
            Vector3 bestPt = transform.position;
            bool found = false;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                Vector3 cp = GetClosestPointOnCompoundSlotAxis(i, fromWorld);
                float d = SqrDistXZ(fromWorld, cp);
                if (d < bestSq)
                {
                    bestSq = d;
                    bestPt = cp;
                    found = true;
                }
            }
            return found ? bestPt : transform.position;
        }

        static Vector3 ClosestPointOnSegment3D(Vector3 segA, Vector3 segB, Vector3 p)
        {
            Vector3 ab = segB - segA;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return segA;
            float t = Mathf.Clamp01(Vector3.Dot(p - segA, ab) / len2);
            return segA + ab * t;
        }

        /// <summary>Punto del eje del tramo (segmento recto o posición de esquina) más cercano a <paramref name="fromWorld"/> en XZ.</summary>
        Vector3 GetClosestPointOnCompoundSlotAxis(int slotIndex, Vector3 fromWorld)
        {
            if (_compoundSegments == null || slotIndex < 0 || slotIndex >= _compoundSegments.Count) return fromWorld;
            var slot = _compoundSegments[slotIndex];
            if (slot.isCorner)
                return slot.pos;

            float halfLen = buildingSO != null ? Mathf.Max(0.25f, buildingSO.compoundSegmentLength * 0.5f) : 0.5f;
            Vector3 f = slot.rot * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f)
                f = Vector3.forward;
            else
                f.Normalize();

            Vector3 segA = slot.pos - f * halfLen;
            Vector3 segB = slot.pos + f * halfLen;
            return ClosestPointOnSegment3D(segA, segB, fromWorld);
        }

        float GetSqrDistToCompoundSlotAxis(int slotIndex, Vector3 fromWorld)
        {
            Vector3 cp = GetClosestPointOnCompoundSlotAxis(slotIndex, fromWorld);
            return SqrDistXZ(fromWorld, cp);
        }

        static float SqrDistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Misma separación lateral que <see cref="ComputeApproachWaypointsParallel"/> (1.5 celdas).</summary>
        float GetCompoundLateralOffsetDistance()
        {
            float cellSize = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
            return cellSize * 1.5f;
        }

        /// <summary>Eje del tramo en XZ (local forward del slot). Esquinas/diagonales: más estable que asumir alineación global.</summary>
        static Vector3 GetSlotTangentXZ(in SegmentSlot slot)
        {
            Vector3 f = slot.rot * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return Vector3.forward;
            return f.normalized;
        }

        /// <summary>Vector lateral unitario en XZ: perpendicular al eje del muro (izquierda respecto al avance del segmento).</summary>
        static Vector3 GetSlotLateralAxisXZ(in SegmentSlot slot)
        {
            Vector3 f = GetSlotTangentXZ(slot);
            Vector3 left = Vector3.Cross(Vector3.up, f);
            if (left.sqrMagnitude < 1e-4f)
            {
                Vector3 r = slot.rot * Vector3.right;
                r.y = 0f;
                return r.sqrMagnitude > 1e-4f ? r.normalized : Vector3.right;
            }
            return left.normalized;
        }

        void GetLateralApproachOffsetsWorldXZ(int slotIndex, float offsetDist, out Vector3 wpPlus, out Vector3 wpMinus)
        {
            wpPlus = wpMinus = transform.position;
            if (_compoundSegments == null || slotIndex < 0 || slotIndex >= _compoundSegments.Count) return;
            var slot = _compoundSegments[slotIndex];
            Vector3 side = GetSlotLateralAxisXZ(slot);
            Vector3 p = slot.pos;
            wpPlus = p + side * offsetDist;
            wpMinus = p - side * offsetDist;
        }

        void ApplyApproachGroundY(int slotIndex, ref Vector3 w)
        {
            if (_compoundSegments == null || slotIndex < 0 || slotIndex >= _compoundSegments.Count) return;
            var slot = _compoundSegments[slotIndex];
            if (buildingSO != null && buildingSO.compoundPathRaycastTerrain)
                w.y = SampleGroundHeight(w, buildingSO.compoundPathGroundMask);
            else
                w.y = slot.pos.y;
        }

        /// <summary>
        /// Resuelve approach: dos laterales ±eje del tramo; prioriza el que tenga NavMesh cercano y esté más cerca en XZ del aldeano.
        /// Si ningún lateral proyecta a NavMesh, devuelve el más cercano en XZ igualmente (fallback).
        /// </summary>
        bool TryResolveApproachWorldForSlot(int slotIndex, Vector3 fromWorld, out Vector3 approachWorld, out bool bothLateralsNavigable)
        {
            bothLateralsNavigable = false;
            approachWorld = transform.position;
            if (_compoundSegments == null || slotIndex < 0 || slotIndex >= _compoundSegments.Count) return false;

            float offsetDist = GetCompoundLateralOffsetDistance();
            GetLateralApproachOffsetsWorldXZ(slotIndex, offsetDist, out Vector3 w1, out Vector3 w2);
            ApplyApproachGroundY(slotIndex, ref w1);
            ApplyApproachGroundY(slotIndex, ref w2);

            float probeY = Mathf.Max(w1.y, w2.y) + 3f;
            var p1 = new Vector3(w1.x, probeY, w1.z);
            var p2 = new Vector3(w2.x, probeY, w2.z);
            bool ok1 = NavMesh.SamplePosition(p1, out NavMeshHit hit1, CompoundApproachNavSampleRadius, NavMesh.AllAreas);
            bool ok2 = NavMesh.SamplePosition(p2, out NavMeshHit hit2, CompoundApproachNavSampleRadius, NavMesh.AllAreas);
            bothLateralsNavigable = ok1 && ok2;

            if (ok1 && ok2)
            {
                float d1 = SqrDistXZ(fromWorld, hit1.position);
                float d2 = SqrDistXZ(fromWorld, hit2.position);
                approachWorld = d1 <= d2 ? hit1.position : hit2.position;
                approachWorld.y = d1 <= d2 ? hit1.position.y : hit2.position.y;
                return true;
            }
            if (ok1) { approachWorld = hit1.position; return true; }
            if (ok2) { approachWorld = hit2.position; return true; }

            bool usePlus = SqrDistXZ(fromWorld, w1) <= SqrDistXZ(fromWorld, w2);
            approachWorld = usePlus ? w1 : w2;
            return false;
        }

        /// <summary>Punto de approach: lateral por eje del tramo + preferencia NavMesh + cercanía al aldeano.</summary>
        Vector3 GetNearestLateralApproachWorldForSlot(int slotIndex, Vector3 fromWorld)
        {
            TryResolveApproachWorldForSlot(slotIndex, fromWorld, out Vector3 approach, out _);
            return approach;
        }

        bool IsCompoundSegmentBuiltOrRemoved(int i)
        {
            if (_compoundSegments == null || _slotProgress == null || i < 0 || i >= _compoundSegments.Count) return false;
            if (_slotRemoved != null && _slotRemoved[i]) return true;
            return _slotProgress[i] >= 1f - 1e-4f;
        }

        bool IsCompoundSegmentIncompleteWork(int i)
        {
            if (_compoundSegments == null || _slotProgress == null || i < 0 || i >= _compoundSegments.Count) return false;
            if (_slotRemoved != null && _slotRemoved[i]) return false;
            return _slotProgress[i] < 1f - 1e-4f;
        }

        int BuiltNeighborCountForIncompleteSlot(int i)
        {
            if (!IsCompoundSegmentIncompleteWork(i)) return 0;
            int c = 0;
            if (IsCompoundSegmentBuiltOrRemoved(i - 1)) c++;
            if (IsCompoundSegmentBuiltOrRemoved(i + 1)) c++;
            return c;
        }

        float CompoundSlotContiguityScoreAdjustment(int slotIndex)
        {
            if (!IsCompoundSegmentIncompleteWork(slotIndex)) return 0f;
            // Solo atraer hacia tramos que tocan segmento ya levantado (cerrar huecos). Sin bonus por "extremo de cadena"
            // para no forzar que todo el mundo trabaje solo en índices 0 / último cuando el muro sigue todo en fantasmas.
            return -(CompoundSlotAdjacentBuiltScoreBonus * BuiltNeighborCountForIncompleteSlot(slotIndex));
        }

        bool IsSlotBuildable(int slot)
        {
            if (_compoundSegments == null || _slotProgress == null) return false;
            if (slot < 0 || slot >= _compoundSegments.Count) return false;
            if (_slotRemoved != null && _slotRemoved[slot]) return false;
            return _slotProgress[slot] < 1f - 1e-4f;
        }

        /// <summary>Mismo lateral que el visual (eje del tramo en XZ); en diagonales <c>rot*right</c> del prefab no coincide con perpendicular al muro.</summary>
        bool TryGetBestApproachPointForSlot(int slot, Vector3 builderPos, out Vector3 approach)
        {
            approach = transform.position;
            if (!IsSlotBuildable(slot)) return false;
            TryResolveApproachWorldForSlot(slot, builderPos, out approach, out _);
            return true;
        }

        public int FindBestDynamicWorkSlot(Vector3 builderPos, float maxPreferredDistance = 999f)
        {
            if (_compoundSegments == null || _slotProgress == null) return -1;

            int best = -1;
            float bestScore = float.MaxValue;
            float maxPreferredSqr = maxPreferredDistance * maxPreferredDistance;

            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                if (!TryGetBestApproachPointForSlot(i, builderPos, out Vector3 approach)) continue;

                float distApproach = SqrDistXZ(approach, builderPos);
                if (distApproach > maxPreferredSqr) continue;

                float distWork = GetSqrDistToCompoundSlotAxis(i, builderPos);

                float score = distApproach * 0.7f + distWork * 0.3f + CompoundSlotContiguityScoreAdjustment(i);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best >= 0) return best;

            bestScore = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                if (!TryGetBestApproachPointForSlot(i, builderPos, out Vector3 approach)) continue;

                float distApproach = SqrDistXZ(approach, builderPos);
                float distWork = GetSqrDistToCompoundSlotAxis(i, builderPos);
                float score = distApproach * 0.7f + distWork * 0.3f + CompoundSlotContiguityScoreAdjustment(i);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Tramo hacia el que conviene caminar: cerca del eje del muro respecto al aldeano, luego approach más cercano.
        /// (El score de <see cref="FindBestDynamicWorkSlot"/> sirve para otras heurísticas; para MoveTo priorizamos proximidad real.)
        /// </summary>
        int FindSlotForMovementTowardWall(Vector3 builderPos)
        {
            if (_compoundSegments == null || _slotProgress == null) return -1;

            float minAxisSq = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                float ax = GetSqrDistToCompoundSlotAxis(i, builderPos);
                if (ax < minAxisSq) minAxisSq = ax;
            }
            if (minAxisSq >= float.MaxValue * 0.5f) return -1;

            float cell = MapGrid.GetCellSizeOrDefault();
            float tieSq = (cell * 1.25f) * (cell * 1.25f);

            int best = -1;
            float bestApSq = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                if (GetSqrDistToCompoundSlotAxis(i, builderPos) > minAxisSq + tieSq) continue;
                if (!TryGetBestApproachPointForSlot(i, builderPos, out Vector3 ap)) continue;
                float dAp = SqrDistXZ(ap, builderPos);
                if (dAp < bestApSq)
                {
                    bestApSq = dAp;
                    best = i;
                }
            }
            if (best >= 0) return best;

            bestApSq = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                if (!TryGetBestApproachPointForSlot(i, builderPos, out Vector3 ap)) continue;
                float dAp = SqrDistXZ(ap, builderPos);
                if (dAp < bestApSq)
                {
                    bestApSq = dAp;
                    best = i;
                }
            }
            return best;
        }

        public Vector3 GetDynamicWorkPosition(Vector3 builderPos)
        {
            int slot = FindSlotForMovementTowardWall(builderPos);
            if (slot >= 0) return _compoundSegments[slot].pos;
            int fi = FirstIncompleteSlotIndex();
            if (fi >= 0) return _compoundSegments[fi].pos;
            return transform.position;
        }

        public Vector3 GetDynamicApproachPoint(Vector3 builderPos)
        {
            int slot = FindSlotForMovementTowardWall(builderPos);
            if (slot >= 0 && TryGetBestApproachPointForSlot(slot, builderPos, out Vector3 approach))
                return approach;
            int fi = FirstIncompleteSlotIndex();
            if (fi >= 0 && TryGetBestApproachPointForSlot(fi, builderPos, out Vector3 ap2))
                return ap2;
            return transform.position;
        }

        /// <summary>API legacy; el muro compuesto ya no reserva tramo por aldeano.</summary>
        public void ReleaseCompoundWorkSlot(Builder b, bool putSlotOnCooldown = false) { }

        /// <summary>API legacy; sin propiedad fija de tramo.</summary>
        public bool TryGetAssignedCompoundSlot(Builder b, out int slot)
        {
            slot = -1;
            return false;
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
            FinalizeCompoundBlocking();
            if (blockPassageOnlyWhenWholeCompoundCompleted)
                ApplyDeferredNavMeshCarvingForCompletedCompound(_compoundRoot);
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

        /// <summary>Tramo donde aplicar trabajo: prioriza el eje del muro más cercano al aldeano (evita aportar a un tramo lejano mientras está pegado a otro).</summary>
        int ResolveWorkSlot(Builder builder)
        {
            Vector3 pos = builder != null ? builder.transform.position : transform.position;
            if (_compoundSegments == null || _slotProgress == null) return -1;

            float minAxisSq = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                float ax = GetSqrDistToCompoundSlotAxis(i, pos);
                if (ax < minAxisSq) minAxisSq = ax;
            }
            if (minAxisSq >= float.MaxValue * 0.5f) return -1;

            float cell = MapGrid.GetCellSizeOrDefault();
            float tieSq = (cell * 0.55f) * (cell * 0.55f);

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                if (!IsSlotBuildable(i)) continue;
                float axisSq = GetSqrDistToCompoundSlotAxis(i, pos);
                if (axisSq > minAxisSq + tieSq) continue;
                if (!TryGetBestApproachPointForSlot(i, pos, out Vector3 ap)) continue;
                float score = axisSq * 0.65f + SqrDistXZ(ap, pos) * 0.35f + CompoundSlotContiguityScoreAdjustment(i);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best >= 0) return best;
            return FindBestDynamicWorkSlot(pos);
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
                    EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength, true);
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
                EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength, true);
                ApplyHealthAndMarkerToSegment(seg, slotIndex, slot);
            }
            else
            {
                if (_phasedSegmentBySlot.TryGetValue(slotIndex, out GameObject wip) && wip != null)
                {
                    var phased = wip.GetComponent<PhasedBuildSegment>();
                    if (phased != null) phased.SetPhase(1f);
                    EnsureSegmentBlocksPassage(wip, buildingSO.compoundSegmentLength, true);
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
                        EnsureSegmentBlocksPassage(seg, buildingSO.compoundSegmentLength, true);
                        ApplyHealthAndMarkerToSegment(seg, slotIndex, slot);
                    }
                }
            }
        }

        /// <summary>Al completar todo el muro: refuerza collider + carve NavMesh en cada pieza marcada (idempotente).</summary>
        void FinalizeCompoundBlocking()
        {
            if (_compoundRoot == null) return;

            float len = buildingSO != null ? buildingSO.compoundSegmentLength : 2f;
            for (int i = 0; i < _compoundRoot.transform.childCount; i++)
            {
                Transform ch = _compoundRoot.transform.GetChild(i);
                if (ch == null) continue;

                var marker = ch.GetComponentInChildren<CompoundWallSegmentMarker>(true);
                if (marker == null) continue;

                EnsureSegmentBlocksPassage(ch.gameObject, len, true);
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

			// Init síncrono: AutoAssignBuilders corre en el mismo frame tras Instantiate; sin esto,
			// _compoundSegments/_slotProgress faltan un frame y el Builder usa huella de edificio normal.
			ValidateAndInitCompoundPath();
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

		void ValidateAndInitCompoundPath()
		{
			bool valid = buildingSO != null && (
				finalPrefab != null ||
				(buildingSO.isCompound && buildingSO.compoundSegmentPrefab != null)
			);
			if (!valid)
			{
				Debug.LogWarning($"BuildSite inválido destruido: {name} (buildingSO o prefab/compoundSegmentPrefab no configurado)");
				Destroy(gameObject);
				return;
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
					var under = _compoundRoot.AddComponent<CompoundWallUnderConstruction>();
					under.Initialize(this);
					progress01 = 0f;
					ComputeApproachWaypointsParallel();
					SpawnFoundationVisualsAlongPath();
				}
			}
		}

		/// <summary>Calcula la ruta paralela al muro (un waypoint por segmento, lateral coherente con la orientación de cada tramo).</summary>
		void ComputeApproachWaypointsParallel()
		{
			if (_compoundSegments == null || _compoundSegments.Count == 0) return;
			_approachWaypoints = new List<Vector3>(_compoundSegments.Count);
			for (int i = 0; i < _compoundSegments.Count; i++)
			{
				// Lista por defecto: lateral + desde el centro del tramo (empate → mismo que antes). Movimiento real usa ±side según posición del aldeano.
				Vector3 wp = GetNearestLateralApproachWorldForSlot(i, _compoundSegments[i].pos);
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
				float s = MapGrid.GetCellSizeOrDefault();
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
                // Tras init síncrono en Start debería existir; si no, no consumir el frame como obra normal.
                if (_slotProgress == null || _compoundSegments == null)
                {
                    if (debugLogs)
                        Debug.LogWarning($"BuildSite: AddWorkSeconds sin datos de path ({name}). ¿Init falló o orden de ejecución inesperado?", this);
                    return;
                }

                int slot = ResolveWorkSlot(builder);
                if (slot < 0)
                {
                    RecalculateCompoundProgress01();
                    TryFinishCompoundPathIfComplete();
                    return;
                }

                if (compoundConstructionDebug && builder != null)
                {
                    TryResolveApproachWorldForSlot(slot, builder.transform.position, out Vector3 appDbg, out bool navOk);
                    Vector3 workDbg = GetWorkPositionForBuilder(builder);
                    Debug.Log($"[Compound {name}] builder={builder.name} slot={slot} work={workDbg} approach={appDbg} navBothLaterals={navOk} distWorkXZ={SqrDistXZ(builder.transform.position, workDbg):F2}", this);
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
                // Misma referencia que el placer (placementY). Fallback = Y del solar (equivale a la instancia antes de alinear).
                float baseY = targetBaseY > -10000f ? targetBaseY : transform.position.y;
                // Aplanar antes de alinear: el heightmap ya se acerca a baseY bajo el footprint; luego la base visual sigue el mismo plano.
                if (buildingSO != null && MapGrid.Instance != null && MapGrid.Instance.IsReady)
                {
                    BuildingTerrainFlattener.FlattenUnderBuilding(transform.position, buildingSO.size, baseY);
                }

                GameObject built = Instantiate(finalPrefab, transform.position, transform.rotation);
                AlignBuiltToTerrain(built, baseY);
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
                WireProductionBuildingFromOwner(built);
                ApplyOwnerFactionToBuilding(built);

                var buildingCtrl = built.GetComponent<BuildingController>();
                if (buildingCtrl != null) buildingCtrl.RefreshObstacleAndCollider();

                // ✅ Aumentar límite de población si el edificio lo proporciona
                if (buildingSO != null && buildingSO.populationProvided > 0)
                {
                    var popManager = Project.Gameplay.Players.PopulationManager.ResolveForOwner(owner);
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
                float cs = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
                box.size = new Vector3(so.size.x * cs, 2f, so.size.y * cs);
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
                var popManager = Project.Gameplay.Players.PopulationManager.ResolveForOwner(owner);
                if (popManager != null)
                    popManager.AddHousingCapacity(so.populationProvided);
            }
        }

        void WireProductionBuildingFromOwner(GameObject built)
        {
            if (built == null || owner == null) return;
            var prod = built.GetComponent<ProductionBuilding>();
            if (prod == null) return;
            prod.owner = owner;
            prod.populationManager = Project.Gameplay.Players.PopulationManager.ResolveForOwner(owner);
        }

        void ApplyOwnerFactionToBuilding(GameObject built)
        {
            if (built == null || owner == null) return;
            var src = owner.GetComponentInParent<FactionMember>();
            if (src == null) return;
            var fm = built.GetComponent<FactionMember>();
            if (fm == null) fm = built.AddComponent<FactionMember>();
            fm.faction = src.faction;
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        /// <summary>Collider físico del segmento (siempre). NavMeshObstacle+carve si <paramref name="applyNavMeshCarve"/>.</summary>
        static void EnsureSegmentBlocksPassage(GameObject segment, float segmentLength, bool applyNavMeshCarve = true)
        {
            if (segment == null) return;
            float cellSize = MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize : 2.5f;
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

            if (applyNavMeshCarve)
                EnsureSegmentNavObstacle(segment, boxSize, boxCenter);
        }

        /// <summary>
        /// Tras completar el último tramo con carve diferido: añade NavMeshObstacle a cada pieza ya colocada bajo el compound root.
        /// </summary>
        void ApplyDeferredNavMeshCarvingForCompletedCompound(GameObject root)
        {
            if (root == null) return;
            var markers = root.GetComponentsInChildren<CompoundWallSegmentMarker>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;
                GameObject seg = markers[i].gameObject;
                var box = seg.GetComponent<BoxCollider>();
                if (box == null || !box.enabled) continue;
                var obs = seg.GetComponent<NavMeshObstacle>();
                if (obs != null && obs.carving) continue;
                EnsureSegmentNavObstacle(seg, box.size, box.center);
            }
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
                if (r == null || !r.enabled || BuildingTerrainAlignment.ShouldExcludeRendererForBaseAlignment(r)) continue;
                bottomY = Mathf.Min(bottomY, r.bounds.min.y);
                foundBounds = true;
            }

            if (!foundBounds)
            {
                var colliders = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null || !c.enabled || BuildingTerrainAlignment.ShouldExcludeColliderForBaseAlignment(c)) continue;
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

        void OnDrawGizmosSelected()
        {
            if (!compoundDebugGizmos) return;
            if (!IsCompoundPathBuilding || _compoundSegments == null) return;

            float off = Application.isPlaying
                ? GetCompoundLateralOffsetDistance()
                : (MapGrid.Instance != null && MapGrid.Instance.IsReady ? MapGrid.Instance.cellSize * 1.5f : 3.75f);

            for (int i = 0; i < _compoundSegments.Count; i++)
            {
                var slot = _compoundSegments[i];
                Vector3 p = slot.pos;
                Vector3 lat = GetSlotLateralAxisXZ(slot);

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(p, p + lat * off);
                Gizmos.DrawLine(p, p - lat * off);
                Gizmos.DrawSphere(p, 0.22f);

                Vector3 refPos = Application.isPlaying ? transform.position : p;
                if (TryResolveApproachWorldForSlot(i, refPos, out Vector3 app, out _))
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(p, app);
                    Gizmos.DrawSphere(app, 0.32f);
                }
            }
        }
    }
}
