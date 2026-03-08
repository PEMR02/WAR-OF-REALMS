using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Project.Gameplay.Map;

namespace Project.Gameplay.Buildings
{
    [RequireComponent(typeof(Collider))]
    public class BuildingController : MonoBehaviour
    {
        [Tooltip("Activa si quieres que el edificio carve el NavMesh en runtime.")]
        public bool carveNavMesh = true;

        [Header("Debug visualización")]
        [Tooltip("Muestra en la Scene View el límite (collider/obstáculo) que impide que las unidades se acerquen. Activa también 'Draw All' en cualquier BuildingController para ver todos.")]
        public bool debugDrawObstacleBounds = false;

        [Tooltip("Si true, TODOS los edificios con BuildingController dibujan su límite. Atajo: Shift+B en Play.")]
        public static bool DrawAllObstacleBounds = false;

        static int _lastDrawAllToggleFrame;
        bool _footprintAppliedWhenGridReady;

        void Awake()
        {
            ApplyColliderAndObstacleToVisualOrFootprint();

            if (carveNavMesh)
            {
                var obs = GetComponent<NavMeshObstacle>();
                if (obs == null) obs = gameObject.AddComponent<NavMeshObstacle>();
                obs.carving = true;
                obs.carveOnlyStationary = true;
                obs.shape = NavMeshObstacleShape.Box;
                ApplySizeToObstacle(obs);
            }
        }

        void Start()
        {
            TryApplyFootprintWhenGridReady();
        }

        void Update()
        {
            // Edificios que están en la escena al cargar: MapGrid se inicializa después (generador de mapa).
            // Aplicar la huella en cuanto el grid esté listo, una sola vez.
            if (!_footprintAppliedWhenGridReady)
                TryApplyFootprintWhenGridReady();

            // Atajo: Shift+B activa/desactiva dibujar límites de TODOS los edificios (Input System).
            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && kb.bKey.wasPressedThisFrame && Time.frameCount != _lastDrawAllToggleFrame)
            {
                _lastDrawAllToggleFrame = Time.frameCount;
                DrawAllObstacleBounds = !DrawAllObstacleBounds;
            }
        }

        void TryApplyFootprintWhenGridReady()
        {
            if (_footprintAppliedWhenGridReady) return;
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
            if (GetComponent<BuildingInstance>() == null) return;

            RefreshObstacleAndCollider();
            _footprintAppliedWhenGridReady = true;
        }

        /// <summary>Fuerza recalcular collider y NavMeshObstacle. Prioriza huella del grid si está listo.</summary>
        public void RefreshObstacleAndCollider()
        {
            ApplyColliderAndObstacleToVisualOrFootprint(preferFootprintWhenReady: true);
            if (carveNavMesh)
            {
                var obs = GetComponent<NavMeshObstacle>();
                if (obs != null) ApplySizeToObstacle(obs);
            }
        }

