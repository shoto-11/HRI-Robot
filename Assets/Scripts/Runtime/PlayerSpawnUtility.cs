using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>参加者スポーン位置を物理床／NavMesh に合わせる。</summary>
public static class PlayerSpawnUtility
{
    public static Vector3 ResolveSpawnPosition(Vector3 target, CharacterController cc, Transform ignoreRoot = null)
    {
        float footOffset = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
        Vector3 probe = new Vector3(target.x, target.y + 3f, target.z);

        if (TryRaycastGround(probe, ignoreRoot, out RaycastHit groundHit))
        {
            target.y = groundHit.point.y - footOffset;
            return target;
        }

        if (NavMesh.SamplePosition(probe, out NavMeshHit navHit, 4f, NavMesh.AllAreas))
        {
            target.y = navHit.position.y - footOffset;
            return target;
        }

        target.y = FactoryLayout.FloorY - footOffset;
        return target;
    }

    public static void ForceGrounded(CharacterController cc)
    {
        if (cc == null || !cc.enabled) return;
        for (int i = 0; i < 8 && !cc.isGrounded; i++)
            cc.Move(Vector3.down * 0.25f);
    }

    static bool TryRaycastGround(Vector3 from, Transform ignoreRoot, out RaycastHit best)
    {
        best = default;
        var hits = Physics.RaycastAll(from, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (ignoreRoot != null && hit.transform.IsChildOf(ignoreRoot)) continue;
            best = hit;
            return true;
        }
        return false;
    }
}
