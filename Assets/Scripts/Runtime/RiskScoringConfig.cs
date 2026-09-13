using UnityEngine;

/// <summary>
/// Proposed 条件の危険度モード。Inspector で切り替え、全 AGV の VehicleRiskCalculator が参照する。
/// 当面の実験は DistanceOnly（近接のみ）を既定とする。
/// </summary>
[DisallowMultipleComponent]
public class RiskScoringConfig : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>R = Rd のみ（距離）。</summary>
        DistanceOnly = 0,
        /// <summary>R = 0.55 Rp + 0.30 Rc + 0.15 Rd。</summary>
        Full = 1,
    }

    [Tooltip("DistanceOnly: 距離のみ / Full: PTTC+CTTC+近接")]
    [SerializeField] Mode scoringMode = Mode.DistanceOnly;

    public static Mode Current { get; private set; } = Mode.DistanceOnly;

    public Mode ScoringMode
    {
        get => scoringMode;
        set
        {
            scoringMode = value;
            Current = value;
        }
    }

    void Awake() => Current = scoringMode;

    void OnValidate()
    {
        if (Application.isPlaying)
            Current = scoringMode;
    }

    void OnEnable() => Current = scoringMode;
}
