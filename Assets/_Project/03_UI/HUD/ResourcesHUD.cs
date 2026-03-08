using TMPro;
using UnityEngine;
using Project.Gameplay.Players;
using Project.Gameplay.Resources;

namespace Project.UI
{
    public class ResourcesHUD : MonoBehaviour
    {
        public PlayerResources player;
        public TextMeshProUGUI text;

        [Header("Tooltip")]
        [TextArea(1, 3)]
        public string tooltipContent = "Recursos actuales: Madera, Piedra, Oro, Comida. Recolecta con aldeanos.";

        void Awake()
        {
            if (text == null) text = GetComponent<TextMeshProUGUI>();
            if (player == null) player = FindFirstObjectByType<PlayerResources>();
        }

        void OnEnable()
        {
            if (player != null)
                player.OnResourceChanged += Refresh;
        }

        void OnDisable()
        {
            if (player != null)
                player.OnResourceChanged -= Refresh;
        }

        void Start()
        {
            Refresh();
            if (text != null && !string.IsNullOrWhiteSpace(tooltipContent))
            {
                var trigger = text.GetComponent<TooltipTrigger>();
                if (trigger == null) trigger = text.gameObject.AddComponent<TooltipTrigger>();
                trigger.content = tooltipContent;
            }
        }

        void Refresh()
        {
            if (player == null || text == null) return;

            text.text =
                $"Wood: {player.Get(ResourceKind.Wood)}\n" +
                $"Stone: {player.Get(ResourceKind.Stone)}\n" +
                $"Gold: {player.Get(ResourceKind.Gold)}\n" +
                $"Food: {player.Get(ResourceKind.Food)}";
        }
    }
}
