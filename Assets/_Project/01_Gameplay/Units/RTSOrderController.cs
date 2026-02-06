using UnityEngine;
using UnityEngine.InputSystem;
using Project.Core.Commands;
using Project.Gameplay.Buildings;
using UnityEngine.EventSystems;


namespace Project.Gameplay.Units
{
    public class RTSOrderController : MonoBehaviour
    {
        [Header("Refs")]
        public Camera cam;
        public RTSSelectionController selection;

        [Header("Raycast Masks")]
        public LayerMask buildSiteMask;
        public LayerMask resourceMask;
        public LayerMask groundMask;

        [Header("Formation")]
        public float formationSpacing = 1.5f;

        private CommandBus _bus;
        [Header("Debug")]
        public bool debugLogs = false;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            _bus = new CommandBus();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null || selection == null) return;
            if (!mouse.rightButton.wasPressedThisFrame) return;
			
			if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
				return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            // cache selección una vez
            var selectedUnits = selection.GetSelected();
            if (selectedUnits == null || selectedUnits.Count == 0) return;

            // 0) PRIORIDAD: Construir en BuildSite
            if (Physics.Raycast(ray, out RaycastHit hitS, 5000f, buildSiteMask))
            {
                var site = hitS.collider.GetComponentInParent<BuildSite>();
                if (site != null)
                {
                    if (debugLogs) Debug.Log("Orden: construir en " + site.name);

                    for (int i = 0; i < selectedUnits.Count; i++)
                    {
                        var u = selectedUnits[i];
                        if (u == null) continue;

                        // Pausar gather manteniendo carga (tu método)
                        var g = u.GetComponent<VillagerGatherer>();
                        if (g != null) g.PauseGatherKeepCarried();

                        var builder = u.GetComponent<Builder>();
                        if (builder != null) builder.SetBuildTarget(site);
                    }
                    return;
                }
            }

            // 1) PRIORIDAD: Recurso
            if (Physics.Raycast(ray, out RaycastHit hitR, 5000f, resourceMask))
            {
                var node = hitR.collider.GetComponentInParent<Project.Gameplay.Resources.ResourceNode>();
                if (node != null)
                {
                    for (int i = 0; i < selectedUnits.Count; i++)
                    {
                        var u = selectedUnits[i];
                        if (u == null) continue;

                        // Cancelar construcción si estaba construyendo
                        var builder = u.GetComponent<Builder>();
                        if (builder != null) builder.SetBuildTarget(null);

                        var g = u.GetComponent<VillagerGatherer>();
                        if (g != null) g.Gather(node);
                    }
                    return;
                }
            }

            // 2) Suelo: mover en formación
            if (Physics.Raycast(ray, out RaycastHit hitG, 5000f, groundMask))
            {
                Vector3 target = hitG.point;

                // Cancelar construcción y recolección para que la orden de mover no se sobrescriba
                for (int i = 0; i < selectedUnits.Count; i++)
                {
                    var u = selectedUnits[i];
                    if (u == null) continue;

                    var builder = u.GetComponent<Builder>();
                    if (builder != null) builder.SetBuildTarget(null);

                    var gatherer = u.GetComponent<VillagerGatherer>();
                    if (gatherer != null) gatherer.PauseGatherKeepCarried();
                }

                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                forward.Normalize();

                var formationPositions = FormationHelper.GenerateGrid(
                    target,
                    selectedUnits.Count,
                    formationSpacing,
                    forward
                );

                for (int i = 0; i < selectedUnits.Count; i++)
                {
                    var mover = selectedUnits[i].GetComponent<UnitMover>();
                    if (mover != null)
                        _bus.Enqueue(new MoveCommand(mover, formationPositions[i]));
                }

                _bus.Flush();
            }
        }
    }
}
