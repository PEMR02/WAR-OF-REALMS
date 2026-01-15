using UnityEngine;

namespace MeshUtils
{
    [AddComponentMenu("")]
    internal class LODBackupComponent : MonoBehaviour
    {
        [SerializeField]
        private Renderer[] originalRenderers = null;

        public Renderer[] OriginalRenderers
        {
            get { return originalRenderers; }
            set { originalRenderers = value; }
        }
    }
}
