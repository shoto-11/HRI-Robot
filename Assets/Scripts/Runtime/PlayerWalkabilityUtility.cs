using UnityEngine;

/// <summary>参加者の CharacterController が床の荷物に引っかからず、棚には当たるようにする。</summary>
public static class PlayerWalkabilityUtility
{
    static readonly string[] KeepEnvironmentPatterns =
    {
        "floor", "ground", "building", "wall", "sunoko", "xr origin",
    };

    public static void ApplyWalkabilityRules()
    {
        EnsureShelfBlockingColliders();
        StripLooseCargoColliders();
    }

    public static void StripDecorativeColliders() => ApplyWalkabilityRules();

    public static void DisableCollidersOn(Transform root)
    {
        if (root == null) return;
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || col.isTrigger) continue;
            if (IsShelfStructure(col)) continue;
            col.enabled = false;
        }
    }

    static void EnsureShelfBlockingColliders()
    {
        int enabled = 0;
        int added = 0;

        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (col == null || col.isTrigger) continue;
            if (!IsShelfStructure(col)) continue;
            if (!col.enabled)
            {
                col.enabled = true;
                enabled++;
            }
        }

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.name != "Shelf") continue;
            if (t.parent == null || t.parent.name != "Shelf") continue;
            if (t.GetComponentInChildren<Collider>(true) != null) continue;
            if (TryAddShelfBoundsCollider(t))
                added++;
        }

        if (enabled > 0 || added > 0)
            Debug.Log($"[PlayerWalkability] 棚コライダー: 再有効化 {enabled}, 追加 {added}");
    }

    static void StripLooseCargoColliders()
    {
        int stripped = 0;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (col == null || col.isTrigger) continue;
            if (col.GetComponent<CharacterController>() != null) continue;
            if (IsShelfStructure(col)) continue;
            if (!ShouldStripLooseCargo(col)) continue;
            if (!col.enabled) continue;
            col.enabled = false;
            stripped++;
        }

        if (stripped > 0)
            Debug.Log($"[PlayerWalkability] 床の荷物コライダーを {stripped} 個無効化しました。");
    }

    static bool ShouldStripLooseCargo(Collider col)
    {
        string n = col.gameObject.name.ToLowerInvariant();
        foreach (var keep in KeepEnvironmentPatterns)
        {
            if (n.Contains(keep))
                return false;
        }

        if (IsLooseCargo(col))
            return true;

        return false;
    }

    static bool IsLooseCargo(Collider col)
    {
        if (HasAncestorNamed(col.transform, "=== Cargo on Shelves ==="))
            return true;
        if (col.GetComponentInParent<CargoZoneMarker>() != null)
            return true;

        string self = col.gameObject.name;
        if (self.Contains("PickupBox") || self.Contains("PlasticBox"))
            return true;

        Transform root = col.transform.root;
        string rootName = root.name;
        if (rootName.StartsWith("Cardboard_") && rootName != "Cardboard_Shelf")
            return true;
        if (rootName.StartsWith("PlasticBox"))
            return true;

        return false;
    }

    static bool IsShelfStructure(Collider col) => IsShelfStructureTransform(col.transform);

    static bool IsShelfStructureTransform(Transform t)
    {
        while (t != null)
        {
            string n = t.name;
            if (n == "Pallet_Shelf" || n == "Cardboard_Shelf")
                return true;
            if (n == "Shelf" && t.parent != null && t.parent.name == "Shelf")
                return true;
            if (n == "Shelf" && HasAncestorNamed(t, "Factory_Environment"))
                return true;
            t = t.parent;
        }
        return false;
    }

    static bool HasAncestorNamed(Transform t, string name)
    {
        while (t != null)
        {
            if (t.name == name) return true;
            t = t.parent;
        }
        return false;
    }

    static bool TryAddShelfBoundsCollider(Transform shelfUnit)
    {
        var rends = shelfUnit.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return false;

        Bounds world = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            world.Encapsulate(rends[i].bounds);

        var box = shelfUnit.GetComponent<BoxCollider>();
        if (box == null)
            box = shelfUnit.gameObject.AddComponent<BoxCollider>();

        Vector3 lossy = shelfUnit.lossyScale;
        box.center = shelfUnit.InverseTransformPoint(world.center);
        box.size = new Vector3(
            world.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.001f),
            world.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.001f),
            world.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.001f));
        box.enabled = true;
        return true;
    }
}
