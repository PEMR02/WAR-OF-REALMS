using UnityEngine;
using Project.Gameplay.Players;

namespace Project.UI
{
    /// <summary>
    /// Barra superior de recursos y población. Actualiza por eventos (sin Update).
    /// </summary>
    public class UIResourceBar : MonoBehaviour
    {
        [Header("Items")]
        [Tooltip("Si algo quedó mal enlazado en el inspector, en Awake se vuelven a tomar desde LeftGroup/RightGroup por nombre (WoodItem, StoneItem, …).")]
        [SerializeField] UIResourceItem woodItem;
        [SerializeField] UIResourceItem stoneItem;
        [SerializeField] UIResourceItem goldItem;
        [SerializeField] UIResourceItem foodItem;
        [SerializeField] UIResourceItem populationItem;

        [Header("Data")]
        [SerializeField] PlayerResources player;
        [SerializeField] PopulationManager populationManager;

        [Header("Iconos (Sprites del Project, opcional)")]
        [Tooltip("Opcional: sprites centralizados. Si ya asignaste 'Icon Sprite' en cada UIResourceItem, puedes dejar estos vacíos.")]
        [SerializeField] Sprite woodIcon;
        [SerializeField] Sprite stoneIcon;
        [SerializeField] Sprite goldIcon;
        [SerializeField] Sprite foodIcon;
        [SerializeField] Sprite populationIcon;

        [Header("Tooltip")]
        [TextArea(1, 3)]
        [SerializeField] string tooltipContent = "Recursos y población. Recolecta con aldeanos.";

        static readonly Color PopulationNormal = Color.white;
        static readonly Color PopulationWarning = Color.yellow;
        static readonly Color PopulationFull = Color.red;

        void Awake()
        {
            BindItemsFromHierarchyNames();

            if (player == null)
                player = PlayerResources.FindPrimaryHumanSkirmish();
            if (populationManager == null)
                populationManager = PopulationManager.FindPrimaryHumanSkirmish();

            ApplyStaticItemPresentation();
        }

        /// <summary>Asegura que WoodItem → madera, etc. aunque en el inspector se cruzaran referencias.</summary>
        void BindItemsFromHierarchyNames()
        {
            var left = transform.Find("LeftGroup");
            if (left != null)
            {
                var w = left.Find("WoodItem")?.GetComponent<UIResourceItem>();
                var s = left.Find("StoneItem")?.GetComponent<UIResourceItem>();
                var g = left.Find("GoldItem")?.GetComponent<UIResourceItem>();
                var f = left.Find("FoodItem")?.GetComponent<UIResourceItem>();
                if (w != null) woodItem = w;
                if (s != null) stoneItem = s;
                if (g != null) goldItem = g;
                if (f != null) foodItem = f;
            }

            var right = transform.Find("RightGroup");
            if (right != null)
            {
                var p = right.Find("PopulationItem")?.GetComponent<UIResourceItem>();
                if (p != null) populationItem = p;
            }
        }

        void OnEnable()
        {
            if (player != null)
                player.OnResourceChanged += OnResourcesChanged;
            if (populationManager != null)
                populationManager.OnPopulationChanged += OnPopulationChanged;

            RefreshFromSources();
        }

        void OnDisable()
        {
            if (player != null)
                player.OnResourceChanged -= OnResourcesChanged;
            if (populationManager != null)
                populationManager.OnPopulationChanged -= OnPopulationChanged;
        }

        void Start()
        {
            if (!string.IsNullOrWhiteSpace(tooltipContent))
            {
                var trigger = gameObject.GetComponent<TooltipTrigger>();
                if (trigger == null)
                    trigger = gameObject.AddComponent<TooltipTrigger>();
                trigger.content = tooltipContent;
            }
        }

        void ApplyStaticItemPresentation()
        {
            if (woodItem != null)
                woodItem.SetData(woodIcon, null, player != null ? player.wood : 0);
            if (stoneItem != null)
                stoneItem.SetData(stoneIcon, null, player != null ? player.stone : 0);
            if (goldItem != null)
                goldItem.SetData(goldIcon, null, player != null ? player.gold : 0);
            if (foodItem != null)
                foodItem.SetData(foodIcon, null, player != null ? player.food : 0);
            if (populationItem != null)
            {
                if (populationManager != null)
                {
                    populationItem.SetData(populationIcon, null, populationManager.CurrentPopulation);
                    populationItem.SetValueFormatted(populationManager.CurrentPopulation, populationManager.MaxPopulation);
                }
                else
                    populationItem.SetData(populationIcon, null, 0);
            }
        }

        void OnResourcesChanged() => RefreshResources();

        void OnPopulationChanged(int current, int max) => SetPopulation(current, max);

        void RefreshFromSources()
        {
            RefreshResources();
            if (populationManager != null)
                SetPopulation(populationManager.CurrentPopulation, populationManager.MaxPopulation);
        }

        void RefreshResources()
        {
            if (player == null) return;
            SetResources(player.wood, player.stone, player.gold, player.food);
        }

        /// <summary>Actualiza solo números de recursos (sin tocar iconos ni etiquetas).</summary>
        public void SetResources(int wood, int stone, int gold, int food)
        {
            woodItem?.SetValue(wood);
            stoneItem?.SetValue(stone);
            goldItem?.SetValue(gold);
            foodItem?.SetValue(food);
        }

        /// <summary>Población actual / máximo con color según cupo.</summary>
        public void SetPopulation(int current, int max)
        {
            if (populationItem == null) return;
            populationItem.SetValueFormatted(current, max);
            if (max <= 0)
            {
                populationItem.SetValueTextColor(PopulationNormal);
                return;
            }

            float ratio = current / (float)max;
            if (current >= max)
                populationItem.SetValueTextColor(PopulationFull);
            else if (ratio >= 0.8f)
                populationItem.SetValueTextColor(PopulationWarning);
            else
                populationItem.SetValueTextColor(PopulationNormal);
        }
    }
}
