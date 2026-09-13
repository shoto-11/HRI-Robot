using UnityEngine;

/// <summary>
/// 人がいずれかの AGV 計画経路上にいるときの、人と AGV の水平最接近距離を記録する。
/// </summary>
[DisallowMultipleComponent]
public class PathApproachTracker : MonoBehaviour
{
    [SerializeField] float pathHalfWidth = FactoryLayout.YieldPathHalfWidthM;

    public float MinApproachOnPathM { get; private set; } = float.PositiveInfinity;
    public bool HadPedestrianOnPath { get; private set; }
    public bool IsTracking { get; private set; }

    Transform _player;

    public void StartTracking()
    {
        ResetTracker();
        ResolvePlayer();
        IsTracking = true;
    }

    public void StopTracking() => IsTracking = false;

    public void ResetTracker()
    {
        StopTracking();
        MinApproachOnPathM = float.PositiveInfinity;
        HadPedestrianOnPath = false;
    }

    void Update()
    {
        if (!IsTracking) return;
        if (_player == null && !ResolvePlayer()) return;

        Vector3 ped = _player.position;
        ped.y = 0f;

        foreach (var agv in FindObjectsByType<AGVAgent>(FindObjectsSortMode.None))
        {
            if (agv == null) continue;
            if (!IsPedestrianOnPlannedPath(agv, ped, pathHalfWidth)) continue;

            HadPedestrianOnPath = true;
            Vector3 agvPos = agv.transform.position;
            agvPos.y = 0f;
            float d = Vector3.Distance(agvPos, ped);
            if (d < MinApproachOnPathM)
                MinApproachOnPathM = d;
        }
    }

    bool ResolvePlayer()
    {
        var xr = GameObject.Find("XR Origin");
        if (xr != null) { _player = xr.transform; return true; }
        if (Camera.main != null) { _player = Camera.main.transform; return true; }
        return false;
    }

    /// <summary>残り計画経路の帯（半幅 pathHalfWidth）に人が乗っているか。</summary>
    public static bool IsPedestrianOnPlannedPath(AGVAgent agv, Vector3 pedFlat, float halfWidth)
    {
        var path = agv.plannedPath;
        if (path == null || path.Length < 2) return false;

        Vector3 start = agv.transform.position;
        start.y = 0f;
        pedFlat.y = 0f;

        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector3 a = (i == 0) ? start : path[i];
            Vector3 b = path[i + 1];
            a.y = 0f;
            b.y = 0f;
            if (DistancePointToSegmentXZ(pedFlat, a, b) <= halfWidth)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 進路前方（沿道距離 0〜stopDistance）に人が帯内でいるか。
    /// 戻り値: along = AGV から人投影点までの経路距離。
    /// </summary>
    public static bool TryGetPedestrianAheadOnPath(
        AGVAgent agv, Vector3 pedFlat, float halfWidth, float stopDistance,
        out float alongPath, out float lateral)
    {
        alongPath = 0f;
        lateral = float.MaxValue;
        var path = agv.plannedPath;
        if (path == null || path.Length < 2) return false;

        Vector3 cursor = agv.transform.position;
        cursor.y = 0f;
        pedFlat.y = 0f;
        float traveled = 0f;

        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector3 a = (i == 0) ? cursor : path[i];
            Vector3 b = path[i + 1];
            a.y = 0f;
            b.y = 0f;
            Vector3 ab = b - a;
            float segLen = ab.magnitude;
            if (segLen < 1e-4f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(pedFlat - a, ab) / (segLen * segLen));
            Vector3 proj = a + ab * t;
            float lat = Vector3.Distance(pedFlat, proj);
            float along = traveled + t * segLen;

            if (lat <= halfWidth && along > 0.05f && along <= stopDistance)
            {
                alongPath = along;
                lateral = lat;
                return true;
            }

            traveled += segLen;
            if (traveled > stopDistance) break;
        }
        return false;
    }

    static float DistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSq);
        return Vector3.Distance(p, a + ab * t);
    }
}
