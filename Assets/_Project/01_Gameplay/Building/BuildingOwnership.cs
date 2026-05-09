using UnityEngine;
using Project.Gameplay.Players;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Owner runtime explícito del edificio para sistemas económicos (depósito, validaciones).
    /// Evita depender de facción compartida entre múltiples IAs.
    /// </summary>
    public class BuildingOwnership : MonoBehaviour
    {
        public PlayerResources owner;
    }
}
