using UnityEngine;

namespace Project.Gameplay.Resources
{
    [CreateAssetMenu(menuName = "Project/Resource Type")]
    public class ResourceTypeSO : ScriptableObject
    {
        public string id;          // wood, stone, gold, food
        public string displayName; // Madera, Piedra...
    }
}
