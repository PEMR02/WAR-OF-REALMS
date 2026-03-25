using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Añadir a la puerta (junto a GateController) para verificar en Play cada pieza:
    /// estado, unidades cerca, carving, y si Entry/Exit están en NavMesh.
    /// Solo activo cuando debugEnabled = true.
    /// </summary>
    public class GateDiagnostics : MonoBehaviour
    {
        [Header("Diagnóstico")]
        [Tooltip("Activar para imprimir estado en Consola cada intervalo.")]
        public bool debugEnabled = false;
        [Tooltip("Segundos entre cada log de estado.")]
        public float logInterval = 2f;

        GateController _gate;
        float _nextLog;
        readonly Collider[] _nearbyUnitsBuffer = new Collider[32];

        void Awake()
        {
            _gate = GetComponent<GateController>();
            if (_gate == null)
                _gate = GetComponentInParent<GateController>();
        }

        void Update()
        {
            if (!debugEnabled || _gate == null) return;
            if (Time.time < _nextLog) return;
            _nextLog = Time.time + logInterval;
            LogState();
        }

        void LogState()
        {
            var c = _gate.gateCenter != null ? _gate.gateCenter : _gate.transform;
            bool anyNear = Physics.CheckSphere(c.position, _gate.openRadius, _gate.unitLayer.value == 0 || _gate.unitLayer.value == -1 ? ~0 : _gate.unitLayer.value);
            int nearCount = 0;
            if (anyNear)
            {
                int mask = _gate.unitLayer.value == 0 || _gate.unitLayer.value == -1 ? ~0 : _gate.unitLayer.value;
                int n = Physics.OverlapSphereNonAlloc(c.position, _gate.openRadius, _nearbyUnitsBuffer, mask);
                for (int i = 0; i < n; i++)
                {
                    if (_nearbyUnitsBuffer[i] != null && _nearbyUnitsBuffer[i].GetComponentInParent<NavMeshAgent>() != null) nearCount++;
                }
            }

            bool obstacleCarving = _gate.obstacle != null && _gate.obstacle.carving;
            bool entryOnNav = _gate.entryPoint != null && NavMesh.SamplePosition(_gate.entryPoint.position, out _, 0.5f, NavMesh.AllAreas);
            bool exitOnNav = _gate.exitPoint != null && NavMesh.SamplePosition(_gate.exitPoint.position, out _, 0.5f, NavMesh.AllAreas);

            Debug.Log($"[GateDiagnostics] {_gate.name} | State={_gate.CurrentState} | UnitsNear={nearCount} | Carving={obstacleCarving} | EntryOnNavMesh={entryOnNav} | ExitOnNavMesh={exitOnNav}", _gate);
        }

        void OnDrawGizmosSelected()
        {
            if (_gate == null) _gate = GetComponent<GateController>() ?? GetComponentInParent<GateController>();
            if (_gate == null || !Application.isPlaying) return;
            if (_gate.entryPoint != null)
            {
                bool onNav = NavMesh.SamplePosition(_gate.entryPoint.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas);
                Gizmos.color = onNav ? Color.green : Color.red;
                Gizmos.DrawWireSphere(_gate.entryPoint.position, 0.3f);
            }
            if (_gate.exitPoint != null)
            {
                bool onNav = NavMesh.SamplePosition(_gate.exitPoint.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas);
                Gizmos.color = onNav ? Color.green : Color.red;
                Gizmos.DrawWireSphere(_gate.exitPoint.position, 0.3f);
            }
        }
    }
}
