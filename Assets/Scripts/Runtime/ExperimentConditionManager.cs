using UnityEngine;

namespace HRIRobot.Experiment
{
    /// <summary>
    /// 比較条件（仕様書 6.1）を切り替える。被験者内・ラテン方格でのカウンターバランスを
    /// 想定し、試行ごとに Condition を設定してからシーンを開始する。
    /// </summary>
    public enum EHMICondition
    {
        NoEHMI = 0,             // 1. eHMIなし（ベースライン）
        PathOnlyNoRisk = 1,     // 2. 経路可視化のみ（危険度符号化なし、単色・一定不透明度）
        DiscreteColorOnly = 2,  // 3. 危険度連動・離散色のみ（3段階、不透明度固定）
        FullGradient = 3,       // 4. 危険度連動・色＋不透明度グラデーション（本提案）
    }

    public class ExperimentConditionManager : MonoBehaviour
    {
        public static ExperimentConditionManager Instance { get; private set; }

        [Header("現在の試行条件")]
        public EHMICondition currentCondition = EHMICondition.FullGradient;

        [Header("条件2用: 経路可視化のみの固定表示")]
        public Color pathOnlyColor = new Color(0.6f, 0.7f, 0.8f);
        [Range(0f, 1f)] public float pathOnlyAlpha = 0.5f;

        [Header("条件3用: 離散色の固定不透明度")]
        [Range(0f, 1f)] public float discreteAlpha = 0.6f;

        void Awake()
        {
            Instance = this;
        }

        /// <summary>現在の条件に応じて危険度スコアを色・不透明度へ変換する。</summary>
        public (Color color, float alpha, bool visible) MapForCurrentCondition(float riskScore)
        {
            switch (currentCondition)
            {
                case EHMICondition.NoEHMI:
                    return (Color.clear, 0f, false);

                case EHMICondition.PathOnlyNoRisk:
                    return (pathOnlyColor, pathOnlyAlpha, true);

                case EHMICondition.DiscreteColorOnly:
                    return (DiscreteColor(riskScore), discreteAlpha, true);

                case EHMICondition.FullGradient:
                default:
                    var (color, alpha) = HRIRobot.Risk.RiskToVisualMapper.Map(riskScore);
                    return (color, alpha, true);
            }
        }

        static Color DiscreteColor(float r)
        {
            // 緑/黄/赤の3段階（色覚多様性配慮が不要な比較条件用のベースライン表現）。
            if (r < 0.34f) return new Color(0.2f, 0.6f, 0.9f); // 青（安全）
            if (r < 0.67f) return new Color(1f, 0.85f, 0.2f);  // 黄（注意）
            return new Color(0.9f, 0.15f, 0.15f);              // 赤（危険）
        }
    }
}
