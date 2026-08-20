using UnityEngine;

/// <summary>
/// 単一の動的基準線に対する TTC と危険度スコア。
/// </summary>
public class VehicleRiskCalculator : MonoBehaviour
{
    public DynamicCrossingLineTracker crossingLine;
    public AGVAgent agv;

    [Range(0f, 1f)] public float currentScore;
    public bool isVisible;

    public bool SkipScoring;

    const float TTC_MAX = FactoryLayout.EhmiTtcMaxSeconds;
    const float DISPLAY_DISTANCE_MAX = FactoryLayout.DisplayDistanceMax;
    const float GAMMA = 0.6f;
    const float SCORE_FLOOR = 0.08f;

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

        float distToPedestrian = Vector3.Distance(transform.position, crossingLine.transform.position);
        float ttc = ComputeTTCAlongPath(
            crossingLine.AxisStart,
            crossingLine.AxisEnd,
            crossingLine.LineHalfWidth);

        bool ttcVisible = !float.IsInfinity(ttc) && ttc <= TTC_MAX;
        bool distanceVisible = distToPedestrian <= DISPLAY_DISTANCE_MAX;
        isVisible = ttcVisible || distanceVisible;

        if (!isVisible)
        {
            currentScore = 0f;
            return;
        }

        float rTtc = float.IsInfinity(ttc)
            ? 0f
            : Mathf.Pow(Mathf.Clamp01(1f - ttc / TTC_MAX), GAMMA);

        float rProx = distToPedestrian >= DISPLAY_DISTANCE_MAX
            ? 0f
            : Mathf.Pow(Mathf.Clamp01(1f - distToPedestrian / DISPLAY_DISTANCE_MAX), GAMMA);

        currentScore = Mathf.Max(SCORE_FLOOR, Mathf.Max(rTtc, rProx));
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
            float d = Vector3.Distance(pos, path[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }
}
