using UnityEngine;

[CreateAssetMenu(menuName = "Project/Grid/Grid Config", fileName = "GridConfig")]
public class GridConfig : ScriptableObject
{
    [Min(0.01f)]
    [Tooltip("Tamaño de celda en metros. 2.5 es ideal para RTS estilo Age of Empires 2 (cada celda = aprox. medio edificio). 1.0 es muy pequeño y hace la grilla difícil de ver.")]
    public float gridSize = 2.5f;
}
