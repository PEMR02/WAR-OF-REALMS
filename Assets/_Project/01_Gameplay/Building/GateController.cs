using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Faction;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    public enum GateState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    /// <summary>
    /// Sistema de puerta RTS robusto:
    /// - Trigger (detección principal)
    /// - NavMeshObstacle carving dinámico
    /// - Repath de agentes cercanos
    /// - "Force pass through" para guiar a los agentes por Entry/Exit
    /// - Filtro de aliados/enemigos via FactionMember (opcional)
    /// </summary>
    [DisallowMultipleComponent]
    public class GateController : MonoBehaviour
    {
        [Header("Refs")]
        public Animator animator;
        public NavMeshObstacle obstacle;
        public Transform gateCenter;
        public Transform entryPoint;
        public Transform exitPoint;

        [Header("Detection")]
        public float openRadius = 4f;
        public float repathRadius = 8f;
        public LayerMask unitLayer = -1;
        public bool allowEnemies = false;

        [Header("Timings")]
        public float openDelay = 0.1f;
        [Tooltip("Segundos tras salir el último antes de cerrar. Unidades grandes (caballo) necesitan más tiempo.")]
        public float closeDelay = 4f;

        [Tooltip("Si Entry/Exit son None en el prefab, se crean en runtime a esta distancia del centro (metros).")]
        public float entryExitDistance = 2.5f;
        [Tooltip("Tamaño del BoxCollider trigger si se crea en runtime (prefab sin trigger).")]
        public Vector3 triggerSize = new Vector3(5f, 2.5f, 3f);

        [Header("Pathfinding (MapGrid)")]
        [Tooltip("Mitad del ancho en metros del corredor de celdas transitables cuando la puerta está abierta (A*).")]
        public float gatePathHalfWidthMeters = 1.75f;
        [Tooltip("Mitad de la profundidad en metros a lo largo del eje forward de la puerta.")]
        public float gatePathHalfDepthMeters = 2.5f;

        [Header("Debug")]
        public bool debugLogs = false;

        [SerializeField] int unitsInside;
        [SerializeField] GateState currentState = GateState.Closed;

        readonly List<Vector2Int> _registeredGatePathCells = new List<Vector2Int>(32);

        static readonly List<GateController> s_gates = new List<GateController>(256);
        static readonly Collider[] s_overlapBuffer = new Collider[128];

        Coroutine _stateRoutine;
        readonly HashSet<int> _trackedAgents = new HashSet<int>(64);
        FactionMember _gateFaction;

        public GateState CurrentState => currentState;

        void Reset()
        {
            gateCenter = transform;
            unitLayer = -1;
        }

        void Awake()
        {
            if (gateCenter == null) gateCenter = transform;

            // Evitar conflicto: GateOpener deshabilita el NavMeshObstacle por completo.
            // Aquí hacemos lo mismo al abrir, porque con carving=false pero enabled=true
            // el obstacle sigue interfiriendo con avoidance local y las unidades se frenan en el hueco.
            var legacy = GetComponent<GateOpener>();
            if (legacy != null)
            {
                legacy.enabled = false;
                if (debugLogs) Debug.Log($"[GateController] {name}: GateOpener deshabilitado para usar solo GateController.", this);
            }

            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (obstacle == null)
                obstacle = GetComponentInChildren<NavMeshObstacle>(true);

            _gateFaction = GetComponentInParent<FactionMember>();

            EnsureEntryExitPoints();

            EnsureTriggerRelay();

            if (obstacle == null)
                Debug.LogError($"[GateController] Falta NavMeshObstacle en '{name}'.", this);
            else
            {
                bool shouldBlock = (currentState == GateState.Closed);
                obstacle.enabled = shouldBlock;
                obstacle.carving = shouldBlock;
            }

            if (animator == null)
                Debug.LogWarning($"[GateController] Falta Animator en '{name}' (se abrirá pathfinding pero no animación).", this);
        }

        /// <summary>
        /// Garantiza dos puntos de paso en lados opuestos del portón.
        /// Si el prefab trae refs nulas o mal colocadas, los corrige automáticamente.
        /// </summary>
        void EnsureEntryExitPoints()
        {
            Transform root = gateCenter != null ? gateCenter : transform;
            Vector3 localFwd = root.InverseTransformDirection(root.forward);
            localFwd.y = 0f;
            if (localFwd.sqrMagnitude < 0.0001f) localFwd = new Vector3(0f, 0f, 1f);
            else localFwd.Normalize();

            Vector3 desiredEntryLocal = -localFwd * entryExitDistance;
            Vector3 desiredExitLocal = localFwd * entryExitDistance;

            if (entryPoint == null)
            {
                var go = new GameObject("EntryPoint");
                go.transform.SetParent(root, false);
                go.transform.localPosition = desiredEntryLocal;
                go.transform.localRotation = Quaternion.identity;
                entryPoint = go.transform;
                if (debugLogs) Debug.Log($"[GateController] {name}: EntryPoint creado en runtime (prefab tenía None).", this);
            }
            if (exitPoint == null)
            {
                var go = new GameObject("ExitPoint");
                go.transform.SetParent(root, false);
                go.transform.localPosition = desiredExitLocal;
                go.transform.localRotation = Quaternion.identity;
                exitPoint = go.transform;
                if (debugLogs) Debug.Log($"[GateController] {name}: ExitPoint creado en runtime (prefab tenía None).", this);
            }

            float entrySigned = Vector3.Dot(entryPoint.localPosition, localFwd);
            float exitSigned = Vector3.Dot(exitPoint.localPosition, localFwd);
            bool tooCloseToEachOther = (entryPoint.localPosition - exitPoint.localPosition).sqrMagnitude < 0.25f;
            bool sameSide = Mathf.Sign(entrySigned) == Mathf.Sign(exitSigned);
            bool tooCloseToCenter = Mathf.Abs(entrySigned) < entryExitDistance * 0.35f || Mathf.Abs(exitSigned) < entryExitDistance * 0.35f;
            if (tooCloseToEachOther || sameSide || tooCloseToCenter)
            {
                entryPoint.localPosition = desiredEntryLocal;
                entryPoint.localRotation = Quaternion.identity;
                exitPoint.localPosition = desiredExitLocal;
                exitPoint.localRotation = Quaternion.identity;
                if (debugLogs)
                    Debug.Log($"[GateController] {name}: Entry/Exit corregidos automáticamente para quedar a ambos lados del portón.", this);
            }
        }

        void OnEnable()
        {
            if (!s_gates.Contains(this))
                s_gates.Add(this);
        }

        void Start()
        {
            StartCoroutine(DelayedSyncGatePathCellsIfOpen());
        }

        IEnumerator DelayedSyncGatePathCellsIfOpen()
        {
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
                {
                    if (currentState == GateState.Open || currentState == GateState.Opening)
                        RegisterPathGridPassage();
                    break;
                }
            }
        }

        void OnDisable()
        {
            UnregisterPathGridPassage();
            s_gates.Remove(this);
        }

        void Update()
        {
            // Abrir si hay unidades DENTRO del trigger O si hay unidades CERCA (proximidad).
            // La proximidad es esencial: si la puerta está cerrada, la NavMesh está tallada y las
            // unidades no pueden path hasta el trigger; con esto la puerta abre al acercarse.
            bool anyNear = AnyValidUnitInOpenRadius();
            bool shouldBeOpen = unitsInside > 0 || anyNear;

            if (shouldBeOpen && currentState != GateState.Open && currentState != GateState.Opening)
                OpenGate();

            // Cerrar solo cuando no hay nadie dentro y nadie cerca (evita cerrar en la cara de quien viene).
            if (unitsInside == 0 && !anyNear && currentState == GateState.Open)
                CloseGate();
        }

        /// <summary>True si hay al menos una unidad válida cerca (horizontalmente) de la puerta.
        /// Usa distancia en plano XZ para no depender de la altura del gateCenter ni de las unidades.</summary>
        bool AnyValidUnitInOpenRadius()
        {
            Transform c = gateCenter != null ? gateCenter : transform;
            Vector3 centerXZ = c.position;
            centerXZ.y = 0f;
            int layerMask = (unitLayer.value == 0 || unitLayer.value == -1) ? ~0 : unitLayer.value;
            float r = openRadius + 4f;
            int n = Physics.OverlapSphereNonAlloc(c.position, r, s_overlapBuffer, layerMask);
            for (int i = 0; i < n; i++)
            {
                if (s_overlapBuffer[i] == null) continue;
                if (!IsValidUnitCollider(s_overlapBuffer[i], out _)) continue;
                Vector3 unitXZ = s_overlapBuffer[i].transform.position;
                unitXZ.y = 0f;
                if ((unitXZ - centerXZ).sqrMagnitude <= openRadius * openRadius)
                    return true;
            }
            return false;
        }

        // Este handler lo llama el relay desde el trigger (ver EnsureTriggerRelay).
        internal void HandleTriggerEnter(Collider other)
        {
            if (!IsValidUnitCollider(other, out NavMeshAgent agent)) return;

            int id = agent.gameObject.GetInstanceID();
            if (_trackedAgents.Add(id))
            {
                unitsInside++;
                if (debugLogs) Debug.Log($"[GateController] {name} enter → unitsInside={unitsInside}", this);
            }

            // Si entra y la puerta aún no está realmente abierta, guiar al punto correcto.
            if (currentState != GateState.Open && currentState != GateState.Opening)
                ForcePassThrough(agent);
        }

        // Este handler lo llama el relay desde el trigger.
        internal void HandleTriggerExit(Collider other)
        {
            if (!IsValidUnitCollider(other, out NavMeshAgent agent)) return;

            int id = agent.gameObject.GetInstanceID();
            if (_trackedAgents.Remove(id))
            {
                unitsInside = Mathf.Max(0, unitsInside - 1);
                if (debugLogs) Debug.Log($"[GateController] {name} exit → unitsInside={unitsInside}", this);
            }
        }

        bool IsValidUnitCollider(Collider other, out NavMeshAgent agent)
        {
            agent = null;
            if (other == null) return false;

            if (unitLayer.value != -1 && unitLayer.value != 0)
            {
                int otherLayerBit = 1 << other.gameObject.layer;
                if ((unitLayer.value & otherLayerBit) == 0)
                    return false;
            }

            agent = other.GetComponentInParent<NavMeshAgent>();
            if (agent == null) return false;

            if (!allowEnemies)
            {
                // Si no hay sistema de facciones, por defecto permitimos (equivalente a "aliado").
                if (_gateFaction != null)
                {
                    var unitFaction = agent.GetComponentInParent<FactionMember>();
                    if (unitFaction != null && FactionMember.IsHostile(_gateFaction.faction, unitFaction.faction))
                        return false;
                }
            }

            return true;
        }

        public void OpenGate()
        {
            if (_stateRoutine != null) StopCoroutine(_stateRoutine);
            _stateRoutine = StartCoroutine(OpenRoutine());
        }

        public void CloseGate()
        {
            if (_stateRoutine != null) StopCoroutine(_stateRoutine);
            _stateRoutine = StartCoroutine(CloseRoutine());
        }

        IEnumerator OpenRoutine()
        {
            currentState = GateState.Opening;
            if (debugLogs) Debug.Log($"[GateController] {name} → Opening", this);

            if (animator != null)
                animator.SetBool("Open", true);

            if (obstacle != null)
            {
                obstacle.carving = false;
                obstacle.enabled = false;
            }

            if (openDelay > 0f)
                yield return new WaitForSeconds(openDelay);

            currentState = GateState.Open;
            if (debugLogs) Debug.Log($"[GateController] {name} → Open", this);

            RegisterPathGridPassage();
            RecalculateNearbyAgents();
        }

        IEnumerator CloseRoutine()
        {
            currentState = GateState.Closing;
            if (debugLogs) Debug.Log($"[GateController] {name} → Closing", this);

            if (animator != null)
                animator.SetBool("Open", false);

            if (closeDelay > 0f)
                yield return new WaitForSeconds(closeDelay);

            if (obstacle != null)
            {
                obstacle.enabled = true;
                obstacle.carving = true;
            }

            currentState = GateState.Closed;
            if (debugLogs) Debug.Log($"[GateController] {name} → Closed", this);

            UnregisterPathGridPassage();
            RecalculateNearbyAgents();
        }

        /// <summary>Marca celdas bajo la puerta como transitables para A* mientras esté abierta.</summary>
        void RegisterPathGridPassage()
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
            UnregisterPathGridPassage();

            Transform root = gateCenter != null ? gateCenter : transform;
            Vector3 center = root.position;
            float cs = Mathf.Max(0.01f, MapGrid.Instance.cellSize);
            Vector3 fwd = root.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            var cells = new HashSet<Vector2Int>();
            int stepsX = Mathf.Max(2, Mathf.CeilToInt((gatePathHalfWidthMeters * 2f) / cs) + 1);
            int stepsZ = Mathf.Max(2, Mathf.CeilToInt((gatePathHalfDepthMeters * 2f) / cs) + 1);
            for (int ix = 0; ix <= stepsX; ix++)
            {
                for (int iz = 0; iz <= stepsZ; iz++)
                {
                    float tx = Mathf.Lerp(-gatePathHalfWidthMeters, gatePathHalfWidthMeters, ix / (float)stepsX);
                    float tz = Mathf.Lerp(-gatePathHalfDepthMeters, gatePathHalfDepthMeters, iz / (float)stepsZ);
                    Vector3 worldPos = center + right * tx + fwd * tz;
                    cells.Add(MapGrid.Instance.WorldToCell(worldPos));
                }
            }

            foreach (Vector2Int c in cells)
            {
                if (!MapGrid.Instance.IsInBounds(c)) continue;
                MapGrid.Instance.SetOpenGatePassable(c, true);
                _registeredGatePathCells.Add(c);
            }

            if (debugLogs && _registeredGatePathCells.Count > 0)
                Debug.Log($"[GateController] {name}: MapGrid paso abierto en {_registeredGatePathCells.Count} celdas.", this);
        }

        void UnregisterPathGridPassage()
        {
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady && _registeredGatePathCells.Count > 0)
            {
                for (int i = 0; i < _registeredGatePathCells.Count; i++)
                    MapGrid.Instance.SetOpenGatePassable(_registeredGatePathCells[i], false);
            }
            _registeredGatePathCells.Clear();
        }

        void LateUpdate()
        {
            if (currentState != GateState.Open) return;
            if (_registeredGatePathCells.Count > 0) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
            RegisterPathGridPassage();
        }

        public void RecalculateNearbyAgents()
        {
            if (gateCenter == null) gateCenter = transform;

            int n = Physics.OverlapSphereNonAlloc(gateCenter.position, repathRadius, s_overlapBuffer, unitLayer.value == 0 ? ~0 : unitLayer);
            for (int i = 0; i < n; i++)
            {
                var hit = s_overlapBuffer[i];
                if (hit == null) continue;

                var agent = hit.GetComponentInParent<NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                {
                    Vector3 dest = agent.destination;
                    agent.ResetPath();
                    agent.SetDestination(dest);
                }
            }
        }

        public void ForcePassThrough(NavMeshAgent agent)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (gateCenter == null) gateCenter = transform;
            if (entryPoint == null || exitPoint == null) return;

            Vector3 toAgent = agent.transform.position - gateCenter.position;
            toAgent.y = 0f;
            Vector3 forward = gateCenter.forward;
            forward.y = 0f;
            if (toAgent.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return;

            float dot = Vector3.Dot(toAgent.normalized, forward.normalized);
            Transform target = dot > 0f ? exitPoint : entryPoint;
            Vector3 dest = GetPointOnNavMesh(target.position, 2.5f);

            agent.isStopped = false;
            agent.SetDestination(dest);
        }

        /// <summary>Devuelve un punto sobre la NavMesh cerca de worldPos (evita destinos en el aire por altura del prefab).</summary>
        static Vector3 GetPointOnNavMesh(Vector3 worldPos, float maxDistance)
        {
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
                return hit.position;
            return worldPos;
        }

        /// <summary>
        /// Encuentra la puerta más cercana a una posición.
        /// Útil para que UnitMover inserte un destino intermedio (estilo AoE).
        /// </summary>
        public static GateController FindNearestGate(Vector3 position, float maxDistance)
        {
            float maxSqr = maxDistance * maxDistance;
            GateController best = null;
            float bestSqr = maxSqr;
            for (int i = 0; i < s_gates.Count; i++)
            {
                var g = s_gates[i];
                if (g == null || !g.isActiveAndEnabled) continue;
                Transform c = g.gateCenter != null ? g.gateCenter : g.transform;
                float d = (c.position - position).sqrMagnitude;
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = g;
                }
            }
            return best;
        }

        /// <summary>
        /// Devuelve una puerta cercana al segmento [from->to] (distancia perpendicular).
        /// Evita que el unit mover tenga que "adivinar" solo por destino final.
        /// </summary>
        public static GateController FindGateOnSegment(Vector3 from, Vector3 to, float maxDistanceToSegment)
        {
            float maxSqr = maxDistanceToSegment * maxDistanceToSegment;
            GateController best = null;
            float bestSqr = maxSqr;

            Vector3 a = from; a.y = 0f;
            Vector3 b = to; b.y = 0f;
            Vector3 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;
            if (abLenSqr < 0.0001f)
                return FindNearestGate(from, maxDistanceToSegment);

            for (int i = 0; i < s_gates.Count; i++)
            {
                var g = s_gates[i];
                if (g == null || !g.isActiveAndEnabled) continue;
                Transform c = g.gateCenter != null ? g.gateCenter : g.transform;
                Vector3 p = c.position; p.y = 0f;

                float t = Vector3.Dot(p - a, ab) / abLenSqr;
                t = Mathf.Clamp01(t);
                Vector3 proj = a + ab * t;
                float dSqr = (p - proj).sqrMagnitude;
                if (dSqr < bestSqr)
                {
                    bestSqr = dSqr;
                    best = g;
                }
            }

            return best;
        }

        void EnsureTriggerRelay()
        {
            // Buscar cualquier Collider isTrigger en la jerarquía y asegurar relay.
            var triggers = GetComponentsInChildren<Collider>(true);
            bool foundAnyTrigger = false;
            for (int i = 0; i < triggers.Length; i++)
            {
                var c = triggers[i];
                if (c == null || !c.isTrigger) continue;
                foundAnyTrigger = true;
                var relay = c.GetComponent<GateTriggerRelay>();
                if (relay == null) relay = c.gameObject.AddComponent<GateTriggerRelay>();
                relay.Bind(this);
            }

            // Si no hay ningún trigger (prefab sin GateTrigger o instancia antigua), crearlo en runtime.
            if (!foundAnyTrigger)
            {
                Transform root = gateCenter != null ? gateCenter : transform;
                var go = new GameObject("GateTrigger");
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = triggerSize;
                box.center = new Vector3(0f, triggerSize.y * 0.5f, 0f);
                var relay = go.AddComponent<GateTriggerRelay>();
                relay.Bind(this);
                if (debugLogs)
                    Debug.Log($"[GateController] {name}: GateTrigger creado en runtime (prefab no tenía collider trigger).", this);
            }
        }

        void OnDrawGizmosSelected()
        {
            Transform c = gateCenter != null ? gateCenter : transform;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(c.position, repathRadius);

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireSphere(c.position, openRadius);

            Gizmos.color = Color.cyan;
            if (entryPoint != null) Gizmos.DrawSphere(entryPoint.position, 0.2f);
            if (exitPoint != null) Gizmos.DrawSphere(exitPoint.position, 0.2f);
        }

        [DisallowMultipleComponent]
        sealed class GateTriggerRelay : MonoBehaviour
        {
            GateController _gate;

            public void Bind(GateController gate) => _gate = gate;

            void OnTriggerEnter(Collider other)
            {
                if (_gate != null) _gate.HandleTriggerEnter(other);
            }

            void OnTriggerExit(Collider other)
            {
                if (_gate != null) _gate.HandleTriggerExit(other);
            }
        }
    }
}

