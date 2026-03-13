using UnityEngine;

namespace Project.Gameplay
{
    /// <summary>
    /// Opcional: colócalo en cualquier GameObject de la escena para asignar el config global del outline
    /// sin usar el RTS Map Generator. Ejecuta muy pronto (Awake, order -300) para que esté antes que los SelectableOutline.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class SelectionOutlineConfigBootstrap : MonoBehaviour
    {
        [Tooltip("Config del borde de selección. Si está asignado, se aplica a todas las unidades, edificios y recursos.")]
        public SelectionOutlineConfig config;

        void Awake()
        {
            if (config != null)
                SelectionOutlineConfig.SetGlobal(config);
        }
    }
}
