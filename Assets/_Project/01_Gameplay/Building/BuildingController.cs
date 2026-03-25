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

        [Header("Selección / Hover")]
        [Tooltip("Escala del BoxCollider de selección en XZ (1 = bounds completos). Valores menores reducen el área para no robar clic a unidades junto al edificio.")]
        [Range(0.5f, 1f)] public float selectionBoundsScaleXZ = 0.82f;

        [Header("Debug visualización")]
        [Tooltip("Muestra en la Scene View el límite (collider/obstáculo) que impide que las unidades se acerquen. Activa también 'Draw All' en cualquier BuildingController para ver todos.")]
        public bool debugDrawObstacleBounds = false;

        [Tooltip("Si true, TODOS los edificios con BuildingController dibujan su límite. Atajo: Shift+B en Play.")]
        public static bool DrawAllObstacleBounds = false;

        static int _lastDrawAllToggleFrame;
        bool _footprintAppliedWhenGridReady;
        BuildingInstance _buildingInstanceCached;
        bool _buildingInstanceResolved;

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
            if (!_buildingInstanceResolved)
            {
                _buildingInstanceCached = GetComponent<BuildingInstance>();
                _buildingInstanceResolved = true;
            }
            if (_buildingInstanceCached == null) return;

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
            // Combinar renderers del edificio (excluyendo hijos utilitarios, decals, plataformas y decoración que inflan la "vista").
            var all = GetComponentsInChildren<Renderer>(true);
            Bounds b = default;
            bool hasBounds = false;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                // No usar VFX para bounds de selección (humo, partículas, trails) porque inflan la caja.
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                string n = r.gameObject.name;
                if (n.Equals("DropAnchor", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("SpawnPoint", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("GroundDecal", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("BasePlatform", System.StringComparison.OrdinalIgnoreCase) ||
                    n.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Platform", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("VFX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("FX", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.gameObject.GetComponent<UnityEngine.Canvas>() != null ||
                    r.gameObject.GetComponent<BuildingGroundDecal>() != null ||
                    r.gameObject.GetComponent<BuildingBasePlatform>() != null)
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
            var biCompound = GetComponent<BuildingInstance>();
            if (biCompound != null && biCompound.buildingSO != null && biCompound.buildingSO.isCompound)
            {
                // Muros/path: NO usar bounds de todos los hijos (un solo Box enorme bloquea raycasts y clics al suelo).
                // Huella en MapGrid + trigger: selección por segmentos (GetComponentInParent<BuildingSelectable>).
                if (TryApplyCompoundFootprintTriggerCollider(biCompound))
                    return;
                var existing = GetComponent<BoxCollider>();
                if (existing != null)
                    existing.isTrigger = true;
                return;
            }

            Vector3 size;
            Vector3 center;

            // Collider de selección/hover: priorizar bounds visuales y reducir en XZ para no robar clic a unidades cerca.
            if (TryGetVisualBoundsSize(out Vector3 visualSize, out Vector3 visualCenter))
            {
                size = visualSize;
                size.x *= Mathf.Clamp01(selectionBoundsScaleXZ);
                size.z *= Mathf.Clamp01(selectionBoundsScaleXZ);
                center = visualCenter;
                ApplyBoxColliderOnly(size, center);
                // NavMeshObstacle: usar huella del grid para que el pathfinding bloquee el área correcta.
                ApplyObstacleFromFootprintOrVisual();
                return;
            }

            // Sin bounds visuales: usar huella del grid.
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
                    center = new Vector3(0f, size.y * 0.5f, 0f);
                    ApplySizeToBoxAndObstacle(size, center);
                    return;
                }
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

        /// <summary>
        /// Muro compuesto: un Box alineado al rect de ocupación del path, solo trigger (no bloquea Physics.Raycast por defecto ni órdenes al suelo).
        /// </summary>
        bool TryApplyCompoundFootprintTriggerCollider(BuildingInstance bi)
        {
            if (MapGrid.Instance == null || !MapGrid.Instance.IsReady) return false;
            if (!bi.overrideOccupiedMin.HasValue || !bi.overrideOccupiedSize.HasValue) return false;
            Vector2Int min = bi.overrideOccupiedMin.Value;
            Vector2Int footprintSize = bi.overrideOccupiedSize.Value;
            if (footprintSize.x <= 0 || footprintSize.y <= 0) return false;

            float cs = MapGrid.Instance.cellSize;
            Vector3 centerWorld = MapGrid.Instance.CellToWorld(new Vector2Int(min.x + footprintSize.x / 2, min.y + footprintSize.y / 2));
            centerWorld.y = transform.position.y + 1f;
            Vector3 sizeWorld = new Vector3(Mathf.Max(1f, footprintSize.x) * cs, 2f, Mathf.Max(1f, footprintSize.y) * cs);
            Vector3 lossy = transform.lossyScale;
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(
                sizeWorld.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)),
                sizeWorld.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)),
                sizeWorld.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
            box.center = transform.InverseTransformPoint(centerWorld);
            box.isTrigger = true;
            return true;
        }

        void ApplyBoxColliderOnly(Vector3 size, Vector3 center)
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.center = center;
            box.isTrigger = false;
        }

        void ApplyObstacleFromFootprintOrVisual()
        {
            var obs = GetComponent<NavMeshObstacle>();
            if (obs == null) return;
            obs.shape = NavMeshObstacleShape.Box;
            if (MapGrid.Instance != null && MapGrid.Instance.IsReady)
            {
                var bi = GetComponent<BuildingInstance>();
                if (bi != null && bi.buildingSO != null)
                {
                    float cs = MapGrid.Instance.cellSize;
                    float w = bi.buildingSO.size.x * cs;
                    float d = bi.buildingSO.size.y * cs;
                    Vector3 lossy = transform.lossyScale;
                    obs.size = new Vector3(w / Mathf.Max(0.001f, Mathf.Abs(lossy.x)), 2f / Mathf.Max(0.001f, Mathf.Abs(lossy.y)), d / Mathf.Max(0.001f, Mathf.Abs(lossy.z)));
                    obs.center = new Vector3(0f, obs.size.y * 0.5f, 0f);
                    return;
                }
            }
            if (TryGetVisualBoundsSize(out Vector3 size, out Vector3 center))
            {
                obs.size = size;
                obs.center = center;
            }
        }

        void ApplySizeToBoxAndObstacle(Vector3 size, Vector3 center)
        {
            ApplyBoxColliderOnly(size, center);
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
