using Project.Gameplay.Map.Generator;
using UnityEngine;

namespace Project.Gameplay.Map.Generation
{
    /// <summary>
    /// Perfil técnico opcional: plantilla de <see cref="MapGenConfig"/> con tuning avanzado (río, MS, skirt, post-proceso).
    /// La autoridad de gameplay (ríos/lagos cuenta, agua canónica, clima, recursos) sigue en <see cref="MatchConfig"/>; el compilador aplica Match encima de esta plantilla.
    /// </summary>
    [CreateAssetMenu(menuName = "Project/Match/Map Generation Profile", fileName = "MapGenerationProfile")]
    public sealed class MapGenerationProfile : ScriptableObject
    {
        [Tooltip("Plantilla técnica del generador definitivo. Campos de gameplay serán sobrescritos por MatchConfig al compilar.")]
        public MapGenConfig technicalTemplate;
    }
}
