using UnityEngine;
using HRIRobot.Experiment;

namespace HRIRobot.Risk
{
    /// <summary>
    /// 危険度スコアに応じて経路（走行中は先細りの三角形状）または停止線を描画する。
    /// 仕様書 4.4〜4.6 準拠。複数車両の重なりは Sorting Order で危険度の高いものを
    /// 手前に描画することで、不透明度の実質的な max 合成を実現する（4.5）。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PathRenderer : MonoBehaviour
    {
        public VehicleRiskCalculator risk;
        [Tooltip("どの横断歩道に対する表示か（risk.crossingLines のインデックス）")]
        public int crossingLineIndex;
        public Vector3[] futurePathPoints;

        [Header("停止線用（車両の子オブジェクトに別途アタッチ）")]
        public LineRenderer stopLineRenderer;

        const float TAPER_WIDTH = 0.3f;
        const float STOP_LINE_HALF_WIDTH = 1.5f;

        LineRenderer lr;
        Gradient gradient;

        void Start()
        {
            lr = GetComponent<LineRenderer>();
            gradient = new Gradient();
        }

        void Update()
        {
            if (risk == null || risk.crossingLines == null || crossingLineIndex >= risk.crossingLines.Length)
                return;

            var line = risk.crossingLines[crossingLineIndex];

            if (line == null || !line.isVisible || futurePathPoints == null || futurePathPoints.Length == 0)
            {
                lr.enabled = false;
                if (stopLineRenderer != null) stopLineRenderer.enabled = false;
                return;
            }

            Color color;
            float alpha;
            bool conditionVisible;

            if (ExperimentConditionManager.Instance != null)
            {
                (color, alpha, conditionVisible) = ExperimentConditionManager.Instance.MapForCurrentCondition(line.currentScore);
            }
            else
            {
                (color, alpha) = RiskToVisualMapper.Map(line.currentScore);
                conditionVisible = true;
            }

            if (!conditionVisible)
            {
                lr.enabled = false;
                if (stopLineRenderer != null) stopLineRenderer.enabled = false;
                return;
            }

            int sortingOrder = Mathf.RoundToInt(line.currentScore * 100);

            if (risk.IsStopped)
            {
                lr.enabled = false;
                DrawStopLine(new Color(color.r, color.g, color.b, alpha), sortingOrder);
            }
            else
            {
                if (stopLineRenderer != null) stopLineRenderer.enabled = false;

                lr.enabled = true;
                lr.positionCount = futurePathPoints.Length;
                lr.SetPositions(futurePathPoints);
                lr.startWidth = TAPER_WIDTH; // 車両側：太い
                lr.endWidth = 0f;            // 前方：幅0（三角の先端）
                lr.sortingOrder = sortingOrder;

                gradient.SetKeys(
                    new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                    new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) }
                );
                lr.colorGradient = gradient;
            }
        }

        void DrawStopLine(Color color, int sortingOrder)
        {
            if (stopLineRenderer == null) return;

            stopLineRenderer.enabled = true;

            Vector3 frontPos = futurePathPoints[0];
            Vector3 forwardDir = futurePathPoints.Length > 1
                ? (futurePathPoints[1] - futurePathPoints[0]).normalized
                : transform.forward;
            Vector3 perpendicular = Vector3.Cross(Vector3.up, forwardDir).normalized;

            stopLineRenderer.positionCount = 2;
            stopLineRenderer.SetPosition(0, frontPos + perpendicular * STOP_LINE_HALF_WIDTH);
            stopLineRenderer.SetPosition(1, frontPos - perpendicular * STOP_LINE_HALF_WIDTH);
            stopLineRenderer.startColor = stopLineRenderer.endColor = color;
            stopLineRenderer.startWidth = stopLineRenderer.endWidth = 0.15f;
            stopLineRenderer.sortingOrder = sortingOrder;
        }
    }
}
