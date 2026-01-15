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

        void Awake()
        {
            if (text == null) text = GetComponent<TextMeshProUGUI>();
            if (player == null) player = FindFirstObjectByType<PlayerResources>();
        }

        void Update()
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
