using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>地上AGV向け NavMesh 経路計画。Y は FactoryLayout.AgvFloorY に固定。</summary>
public static class AGVRoutePlanner
{
    public const float MinSegDist = 0.15f;

    public static Vector3[] CalculatePath(Vector3 from, Vector3 to)
    {
        Vector3 a = FactoryLayout.Flatten(from);
        Vector3 b = FactoryLayout.Flatten(to);

        if (!NavMesh.SamplePosition(a, out var hitA, 4f, NavMesh.AllAreas)
            || !NavMesh.SamplePosition(b, out var hitB, 4f, NavMesh.AllAreas))
            return FlattenCorners(new[] { a, b });

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(hitA.position, hitB.position, NavMesh.AllAreas, path)
            || path.corners == null || path.corners.Length < 2)
            return FlattenCorners(new[] { a, b });

        return FlattenCorners(path.corners);
    }

    public static Vector3[] TrimToRemainingRoute(Vector3[] route, Vector3 currentPos)
    {
        if (route == null || route.Length == 0)
            return System.Array.Empty<Vector3>();

        Vector3 cur = FactoryLayout.Flatten(currentPos);
        if (route.Length == 1)
            return new[] { cur, FactoryLayout.Flatten(route[0]) };

        int bestSeg = 0;
        float bestDist = float.MaxValue;
        float bestT = 0f;
        for (int i = 0; i < route.Length - 1; i++)
        {
            Vector3 closest = ClosestOnSegment(cur, route[i], route[i + 1], out float t);
            float d = (cur - closest).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestSeg = i;
                bestT = t;
            }
        }

        int startIdx = bestT >= 0.95f ? bestSeg + 2 : bestSeg + 1;
        startIdx = Mathf.Clamp(startIdx, 0, route.Length);

        var result = new List<Vector3> { cur };
        for (int i = startIdx; i < route.Length; i++)
        {
            Vector3 p = FactoryLayout.Flatten(route[i]);
            if ((result[result.Count - 1] - p).sqrMagnitude > MinSegDist * MinSegDist)
                result.Add(p);
        }
        if (result.Count == 1)
            result.Add(FactoryLayout.Flatten(route[route.Length - 1]));
        return result.ToArray();
    }

    static Vector3[] FlattenCorners(Vector3[] corners)
    {
        var list = new List<Vector3>(corners.Length);
        foreach (var c in corners)
        {
            Vector3 p = FactoryLayout.Flatten(c);
            if (list.Count == 0 || (list[list.Count - 1] - p).sqrMagnitude > MinSegDist * MinSegDist)
                list.Add(p);
        }
        if (list.Count == 1) list.Add(list[0]);
        return list.ToArray();
    }

    static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
    {
        Vector3 ab = b - a;
        float mag = ab.sqrMagnitude;
        if (mag < 1e-8f) { t = 0f; return a; }
        t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / mag);
        return a + ab * t;
    }
}
