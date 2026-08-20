using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 実験 3 条件の経路可視化切替。
/// Baseline: 単色・不透明度 1.0 / No-AR: 非表示 / Proposed: 危険度連動。
/// </summary>
public class AGVPathVisualizer : MonoBehaviour
{
    public enum VisMode { Baseline, NoAR, Proposed }

    public static VisMode CurrentMode { get; private set; } = VisMode.NoAR;

    [Header("経路表示")]
    [Tooltip("Play 開始時の条件。1: Baseline / 2: NoAR / 3: Proposed。実験メニューからも切り替え可。")]
    [SerializeField] VisMode mode = VisMode.NoAR;

    void Awake()
    {
        CurrentMode = mode;
        ApplyMode();
    }

    void Update()
    {
        if (ExperimentStartMenu.Instance != null && ExperimentStartMenu.Instance.IsVisible) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) SetMode(VisMode.Baseline);
        else if (kb.digit2Key.wasPressedThisFrame) SetMode(VisMode.NoAR);
        else if (kb.digit3Key.wasPressedThisFrame) SetMode(VisMode.Proposed);
    }

    public void SetMode(VisMode newMode)
    {
        mode = newMode;
        CurrentMode = newMode;
        ApplyMode();
        Debug.Log($"[AGVPathVisualizer] モード → {mode}");
    }

    public void RefreshAgents() => ApplyMode();

    void ApplyMode()
    {
        CurrentMode = mode;
        bool proposed = mode == VisMode.Proposed;
        bool baseline = mode == VisMode.Baseline;
        foreach (var risk in FindObjectsByType<VehicleRiskCalculator>(FindObjectsSortMode.None))
        {
            risk.enabled = proposed || baseline;
            risk.SkipScoring = baseline;
            if (baseline)
            {
                risk.isVisible = true;
                risk.currentScore = 1f;
            }
        }
    }
}
