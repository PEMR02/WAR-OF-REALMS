using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Buildings;

namespace Project.Gameplay.Units
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Builder : MonoBehaviour
    {
        public float buildPower = 1f;
        public float buildRange = 1.6f;

        NavMeshAgent _agent;
        UnitMover _mover;
        BuildSite _target;

       void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_mover = GetComponent<UnitMover>();
		}

        public void SetBuildTarget(BuildSite site)
        {
            var g = GetComponent<VillagerGatherer>();
			if (g != null) g.PauseGatherKeepCarried();

			if (_target == site) return;

            if (_target != null) _target.Unregister(this);
            _target = site;

            if (_target != null)
            {
                _target.Register(this);
                MoveTo(_target.transform.position);
            }
        }

        public void ClearBuildTargetIfThis(BuildSite site)
        {
            if (_target != site) return;
            SetBuildTarget(null);
        }

        void Update()
        {
            if (_target == null) return;
            if (_target.IsCompleted) { SetBuildTarget(null); return; }

            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = _target.transform.position; b.y = 0f;
            float dist = Vector3.Distance(a, b);

            if (dist > buildRange)
            {
                if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.15f))
                    MoveTo(_target.transform.position);
                return;
            }

            if (_agent.hasPath) _agent.ResetPath();
            _target.AddWorkSeconds(buildPower * Time.deltaTime);
        }

        void MoveTo(Vector3 pos)
        {
			if (_mover != null) _mover.MoveTo(pos);
			else _agent.SetDestination(pos);
        }
    }
}
