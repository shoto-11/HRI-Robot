using UnityEngine;

/// <summary>地上移動シミュレータ。高度ジッタは持たず Y を固定する。</summary>
public static class AGVMotionSimulator
{
    public const float DefaultStep = 0.1f;
    const float ArrivalDist = 0.35f;

    public static void Advance(
        ref Vector3 pos, ref Vector3 vel, ref int seg,
        Vector3[] route, float speed, float turnSpeed, float dt)
    {
        if (route == null || route.Length == 0 || seg >= route.Length) return;

        Vector3 target = FactoryLayout.Flatten(route[seg]);
        Vector3 to = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
        float dist = to.magnitude;

        if (dist < ArrivalDist)
        {
            pos = new Vector3(target.x, FactoryLayout.AgvFloorY, target.z);
            vel = Vector3.zero;
            if (seg < route.Length - 1) seg++;
            return;
        }

        Vector3 desired = to.normalized;
        Vector3 cur = new Vector3(vel.x, 0f, vel.z);
        cur = cur.sqrMagnitude > 0.001f ? cur.normalized : desired;
        Vector3 dir = Vector3.Slerp(cur, desired, Mathf.Clamp01(turnSpeed * dt));
        vel = dir * speed;
        pos = FactoryLayout.Flatten(pos + vel * dt);
    }

    public static bool HasArrived(Vector3 pos, Vector3[] route, int seg)
    {
        if (route == null || route.Length == 0) return true;
        if (seg < route.Length - 1) return false;
        Vector3 t = FactoryLayout.Flatten(route[route.Length - 1]);
        float dx = pos.x - t.x;
        float dz = pos.z - t.z;
        return dx * dx + dz * dz <= ArrivalDist * ArrivalDist;
    }
}
