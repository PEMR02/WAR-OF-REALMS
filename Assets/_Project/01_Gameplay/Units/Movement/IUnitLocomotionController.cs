using UnityEngine;
using UnityEngine.AI;

namespace Project.Gameplay.Units.Movement
{
    public interface IUnitLocomotionController
    {
        NavMeshAgent Agent { get; }
        bool IsOnNavMesh { get; }
        bool PathPending { get; }
        bool HasPath { get; }
        float RemainingDistance { get; }
        Vector3 Velocity { get; }
        Vector3 Destination { get; }
        bool HasPartialPath { get; }

        bool TryEnsureOnNavMesh();
        bool TrySamplePosition(Vector3 worldPos, float radius, out Vector3 sampled);
        void SetDestination(Vector3 worldPos);
        void Stop();
        void Warp(Vector3 worldPos);
    }
}
