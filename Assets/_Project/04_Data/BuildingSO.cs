using UnityEngine;

namespace Project.Gameplay.Buildings
{
    [CreateAssetMenu(menuName = "Project/Building")]
    public class BuildingSO : ScriptableObject
    {
        public string id;
        public GameObject prefab;

        [Header("Footprint (meters)")]
        public Vector2 size = new Vector2(4, 4);

        [Header("Placement")]
        public bool requiresFlatGround = false;

        [Header("Costs")]
        public Cost[] costs;

        [System.Serializable]
        public class Cost
        {
            public Project.Gameplay.Resources.ResourceKind kind;
            public int amount;
        }
		
		public float buildTimeSeconds = 10f;

    }
}
