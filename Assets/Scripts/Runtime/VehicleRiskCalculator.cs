using UnityEngine;

/// <summary>
/// PTTC（相対速度）と path TTC の大きい方（比較時のみ PTTC×2）を 0.85、近接を 0.15 で合成する。
/// 経路表示可否は距離ゲート（d ≤ D_max）のみ。
/// </summary>
public class VehicleRiskCalculator : MonoBehaviour
{
    public DynamicCrossingLineTracker crossingLine;
    public AGVAgent agv;

    [Range(0f, 1f)] public float currentScore;
    public bool isVisible;

    public bool SkipScoring;

    [Header("融合（時間系の勝者 × wTime + 近接 × wProx）")]
    [SerializeField] float weightTime = 0.85f;
    [SerializeField] float weightProximity = 0.15f;
    [Tooltip("勝ち負け判定のみ。スコア本体には掛けない。2·Rp ≥ Rc なら Rp を採用。")]
    [SerializeField] float pttcCompareFactor = 2f;

    const float TTC_MAX = FactoryLayout.EhmiTtcMaxSeconds;
    const float DISPLAY_DISTANCE_MAX = FactoryLayout.DisplayDistanceMax;
    const float GAMMA = 0.6f;
    const float V_CLOSE_EPS = 0.05f;

    PlayerLocomotion _pedestrianLocomotion;

    public bool IsStopped => agv != null && agv.IsStopped;

