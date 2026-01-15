using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Units
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class UnitMover : MonoBehaviour
    {
        private NavMeshAgent _agent;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        public void MoveTo(Vector3 worldPos)
        {
            if (_agent == null) return;
            _agent.isStopped = false;
            _agent.SetDestination(worldPos);
        }
    }
}
