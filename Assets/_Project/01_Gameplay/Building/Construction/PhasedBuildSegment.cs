using UnityEngine;

namespace Project.Gameplay.Buildings
{
    /// <summary>
    /// Muestra el segmento de muro por fases según el progreso de construcción.
    /// Opción 1: Prefab con hijos Base, Body, Top → se muestran/ocultan por fases.
    /// Opción 2: Una sola mesh → activar "Single mesh: crecer en Y" para que crezca en altura según progreso (pivot abajo).
    /// </summary>
    public class PhasedBuildSegment : MonoBehaviour
    {
        [Header("Partes por fase (opcional: se buscan por nombre si están vacíos)")]
        [Tooltip("Parte inferior del muro (piedra base).")]
        [SerializeField] Transform phaseBase;
        [Tooltip("Cuerpo del muro (ladrillos).")]
        [SerializeField] Transform phaseBody;
        [Tooltip("Coronación / almenas.")]
        [SerializeField] Transform phaseTop;

        [Header("Una sola mesh (si no usas Base/Body/Top)")]
        [Tooltip("Si true y no hay partes asignadas, la mesh única crece en altura (eje Y) según el progreso. Pivot del prefab abajo.")]
        [SerializeField] bool singleMeshGrowByScale = false;

        const string NameBase = "Base";
        const string NameBody = "Body";
        const string NameTop = "Top";

        Vector3 _fullScale = Vector3.one;
        bool _useParts;

        void Awake()
        {
            if (phaseBase == null) phaseBase = transform.Find(NameBase);
            if (phaseBody == null) phaseBody = transform.Find(NameBody);
            if (phaseTop == null) phaseTop = transform.Find(NameTop);

            _fullScale = transform.localScale;
            _useParts = (phaseBase != null || phaseBody != null || phaseTop != null);
        }

        /// <summary>Actualiza la fase visual según progreso 0–1 del segmento actual.</summary>
        /// <param name="progress01">0 = solo base; ~0.33 = base+body; ~0.66–1 = completo (base+body+top).</param>
        public void SetPhase(float progress01)
        {
            if (_useParts)
            {
                bool showBase = true;
                bool showBody = progress01 >= 1f / 3f;
                bool showTop = progress01 >= 2f / 3f;

                if (phaseBase != null) phaseBase.gameObject.SetActive(showBase);
                if (phaseBody != null) phaseBody.gameObject.SetActive(showBody);
                if (phaseTop != null) phaseTop.gameObject.SetActive(showTop);
                return;
            }

            if (singleMeshGrowByScale)
            {
                // Una sola mesh: crecer en Y desde mínimo hasta full (pivot abajo = crece hacia arriba)
                float t = Mathf.Clamp01(progress01);
                float minHeight = 0.2f;
                float heightScale = Mathf.Lerp(minHeight, 1f, t);
                transform.localScale = new Vector3(_fullScale.x, _fullScale.y * heightScale, _fullScale.z);
            }
        }
    }
}
