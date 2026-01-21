using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Representa una instancia de edificio en el mundo.
    /// Mantiene referencia al BuildingSO que lo generó.
    /// </summary>
    public class BuildingInstance : MonoBehaviour
    {
        [Header("Building Data")]
        public BuildingSO buildingSO;  // El SO que define este edificio
        
        [Header("Runtime")]
        public float constructionProgress = 1f;  // 0-1 (1 = completado)
        public bool isComplete = true;
        
        void Awake()
        {
            // Si no hay BuildingSO asignado, intentar inferir desde el nombre
            if (buildingSO == null)
            {
                Debug.LogWarning($"BuildingInstance en {gameObject.name} no tiene BuildingSO asignado.");
            }
        }
    }
}
