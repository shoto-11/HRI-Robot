using UnityEngine;

/// <summary>
/// 危険度に応じて経路（走行中は三角先端）または停止線を描画する。
/// プレイヤーから 10m 以内のロボットのみ表示する。経路そのものは残り全長。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PathRenderer : MonoBehaviour
{
    public VehicleRiskCalculator risk;
    public AGVAgent agv;
    public LineRenderer stopLineRenderer;

    const float TAPER_WIDTH = 0.65f;
    const float TIP_WIDTH = 0.12f;
    /// <summary>床面からの経路表示クリアランス。ロボット本体より低く、床付近に描画する。</summary>
    const float PATH_GROUND_CLEARANCE = 0.05f;
    const float ROBOT_HALF_LENGTH = FactoryLayout.AgvFootprintM * 0.5f;
    const float STOP_LINE_HALF_WIDTH = 0.5f; // 全幅 1.0 m
    const float DISPLAY_PLAYER_DISTANCE = FactoryLayout.DisplayDistanceMax;
    static readonly Color BaselineColor = new Color(0.25f, 0.55f, 1f);

    LineRenderer lr;
    Gradient gradient;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        gradient = new Gradient();
    }

    void Update()
    {
        if (risk == null)
        {
            Hide();
            return;
        }

        var mode = AGVPathVisualizer.CurrentMode;
        if (mode == AGVPathVisualizer.VisMode.NoAR)
        {
            Hide();
            return;
        }

        bool modeVisible = mode == AGVPathVisualizer.VisMode.Baseline || risk.isVisible;
        Vector3[] path = agv != null ? agv.plannedPath : null;
        if (!modeVisible || path == null || path.Length < 2 || !IsWithinPlayerRange())
        {
            Hide();
            return;
        }

        Color color;
        float alpha;
        if (mode == AGVPathVisualizer.VisMode.Baseline)
        {
            color = BaselineColor;
            alpha = 1f;
        }
        else
        {
            (color, alpha) = RiskToVisualMapper.Map(risk.currentScore);
        }

        int sortingOrder = Mathf.RoundToInt(risk.currentScore * 100);

        if (risk.IsStopped)
        {
            lr.enabled = false;
            DrawStopLine(new Color(color.r, color.g, color.b, alpha), path, sortingOrder);
        }
        else
        {
            if (stopLineRenderer != null) stopLineRenderer.enabled = false;
            lr.enabled = true;
            Vector3[] display = BuildDisplayPath(path);
            lr.positionCount = display.Length;
            lr.SetPositions(display);
            lr.startWidth = TAPER_WIDTH;
            lr.endWidth = TIP_WIDTH;
            lr.sortingOrder = sortingOrder;
            Color c0 = new Color(color.r, color.g, color.b, alpha);
            lr.startColor = c0;
            lr.endColor = c0;
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
            lr.colorGradient = gradient;
        }
    }

    void Hide()
    {
        lr.enabled = false;
        if (stopLineRenderer != null) stopLineRenderer.enabled = false;
    }

    bool IsWithinPlayerRange()
    {
        Transform player = Camera.main != null ? Camera.main.transform : null;
        if (player == null) return false;

        Vector3 robot = agv != null ? agv.transform.position : transform.position;
        Vector3 a = robot; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        return Vector3.Distance(a, b) <= DISPLAY_PLAYER_DISTANCE;
    }

    void DrawStopLine(Color color, Vector3[] path, int sortingOrder)
    {
        if (stopLineRenderer == null) return;
        stopLineRenderer.enabled = true;
        Vector3[] display = BuildDisplayPath(path);
        Vector3 frontPos = display[0];
        Vector3 forwardDir = display.Length > 1 ? (display[1] - display[0]).normalized : GetPathForward(display);
        Vector3 perpendicular = Vector3.Cross(Vector3.up, forwardDir).normalized;
        stopLineRenderer.positionCount = 2;
        stopLineRenderer.SetPosition(0, frontPos + perpendicular * STOP_LINE_HALF_WIDTH);
        stopLineRenderer.SetPosition(1, frontPos - perpendicular * STOP_LINE_HALF_WIDTH);
        stopLineRenderer.startColor = stopLineRenderer.endColor = color;
        stopLineRenderer.startWidth = stopLineRenderer.endWidth = 0.18f;
        stopLineRenderer.sortingOrder = sortingOrder;
    }

    Vector3[] BuildDisplayPath(Vector3[] path)
    {
        if (path == null || path.Length == 0)
            return path;

        float y = FactoryLayout.FloorY + PATH_GROUND_CLEARANCE;
        var display = new Vector3[path.Length];
        for (int i = 0; i < path.Length; i++)
            display[i] = new Vector3(path[i].x, y, path[i].z);

        Vector3 forward = GetPathForward(display);
        display[0] += forward * ROBOT_HALF_LENGTH;

        if (display.Length >= 2 && (display[1] - display[0]).sqrMagnitude < 0.04f)
        {
            var trimmed = new System.Collections.Generic.List<Vector3> { display[0] };
            for (int i = 1; i < display.Length; i++)
            {
                if ((trimmed[trimmed.Count - 1] - display[i]).sqrMagnitude > 0.04f)
                    trimmed.Add(display[i]);
            }
            if (trimmed.Count == 1 && path.Length > 0)
                trimmed.Add(new Vector3(path[path.Length - 1].x, y, path[path.Length - 1].z));
            return trimmed.ToArray();
        }

        return display;
    }

    Vector3 GetPathForward(Vector3[] display)
    {
        if (display.Length >= 2)
        {
            Vector3 dir = display[1] - display[0];
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
                return dir.normalized;
        }

        if (agv != null)
        {
            Vector3 fwd = agv.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f)
                return fwd.normalized;
        }

        Vector3 tfwd = transform.forward;
        tfwd.y = 0f;
        return tfwd.sqrMagnitude > 1e-4f ? tfwd.normalized : Vector3.forward;
    }
}
