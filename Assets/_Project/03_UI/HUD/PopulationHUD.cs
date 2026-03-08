using TMPro;
using UnityEngine;
using Project.Gameplay.Players;

namespace Project.UI
{
    /// <summary>
    /// Muestra la población actual del jugador en el HUD
    /// </summary>
    public class PopulationHUD : MonoBehaviour
    {
        [Header("Refs")]
        public PopulationManager populationManager;
        public TextMeshProUGUI populationText;

        [Header("Tooltip")]
        [TextArea(1, 3)]
        public string tooltipContent = "Población actual / máxima. Construye casas para aumentar el máximo.";

        void Awake()
        {
            if (populationManager == null)
                populationManager = FindFirstObjectByType<PopulationManager>();

            if (populationText == null)
                populationText = GetComponent<TextMeshProUGUI>();
        }

        void OnEnable()
        {
            if (populationManager != null)
                populationManager.OnPopulationChanged += RefreshDisplay;
        }

        void OnDisable()
        {
            if (populationManager != null)
                populationManager.OnPopulationChanged -= RefreshDisplay;
        }

        void Start()
        {
            RefreshDisplay(0, 0);
            if (populationText != null && !string.IsNullOrWhiteSpace(tooltipContent))
            {
                var trigger = populationText.GetComponent<TooltipTrigger>();
                if (trigger == null) trigger = populationText.gameObject.AddComponent<TooltipTrigger>();
                trigger.content = tooltipContent;
            }
        }

        void RefreshDisplay(int current, int max)
        {
            if (populationText == null || populationManager == null)
                return;

            int actualCurrent = populationManager.CurrentPopulation;
            int actualMax = populationManager.MaxPopulation;

            // Cambiar color si está cerca del límite
            Color textColor = Color.white;
            if (actualCurrent >= actualMax)
                textColor = Color.red; // Población llena
            else if (actualCurrent >= actualMax * 0.8f)
                textColor = Color.yellow; // Casi lleno

            populationText.color = textColor;
            populationText.text = $"Pop: {actualCurrent}/{actualMax}";
        }
    }
}
