using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Units
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitMover : MonoBehaviour
    {
        private NavMeshAgent _agent;
        [Header("NavMesh")]
        public float snapToNavMeshRadius = 50f;
        private float _lastFixAttempt = 0f;
        [Header("Debug")]
        public bool debugLogs = false;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            // Prefabs pueden tener radio=3; garantizar mínimo para encontrar NavMesh tras bake
            if (snapToNavMeshRadius < 20f)
                snapToNavMeshRadius = 20f;
        }
        
        void Start()
        {
            // Intentar después de que todo se inicialice
            Invoke(nameof(EnsureOnNavMesh), 0.5f);
        }

        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null) return;
            if (!EnsureOnNavMesh())
            {
                Debug.LogWarning($"{name}: No pudo moverse, no está en NavMesh");
                return;
            }
            _agent.isStopped = false;
            _agent.SetDestination(worldPos);
        }

        bool EnsureOnNavMesh()
        {
            if (_agent == null) return false;
            if (_agent.isOnNavMesh) return true;
            
            // Solo intentar cada 1 segundo
            if (Time.time - _lastFixAttempt < 1f)
                return false;
                
            _lastFixAttempt = Time.time;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, snapToNavMeshRadius, NavMesh.AllAreas))
            {
                if (debugLogs)
                    Debug.Log($"{name}: Recolocado en NavMesh desde {transform.position} a {hit.position}");
                _agent.Warp(hit.position);
                return true;
            }
            else
            {
                if (debugLogs)
                    Debug.LogWarning($"{name}: No se encontró NavMesh cerca (radio={snapToNavMeshRadius}). Posición: {transform.position}");
            }

            return false;
        }
    }
}
