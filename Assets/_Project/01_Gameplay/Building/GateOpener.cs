using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Puerta que se abre cuando una unidad se acerca: desbloquea pathfinding y colliders para dejar pasar.
    /// Fuerza la puerta a no-Static para que la NavMesh no la hornee como bloqueante; solo el NavMeshObstacle (desactivado al abrir) bloquea.
    /// </summary>
    public class GateOpener : MonoBehaviour
    {
        [Tooltip("Radio en metros para detectar unidades que abren la puerta.")]
        public float openRadius = 3.5f;
        [Tooltip("Capas que cuentan como unidad (ej. Unit). -1 = todas las capas.")]
        public LayerMask unitLayers = -1;
        [Tooltip("Nombre del parámetro bool del Animator (ej. Open). Si está vacío no se usa Animator.")]
        public string animatorOpenParam = "Open";
        [Tooltip("Si true, el obstáculo de NavMesh se desactiva al abrir para que el pathfinding pase por la puerta.")]
        public bool affectPathfinding = true;
        [Tooltip("Si true, al abrir los colliders de la puerta pasan a trigger (las unidades pueden atravesar).")]
        public bool affectColliders = true;

        [Header("Debug")]
        [Tooltip("Si true, escribe en Consola cuando detecta unidad y cuando abre/cierra (una vez por cambio).")]
        public bool debugLog = false;

        Animator _animator;
        NavMeshObstacle[] _obstacles;
        Collider[] _blockingColliders;
        bool _open;
        Collider[] _overlapBuffer;

        void Awake()
        {
            // Si la puerta es Static, la NavMesh la hornea como no transitable y al desactivar el obstáculo no aparece paso. Forzar no-Static.
            SetStaticRecursive(gameObject, false);

            _animator = GetComponentInChildren<Animator>();
            var obsList = new System.Collections.Generic.List<NavMeshObstacle>(GetComponentsInChildren<NavMeshObstacle>(true));
            if (obsList.Count == 0 && affectPathfinding)
            {
                var obs = gameObject.AddComponent<NavMeshObstacle>();
                obs.shape = NavMeshObstacleShape.Box;
                obs.size = new Vector3(4f, 3f, 2f);
                obs.carving = false;
                obsList.Add(obs);
            }
            foreach (var o in obsList)
                o.carving = false;
            _obstacles = obsList.ToArray();
            if (affectColliders)
            {
                var all = GetComponentsInChildren<Collider>(true);
                var list = new System.Collections.Generic.List<Collider>();
                for (int i = 0; i < all.Length; i++)
                    if (!all[i].isTrigger) list.Add(all[i]);
                _blockingColliders = list.ToArray();
            }
            _overlapBuffer = new Collider[24];
        }

        void Update()
        {
            bool anyUnitNear = AnyUnitInRadius(transform.position, openRadius);
            if (anyUnitNear != _open)
            {
                _open = anyUnitNear;
                if (debugLog) Debug.Log($"[GateOpener] {gameObject.name} → {( _open ? "ABRIR" : "CERRAR" )} (unidad cerca: {anyUnitNear})", this);
                ApplyOpenState(_open);
            }
        }

        bool AnyUnitInRadius(Vector3 center, float radius)
        {
            int count = (unitLayers.value == 0 || unitLayers.value == -1)
                ? Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer)
                : Physics.OverlapSphereNonAlloc(center, radius, _overlapBuffer, unitLayers);
            for (int i = 0; i < count; i++)
            {
                if (_overlapBuffer[i] == null) continue;
                var root = _overlapBuffer[i].transform.root;
                if (root.GetComponentInChildren<Project.Gameplay.Units.UnitMover>(true) != null)
                    return true;
            }
            return false;
        }

        void ApplyOpenState(bool open)
        {
            if (!string.IsNullOrEmpty(animatorOpenParam) && _animator != null)
                _animator.SetBool(animatorOpenParam, open);

            if (affectPathfinding && _obstacles != null)
            {
                for (int i = 0; i < _obstacles.Length; i++)
                {
                    if (_obstacles[i] != null)
                        _obstacles[i].enabled = !open;
                }
                if (open)
                    RecalculatePathsForUnitsNearby();
            }

            if (affectColliders && _blockingColliders != null)
            {
                for (int i = 0; i < _blockingColliders.Length; i++)
                {
                    if (_blockingColliders[i] != null)
                        _blockingColliders[i].isTrigger = open;
                }
            }
        }

        /// <summary>Fuerza a las unidades cerca a recalcular ruta para que pasen por la puerta recién abierta.</summary>
        void RecalculatePathsForUnitsNearby()
        {
            int mask = (unitLayers.value == 0 || unitLayers.value == -1) ? -1 : unitLayers.value;
            int n = Physics.OverlapSphereNonAlloc(transform.position, openRadius + 2f, _overlapBuffer, mask);
            for (int i = 0; i < n; i++)
            {
                if (_overlapBuffer[i] == null) continue;
                var root = _overlapBuffer[i].transform.root;
                if (root.GetComponentInChildren<Project.Gameplay.Units.UnitMover>(true) == null) continue;
                var agent = root.GetComponentInChildren<NavMeshAgent>(true);
                if (agent != null && agent.isOnNavMesh && agent.hasPath)
                    agent.SetDestination(agent.destination);
            }
        }

        void OnEnable()
        {
            ApplyOpenState(_open);
        }

        static void SetStaticRecursive(GameObject go, bool value)
        {
            if (go == null) return;
            go.isStatic = value;
            for (int i = 0; i < go.transform.childCount; i++)
                SetStaticRecursive(go.transform.GetChild(i).gameObject, value);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = _open ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, openRadius);
        }
    }
}
