using UnityEngine;

[CreateAssetMenu(menuName = "Project/Grid/Grid Config", fileName = "GridConfig")]
public class GridConfig : ScriptableObject
{
    [Min(0.01f)]
    public float gridSize = 1f;
}
