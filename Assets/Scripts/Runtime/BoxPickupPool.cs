using System.Collections.Generic;
using UnityEngine;

/// <summary>シーン内の箱を管理する。AGV が Claim / Return する。</summary>
public class BoxPickupPool : MonoBehaviour
{
    public static BoxPickupPool Instance { get; private set; }

    [SerializeField] string[] namePatterns = { "PickupBox", "PlasticBox" };

    readonly List<Transform> _available = new();
    readonly HashSet<Transform> _inUse = new();
    readonly Dictionary<Transform, (Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale)> _origin = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Collect();
    }

    public void Collect()
    {
        _available.Clear();
        _origin.Clear();
        foreach (var m in FindObjectsByType<CargoZoneMarker>(FindObjectsSortMode.InstanceID))
        {
            if (m == null || m.Kind != FactoryLayout.CargoZoneKind.Pickup || !m.EnabledForGameplay)
                continue;
            foreach (var box in m.GetPickupBoxes())
                TryRegister(box);
        }
        SortAvailable();
        foreach (var box in _available)
            PlayerWalkabilityUtility.DisableCollidersOn(box);
        Debug.Log($"[BoxPickupPool] 箱を {_available.Count} 個登録。");
    }

    void TryRegister(Transform t)
    {
        if (t == null || !Matches(t.name)) return;
        if (_available.Contains(t) || _inUse.Contains(t)) return;
        t.gameObject.isStatic = false;
        PlayerWalkabilityUtility.DisableCollidersOn(t);
        _available.Add(t);
        _origin[t] = (t.parent, t.localPosition, t.localRotation, t.localScale);
    }

    bool Matches(string n)
    {
        foreach (var p in namePatterns)
            if (n.Contains(p)) return true;
        return false;
    }

    void SortAvailable()
    {
        _available.Sort((a, b) =>
        {
            int cx = a.position.x.CompareTo(b.position.x);
            if (cx != 0) return cx;
            return a.position.z.CompareTo(b.position.z);
        });
    }

    public int AvailableCount => _available.Count;

    /// <summary>不足分の箱をピックアップゾーンに追加し、AGV 台数に揃える。</summary>
    public int EnsureCount(int needed)
    {
        if (needed <= _available.Count) return 0;

        var markers = FindPickupMarkers();
        Transform template = _available.Count > 0 ? _available[0] : FindAnyTemplateBox();
        int spawned = 0;
        int next = _available.Count;

        while (_available.Count < needed)
        {
            Transform parent = null;
            Vector3 worldPos;
            if (markers.Count > 0)
            {
                var marker = markers[next % markers.Count];
                parent = marker.transform;
                int ring = next / markers.Count;
                float jx = ((ring % 3) - 1) * 0.35f;
                float jz = (((ring / 3) % 3) - 1) * 0.35f;
                worldPos = marker.transform.TransformPoint(new Vector3(jx, 0.08f, jz));
            }
            else if (FactoryLayout.PickupZones.Count > 0)
            {
                var zone = FactoryLayout.PickupZones[next % FactoryLayout.PickupZones.Count];
                int ring = next / FactoryLayout.PickupZones.Count;
                float jx = ((ring % 3) - 1) * 0.35f;
                float jz = (((ring / 3) % 3) - 1) * 0.35f;
                worldPos = zone.FloorCenter + new Vector3(jx, 0.08f, jz);
            }
            else
            {
                worldPos = FactoryLayout.Flatten(FactoryLayout.StationA) + Vector3.right * (0.4f * next);
            }

            Transform box = SpawnBox(template, parent, worldPos, next + 1);
            TryRegister(box);
            next++;
            spawned++;
        }

        SortAvailable();
        return spawned;
    }

    static List<CargoZoneMarker> FindPickupMarkers()
    {
        var list = new List<CargoZoneMarker>();
        foreach (var m in FindObjectsByType<CargoZoneMarker>(FindObjectsSortMode.InstanceID))
        {
            if (m == null || m.Kind != FactoryLayout.CargoZoneKind.Pickup || !m.EnabledForGameplay)
                continue;
            list.Add(m);
        }
        return list;
    }

    Transform FindAnyTemplateBox()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t != null && Matches(t.name)) return t;
        }
        return null;
    }

    Transform SpawnBox(Transform template, Transform parent, Vector3 worldPos, int index)
    {
        GameObject go;
        if (template != null)
            go = Instantiate(template.gameObject, parent);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
            if (parent != null) go.transform.SetParent(parent, true);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        PlayerWalkabilityUtility.DisableCollidersOn(go.transform);

        go.name = $"PickupBox_{index:D2}";
        go.isStatic = false;
        go.SetActive(true);
        if (parent != null)
            go.transform.position = worldPos;
        else
            go.transform.SetPositionAndRotation(worldPos, Quaternion.identity);
        return go.transform;
    }

    public Transform ClaimForAgv(int index, Vector3 nearPos)
    {
        if (_available.Count == 0) return null;
        Transform best = null;
        float bestDist = float.MaxValue;
        int start = Mathf.Abs(index) % _available.Count;
        for (int i = 0; i < _available.Count; i++)
        {
            var candidate = _available[(start + i) % _available.Count];
            if (candidate == null) continue;
            float d = (candidate.position - nearPos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = candidate;
            }
        }
        return ClaimTransform(best);
    }

    public bool TryClaimNearest(Vector3 nearPos, out Transform box)
    {
        box = null;
        if (_available.Count == 0) return false;
        int bestIdx = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _available.Count; i++)
        {
            var candidate = _available[i];
            if (candidate == null) continue;
            float d = (candidate.position - nearPos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        box = ClaimTransform(_available[bestIdx]);
        return box != null;
    }

    public void ReleaseReservation(Transform box)
    {
        if (box == null || !_inUse.Contains(box)) return;
        RestoreOrigin(box);
        _inUse.Remove(box);
        if (!_available.Contains(box))
        {
            _available.Add(box);
            SortAvailable();
        }
    }

    public bool ClaimSpecific(Transform box)
    {
        if (box == null) return false;
        if (_inUse.Contains(box)) return true;
        if (!_available.Contains(box)) return false;
        ClaimTransform(box);
        return true;
    }

    Transform ClaimTransform(Transform best)
    {
        if (best == null) return null;
        _available.Remove(best);
        _inUse.Add(best);
        PlayerWalkabilityUtility.DisableCollidersOn(best);
        return best;
    }

    public void Return(Transform box, Vector3 placePos)
    {
        if (box == null) return;
        _inUse.Remove(box);
        box.SetParent(null);
        box.position = placePos;
        if (_origin.TryGetValue(box, out var o))
        {
            box.rotation = o.localRot;
            box.localScale = o.scale;
        }
        PlayerWalkabilityUtility.DisableCollidersOn(box);
        _available.Add(box);
        SortAvailable();
    }

    public void ResetAll()
    {
        foreach (var box in new List<Transform>(_inUse))
        {
            if (box == null) continue;
            RestoreOrigin(box);
            _available.Add(box);
        }
        _inUse.Clear();
        SortAvailable();
    }

    void RestoreOrigin(Transform box)
    {
        box.SetParent(null);
        if (_origin.TryGetValue(box, out var o))
        {
            box.SetParent(o.parent);
            box.localPosition = o.localPos;
            box.localRotation = o.localRot;
            box.localScale = o.scale;
        }
        PlayerWalkabilityUtility.DisableCollidersOn(box);
    }
}
