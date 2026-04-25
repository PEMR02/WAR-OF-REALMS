using UnityEngine;
using UnityEngine.AI;
using Project.Gameplay.Resources;

namespace Project.Gameplay.Units
{
    /// <summary>
    /// Sincroniza el Animator con el movimiento: actualiza el parámetro "Speed" según la velocidad del NavMeshAgent.
    /// Añadir a prefabs que usen un controller con Idle/Walk/Run (ej. TT_Archer_Animator).
    /// No actualiza Speed si existe AnimalPastureBehaviour (la vaca y otros animales lo llevan).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class UnitAnimatorDriver : MonoBehaviour
    {
        [Tooltip("Nombre del parámetro float del Animator (por defecto Speed).")]
        public string speedParameter = "Speed";

        [Tooltip("Velocidad del agente que se considera 'run' para el animator (opcional, escala visual).")]
        public float runThreshold = 2f;
        [Tooltip("Velocidad mínima visual mientras NavMesh está calculando path (evita corte brusco Idle entre órdenes).")]
        public float pendingPathVisualSpeed = 0.12f;

        Animator _animator;
        NavMeshAgent _agent;
        bool _skipDriver; // true si tiene AnimalPastureBehaviour (animales llevan su propia lógica)

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
                _agent = GetComponentInParent<NavMeshAgent>();
            _skipDriver = GetComponentInParent<AnimalPastureBehaviour>() != null;
        }

        void Update()
        {
            if (_skipDriver) return;
            if (_animator == null || !_animator.isInitialized) return;

            float speed = 0f;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                speed = _agent.velocity.magnitude;
                if (_agent.pathPending && speed < pendingPathVisualSpeed)
                    speed = pendingPathVisualSpeed;
            }

            _animator.SetFloat(speedParameter, speed);
        }
    }
}