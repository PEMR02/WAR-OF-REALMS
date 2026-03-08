using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay;
using Project.Gameplay.Units;

namespace Project.UI
{
    /// <summary>
    /// Atajo de teclado para ir al siguiente aldeano ocioso (estilo Age of Empires).
    /// Selecciona al siguiente en la lista y centra la cámara en él.
    /// </summary>
    public class IdleVillagerHotkey : MonoBehaviour
    {
        [Header("Refs")]
        public RTSSelectionController selection;
        public RTSCameraController rtsCamera;

        [Header("Hotkey")]
        [Tooltip("Tecla para ir al siguiente aldeano ocioso (sin recolectar ni construir).")]
        public Key key = Key.H;

        void Awake()
        {
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            if (rtsCamera == null) rtsCamera = FindFirstObjectByType<RTSCameraController>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
            if (selection == null) return;
            if (rtsCamera == null) rtsCamera = FindFirstObjectByType<RTSCameraController>();

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (!kb[key].wasPressedThisFrame) return;

            UnitSelectable[] idleList = GetIdleVillagerSelectables();
            if (idleList == null || idleList.Length == 0) return;

            // Si ya tenemos seleccionado un aldeano ocioso, pasar al siguiente
            int currentIndex = -1;
            var selected = selection.GetSelected();
            if (selected != null && selected.Count == 1)
            {
                var sel = selected[0];
                for (int i = 0; i < idleList.Length; i++)
                {
                    if (idleList[i] == sel) { currentIndex = i; break; }
                }
            }

            int nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % idleList.Length;
            UnitSelectable next = idleList[nextIndex];
            selection.SelectOnly(next);

            if (rtsCamera != null)
                rtsCamera.MoveToWorldPosition(next.transform.position);
        }

        /// <summary>Devuelve todos los UnitSelectable que son aldeanos ociosos (sin recolectar ni construir).</summary>
        public static UnitSelectable[] GetIdleVillagerSelectables()
        {
            var gatherers = Object.FindObjectsByType<VillagerGatherer>(FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < gatherers.Length; i++)
            {
                if (gatherers[i] == null || !gatherers[i].IsIdle) continue;
                var b = gatherers[i].GetComponent<Builder>();
                if (b != null && b.HasBuildTarget) continue;
                var us = gatherers[i].GetComponent<UnitSelectable>();
                if (us != null) count++;
            }

            if (count == 0) return null;
            var result = new UnitSelectable[count];
            int n = 0;
            for (int i = 0; i < gatherers.Length && n < count; i++)
            {
                if (gatherers[i] == null || !gatherers[i].IsIdle) continue;
                var b = gatherers[i].GetComponent<Builder>();
                if (b != null && b.HasBuildTarget) continue;
                var us = gatherers[i].GetComponent<UnitSelectable>();
                if (us != null) result[n++] = us;
            }
            return result;
        }
    }
}
