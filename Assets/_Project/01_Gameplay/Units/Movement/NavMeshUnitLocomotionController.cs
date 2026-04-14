using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Units.Movement
{
    /// <summary>
    /// Encapsula la locomoción física/NavMesh. No decide táctica ni formaciones.
    /// </summary>
    public sealed class NavMeshUnitLocomotionController : IUnitLocomotionController
    {
        readonly NavMeshAgent _agent;
        readonly float _snapToNavMeshRadius;

        public NavMeshUnitLocomotionController(NavMeshAgent agent, float snapToNavMeshRadius)
        {
            _agent = agent;
            _snapToNavMeshRadius = snapToNavMeshRadius;
        }

        public NavMeshAgent Agent => _agent;
        public bool IsOnNavMesh => _agent != null && _agent.enabled && _agent.isOnNavMesh;
        public bool PathPending => _agent != null && _agent.pathPending;
        public bool HasPath => _agent != null && _agent.hasPath;
        public float RemainingDistance => _agent != null ? _agent.remainingDistance : 0f;
        public Vector3 Velocity => _agent != null ? _agent.velocity : Vector3.zero;
        public Vector3 Destination => _agent != null ? _agent.destination : Vector3.zero;
        public bool HasPartialPath => _agent != null && _agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathPartial;

        public bool TryEnsureOnNavMesh()
        {
            if (_agent == null || !_agent.enabled)
                return false;
            if (_agent.isOnNavMesh)
                return true;

            if (NavMesh.SamplePosition(_agent.transform.position, out NavMeshHit hit, _snapToNavMeshRadius, NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
                return true;
            }

            return false;
        }

        public bool TrySamplePosition(Vector3 worldPos, float radius, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                sampled = hit.position;
                return true;
            }

            sampled = worldPos;
            return false;
        }

        public void SetDestination(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                return;
            _agent.isStopped = false;
            _agent.SetDestination(worldPos);
        }

        public void Stop()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                return;
            _agent.ResetPath();
            _agent.isStopped = true;
        }

        public void Warp(Vector3 worldPos)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                return;
            _agent.Warp(worldPos);
        }
    }
}
