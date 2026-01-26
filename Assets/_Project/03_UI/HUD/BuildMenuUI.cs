using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.Gameplay.Buildings;
using Project.Gameplay.Players;

namespace Project.UI
{
    public class BuildMenuUI : MonoBehaviour
    {
        [Header("Refs")]
        public BuildingPlacer placer;
        public PlayerResources owner;

        [Header("UI")]
        public TextMeshProUGUI infoText; // opcional: muestra costos / estado

        [Header("Buildings")]
        public BuildingSO townCenter;
        public BuildingSO house;
        public BuildingSO barracks;

        [Header("Buttons")]
        public Button btnTownCenter;
        public Button btnHouse;
        public Button btnBarracks;

        void Awake()
        {
            if (placer == null) placer = FindFirstObjectByType<BuildingPlacer>();
            if (owner == null) owner = FindFirstObjectByType<PlayerResources>();

            if (placer != null && placer.owner == null)
                placer.owner = owner;

            // Si infoText no está asignado, intenta encontrarlo por nombre
            if (infoText == null)
            {
                var t = GameObject.Find("BuildInfoText");
                if (t != null) infoText = t.GetComponent<TextMeshProUGUI>();
            }

            Hook(btnTownCenter, townCenter);
            Hook(btnHouse, house);
            Hook(btnBarracks, barracks);

            RefreshInfo(null);
        }

        void Update()
        {
            // Refresco simple del texto informativo
            if (infoText != null && placer != null && placer.selectedBuilding != null)
            {
                bool canAfford = placer.CanAfford(placer.selectedBuilding);
                string affordText = canAfford ? "[OK]" : "[!] Sin recursos";
                
                infoText.text = $"Construyendo: {placer.selectedBuilding.id}\n{CostString(placer.selectedBuilding)}\n{affordText}";
            }
        }

        void Hook(Button btn, BuildingSO b)
        {
            if (btn == null) return;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (placer == null) return;
                if (b == null)
                {
                    Debug.LogWarning("BuildMenuUI: BuildingSO no asignado en este botón.");
                    return;
                }

                // Si ya estás construyendo, cancela primero
                if (placer.IsPlacing)
                {
                    placer.Cancel();
                }

                placer.owner = owner;
                placer.selectedBuilding = b;

                Debug.Log($"BuildMenuUI click: {b.id}");

                placer.Begin();
                RefreshInfo(b);
            });

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null && b != null)
                tmp.text = b.id;
        }

        void RefreshInfo(BuildingSO b)
        {
            if (infoText == null) return;

            if (b == null)
            {
                infoText.text = "Selecciona un edificio para construir.";
                return;
            }

            bool canAfford = placer != null && placer.CanAfford(b);
            string affordText = canAfford ? "[OK] Puedes construir" : "[!] Sin recursos suficientes";

            infoText.text = $"Seleccionado: {b.id}\n{CostString(b)}\n{affordText}";
        }

        string CostString(BuildingSO b)
        {
            if (b == null || b.costs == null || b.costs.Length == 0)
                return "Costo: Gratis";

            System.Text.StringBuilder sb = new();
            sb.Append("Costo: ");

            for (int i = 0; i < b.costs.Length; i++)
            {
                var c = b.costs[i];
                sb.Append($"{c.kind}:{c.amount}");

                if (i < b.costs.Length - 1)
                    sb.Append(" | ");
            }

            return sb.ToString();
        }
    }
}