    void Update()
    {
        if (crossingLine == null || agv == null)
        {
            isVisible = false;
            currentScore = 0f;
            return;
        }

        if (SkipScoring)
        {
            isVisible = true;
            currentScore = 1f;
            return;
        }

        Vector3 ped = crossingLine.transform.position;
        float d = HorizontalDistance(transform.position, ped);

        // 表示ゲートは距離のみ（T_max はスコア正規化専用）
        isVisible = d <= DISPLAY_DISTANCE_MAX;
        if (!isVisible)
        {
            currentScore = 0f;
            return;
        }

        float tp = ComputePttc(ped);
        float tc = ComputeTTCAlongPath(
            crossingLine.AxisStart,
            crossingLine.AxisEnd,
            crossingLine.LineHalfWidth);

        float rp = TimeToScore(tp);
        float rc = TimeToScore(tc);
        float rd = d >= DISPLAY_DISTANCE_MAX
            ? 0f
            : Mathf.Pow(Mathf.Clamp01(1f - d / DISPLAY_DISTANCE_MAX), GAMMA);

        // ×pttcCompareFactor は判定のみ。勝った方の素のスコアを使う。
        float timeRisk = (pttcCompareFactor * rp >= rc) ? rp : rc;
        currentScore = Mathf.Clamp01(weightTime * timeRisk + weightProximity * rd);
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    Vector3 GetPedestrianVelocity()
    {
        if (_pedestrianLocomotion == null && crossingLine != null)
            _pedestrianLocomotion = crossingLine.GetComponentInParent<PlayerLocomotion>();
        if (_pedestrianLocomotion != null)
            return _pedestrianLocomotion.HorizontalVelocity;
        return Vector3.zero;
    }

    /// <summary>
    /// 人への PTTC: d / v_close。
    /// v_close = r̂ · (v_AGV − v_ped)（相対速度の閉じ込み成分）。近づかないときは ∞。
    /// </summary>
    float ComputePttc(Vector3 pedestrianPos)
    {
        Vector3 r = pedestrianPos - transform.position;
        r.y = 0f;
        float dist = r.magnitude;
        if (dist < 1e-4f) return 0f;

        Vector3 vAgv = agv.Velocity;
        vAgv.y = 0f;
        Vector3 vPed = GetPedestrianVelocity();
        vPed.y = 0f;
        Vector3 vRel = vAgv - vPed;

        float vClose = Vector3.Dot(r.normalized, vRel);
        if (vClose < V_CLOSE_EPS) return Mathf.Infinity;

        return dist / vClose;
    }

    static float TimeToScore(float t)
    {
        if (float.IsInfinity(t) || t > TTC_MAX) return 0f;
        return Mathf.Pow(Mathf.Clamp01(1f - t / TTC_MAX), GAMMA);
    }

    float ComputeTTCAlongPath(Vector3 axisStart, Vector3 axisEnd, float halfWidth)
    {
        var path = agv.plannedPath;
        if (path == null || path.Length < 2) return Mathf.Infinity;

        int startIdx = FindClosestWaypointIndex(transform.position, path);
        float accumulatedDist = 0f;

        for (int i = startIdx; i < path.Length - 1; i++)
        {
            Vector3 a = (i == startIdx) ? transform.position : path[i];
            Vector3 b = path[i + 1];

            if (PathSegmentCrossesThickLine(a, b, axisStart, axisEnd, halfWidth, out Vector3 hit))
            {
                accumulatedDist += Vector3.Distance(a, hit);
                return accumulatedDist / Mathf.Max(agv.currentSpeed, 0.1f);
            }

            accumulatedDist += Vector3.Distance(a, b);
            if (accumulatedDist / Mathf.Max(agv.currentSpeed, 0.1f) > TTC_MAX) break;
        }
        return Mathf.Infinity;
    }

    static bool PathSegmentCrossesThickLine(Vector3 p1, Vector3 p2, Vector3 axisStart, Vector3 axisEnd, float halfWidth, out Vector3 hit)
    {
        if (SegmentsIntersect(p1, p2, axisStart, axisEnd, out hit))
            return true;

        Vector3 axis = axisEnd - axisStart;
        if (axis.sqrMagnitude < 1e-8f) return false;

        Vector3 perp = Vector3.Cross(Vector3.up, axis.normalized).normalized * halfWidth;
        if (SegmentsIntersect(p1, p2, axisStart + perp, axisEnd + perp, out hit)) return true;
        if (SegmentsIntersect(p1, p2, axisStart - perp, axisEnd - perp, out hit)) return true;

        return SegmentPairWithinDistance(p1, p2, axisStart, axisEnd, halfWidth, out hit);
    }

    static bool SegmentPairWithinDistance(
        Vector3 p1, Vector3 p2, Vector3 a, Vector3 b, float maxDist, out Vector3 hit)
    {
        SegmentSegmentDistanceSqXZ(p1, p2, a, b, out float distSq, out Vector3 onAgv, out _);
        if (distSq > maxDist * maxDist) { hit = p1; return false; }
        hit = onAgv;
        return true;
    }

    static void SegmentSegmentDistanceSqXZ(
        Vector3 p1, Vector3 p2, Vector3 a, Vector3 b,
        out float distSq, out Vector3 closestOnAgv, out Vector3 closestOnAxis)
    {
        Vector2 p = new(p1.x, p1.z), r = new(p2.x - p1.x, p2.z - p1.z);
        Vector2 q = new(a.x, a.z), s = new(b.x - a.x, b.z - a.z);
        float rLenSq = r.sqrMagnitude;
        float sLenSq = s.sqrMagnitude;

        if (rLenSq < 1e-8f && sLenSq < 1e-8f)
        {
            closestOnAgv = p1;
            closestOnAxis = a;
            distSq = (p - q).sqrMagnitude;
            return;
        }

        float t = 0f, u = 0f;
        if (rLenSq < 1e-8f)
        {
            t = 0f;
            u = Mathf.Clamp01(Vector2.Dot(p - q, s) / sLenSq);
        }
        else if (sLenSq < 1e-8f)
        {
            u = 0f;
            t = Mathf.Clamp01(Vector2.Dot(q - p, r) / rLenSq);
        }
        else
        {
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-8f)
            {
                t = 0f;
                u = Mathf.Clamp01(Vector2.Dot(p - q, s) / sLenSq);
            }
            else
            {
                t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / denom;
                u = ((q.x - p.x) * r.y - (q.y - p.y) * r.x) / denom;
                t = Mathf.Clamp01(t);
                u = Mathf.Clamp01(u);
            }
        }

        Vector2 cp = p + t * r;
        Vector2 cq = q + u * s;
        closestOnAgv = new Vector3(cp.x, p1.y, cp.y);
        closestOnAxis = new Vector3(cq.x, a.y, cq.y);
        distSq = (cp - cq).sqrMagnitude;
    }

    static bool SegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 hit)
    {
        hit = Vector3.zero;
        Vector2 a = new Vector2(p1.x, p1.z), b = new Vector2(p2.x, p2.z);
        Vector2 c = new Vector2(p3.x, p3.z), d = new Vector2(p4.x, p4.z);

        Vector2 r = b - a, s = d - c;
        float denom = r.x * s.y - r.y * s.x;
        if (Mathf.Abs(denom) < 0.0001f) return false;

        float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denom;
        float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denom;

        if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
        {
            Vector2 p = a + t * r;
            hit = new Vector3(p.x, p1.y, p.y);
            return true;
        }
        return false;
    }

    int FindClosestWaypointIndex(Vector3 pos, Vector3[] path)
    {
        int best = 0; float bestDist = float.MaxValue;
        for (int i = 0; i < path.Length; i++)
        {
            float dist = Vector3.Distance(pos, path[i]);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }
}
