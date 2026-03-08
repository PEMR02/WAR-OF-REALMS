using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Combat;
using Project.Gameplay.Players;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Permite al aldeano reparar edificios: se mueve hasta el edificio y restaura vida consumiendo recursos.
    /// Asignar a prefabs de aldeano. Orden: click derecho sobre edificio dañado.
    /// Setup: para reparar edificios hay que añadir este componente (Repairer) al prefab del aldeano
    /// (Assets/_Project/08_Prefabs/Units/Aldeano.prefab). Ver también 03_UI/HUD/Setup_Notifications_And_Hotkeys.md.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Repairer : MonoBehaviour
    {
        [Header("Reparación")]
        [Tooltip("Vida restaurada por segundo.")]
        public float repairRatePerSecond = 15f;
        [Tooltip("Recurso que se consume al reparar (típico: madera).")]
        public ResourceKind repairResource = ResourceKind.Wood;
        [Tooltip("Cantidad de recurso por punto de vida restaurado.")]
        public float resourcePerHP = 0.1f;
        [Tooltip("Rango en metros para poder reparar.")]
        public float repairRange = 3f;

        [Header("Refs")]
        public PlayerResources owner;

        NavMeshAgent _agent;
        UnitMover _mover;
        Health _repairTarget;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _mover = GetComponent<UnitMover>();
            if (owner == null) owner = FindFirstObjectByType<PlayerResources>();
        }

        /// <summary>Asigna un edificio (Health) para reparar. Null = dejar de reparar.</summary>
        public void SetRepairTarget(Health target)
        {
            _repairTarget = target;
            if (_repairTarget != null && _mover != null)
                _mover.MoveTo(_repairTarget.transform.position);
        }

        public bool HasRepairTarget => _repairTarget != null && _repairTarget.IsAlive;

        void Update()
        {
            if (_repairTarget == null || !_repairTarget.IsAlive)
            {
                _repairTarget = null;
                return;
            }

            float dist = Vector3.Distance(transform.position, _repairTarget.transform.position);
            if (dist > repairRange)
                return;

            if (_agent != null && _agent.hasPath)
                _agent.ResetPath();

            int needHP = _repairTarget.MaxHP - _repairTarget.CurrentHP;
            if (needHP <= 0) { _repairTarget = null; return; }

            float delta = repairRatePerSecond * Time.deltaTime;
            int toHeal = Mathf.Min(needHP, Mathf.RoundToInt(delta));
            if (toHeal <= 0) return;

            int cost = Mathf.Max(1, Mathf.RoundToInt(toHeal * resourcePerHP));
            if (owner != null && owner.Get(repairResource) < cost)
            {
                int have = owner.Get(repairResource);
                if (have <= 0) return;
                toHeal = Mathf.Min(toHeal, Mathf.Max(1, Mathf.FloorToInt(have / resourcePerHP)));
                cost = Mathf.Max(1, Mathf.RoundToInt(toHeal * resourcePerHP));
            }

            if (toHeal <= 0) return;
            if (owner != null) owner.Subtract(repairResource, cost);
            _repairTarget.Heal(toHeal);
        }
    }
}
