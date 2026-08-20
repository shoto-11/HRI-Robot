using UnityEngine;

/// <summary>
/// 参加者の視線方向へレイを飛ばし、棚などに当たるまでの太い基準線（XZ）を毎フレーム更新する。
/// </summary>
public class DynamicCrossingLineTracker : MonoBehaviour
{
    [Header("視線")]
    [SerializeField] Transform lookTransform;
    [Tooltip("レイ起点の高さ（lookTransform 未設定時、参加者位置からの Y オフセット）。")]
    [SerializeField] float rayOriginHeight = 1.6f;

    [Header("基準線")]
    [Tooltip("視線方向の太線の半幅（m）。全幅 = 2 × この値。")]
    [SerializeField] float lineHalfWidth = 0.5f;
    [SerializeField] float maxRayDistance = 40f;
    [SerializeField] LayerMask obstacleMask = ~0;

    [Header("デバッグ表示用（任意）")]
    public Transform lineStart, lineEnd;

    public float LineHalfWidth => lineHalfWidth;
    public Vector3 AxisStart { get; private set; }
    public Vector3 AxisEnd { get; private set; }

    void Start() => EnsureLineEnds();

    void Update()
    {
        Vector3 origin = GetRayOrigin();
        Vector3 forward = GetLookForwardXZ();
        Vector3 hitPoint = CastLookRay(origin, forward);

        Vector3 floor = FlattenToParticipantFloor(transform.position);
        AxisStart = floor;
        AxisEnd = FlattenToParticipantFloor(hitPoint);

        if (forward.sqrMagnitude < 1e-6f)
            forward = transform.forward;

        Vector3 perpendicular = Vector3.Cross(Vector3.up, forward).normalized;
        if (lineStart != null) lineStart.position = AxisEnd + perpendicular * lineHalfWidth;
        if (lineEnd != null) lineEnd.position = AxisEnd - perpendicular * lineHalfWidth;
    }

    Vector3 GetRayOrigin()
    {
        if (lookTransform != null)
            return lookTransform.position;
        var cam = Camera.main;
        if (cam != null) return cam.transform.position;
        return transform.position + Vector3.up * rayOriginHeight;
    }

    Vector3 GetLookForwardXZ()
    {
        Transform look = lookTransform != null ? lookTransform : Camera.main?.transform;
        Vector3 forward = look != null ? look.forward : transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
    }

    Vector3 CastLookRay(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return origin + Vector3.forward * maxRayDistance;

        var hits = Physics.RaycastAll(origin, direction, maxRayDistance, obstacleMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (IsUnderVisualizationOnly(hit.transform)) continue;
            return hit.point;
        }
        return origin + direction * maxRayDistance;
    }

    static bool IsUnderVisualizationOnly(Transform t)
    {
        int vizLayer = VisualizationLayers.SceneViewOnlyLayer;
        if (vizLayer < 0) return false;
        for (var cur = t; cur != null; cur = cur.parent)
            if (cur.gameObject.layer == vizLayer) return true;
        return false;
    }

    Vector3 FlattenToParticipantFloor(Vector3 p)
    {
        p.y = transform.position.y;
        return p;
    }

    void EnsureLineEnds()
    {
        if (lineStart == null)
        {
            var go = new GameObject("LookLineStart");
            go.transform.SetParent(transform, false);
            lineStart = go.transform;
        }
        if (lineEnd == null)
        {
            var go = new GameObject("LookLineEnd");
            go.transform.SetParent(transform, false);
            lineEnd = go.transform;
        }

        if (lookTransform == null)
        {
            var offset = transform.Find("Camera Offset/Main Camera");
            if (offset != null) lookTransform = offset;
            else if (Camera.main != null) lookTransform = Camera.main.transform;
        }
    }
}
