using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Project.Gameplay;
using Project.Gameplay.Units;
using Project.Gameplay.Buildings;

namespace Project.UI
{
    /// <summary>
    /// Atajo de teclado para centrar la cámara en el Town Center del jugador.
    /// </summary>
    public class TownCenterHotkey : MonoBehaviour
    {
        [Header("Refs")]
        public RTSCameraController rtsCamera;
        public RTSSelectionController selection;

        [Header("Hotkey")]
        public Key key = Key.Home;

        [Header("Identificación")]
        [Tooltip("IDs de edificio que se consideran Town Center (ej. TownCenter, PF_TownCenter). Se busca el primero en escena.")]
        public string[] townCenterIds = new[] { "TownCenter", "PF_TownCenter", "Town Centre" };

        void Awake()
        {
            if (rtsCamera == null) rtsCamera = FindFirstObjectByType<RTSCameraController>();
            if (selection == null) selection = FindFirstObjectByType<RTSSelectionController>();
        }

        void Update()
        {
            if (Keyboard.current == null || !Keyboard.current[key].wasPressedThisFrame) return;
            if (UiInputRaycast.IsPointerOverGameObject()) return;

            Transform tc = FindFirstTownCenter();
            if (tc != null)
            {
                rtsCamera?.MoveToWorldPosition(tc.position);
                selection?.ClearSelection();
            }
        }

        Transform FindFirstTownCenter()
        {
            var all = FindObjectsByType<BuildingInstance>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var bi = all[i];
                if (bi == null || bi.buildingSO == null) continue;
                string id = bi.buildingSO.id;
                if (string.IsNullOrEmpty(id)) continue;
                for (int j = 0; j < townCenterIds.Length; j++)
                {
                    if (id.IndexOf(townCenterIds[j], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return bi.transform;
                }
            }
            return null;
        }
    }
}
