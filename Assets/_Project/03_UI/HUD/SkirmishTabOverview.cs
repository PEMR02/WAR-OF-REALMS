using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Project.Gameplay.Combat;
using Project.Gameplay.Faction;
using Project.Gameplay.Players;
using Project.Gameplay.Units;

namespace Project.UI
{
    /// <summary>
    /// Pulsa TAB para ver recursos y recuentos de unidades por bando (jugador vs IA).
    /// Se crea solo si no hay otra instancia en la escena cargada.
    /// </summary>
    public sealed class SkirmishTabOverview : MonoBehaviour
    {
        [Tooltip("Tamaño de fuente del panel (~3× el label por defecto de IMGUI).")]
        [Min(12)] public int panelFontSize = 42;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureInScene()
        {
            if (FindFirstObjectByType<SkirmishTabOverview>() != null) return;
            new GameObject(nameof(SkirmishTabOverview)).AddComponent<SkirmishTabOverview>();
        }

        bool _visible;
        string _text = string.Empty;
        int _frameSkip;
        GUIStyle _largeLabelStyle;
        int _cachedFontSize;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame)
                _visible = !_visible;

            if (!_visible) return;
            _frameSkip++;
            if (_frameSkip % 15 == 0)
                RebuildText();
        }

        void OnGUI()
        {
            if (!_visible) return;
            if (string.IsNullOrEmpty(_text))
                RebuildText();

            if (_largeLabelStyle == null || _cachedFontSize != panelFontSize)
            {
                _cachedFontSize = Mathf.Max(12, panelFontSize);
                _largeLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = _cachedFontSize,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Clip
                };
                _largeLabelStyle.normal.textColor = Color.white;
                _largeLabelStyle.padding = new RectOffset(12, 12, 10, 10);
            }

            float margin = 16f;
            float w = Mathf.Max(400f, Screen.width - margin * 2f);
            float h = Mathf.Max(320f, Screen.height - margin * 2f - 40f);
            GUILayout.BeginArea(new Rect(margin, 40f, w, h), GUI.skin.box);
            GUILayout.Label(_text, _largeLabelStyle, GUILayout.ExpandHeight(true));
            GUILayout.EndArea();
        }

        void RebuildText()
        {
            var sb = new StringBuilder(768);
            sb.AppendLine("Resumen de partida (TAB para cerrar)");
            sb.AppendLine("─────────────────────────────────────");

            var humanBank = PlayerResources.FindPrimaryHumanSkirmish();
            sb.AppendLine("Tu bando (jugador)");
            AppendBankLine(sb, humanBank);
            AppendLivingUnitCounts(sb, FactionId.Player);

            sb.AppendLine("Enemigos");
            bool anyEnemyBase = false;
            for (int slot = 1; slot <= 8; slot++)
            {
                var tc = GameObject.Find($"TownCenter_Player{slot}");
                if (tc == null) continue;
                var fm = tc.GetComponent<FactionMember>();
                if (fm == null || fm.IsPlayer) continue;
                var bank = tc.GetComponent<PlayerResources>();
                sb.AppendLine($"  Base IA #{slot}");
                AppendBankLine(sb, bank);
                anyEnemyBase = true;
            }
            if (!anyEnemyBase)
                sb.AppendLine("  (Sin TC enemigo con banco propio)");
            AppendLivingUnitCounts(sb, FactionId.Enemy);

            _text = sb.ToString();
        }

        static void AppendBankLine(StringBuilder sb, PlayerResources bank)
        {
            if (bank != null)
                sb.AppendLine($"    Madera {bank.wood}, piedra {bank.stone}, oro {bank.gold}, comida {bank.food}");
            else
                sb.AppendLine("    Recursos: —");
        }

        static void AppendLivingUnitCounts(StringBuilder sb, FactionId faction)
        {
            int alive = 0, villagers = 0, military = 0;
            var units = FindObjectsByType<UnitSelectable>(FindObjectsSortMode.None);
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (u == null) continue;
                var fm = u.GetComponentInParent<FactionMember>();
                if (fm == null || fm.faction != faction) continue;
                var h = u.GetComponentInParent<Health>();
                if (h == null || !h.IsAlive) continue;

                alive++;
                if (u.GetComponentInParent<VillagerGatherer>() != null)
                    villagers++;
                else if (u.GetComponentInParent<UnitAttacker>() != null)
                    military++;
            }

            string label = faction == FactionId.Player ? "  Tus unidades vivas" : "  Unidades enemigas vivas";
            sb.AppendLine($"{label}: {alive} (aprox. aldeanos {villagers}, combate {military})");
            sb.AppendLine();
        }
    }
}
