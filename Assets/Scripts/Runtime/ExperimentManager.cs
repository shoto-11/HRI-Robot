using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance { get; private set; }

    [Header("ケースごとのシード")]
    [Tooltip("ケース 1〜10 で使う乱数シード。台数・速度・配置がこの値から決まる。")]
    [SerializeField] int[] seeds =
    {
        42, 137, 256, 512, 1024,
        777, 314, 999, 2048, 100
    };

    [Header("コース")]
    [SerializeField] Vector3 stationAPos = new Vector3(22f, 0.2f, 9f);
    [SerializeField] Vector3 stationBPos = new Vector3(9f, 0.2f, 58f);
    [SerializeField] float arrivalRadius = 3.0f;

    public int CaseCount => seeds != null && seeds.Length > 0 ? seeds.Length : 10;
    public int GetSeed(int caseIndex)
    {
        if (seeds == null || seeds.Length == 0) return caseIndex * 1000 + 1;
        if (caseIndex >= 0 && caseIndex < seeds.Length) return seeds[caseIndex];
        return caseIndex * 1000 + 1;
    }

    enum State { Idle, Intro, Running }
    State _state = State.Idle;

    int _caseIndex;
    AGVPathVisualizer.VisMode _currentMode;

    AGVSpawner _spawner;
    AGVPathVisualizer _visualizer;
    Transform _xrOrigin;
    GameObject _uiRoot;
    Text _mainText;
    Text _subText;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (GetComponent<URPMaterialFixer>() == null)
            gameObject.AddComponent<URPMaterialFixer>();
        stationAPos = FactoryLayout.PlayerSpawnPosition;
        stationBPos = FactoryLayout.StationB;
    }

    void Start()
    {
        FixStationAMarkerIfLegacy();
        _spawner = FindFirstObjectByType<AGVSpawner>();
        _visualizer = FindFirstObjectByType<AGVPathVisualizer>();
        _xrOrigin = GameObject.Find("XR Origin")?.transform ?? Camera.main?.transform.root;
        BuildUI();
        SetUIVisible(false);
    }

    void Update()
    {
        if (_state == State.Running || _state == State.Intro)
            CheckArrival();
    }

    public void BeginExperiment(AGVPathVisualizer.VisMode mode) => BeginCase(mode, 0);

    public void BeginCase(AGVPathVisualizer.VisMode mode, int caseIndex)
    {
        ExperimentStartMenu.Instance?.Hide();
        _currentMode = mode;
        _visualizer?.SetMode(mode);
        StartCase(caseIndex);
    }

    public void BeginFullSession()
    {
        Debug.LogWarning("[Experiment] フォルダ式メニューではケースを個別に選んでください。最初の条件のケース1から開始します。");
        BeginCase(AGVPathVisualizer.VisMode.Baseline, 0);
    }

    void StartCase(int index)
    {
        _caseIndex = Mathf.Clamp(index, 0, CaseCount - 1);
        _state = State.Intro;
        int seed = GetSeed(_caseIndex);
        TeleportPlayer(FactoryLayout.PlayerSpawnPosition);
        _spawner?.RestartWithSeed(seed);
        FindFirstObjectByType<PathDeviationTracker>()?.RecalculatePath();
        MeasurementHub.Instance?.OnCaseStart(_currentMode.ToString(), _caseIndex);
        ShowCaseStart();
        StartCoroutine(EndIntroAfter(3f));
    }

    IEnumerator EndIntroAfter(float sec)
    {
        yield return new WaitForSeconds(sec);
        if (_state == State.Intro)
        {
            _state = State.Running;
            SetUIVisible(false);
        }
    }

    void CheckArrival()
    {
        if (_xrOrigin == null) return;
        Vector3 p = _xrOrigin.position; p.y = 0f;
        float d = Vector3.Distance(p, new Vector3(stationBPos.x, 0f, stationBPos.z));
        if (d > arrivalRadius) return;

        _state = State.Idle;
        StopAllCoroutines();
        MeasurementHub.Instance?.OnCaseComplete(_currentMode.ToString(), _caseIndex);
        _spawner?.ClearSession();
        TeleportPlayer(FactoryLayout.PlayerSpawnPosition);
        SetUIVisible(false);
        var menu = ExperimentStartMenu.Instance ?? FindFirstObjectByType<ExperimentStartMenu>();
        menu?.ShowAfterCase(_currentMode, _caseIndex);
        Debug.Log($"[Experiment] ケース完了 → メニューへ戻ります ({_currentMode} / ケース {_caseIndex + 1})");
    }

    static void FixStationAMarkerIfLegacy()
    {
        var pillar = GameObject.Find("Station_A")?.transform.Find("Pillar");
        if (pillar == null) return;
        if (pillar.localPosition.sqrMagnitude > 0.05f) return;
        pillar.localPosition = new Vector3(-1.6f, 1.2f, -1.2f);
    }

    void TeleportPlayer(Vector3 pos)
    {
        if (_xrOrigin == null)
            _xrOrigin = GameObject.Find("XR Origin")?.transform ?? Camera.main?.transform.root;
        if (_xrOrigin == null) return;

        var cc = _xrOrigin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        pos = PlayerSpawnUtility.ResolveSpawnPosition(pos, cc, _xrOrigin);
        _xrOrigin.SetPositionAndRotation(pos, FactoryLayout.SpawnRotation);

        var cameraOffset = _xrOrigin.Find("Camera Offset");
        if (cameraOffset != null)
            cameraOffset.localRotation = Quaternion.identity;

        _xrOrigin.GetComponent<PlayerLocomotion>()?.ResetView();

        if (cc != null) cc.enabled = true;
        PlayerSpawnUtility.ForceGrounded(cc);
        Physics.SyncTransforms();
    }

    void BuildUI()
    {
        _uiRoot = new GameObject("ExperimentUI");
        _uiRoot.transform.SetParent(transform, false);
        var canvas = _uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _uiRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        var rt = _uiRoot.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(95f, 52f);
        rt.localScale = Vector3.one * 0.01f;
        var bg = new GameObject("BG");
        bg.transform.SetParent(_uiRoot.transform, false);
        bg.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.15f, 0.93f);
        bg.GetComponent<RectTransform>().sizeDelta = new Vector2(93f, 50f);
        _mainText = MakeText(_uiRoot.transform, "main", new Vector2(0f, 11f), new Vector2(89f, 22f), 8f, FontStyle.Bold);
        _subText = MakeText(_uiRoot.transform, "sub", new Vector2(0f, -12f), new Vector2(89f, 16f), 4f, FontStyle.Normal);
    }

    static Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, float fs, FontStyle style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = Mathf.RoundToInt(fs);
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return t;
    }

    void PositionUI()
    {
        Transform cam = Camera.main?.transform ?? _xrOrigin;
        if (cam == null) return;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();
        _uiRoot.transform.position = cam.position + fwd * 2.5f + Vector3.up * 0.1f;
        _uiRoot.transform.rotation = Quaternion.LookRotation(fwd);
    }

    void SetUIVisible(bool v)
    {
        if (_uiRoot == null)
            BuildUI();
        if (_uiRoot == null) return;
        _uiRoot.SetActive(v);
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (Instance == this) Instance = null;
    }

    void ShowCaseStart()
    {
        if (_uiRoot == null || _mainText == null || _subText == null)
            BuildUI();
        if (_mainText == null || _subText == null) return;
        _mainText.text = $"ケース  {_caseIndex + 1}  /  {CaseCount}";
        _subText.text = "Station A から Station B へ移動してください";
        PositionUI();
        SetUIVisible(true);
    }
}
