using UnityEngine;

/// <summary>通路オーバーレイ用レイヤー。プレイヤーカメラから除外する。</summary>
public static class VisualizationLayers
{
    public const string SceneViewOnlyLayerName = "AGVCorridorViz";

    public static int SceneViewOnlyLayer => LayerMask.NameToLayer(SceneViewOnlyLayerName);

    public static void SetLayerRecursive(GameObject go, int layer)
    {
        if (go == null || layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    public static void ApplySceneViewOnlyLayer(Transform root)
    {
        int layer = SceneViewOnlyLayer;
        if (root != null && layer >= 0)
            SetLayerRecursive(root.gameObject, layer);
    }

    public static void ExcludeFromPlayerCameras()
    {
        int layer = SceneViewOnlyLayer;
        if (layer < 0) return;
        int mask = ~(1 << layer);
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null) continue;
            if (!cam.CompareTag("MainCamera")) continue;
            cam.cullingMask &= mask;
        }
    }
}
