using TMPro;
using UnityEngine;
using Project.Gameplay.Units;

namespace Project.UI
{
    /// <summary>
    /// Muestra la cantidad de aldeanos ociosos (sin recolectar ni construir).
    /// </summary>
    public class IdleVillagerHUD : MonoBehaviour
    {
        public TextMeshProUGUI text;
        [Tooltip("Intervalo de actualización en segundos.")]
        public float pollInterval = 0.5f;

        float _timer;

        void Awake()
        {
            if (text == null) text = GetComponent<TextMeshProUGUI>();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = pollInterval;

            int idle = 0;
            var gatherers = FindObjectsByType<VillagerGatherer>(FindObjectsSortMode.None);
            for (int i = 0; i < gatherers.Length; i++)
            {
                var g = gatherers[i];
                if (g == null || !g.IsIdle) continue;
                var b = g.GetComponent<Builder>();
                if (b != null && b.HasBuildTarget) continue;
                idle++;
            }

            if (text != null)
                text.text = idle > 0 ? $"Ociosos: {idle}" : "";
        }
    }
}
