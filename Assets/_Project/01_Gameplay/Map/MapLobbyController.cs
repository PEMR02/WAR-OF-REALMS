using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Project.Gameplay.Map.Generator;

namespace Project.Gameplay.Map
{
    /// <summary>
    /// Pantalla pre-partida: configuración de mapa (layout, hidrología, recursos, perfiles) y vista previa 2D.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class MapLobbyController : MonoBehaviour
    {
        const string PrefsPrefix = "WOR_MapLobby_";

        [Tooltip("Si null, se busca en la escena al Awake.")]
        public RTSMapGenerator mapGenerator;

        [Tooltip("Si true, el RTSMapGenerator no genera el mapa en Start hasta pulsar Iniciar partida.")]
        public bool blockInitialGeneration = true;

        [Min(32)] public int previewTextureMaxSize = 480;

        static readonly int[] MapSizes = { 192, 256, 320, 384 };
        /// <summary>Tamaño de celda en mundo (m): fijo 3 m, sin selector en el lobby.</summary>
        public const float LobbyWorldCellSizeMeters = 3f;

        RTSMapGenerator _gen;
        GameObject _root;
        RawImage _previewImage;
        Text _statusText;
        Text _summaryBody;
        InputField _seedField;
        MapPreviewOverlayMode _overlayMode = MapPreviewOverlayMode.Terrain;

        Text _mtnValueLabel, _riverValueLabel, _lakeValueLabel, _waterValueLabel;
        int _waterPercentUi = 40;

        int _mapSizeIndex = 1;
        /// <summary>0 = 1 jugador … 3 = 4 jugadores.</summary>
        int _lobbyPlayerCountIndex = 1;

        Button[] _tabBarButtons = new Button[4];
        GameObject[] _tabPanels = new GameObject[4];
        int _currentTabIndex;
        Button[] _mapSizeChips = new Button[4];
        Button[] _playerCountChips = new Button[4];
        GameObject[] _playerSlotRows = new GameObject[4];
        Button[] _slotHumanBtns = new Button[4];
        Button[] _slotAiBtns = new Button[4];

        static readonly Color RtsChipIdle = new Color(0.12f, 0.13f, 0.16f, 1f);
        static readonly Color RtsChipOn = new Color(0.22f, 0.34f, 0.26f, 1f);
        static readonly Color RtsTabOn = new Color(0.2f, 0.28f, 0.22f, 1f);
        static readonly Color RtsTabOff = new Color(0.09f, 0.1f, 0.12f, 1f);
        static readonly Color RtsAccentLine = new Color(0.55f, 0.48f, 0.32f, 0.9f);
        int _forestTier = 1, _goldTier = 2, _stoneTier = 2;
        bool _animalsOn = true;
        int _spawnUiIndex = 1;

        Vector2Int _snapTrees, _snapGold, _snapStone, _snapAnimals;
        int _selectedThemeIndex = 0;

        Texture2D _lastPreview;

        void Awake() => TryRegisterWithGenerator();
        void Start() => TryRegisterWithGenerator();

        void TryRegisterWithGenerator()
        {
            if (_gen == null)
            {
                _gen = mapGenerator
                    ?? GetComponent<RTSMapGenerator>()
                    ?? GetComponentInParent<RTSMapGenerator>()
                    ?? FindFirstObjectByType<RTSMapGenerator>(FindObjectsInactive.Include);
            }
            if (_gen != null && blockInitialGeneration)
                _gen.RegisterDeferredMapLobby(this);
        }

        internal void Open(RTSMapGenerator generator)
        {
            _gen = generator;
            EnsureUiBuilt();
            PullFromGenerator();
            _root.SetActive(true);
        }

        void OnDestroy()
        {
            if (_lastPreview != null)
                Destroy(_lastPreview);
        }

        void EnsureUiBuilt()
        {
            if (_root != null) return;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _root = new GameObject("MapLobbyCanvas");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            _root.AddComponent<GraphicRaycaster>();
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreateChild(_root.transform, "Panel");
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.06f, 0.09f, 0.96f);

            var row = CreateChild(panel.transform, "Columns");
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0.02f, 0.03f);
            rowRt.anchorMax = new Vector2(0.98f, 0.97f);
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childAlignment = TextAnchor.UpperCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            BuildLeftColumn(row.transform);
            BuildCenterColumn(row.transform);
            BuildRightColumn(row.transform);
        }

        int LobbyPlayerCount => _lobbyPlayerCountIndex + 1;

        void BuildLeftColumn(Transform parent)
        {
            var aside = CreateCard(parent, "Left", 432f, true);
            var outer = aside.gameObject.AddComponent<VerticalLayoutGroup>();
            outer.spacing = 6f;
            outer.padding = new RectOffset(6, 12, 10, 12);
            outer.childAlignment = TextAnchor.UpperLeft;
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = true;
            outer.childForceExpandHeight = false;

            var head = CreateChild(aside.transform, "LeftHead");
            head.AddComponent<LayoutElement>().preferredHeight = 48f;
            var headV = head.AddComponent<VerticalLayoutGroup>();
            headV.spacing = 2f;
            headV.childAlignment = TextAnchor.UpperLeft;
            headV.childControlWidth = true;
            headV.childForceExpandWidth = true;
            AddKicker(head.transform, "RTS · Pre-partida", new Color(0.72f, 0.62f, 0.42f));
            AddHeading(head.transform, "Preparar batalla", 19);

            var accent = CreateChild(aside.transform, "AccentLine");
            accent.AddComponent<LayoutElement>().preferredHeight = 2f;
            accent.AddComponent<Image>().color = RtsAccentLine;

            var tabBarGo = CreateChild(aside.transform, "TabBar");
            tabBarGo.AddComponent<LayoutElement>().preferredHeight = 38f;
            var tabH = tabBarGo.AddComponent<HorizontalLayoutGroup>();
            tabH.spacing = 4f;
            tabH.childAlignment = TextAnchor.MiddleCenter;
            tabH.childControlWidth = true;
            tabH.childControlHeight = true;
            tabH.childForceExpandWidth = true;
            tabH.childForceExpandHeight = true;
            string[] tabTitles = { "MAPA", "TERRENO", "RECURSOS", "JUGADORES" };
            for (int t = 0; t < 4; t++)
            {
                int ti = t;
                _tabBarButtons[t] = CreateTabBarButton(tabBarGo.transform, tabTitles[t], () => SelectLeftTab(ti));
            }

            var body = CreateChild(aside.transform, "TabBody");
            var bodyLe = body.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            bodyLe.minHeight = 340f;
            body.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.1f, 0.92f);

            for (int t = 0; t < 4; t++)
            {
                var panel = CreateChild(body.transform, $"Panel_{t}");
                panel.AddComponent<LayoutElement>().flexibleHeight = 1f;
                var pv = panel.AddComponent<VerticalLayoutGroup>();
                pv.spacing = 6f;
                pv.padding = new RectOffset(4, 10, 8, 10);
                pv.childAlignment = TextAnchor.UpperLeft;
                pv.childControlWidth = true;
                pv.childForceExpandWidth = true;
                _tabPanels[t] = panel;
            }

            BuildMapaTabContent(_tabPanels[0].transform);
            BuildTerrenoTabContent(_tabPanels[1].transform);
            BuildRecursosTabContent(_tabPanels[2].transform);
            BuildJugadoresTabContent(_tabPanels[3].transform);

            SelectLeftTab(0);
        }

        void SelectLeftTab(int idx)
        {
            _currentTabIndex = Mathf.Clamp(idx, 0, 3);
            for (int i = 0; i < 4; i++)
            {
                _tabPanels[i].SetActive(i == _currentTabIndex);
                var img = _tabBarButtons[i].targetGraphic as Image;
                if (img != null)
                    img.color = i == _currentTabIndex ? RtsTabOn : RtsTabOff;
            }
        }

        Button CreateTabBarButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateChild(parent, "Tab_" + label);
            go.AddComponent<LayoutElement>().minHeight = 34f;
            var le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var img = go.AddComponent<Image>();
            img.color = RtsTabOff;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var tgo = CreateChild(go.transform, "T");
            var txt = tgo.AddComponent<Text>();
            txt.font = LobbyUiFont();
            txt.text = label;
            txt.fontSize = 11;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.9f, 0.88f, 0.8f);
            txt.alignment = TextAnchor.MiddleCenter;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            return btn;
        }

        void BuildMapaTabContent(Transform parent)
        {
            AddSectionTitle(parent, "Mapa");
            AddLabel(parent, "Tamaño (celdas)");
            var mapRow = CreateHorizontalRow(parent);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _mapSizeChips[i] = CreateRtsChipButton(mapRow.transform, MapSizes[i].ToString(CultureInfo.InvariantCulture), () =>
                {
                    _mapSizeIndex = idx;
                    RefreshMapaChipVisuals();
                    PushLayoutFromUi();
                    RefreshSummary();
                });
            }

            AddLabel(parent, "Jugadores en partida");
            var pcRow = CreateHorizontalRow(parent);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _playerCountChips[i] = CreateRtsChipButton(pcRow.transform, (i + 1).ToString(CultureInfo.InvariantCulture), () =>
                {
                    int old = LobbyPlayerCount;
                    _lobbyPlayerCountIndex = idx;
                    OnLobbyPlayerCountChanged(old, LobbyPlayerCount);
                    RefreshMapaChipVisuals();
                    PushLayoutFromUi();
                    RefreshPlayerSlotUi();
                    RefreshSummary();
                });
            }

            AddLabel(parent, "Seed");
            var seedRow = CreateHorizontalRow(parent);
            _seedField = CreateInputField(seedRow.transform, 200f);
            _seedField.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            CreateSmallButton(seedRow.transform, "Random", OnRandomSeedClicked);
        }

        Button CreateRtsChipButton(Transform parent, string cap, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateChild(parent, cap);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            le.minWidth = 56f;
            var img = go.AddComponent<Image>();
            img.color = RtsChipIdle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var tgo = CreateChild(go.transform, "L");
            var txt = tgo.AddComponent<Text>();
            txt.font = LobbyUiFont();
            txt.text = cap;
            txt.fontSize = 14;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.93f, 0.9f, 0.82f);
            txt.alignment = TextAnchor.MiddleCenter;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            return btn;
        }

        void RefreshMapaChipVisuals()
        {
            for (int i = 0; i < _mapSizeChips.Length; i++)
            {
                if (_mapSizeChips[i] == null) continue;
                var img = _mapSizeChips[i].targetGraphic as Image;
                if (img != null) img.color = i == _mapSizeIndex ? RtsChipOn : RtsChipIdle;
            }
            for (int i = 0; i < _playerCountChips.Length; i++)
            {
                if (_playerCountChips[i] == null) continue;
                var img = _playerCountChips[i].targetGraphic as Image;
                if (img != null) img.color = i == _lobbyPlayerCountIndex ? RtsChipOn : RtsChipIdle;
            }
        }

        void BuildTerrenoTabContent(Transform parent)
        {
            AddSectionTitle(parent, "Terreno e hidrología");
            AddIntStepper(parent, "Montañas", 0, 12,
                () => _gen != null ? _gen.lobbyMacroMountainMasses : 2,
                v => { if (_gen != null) _gen.lobbyMacroMountainMasses = v; },
                v => $"{v}",
                out _mtnValueLabel);
            AddIntStepper(parent, "Ríos", 0, 8,
                () => _gen != null ? _gen.riverCount : 3,
                v => { if (_gen != null) _gen.riverCount = v; },
                v => $"{v}",
                out _riverValueLabel);
            AddIntStepper(parent, "Lagos", 0, 12,
                () => _gen != null ? _gen.lakeCount : 2,
                v => { if (_gen != null) _gen.lakeCount = v; },
                v => $"{v}",
                out _lakeValueLabel);
            _waterPercentUi = _gen != null && _gen.waterHeightRelative >= 0f && _gen.waterHeightRelative <= 1f
                ? Mathf.RoundToInt(_gen.waterHeightRelative * 100f)
                : 40;
            AddIntStepper(parent, "Agua base %", 0, 100,
                () => _waterPercentUi,
                v =>
                {
                    _waterPercentUi = v;
                    if (_gen != null) _gen.waterHeightRelative = Mathf.Clamp01(v / 100f);
                },
                v => $"{v / 100f:0.00}",
                out _waterValueLabel);
        }

        void BuildRecursosTabContent(Transform parent)
        {
            AddSectionTitle(parent, "Recursos del mapa");
            AddLabel(parent, "Bajo · Med · Alto");
            CreateTierRowRts(parent, "Bosque", v => { _forestTier = v; }, ApplyResourceTiers);
            CreateTierRowRts(parent, "Oro", v => { _goldTier = v; }, ApplyResourceTiers);
            CreateTierRowRts(parent, "Piedra", v => { _stoneTier = v; }, ApplyResourceTiers);
            AddLabel(parent, "Animales");
            var ar = CreateHorizontalRow(parent);
            CreateRtsChipButton(ar.transform, "OFF", () => { _animalsOn = false; ApplyResourceTiers(); RefreshSummary(); });
            CreateRtsChipButton(ar.transform, "ON", () => { _animalsOn = true; ApplyResourceTiers(); RefreshSummary(); });
        }

        void CreateTierRowRts(Transform parent, string prefix, System.Action<int> setTier, System.Action onChange)
        {
            var row = CreateHorizontalRow(parent);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var title = CreateChild(row.transform, "Tt");
            title.AddComponent<LayoutElement>().minWidth = 72f;
            var tt = title.AddComponent<Text>();
            tt.font = LobbyUiFont();
            tt.text = prefix;
            tt.fontSize = 12;
            tt.color = new Color(0.75f, 0.78f, 0.82f);
            tt.alignment = TextAnchor.MiddleLeft;
            for (int t = 0; t < 3; t++)
            {
                int tier = t;
                string[] names = { "Bajo", "Med", "Alto" };
                CreateRtsChipButton(row.transform, names[t], () =>
                {
                    setTier(tier);
                    onChange?.Invoke();
                    RefreshSummary();
                });
            }
        }

        void BuildJugadoresTabContent(Transform parent)
        {
            AddSectionTitle(parent, "Plazas");
            AddBody(parent, "Mínimo un humano. Nuevas plazas → IA por defecto.", 11);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var row = CreateChild(parent, $"Plaza_{i}");
                row.AddComponent<LayoutElement>().minHeight = 44f;
                var h = row.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 8f;
                h.childAlignment = TextAnchor.MiddleLeft;
                var lbl = CreateChild(row.transform, "Lbl");
                lbl.AddComponent<LayoutElement>().minWidth = 88f;
                var lt = lbl.AddComponent<Text>();
                lt.font = LobbyUiFont();
                lt.fontSize = 12;
                lt.fontStyle = FontStyle.Bold;
                lt.color = new Color(0.88f, 0.85f, 0.78f);
                lt.text = $"Jugador {i + 1}";
                lt.alignment = TextAnchor.MiddleLeft;
                _slotHumanBtns[idx] = CreateRtsChipButton(row.transform, "Humano",
                    () => TrySetSlotHuman(idx, true));
                _slotAiBtns[idx] = CreateRtsChipButton(row.transform, "IA",
                    () => TrySetSlotHuman(idx, false));
                _playerSlotRows[idx] = row;
            }
        }

        void OnLobbyPlayerCountChanged(int previousCount, int newCount)
        {
            if (_gen == null) return;
            _gen.EnsureLobbyPlayerSlotsArray();
            if (newCount > previousCount)
            {
                for (int i = previousCount; i < newCount; i++)
                    _gen.lobbyPlayerSlotIsHuman[i] = false;
            }
            SyncSlotsAtLeastOneHuman();
            RefreshPlayerSlotUi();
        }

        void SyncSlotsAtLeastOneHuman()
        {
            if (_gen == null) return;
            int n = LobbyPlayerCount;
            int hum = 0;
            for (int i = 0; i < n; i++)
                if (_gen.lobbyPlayerSlotIsHuman[i]) hum++;
            if (hum == 0)
                _gen.lobbyPlayerSlotIsHuman[0] = true;
        }

        void TrySetSlotHuman(int slot, bool human)
        {
            if (_gen == null) return;
            int n = LobbyPlayerCount;
            if (slot < 0 || slot >= n) return;
            _gen.EnsureLobbyPlayerSlotsArray();
            if (!human)
            {
                int hum = 0;
                for (int i = 0; i < n; i++)
                    if (_gen.lobbyPlayerSlotIsHuman[i]) hum++;
                if (_gen.lobbyPlayerSlotIsHuman[slot] && hum <= 1)
                    return;
            }
            _gen.lobbyPlayerSlotIsHuman[slot] = human;
            RefreshPlayerSlotUi();
            RefreshSummary();
        }

        void RefreshPlayerSlotUi()
        {
            int n = LobbyPlayerCount;
            for (int i = 0; i < 4; i++)
            {
                if (_playerSlotRows[i] == null) continue;
                _playerSlotRows[i].SetActive(i < n);
                if (i >= n || _gen == null) continue;
                bool hum = _gen.lobbyPlayerSlotIsHuman[i];
                var ih = _slotHumanBtns[i].targetGraphic as Image;
                var ia = _slotAiBtns[i].targetGraphic as Image;
                if (ih != null) ih.color = hum ? RtsChipOn : RtsChipIdle;
                if (ia != null) ia.color = !hum ? RtsChipOn : RtsChipIdle;
            }
        }

        void BuildCenterColumn(Transform parent)
        {
            var main = CreateCard(parent, "Main", -1f, true);
            var leFlex = main.gameObject.AddComponent<LayoutElement>();
            leFlex.flexibleWidth = 1f;
            leFlex.minWidth = 400f;

            var v = main.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f;
            v.padding = new RectOffset(18, 18, 16, 16);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            AddKicker(main.transform, "Previsualización 2D", new Color(0.45f, 0.9f, 0.65f));
            AddHeading(main.transform, "Vista táctica del mapa", 22);
            AddBody(main.transform,
                "Revisa layout, agua, relieve y spawns. Usa los botones para alternar capa semántica o recursos colocados.", 12);

            var tools = CreateHorizontalRow(main.transform);
            CreateSmallButton(tools.transform, "Regenerar preview", () => RunPreview(_overlayMode));
            CreateSmallButton(tools.transform, "Ver regiones", () => RunPreview(MapPreviewOverlayMode.Regions));
            CreateSmallButton(tools.transform, "Ver recursos", () => RunPreview(MapPreviewOverlayMode.Resources));

            var previewHolder = CreateChild(main.transform, "PreviewHolder");
            var previewRt = previewHolder.GetComponent<RectTransform>();
            previewRt.sizeDelta = new Vector2(0, 320f);
            var previewBg = previewHolder.AddComponent<Image>();
            previewBg.color = new Color(0.03f, 0.04f, 0.06f, 1f);
            var previewLe = previewHolder.AddComponent<LayoutElement>();
            previewLe.preferredHeight = 320f;
            previewLe.flexibleHeight = 0f;
            previewLe.flexibleWidth = 1f;

            var rawGo = CreateChild(previewHolder.transform, "Raw");
            var rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(8, 8);
            rawRt.offsetMax = new Vector2(-8, -8);
            _previewImage = rawGo.AddComponent<RawImage>();
            _previewImage.uvRect = new Rect(0, 0, 1, 1);
            _previewImage.texture = null;
            _previewImage.color = new Color(0.07f, 0.08f, 0.1f, 1f);

            _statusText = AddBody(main.transform,
                "Pulsa «Regenerar preview» para generar el vistazo. Leyenda en el panel derecho.", 11).GetComponent<Text>();

            var cardsRow = CreateChild(main.transform, "SummaryRow");
            var cardsH = cardsRow.AddComponent<HorizontalLayoutGroup>();
            cardsH.spacing = 10f;
            cardsH.padding = new RectOffset(0, 0, 4, 8);
            cardsH.childControlWidth = true;
            cardsH.childControlHeight = true;
            cardsH.childForceExpandWidth = true;
            cardsH.childForceExpandHeight = true;
            var cardsLe = cardsRow.AddComponent<LayoutElement>();
            cardsLe.minHeight = 200f;
            cardsLe.preferredHeight = 220f;
            cardsLe.flexibleHeight = 1f;
            cardsLe.flexibleWidth = 1f;

            CreateSummaryCard(cardsRow.transform, "Resumen", out _summaryBody);
            CreateSummaryCard(cardsRow.transform,
                "Balance esperado",
                out Text bal);
            bal.text = "• Oro y piedra suelen favorecer relieve.\n• Bosque y fauna suelen densificarse cerca del agua.\n• Spawns priorizan planos seguros cuando el generador lo permite.";

            var actCard = CreateChild(cardsRow.transform, "ActionsCard");
            StyleCard(actCard, 1f);
            var actV = actCard.AddComponent<VerticalLayoutGroup>();
            actV.padding = new RectOffset(12, 12, 12, 12);
            actV.spacing = 8;
            AddKicker(actCard.transform, "Acciones", new Color(0.75f, 0.75f, 0.78f));
            CreatePrimaryButton(actCard.transform, "Iniciar partida", OnStartGameClicked);
            CreateSmallButton(actCard.transform, "Guardar configuración", OnSaveConfigClicked);
        }

        void BuildRightColumn(Transform parent)
        {
            var aside = CreateCard(parent, "Right", 300f, true);
            var v = aside.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10f;
            v.padding = new RectOffset(16, 16, 14, 14);
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            AddKicker(aside.transform, "Perfiles", new Color(0.75f, 0.65f, 1f));
            AddHeading(aside.transform, "Estilo técnico", 20);
            AddBody(aside.transform,
                "Atajos de tuning coherente con el generador alpha/legacy. Puedes afinar después con los controles.", 11);

            string[] names = { "Alpha Neutral", "Highlands", "Wetlands", "Drylands" };
            string[] descs = {
                "Base técnica equilibrada; macro relieve según slider.",
                "Más relieve, más piedra y montañas.",
                "Más agua, ríos/lagos y bosque.",
                "Menos agua; llanuras más secas y amplias."
            };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                CreateThemeButton(aside.transform, names[i], descs[i], () => ApplyTheme(idx));
            }

            AddSectionTitle(aside.transform, "Leyenda");
            AddLegendRow(aside.transform, new Color(0.58f, 0.54f, 0.52f), "Montaña");
            AddLegendRow(aside.transform, new Color(0.85f, 0.62f, 0.18f), "Colina");
            AddLegendRow(aside.transform, new Color(0.52f, 0.82f, 0.32f), "Planicie");
            AddLegendRow(aside.transform, new Color(0.28f, 0.58f, 0.94f), "Río / orilla");
            AddLegendRow(aside.transform, new Color(0.18f, 0.72f, 0.88f), "Lago");
            AddLegendRow(aside.transform, new Color(0.08f, 0.45f, 0.3f), "Bosque");
            AddLegendRow(aside.transform, new Color(0.95f, 0.28f, 0.38f), "Spawn (ciudad)");

            AddBody(aside.transform,
                "Si el match usa configuración alpha, layout/hidrología también se sincronizan al empezar.", 10);
        }

        static RectTransform CreateCard(Transform parent, string name, float preferredWidth, bool stretchHeight)
        {
            var go = CreateChild(parent, name);
            var rt = go.GetComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            if (preferredWidth > 0) le.preferredWidth = preferredWidth;
            if (stretchHeight)
            {
                le.flexibleHeight = 1f;
                le.minHeight = 200f;
            }
            var img = go.AddComponent<Image>();
            img.color = new Color(0.09f, 0.11f, 0.14f, 0.92f);
            return rt;
        }

        static void StyleCard(GameObject go, float flexW)
        {
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.14f, 0.17f, 0.85f);
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = flexW;
            le.flexibleHeight = 1f;
            le.minHeight = 140f;
        }

        GameObject CreateSummaryCard(Transform parent, string title, out Text body)
        {
            var go = CreateChild(parent, title);
            StyleCard(go, 1f);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 12, 12);
            v.spacing = 6;
            v.childControlHeight = true;
            v.childForceExpandHeight = true;
            AddKicker(go.transform, title.ToUpperInvariant(), new Color(0.65f, 0.68f, 0.72f));
            var txtGo = CreateChild(go.transform, "Body");
            body = txtGo.AddComponent<Text>();
            body.font = LobbyUiFont();
            body.fontSize = 13;
            body.color = new Color(0.9f, 0.92f, 0.95f);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            var le = txtGo.AddComponent<LayoutElement>();
            le.minHeight = 96f;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;
            return go;
        }

        Button CreateThemeButton(Transform parent, string title, string desc, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateChild(parent, title);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.14f, 0.16f, 0.2f, 0.95f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 72f;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 10, 10);
            v.spacing = 4;
            var t1 = AddHeading(go.transform, title, 15);
            var t2 = AddBody(go.transform, desc, 11);
            return btn;
        }

        void AddLegendRow(Transform parent, Color dot, string label)
        {
            var row = CreateHorizontalRow(parent);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(4, 4, 2, 2);
            var dotGo = CreateChild(row.transform, "dot");
            var dotLe = dotGo.AddComponent<LayoutElement>();
            dotLe.minWidth = dotLe.preferredWidth = 14f;
            dotLe.minHeight = 14f;
            var dImg = dotGo.AddComponent<Image>();
            dImg.color = dot;
            AddBody(row.transform, label, 12);
        }

        static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        static GameObject CreateHorizontalRow(Transform parent)
        {
            var go = CreateChild(parent, "Row");
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = false;
            return go;
        }

        static void AddKicker(Transform parent, string msg, Color c)
        {
            var go = CreateChild(parent, "Kicker");
            var t = go.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.text = msg.ToUpperInvariant();
            t.fontSize = 11;
            t.fontStyle = FontStyle.Bold;
            t.color = c;
            t.alignment = TextAnchor.UpperLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 18f;
        }

        static Text AddHeading(Transform parent, string msg, int size)
        {
            var go = CreateChild(parent, "H");
            var t = go.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.text = msg;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.UpperLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 8;
            return t;
        }

        static Text AddBody(Transform parent, string msg, int size)
        {
            var go = CreateChild(parent, "P");
            var t = go.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.text = msg;
            t.fontSize = size;
            t.color = new Color(0.72f, 0.76f, 0.82f);
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 24f;
            le.flexibleWidth = 1f;
            return t;
        }

        static void AddSectionTitle(Transform parent, string msg)
        {
            var go = CreateChild(parent, "Sec");
            var t = go.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.text = msg;
            t.fontSize = 13;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(0.78f, 0.82f, 0.88f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 22f;
        }

        static void AddLabel(Transform parent, string msg)
        {
            var go = CreateChild(parent, "Lbl");
            var t = go.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.text = msg;
            t.fontSize = 11;
            t.color = new Color(0.55f, 0.58f, 0.65f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 16f;
        }

        void AddIntStepper(
            Transform parent,
            string label,
            int min,
            int max,
            System.Func<int> get,
            System.Action<int> set,
            System.Func<int, string> format,
            out Text valueLabel)
        {
            AddLabel(parent, label);
            var row = CreateHorizontalRow(parent);
            var valGo = CreateChild(row.transform, "Val");
            Text labelTxt = valGo.AddComponent<Text>();
            labelTxt.font = LobbyUiFont();
            labelTxt.fontSize = 13;
            labelTxt.color = new Color(0.88f, 0.9f, 0.93f);
            labelTxt.alignment = TextAnchor.MiddleCenter;
            var valLe = valGo.AddComponent<LayoutElement>();
            valLe.minWidth = 120f;
            labelTxt.text = format(get());
            var minus = CreateSmallButton(row.transform, "−", () =>
            {
                int v = Mathf.Clamp(get() - 1, min, max);
                set(v);
                labelTxt.text = format(v);
                RefreshSummary();
            });
            minus.gameObject.GetComponent<LayoutElement>().preferredWidth = 48f;
            var plus = CreateSmallButton(row.transform, "+", () =>
            {
                int v = Mathf.Clamp(get() + 1, min, max);
                set(v);
                labelTxt.text = format(v);
                RefreshSummary();
            });
            plus.gameObject.GetComponent<LayoutElement>().preferredWidth = 48f;
            minus.transform.SetSiblingIndex(0);
            valGo.transform.SetSiblingIndex(1);
            plus.transform.SetSiblingIndex(2);
            valueLabel = labelTxt;
        }

        static InputField CreateInputField(Transform parent, float width)
        {
            var inputGo = CreateChild(parent, "Input");
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.sizeDelta = new Vector2(width, 32f);
            var le = inputGo.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            var bg = inputGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.16f, 0.2f, 1f);
            var input = inputGo.AddComponent<InputField>();
            var textGo = CreateChild(inputGo.transform, "Text");
            var t = textGo.AddComponent<Text>();
            t.font = LobbyUiFont();
            t.fontSize = 14;
            t.color = Color.white;
            t.supportRichText = false;
            t.alignment = TextAnchor.MiddleLeft;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);
            input.textComponent = t;
            input.lineType = InputField.LineType.SingleLine;
            input.characterValidation = InputField.CharacterValidation.Integer;
            input.customCaretColor = true;
            input.caretColor = Color.white;
            return input;
        }

        Button CreateSmallButton(Transform parent, string cap, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateChild(parent, cap);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 34f;
            le.preferredWidth = 130f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.16f, 0.18f, 0.22f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = ColorBlock.defaultColorBlock;
            btn.onClick.AddListener(onClick);
            var tgo = CreateChild(go.transform, "Label");
            var txt = tgo.AddComponent<Text>();
            txt.font = LobbyUiFont();
            txt.text = cap;
            txt.fontSize = 12;
            txt.color = new Color(0.92f, 0.94f, 0.96f);
            txt.alignment = TextAnchor.MiddleCenter;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }

        void CreatePrimaryButton(Transform parent, string cap, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateChild(parent, cap);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.72f, 0.48f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var tgo = CreateChild(go.transform, "Label");
            var txt = tgo.AddComponent<Text>();
            txt.font = LobbyUiFont();
            txt.text = cap;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.06f, 0.08f, 0.09f);
            txt.alignment = TextAnchor.MiddleCenter;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
        }

        void PullFromGenerator()
        {
            if (_gen == null) return;
            _seedField.text = _gen.seed.ToString(CultureInfo.InvariantCulture);
            _mapSizeIndex = Mathf.Clamp(System.Array.IndexOf(MapSizes, _gen.width), 0, MapSizes.Length - 1);
            if (_mapSizeIndex < 0) _mapSizeIndex = 1;
            _lobbyPlayerCountIndex = Mathf.Clamp(_gen.playerCount, 1, 4) - 1;
            _gen.mapCellSizeWorld = LobbyWorldCellSizeMeters;
            _spawnUiIndex = Mathf.Clamp(_gen.lobbySpawnPatternUi, 0, 2);

            _waterPercentUi = _gen.waterHeightRelative >= 0f && _gen.waterHeightRelative <= 1f
                ? Mathf.RoundToInt(_gen.waterHeightRelative * 100f)
                : 40;

            _snapTrees = _gen.globalTrees;
            _snapGold = _gen.globalGold;
            _snapStone = _gen.globalStone;
            _snapAnimals = _gen.globalAnimals;
            _animalsOn = _gen.globalAnimals.y > 0;

            _gen.EnsureLobbyPlayerSlotsArray();
            RefreshSliderLabels();
            RefreshMapaChipVisuals();
            RefreshPlayerSlotUi();
            RefreshSummary();
            _statusText.text = "Pulsa «Regenerar preview» para un vistazo rápido del mapa.";
        }

        void RefreshSliderLabels()
        {
            if (_gen == null) return;
            if (_mtnValueLabel != null) _mtnValueLabel.text = $"{_gen.lobbyMacroMountainMasses} masas";
            if (_riverValueLabel != null) _riverValueLabel.text = $"{_gen.riverCount} ríos";
            if (_lakeValueLabel != null) _lakeValueLabel.text = $"{_gen.lakeCount} lagos";
            float n = _waterPercentUi / 100f;
            if (_waterValueLabel != null) _waterValueLabel.text = $"{n:0.00} norm.";
        }

        void RefreshSummary()
        {
            if (_gen == null || _summaryBody == null) return;
            int w = MapSizes[Mathf.Clamp(_mapSizeIndex, 0, MapSizes.Length - 1)];
            int pc = LobbyPlayerCount;
            int humans = 0, ais = 0;
            for (int i = 0; i < pc; i++)
            {
                if (_gen.lobbyPlayerSlotIsHuman[i]) humans++;
                else ais++;
            }
            _summaryBody.text =
                $"Mapa\t{w} × {w}\n" +
                $"Plazas\t{pc} ({humans} humanos · {ais} IA)\n" +
                $"Ríos\t{_gen.riverCount}\n" +
                $"Lagos\t{_gen.lakeCount}\n" +
                $"Montañas (macro)\t{_gen.lobbyMacroMountainMasses}\n" +
                $"Celda\t{LobbyWorldCellSizeMeters:0.0} m (fija)";
        }

        void PushLayoutFromUi()
        {
            if (_gen == null) return;
            int w = MapSizes[Mathf.Clamp(_mapSizeIndex, 0, MapSizes.Length - 1)];
            _gen.width = w;
            _gen.height = w;
            _gen.playerCount = LobbyPlayerCount;
            _gen.EnsureLobbyPlayerSlotsArray();
            SyncSlotsAtLeastOneHuman();
            _gen.mapCellSizeWorld = LobbyWorldCellSizeMeters;
            _gen.lobbySpawnPatternUi = Mathf.Clamp(_spawnUiIndex, 0, 2);
            ApplySpawnPatternHeuristic();
        }

        void ApplySpawnPatternHeuristic()
        {
            if (_gen == null) return;
            switch (_spawnUiIndex)
            {
                case 0:
                    _gen.spawnEdgePadding = 28f;
                    _gen.minPlayerDistance2p = 110f;
                    _gen.minPlayerDistance4p = 96f;
                    break;
                case 1:
                    _gen.spawnEdgePadding = 20f;
                    _gen.minPlayerDistance2p = 120f;
                    _gen.minPlayerDistance4p = 100f;
                    break;
                default:
                    _gen.spawnEdgePadding = 22f;
                    _gen.minPlayerDistance2p = 140f;
                    _gen.minPlayerDistance4p = 108f;
                    break;
            }
        }

        static float TierMultiplier(int tier) => tier <= 0 ? 0.72f : tier == 1 ? 1f : 1.35f;

        static Vector2Int ScaleVec(Vector2Int v, float m) =>
            new(Mathf.Max(0, Mathf.RoundToInt(v.x * m)), Mathf.Max(0, Mathf.RoundToInt(v.y * m)));

        void ApplyResourceTiers()
        {
            if (_gen == null) return;
            float fm = TierMultiplier(_forestTier);
            float gm = TierMultiplier(_goldTier);
            float sm = TierMultiplier(_stoneTier);
            _gen.globalTrees = ScaleVec(_snapTrees, fm);
            _gen.globalGold = ScaleVec(_snapGold, gm);
            _gen.globalStone = ScaleVec(_snapStone, sm);
            if (_animalsOn)
            {
                var baseAnimals = (_snapAnimals.x <= 0 && _snapAnimals.y <= 0) ? new Vector2Int(8, 20) : _snapAnimals;
                _gen.globalAnimals = ScaleVec(baseAnimals, 1f);
            }
            else
                _gen.globalAnimals = Vector2Int.zero;
        }

        void ApplyTheme(int idx)
        {
            if (_gen == null) return;
            _selectedThemeIndex = idx;
            switch (idx)
            {
                case 1:
                    _gen.terrainFlatness = 0.42f;
                    _gen.heightMultiplier = 11f;
                    _gen.lobbyMacroMountainMasses = Mathf.Max(_gen.lobbyMacroMountainMasses, 3);
                    _gen.globalStone = ScaleVec(_gen.globalStone, 1.15f);
                    _gen.riverCount = Mathf.Max(1, _gen.riverCount - 1);
                    break;
                case 2:
                    _gen.riverCount = Mathf.Min(8, _gen.riverCount + 1);
                    _gen.lakeCount = Mathf.Min(12, _gen.lakeCount + 1);
                    _gen.waterHeightRelative = Mathf.Clamp01(_gen.waterHeightRelative + 0.06f);
                    _gen.globalTrees = ScaleVec(_gen.globalTrees, 1.2f);
                    _gen.terrainFlatness = Mathf.Min(0.72f, _gen.terrainFlatness + 0.06f);
                    break;
                case 3:
                    _gen.riverCount = Mathf.Max(0, _gen.riverCount - 1);
                    _gen.lakeCount = Mathf.Max(0, _gen.lakeCount - 1);
                    _gen.waterHeightRelative = Mathf.Clamp01(_gen.waterHeightRelative - 0.07f);
                    _gen.terrainFlatness = Mathf.Min(0.85f, _gen.terrainFlatness + 0.08f);
                    _gen.heightMultiplier = Mathf.Max(5f, _gen.heightMultiplier - 1.5f);
                    break;
                default:
                    break;
            }

            PullFromGenerator();
            UpdateSnapFromGenerator();
            ApplyResourceTiers();
            RefreshSummary();
        }

        void UpdateSnapFromGenerator()
        {
            if (_gen == null) return;
            _snapTrees = _gen.globalTrees;
            _snapGold = _gen.globalGold;
            _snapStone = _gen.globalStone;
            _snapAnimals = _gen.globalAnimals;
        }

        void PushToGenerator()
        {
            if (_gen == null) return;
            PushLayoutFromUi();
            if (int.TryParse(_seedField.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                _gen.seed = s;
            _gen.waterHeightRelative = Mathf.Clamp01(_waterPercentUi / 100f);
            ApplyResourceTiers();
            _gen.randomSeedOnPlay = false;
        }

        void RunPreview(MapPreviewOverlayMode mode)
        {
            _overlayMode = mode;
            PushToGenerator();
            if (_lastPreview != null)
            {
                Destroy(_lastPreview);
                _lastPreview = null;
            }
            if (_gen.TryBuildMapPreview(out var tex, out var err, previewTextureMaxSize, mode))
            {
                _lastPreview = tex;
                _previewImage.texture = tex;
                _previewImage.color = Color.white;
                _statusText.text = mode switch
                {
                    MapPreviewOverlayMode.Regions => "Capa: regiones (semántica alpha o estimación por agua/relieve).",
                    MapPreviewOverlayMode.Resources => "Capa: recursos colocados en el grid lógico.",
                    _ => "Capa: terreno base (tipos de celda). La partida valida conexión de caminos y NavMesh."
                };
            }
            else
            {
                _previewImage.texture = null;
                _previewImage.color = new Color(0.07f, 0.08f, 0.1f, 1f);
                _statusText.text = string.IsNullOrEmpty(err) ? "Error en vista previa." : err;
            }
        }

        void OnRandomSeedClicked()
        {
            _seedField.text = UnityEngine.Random.Range(1, 2_000_000_000).ToString(CultureInfo.InvariantCulture);
        }

        void OnSaveConfigClicked()
        {
            PushToGenerator();
            if (_gen == null) return;
            PlayerPrefs.SetInt(PrefsPrefix + "seed", _gen.seed);
            PlayerPrefs.SetInt(PrefsPrefix + "mapSizeIdx", _mapSizeIndex);
            PlayerPrefs.SetInt(PrefsPrefix + "players", _lobbyPlayerCountIndex);
            PlayerPrefs.SetInt(PrefsPrefix + "theme", _selectedThemeIndex);
            PlayerPrefs.Save();
            _statusText.text = "Configuración guardada en PlayerPrefs (prefijo WOR_MapLobby_).";
        }

        static Font LobbyUiFont()
        {
            var f = global::UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;
            f = global::UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) return f;
            try { return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Helvetica", "Liberation Sans" }, 16); }
            catch { return null; }
        }

        void OnStartGameClicked()
        {
            PushToGenerator();
            if (_root != null)
                _root.SetActive(false);
            if (_gen != null)
            {
                _gen.PrepareGenerateFromLobby();
                _gen.Generate();
            }
        }
    }
}
