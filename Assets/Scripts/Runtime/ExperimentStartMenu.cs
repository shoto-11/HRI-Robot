using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 二段階フォルダ式の実験開始メニュー（画面オーバーレイ、PC クリック対応）。
/// 1) 条件フォルダ（Baseline / No-AR / Proposed）
/// 2) ケース 1〜10
/// </summary>
public class ExperimentStartMenu : MonoBehaviour
{
    public static ExperimentStartMenu Instance { get; private set; }
    public bool IsVisible => _menuRoot != null && _menuRoot.activeSelf;

    enum Screen { Folders, Cases }

    GameObject _menuRoot;
    RectTransform _folderPanel;
    RectTransform _casePanel;
    Screen _screen = Screen.Folders;
    AGVPathVisualizer.VisMode _folder = AGVPathVisualizer.VisMode.Proposed;
    int _selectedCase;
    Text _detailText;
    Text _startLabel;
    Image[] _caseImages;
    Outline[] _caseOutlines;

    void Start()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        UiEventSystemBootstrap.Ensure();
        BuildMenu();
        ShowFolders();
        ApplyCursor(true);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || _menuRoot == null || !_menuRoot.activeSelf) return;

        if (_screen == Screen.Folders)
        {
            if (kb.digit1Key.wasPressedThisFrame) OpenFolder(AGVPathVisualizer.VisMode.Baseline);
            else if (kb.digit2Key.wasPressedThisFrame) OpenFolder(AGVPathVisualizer.VisMode.NoAR);
            else if (kb.digit3Key.wasPressedThisFrame) OpenFolder(AGVPathVisualizer.VisMode.Proposed);
        }
        else
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) ShowFolders();
            else if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) StartSelectedCase();
            else if (kb.digit1Key.wasPressedThisFrame) SelectCase(0);
            else if (kb.digit2Key.wasPressedThisFrame) SelectCase(1);
            else if (kb.digit3Key.wasPressedThisFrame) SelectCase(2);
            else if (kb.digit4Key.wasPressedThisFrame) SelectCase(3);
            else if (kb.digit5Key.wasPressedThisFrame) SelectCase(4);
            else if (kb.digit6Key.wasPressedThisFrame) SelectCase(5);
            else if (kb.digit7Key.wasPressedThisFrame) SelectCase(6);
            else if (kb.digit8Key.wasPressedThisFrame) SelectCase(7);
            else if (kb.digit9Key.wasPressedThisFrame) SelectCase(8);
            else if (kb.digit0Key.wasPressedThisFrame) SelectCase(9);
        }
    }

    public void ShowAfterCase(AGVPathVisualizer.VisMode mode, int completedCase)
    {
        if (_menuRoot == null) BuildMenu();
        _menuRoot.SetActive(true);
        _folder = mode;
        _selectedCase = completedCase;
        ShowCases();
        ApplyCursor(true);
    }

    void BuildMenu()
    {
        _menuRoot = new GameObject("ExperimentMenu");
        var canvas = _menuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        canvas.pixelPerfect = true;
        _menuRoot.AddComponent<GraphicRaycaster>();

        var scaler = _menuRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        scaler.referencePixelsPerUnit = 100f;

        var dim = CreateUi("Dim", _menuRoot.transform);
        var dimImg = dim.gameObject.AddComponent<Image>();
        dimImg.sprite = UiSprite();
        dimImg.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        dimImg.raycastTarget = true;
        Stretch(dim);

        _folderPanel = CreatePanel(_menuRoot.transform, "FolderPanel", new Vector2(1280f, 740f), new Color(0.07f, 0.09f, 0.14f, 0.96f));
        _casePanel = CreatePanel(_menuRoot.transform, "CasePanel", new Vector2(1280f, 740f), new Color(0.07f, 0.09f, 0.14f, 0.96f));

        BuildFolderScreen(_folderPanel);
        BuildCaseScreen(_casePanel);
    }

    void BuildFolderScreen(RectTransform bg)
    {
        AddText(bg, "実験コントロール", new Vector2(0f, 310f), new Vector2(1180f, 56f), 36, FontStyle.Bold, Color.white);
        AddText(bg, "まず条件フォルダを選んでください。その中でケース 1-10 を選びます。",
            new Vector2(0f, 250f), new Vector2(1180f, 40f), 20, FontStyle.Normal, new Color(0.78f, 0.82f, 0.86f));

        CreateFolderCard(bg, "ベースライン", "全台の経路を同じ見た目で表示",
            new Color(0.16f, 0.38f, 0.72f), new Vector2(-400f, 10f), AGVPathVisualizer.VisMode.Baseline);
        CreateFolderCard(bg, "no-ar", "床矢印・経路ラインなし",
            new Color(0.32f, 0.34f, 0.38f), new Vector2(0f, 10f), AGVPathVisualizer.VisMode.NoAR);
        CreateFolderCard(bg, "提案手法", "危険度に応じて色と不透明度を変える",
            new Color(0.14f, 0.52f, 0.28f), new Vector2(400f, 10f), AGVPathVisualizer.VisMode.Proposed);

        AddText(bg, "同一ケースならロボットの動きは3条件で同じです（シード固定）。推奨の実施順は仕様書のラテン方格に従います。",
            new Vector2(0f, -310f), new Vector2(1180f, 64f), 16, FontStyle.Normal, new Color(0.70f, 0.74f, 0.78f));
    }

    void CreateFolderCard(RectTransform parent, string title, string desc, Color color, Vector2 pos, AGVPathVisualizer.VisMode mode)
    {
        var card = CreateUi($"Folder_{mode}", parent);
        var img = card.gameObject.AddComponent<Image>();
        img.sprite = UiSprite();
        img.color = new Color(0.12f, 0.14f, 0.18f, 0.98f);
        img.raycastTarget = true;
        Center(card, new Vector2(360f, 340f), pos);

        var btn = card.gameObject.AddComponent<Button>();
        StyleButton(btn);
        var captured = mode;
        btn.onClick.AddListener(() => OpenFolder(captured));

        AddText(card, title, new Vector2(0f, 100f), new Vector2(320f, 48f), 28, FontStyle.Bold, Color.white);
        AddText(card, desc, new Vector2(0f, 20f), new Vector2(320f, 80f), 18, FontStyle.Normal, new Color(0.82f, 0.85f, 0.88f));

        var open = CreateUi("Open", card);
        var openImg = open.gameObject.AddComponent<Image>();
        openImg.sprite = UiSprite();
        openImg.color = color;
        openImg.raycastTarget = false;
        Center(open, new Vector2(200f, 56f), new Vector2(0f, -110f));
        AddText(open, "開く", Vector2.zero, new Vector2(200f, 56f), 22, FontStyle.Bold, Color.white);
    }

    void BuildCaseScreen(RectTransform bg)
    {
        AddText(bg, "title", new Vector2(0f, 310f), new Vector2(1180f, 48f), 32, FontStyle.Bold, Color.white).name = "CaseTitle";
        AddText(bg, "crumb", new Vector2(0f, 260f), new Vector2(1180f, 32f), 18, FontStyle.Normal, new Color(0.70f, 0.74f, 0.78f)).name = "CaseCrumb";

        _caseImages = new Image[10];
        _caseOutlines = new Outline[10];
        for (int i = 0; i < 10; i++)
        {
            int col = i % 5;
            int row = i / 5;
            var go = CreateUi($"Case_{i + 1}", bg);
            var img = go.gameObject.AddComponent<Image>();
            img.sprite = UiSprite();
            img.color = new Color(0.16f, 0.18f, 0.22f, 1f);
            img.raycastTarget = true;
            var outline = go.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.85f, 0.40f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.enabled = false;
            var btn = go.gameObject.AddComponent<Button>();
            StyleButton(btn);
            int captured = i;
            btn.onClick.AddListener(() => SelectCase(captured));
            Center(go, new Vector2(200f, 72f), new Vector2(-440f + col * 220f, 150f - row * 96f));
            AddText(go, $"ケース {i + 1}", Vector2.zero, new Vector2(200f, 72f), 22, FontStyle.Bold, Color.white);
            _caseImages[i] = img;
            _caseOutlines[i] = outline;
        }

        _detailText = AddText(bg, "detail", new Vector2(0f, -70f), new Vector2(1180f, 110f), 20, FontStyle.Normal, new Color(0.88f, 0.90f, 0.92f));

        MakeButton(bg, "Back", "← 条件フォルダへ", new Vector2(-380f, -280f), new Vector2(360f, 64f),
            new Color(0.22f, 0.24f, 0.28f), ShowFolders);
        var start = MakeButton(bg, "Start", "開始", new Vector2(220f, -280f), new Vector2(640f, 64f),
            new Color(0.16f, 0.55f, 0.28f), StartSelectedCase);
        _startLabel = start.GetComponentInChildren<Text>();
    }

    Button MakeButton(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = CreateUi(name, parent);
        var img = go.gameObject.AddComponent<Image>();
        img.sprite = UiSprite();
        img.color = color;
        img.raycastTarget = true;
        var btn = go.gameObject.AddComponent<Button>();
        StyleButton(btn);
        btn.onClick.AddListener(onClick);
        Center(go, size, pos);
        AddText(go, label, Vector2.zero, size, 22, FontStyle.Bold, Color.white);
        return btn;
    }

    static void StyleButton(Button btn)
    {
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.targetGraphic = btn.GetComponent<Image>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    void ShowFolders()
    {
        _screen = Screen.Folders;
        if (_folderPanel != null) _folderPanel.gameObject.SetActive(true);
        if (_casePanel != null) _casePanel.gameObject.SetActive(false);
    }

    void OpenFolder(AGVPathVisualizer.VisMode mode)
    {
        _folder = mode;
        _selectedCase = 0;
        ShowCases();
    }

    void ShowCases()
    {
        _screen = Screen.Cases;
        if (_folderPanel != null) _folderPanel.gameObject.SetActive(false);
        if (_casePanel != null) _casePanel.gameObject.SetActive(true);
        SetNamed(_casePanel, "CaseTitle", $"{ConditionLabel(_folder)} / ケース選択");
        SetNamed(_casePanel, "CaseCrumb", $"条件フォルダ > {ConditionLabel(_folder)} > ケース 1-10");
        SelectCase(_selectedCase);
    }

    void SelectCase(int index)
    {
        var mgr = ExperimentManager.Instance;
        int count = mgr != null ? mgr.CaseCount : 10;
        _selectedCase = Mathf.Clamp(index, 0, count - 1);
        if (_caseOutlines != null)
        {
            for (int i = 0; i < _caseOutlines.Length; i++)
            {
                if (_caseOutlines[i] != null) _caseOutlines[i].enabled = i == _selectedCase;
                if (_caseImages[i] != null)
                    _caseImages[i].color = i == _selectedCase
                        ? new Color(0.12f, 0.32f, 0.18f, 1f)
                        : new Color(0.16f, 0.18f, 0.22f, 1f);
            }
        }
        RefreshCaseDetails();
    }

    void RefreshCaseDetails()
    {
        var mgr = ExperimentManager.Instance;
        int seed = mgr != null ? mgr.GetSeed(_selectedCase) : _selectedCase;
        var spawner = FindFirstObjectByType<AGVSpawner>();
        int agvCount = spawner != null ? spawner.PreviewCount(seed) : 0;
        var loco = FindFirstObjectByType<PlayerLocomotion>();
        float walk = loco != null ? loco.MoveSpeed : 1.4f;

        if (_detailText != null)
        {
            _detailText.text =
                $"選択中: {ConditionLabel(_folder)} / ケース {_selectedCase + 1}\n" +
                $"seed: {seed}（このケースなら 3 条件で運動一致）\n" +
                $"Start=緑 Goal=赤 歩行 {walk:0.0} m/s AGV {agvCount} 台";
        }
        if (_startLabel != null)
            _startLabel.text = $"{ConditionLabel(_folder)} / ケース {_selectedCase + 1} を開始";
    }

    void StartSelectedCase()
    {
        Hide();
        var mgr = ExperimentManager.Instance ?? FindFirstObjectByType<ExperimentManager>();
        mgr?.BeginCase(_folder, _selectedCase);
    }

    public void Hide()
    {
        if (_menuRoot != null) _menuRoot.SetActive(false);
        ApplyCursor(false);
    }

    static void ApplyCursor(bool menuOpen)
    {
        Cursor.visible = true;
        Cursor.lockState = menuOpen ? CursorLockMode.None : Cursor.lockState;
        if (menuOpen)
            Cursor.lockState = CursorLockMode.None;
    }

    static string ConditionLabel(AGVPathVisualizer.VisMode mode) => mode switch
    {
        AGVPathVisualizer.VisMode.Baseline => "ベースライン",
        AGVPathVisualizer.VisMode.NoAR => "no-ar",
        AGVPathVisualizer.VisMode.Proposed => "提案手法",
        _ => mode.ToString(),
    };

    static Font _menuFont;

    static Font MenuFont()
    {
        if (_menuFont != null) return _menuFont;
        _menuFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 40);
        if (_menuFont == null)
            _menuFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        return _menuFont;
    }

    static Sprite _uiSprite;
    static Sprite UiSprite()
    {
        if (_uiSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            _uiSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _uiSprite.name = "ExperimentMenuWhite";
        }
        return _uiSprite;
    }

    static RectTransform CreateUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static RectTransform CreatePanel(Transform parent, string name, Vector2 size, Color color)
    {
        var rt = CreateUi(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UiSprite();
        img.color = color;
        img.raycastTarget = true;
        Center(rt, size, Vector2.zero);
        return rt;
    }

    static Text AddText(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize, FontStyle style, Color color)
    {
        var rt = CreateUi("Text", parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.text = text;
        t.font = MenuFont();
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.resizeTextForBestFit = false;
        t.supportRichText = false;
        Center(rt, size, pos);
        return t;
    }

    static void Center(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Mathf.Round(size.x), Mathf.Round(size.y));
        rt.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void SetNamed(RectTransform panel, string goName, string text)
    {
        if (panel == null) return;
        var tr = panel.Find(goName);
        var t = tr != null ? tr.GetComponent<Text>() : null;
        if (t != null) t.text = text;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