        /// <summary>Obtiene el tamaño a usar: preferencia por bounds visuales del modelo (así el collider no es más grande que el edificio y las unidades pueden acercarse). Devuelve tamaño y centro en espacio local del transform.</summary>
        bool TryGetVisualBoundsSize(out Vector3 size, out Vector3 center)
        {
            size = Vector3.zero;
            center = Vector3.zero;
            // Combinar TODOS los renderers del edificio (excluyendo hijos utilitarios),
            // para que el collider cubra el modelo completo y sea clickeable en toda su altura.
            var all = GetComponentsInChildren<Renderer>(true);
            Bounds b = default;
            bool hasBounds = false;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                if (n.Equals("DropAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("SpawnPoint", System.StringComparison.OrdinalIgnoreCase) ||
                    r.gameObject.GetComponent<UnityEngine.Canvas>() != null)
                    continue;

                if (!hasBounds)
                {
                    b = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds) return false;
            Vector3 worldSize = b.size;
            Vector3 lossy = transform.lossyScale;
            if (Mathf.Abs(lossy.x) < 0.001f || Mathf.Abs(lossy.z) < 0.001f) return false;
            // BoxCollider usa espacio local: tamaño local = mundo / escala
            Vector3 localSize = new Vector3(worldSize.x / Mathf.Abs(lossy.x), worldSize.y / Mathf.Abs(lossy.y), worldSize.z / Mathf.Abs(lossy.z));
            if (localSize.x < 0.1f || localSize.z < 0.1f) return false;
            // No recortar altura para selección: edificios altos (ej. TownCenter) deben ser clickeables en todo el mesh.
            size = new Vector3(localSize.x, Mathf.Max(2f, localSize.y), localSize.z);
            center = transform.InverseTransformPoint(b.center);
            return true;
        }

        void ApplySizeToObstacle(NavMeshObstacle obs)
        {
            if (obs == null) return;
            if (TryGetVisualBoundsSize(out Vector3 size, out Vector3 center))
            {
                obs.size = size;
                obs.center = center;
                return;
            }
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                var bi = GetComponent<BuildingInstance>();
                if (bi != null && bi.buildingSO != null)
                {
                    float cs = MapGrid.Instance.cellSize;
                    Vector3 worldSize = new Vector3(bi.buildingSO.size.x * cs, 2f, bi.buildingSO.size.y * cs);
                    Vector3 lossy = transform.lossyScale;
                    obs.size = new Vector3(worldSize.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)), worldSize.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)), worldSize.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
                    obs.center = new Vector3(0f, 1f, 0f);
                }
            }
        }

        void ApplyColliderAndObstacleToVisualOrFootprint(bool preferFootprintWhenReady = false)
        {
            Vector3 size;
            Vector3 center;

            // Huella del grid cuando está listo; limitada al tamaño visual para no alejar unidades (TC del mapa = mismo tamaño que TC construido).
            if (preferFootprintWhenReady && MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                var bi = GetComponent<BuildingInstance>();
                if (bi != null && bi.buildingSO != null)
                {
                    float cs = MapGrid.Instance.cellSize;
                    float w = bi.buildingSO.size.x * cs;
                    float d = bi.buildingSO.size.y * cs;
                    Vector3 lossy = transform.lossyScale;
                    size = new Vector3(w / Mathf.Max(0.001f, Mathf.Abs(lossy.x)), 2f / Mathf.Max(0.001f, Mathf.Abs(lossy.y)), d / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
                    // Centro por defecto en la base del volumen (evita box "volando" si no hay bounds visuales).
                    center = new Vector3(0f, size.y * 0.5f, 0f);

                    // Ajustar con bounds visuales para que la selección cubra todo el edificio en Y.
                    // Mantenemos X/Z anclados a huella de grid para no romper el comportamiento RTS.
                    if (TryGetVisualBoundsSize(out Vector3 visualSize, out Vector3 visualCenter))
                    {
                        size.x = Mathf.Min(size.x, visualSize.x);
                        size.z = Mathf.Min(size.z, visualSize.z);
                        size.y = Mathf.Max(size.y, visualSize.y);
                        center.y = visualCenter.y;
                    }
                    ApplySizeToBoxAndObstacle(size, center);
                    return;
                }
            }

            if (TryGetVisualBoundsSize(out size, out center))
            {
                ApplySizeToBoxAndObstacle(size, center);
                return;
            }

            // Fallback: huella del grid en espacio LOCAL
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return;
            var buildingInstance = GetComponent<BuildingInstance>();
            if (buildingInstance == null || buildingInstance.buildingSO == null) return;

            float cellSize = MapGrid.Instance.cellSize;
            float width = buildingInstance.buildingSO.size.x * cellSize;
            float depth = buildingInstance.buildingSO.size.y * cellSize;
            Vector3 scale = transform.lossyScale;
            size = new Vector3(width / Mathf.Max(0.001f, Mathf.Abs(scale.x)), 2f / Mathf.Max(0.001f, Mathf.Abs(scale.y)), depth / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
            center = new Vector3(0f, 1f, 0f);
            ApplySizeToBoxAndObstacle(size, center);
        }

        void ApplySizeToBoxAndObstacle(Vector3 size, Vector3 center)
        {
            // Asegurar collider de selección consistente en el root del edificio.
            // Si el prefab trae solo colliders parciales (ej. torre/reloj), el clic falla en la base.
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.center = center;
            box.isTrigger = false;

            var obs = GetComponent<NavMeshObstacle>();
            if (obs != null) { obs.shape = NavMeshObstacleShape.Box; obs.size = size; obs.center = center; }
        }

        void OnDrawGizmos()
        {
            if (!DrawAllObstacleBounds && !debugDrawObstacleBounds) return;

            Bounds? bounds = null;
            var box = GetComponent<BoxCollider>();
            if (box != null && box.enabled)
                bounds = box.bounds;

            if (!bounds.HasValue)
            {
                var obs = GetComponent<NavMeshObstacle>();
                if (obs != null && obs.enabled && obs.shape == NavMeshObstacleShape.Box)
                {
                    Vector3 centerWorld = transform.TransformPoint(obs.center);
                    Vector3 sizeWorld = Vector3.Scale(obs.size, transform.lossyScale);
                    bounds = new Bounds(centerWorld, sizeWorld);
                }
            }

            if (bounds.HasValue)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f); // naranja visible
                Gizmos.DrawWireCube(bounds.Value.center, bounds.Value.size);
            }
        }
    }
}
