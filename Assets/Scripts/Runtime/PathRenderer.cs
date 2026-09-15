using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 危険度に応じて経路（走行中は AGV 本体と同幅の長方形リボン）または停止線（同幅の短冊）を描画する。
/// 表示制限は水平距離 d ≤ D_max（FactoryLayout.DisplayDistanceMax）のみ。
/// 経路・停止線とも単一メッシュで描き、半透明の二重描画で濃くならない。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PathRenderer : MonoBehaviour
{
    public VehicleRiskCalculator risk;
    public AGVAgent agv;
    public LineRenderer stopLineRenderer;

    const float PATH_GROUND_CLEARANCE = 0.05f;
    const float DISPLAY_PLAYER_DISTANCE = FactoryLayout.DisplayDistanceMax;
    const float MinPointSpacing = 0.05f;
    static readonly Color BaselineColor = new Color(0.25f, 0.55f, 1f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;
    Mesh _mesh;
    Material _runtimeMat;
    MaterialPropertyBlock _mpb;
    float _cachedWidth = -1f;
    float _cachedHalfLength = -1f;
    int _lastPathVersion = int.MinValue;
    float _lastAlpha = -1f;
    Color _lastColor;
    bool _lastStopped;
    int _frameOffset;
    static readonly List<Vector3> _centers = new(64);
    static readonly List<Vector3> _verts = new(128);
    static readonly List<Vector3> _normals = new(128);
    static readonly List<int> _tris = new(256);
    static readonly Vector3[] _boundCorners = new Vector3[8];

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "AGV_PathRibbon" };
        _mesh.MarkDynamic();
        _meshFilter.sharedMesh = _mesh;
        _mpb = new MaterialPropertyBlock();
        _frameOffset = GetInstanceID() & 1;

        var legacy = GetComponent<LineRenderer>();
        if (legacy != null) legacy.enabled = false;

        EnsureMaterial();
    }

    void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_runtimeMat != null) Destroy(_runtimeMat);
    }

    void Update()
    {
        if (risk == null)
        {
            Hide();
            return;
        }

        var mode = AGVPathVisualizer.CurrentMode;
        if (mode == AGVPathVisualizer.VisMode.NoAR)
        {
            Hide();
            return;
        }

        // 複数 AGV のメッシュ再構築をフレーム分散して CPU スパイクを抑える。
        if (((Time.frameCount + _frameOffset) & 1) != 0 && _meshRenderer != null && _meshRenderer.enabled)
            return;

        bool modeVisible = mode == AGVPathVisualizer.VisMode.Baseline || risk.isVisible;
        Vector3[] path = agv != null ? agv.plannedPath : null;
        if (!modeVisible || path == null || path.Length < 2 || !IsWithinPlayerRange())
        {
            Hide();
            return;
        }

        Color color;
        float alpha;
        if (mode == AGVPathVisualizer.VisMode.Baseline)
        {
            color = BaselineColor;
            alpha = 1f;
        }
        else
        {
            (color, alpha) = RiskToVisualMapper.Map(risk.currentScore);
        }

        int sortingOrder = Mathf.RoundToInt(risk.currentScore * 100);
        _meshRenderer.sortingOrder = sortingOrder;

        bool stopped = risk.IsStopped;
        int pathVersion = path.Length * 397
            ^ path[0].GetHashCode()
            ^ path[path.Length - 1].GetHashCode()
            ^ (agv != null ? agv.transform.position.GetHashCode() : 0);
        if (_meshRenderer.enabled
            && pathVersion == _lastPathVersion
            && stopped == _lastStopped
            && Mathf.Abs(alpha - _lastAlpha) < 0.02f
            && ApproximatelyColor(color, _lastColor))
            return;

        _lastPathVersion = pathVersion;
        _lastStopped = stopped;
        _lastAlpha = alpha;
        _lastColor = color;

        if (stopped)
        {
            if (stopLineRenderer != null) stopLineRenderer.enabled = false;
            BuildStopBar(path, new Color(color.r, color.g, color.b, alpha));
        }
        else
        {
            if (stopLineRenderer != null) stopLineRenderer.enabled = false;
            BuildRibbon(path, new Color(color.r, color.g, color.b, alpha));
        }
    }

    static bool ApproximatelyColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.02f
            && Mathf.Abs(a.g - b.g) < 0.02f
            && Mathf.Abs(a.b - b.b) < 0.02f;
    }

    void Hide()
    {
        _lastPathVersion = int.MinValue;
        ClearRibbon();
        if (stopLineRenderer != null) stopLineRenderer.enabled = false;
    }

    void ClearRibbon()
    {
        if (_mesh == null) return;
        _mesh.Clear();
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }

    bool IsWithinPlayerRange()
    {
        Transform player = Camera.main != null ? Camera.main.transform : null;
        if (player == null) return false;

        Vector3 robot = agv != null ? agv.transform.position : transform.position;
        Vector3 a = robot; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        return Vector3.Distance(a, b) <= DISPLAY_PLAYER_DISTANCE;
    }

    /// <summary>
    /// 停止線は経路リボンと同じ半幅（AGV 本体幅）の短冊メッシュ。
    /// </summary>
    void BuildStopBar(Vector3[] path, Color color)
    {
        EnsureMaterial();
        if (!BuildCenterline(path) || _centers.Count < 1)
        {
            ClearRibbon();
            return;
        }

        float halfW = ResolveFootprint().halfWidth;
        const float halfDepth = 0.09f;
        Vector3 frontPos = _centers[0];
        Vector3 forwardDir = _centers.Count > 1
            ? FlatDir(_centers[1] - _centers[0])
            : GetFallbackForward();
        Vector3 perpendicular = Vector3.Cross(Vector3.up, forwardDir).normalized;

        Vector3 fl = frontPos - perpendicular * halfW - forwardDir * halfDepth;
        Vector3 fr = frontPos + perpendicular * halfW - forwardDir * halfDepth;
        Vector3 bl = frontPos - perpendicular * halfW + forwardDir * halfDepth;
        Vector3 br = frontPos + perpendicular * halfW + forwardDir * halfDepth;

        _verts.Clear();
        _tris.Clear();
        _verts.Add(transform.InverseTransformPoint(fl));
        _verts.Add(transform.InverseTransformPoint(fr));
        _verts.Add(transform.InverseTransformPoint(bl));
        _verts.Add(transform.InverseTransformPoint(br));
        _tris.Add(0);
        _tris.Add(2);
        _tris.Add(1);
        _tris.Add(1);
        _tris.Add(2);
        _tris.Add(3);

        ApplyMesh(color);
    }

    void BuildRibbon(Vector3[] path, Color color)
    {
        EnsureMaterial();
        if (!BuildCenterline(path) || _centers.Count < 2)
        {
            ClearRibbon();
            return;
        }

        float half = ResolveFootprint().halfWidth;
        _verts.Clear();
        _tris.Clear();

        for (int i = 0; i < _centers.Count; i++)
        {
            Vector3 tan;
            if (i == 0)
                tan = FlatDir(_centers[1] - _centers[0]);
            else if (i == _centers.Count - 1)
                tan = FlatDir(_centers[i] - _centers[i - 1]);
            else
            {
                Vector3 inT = FlatDir(_centers[i] - _centers[i - 1]);
                Vector3 outT = FlatDir(_centers[i + 1] - _centers[i]);
                tan = FlatDir(inT + outT);
                if (tan.sqrMagnitude < 1e-6f)
                    tan = outT;
            }

            Vector3 perp = Vector3.Cross(Vector3.up, tan).normalized;
            _verts.Add(transform.InverseTransformPoint(_centers[i] - perp * half));
            _verts.Add(transform.InverseTransformPoint(_centers[i] + perp * half));

            if (i < _centers.Count - 1)
            {
                int b = i * 2;
                _tris.Add(b);
                _tris.Add(b + 2);
                _tris.Add(b + 1);
                _tris.Add(b + 1);
                _tris.Add(b + 2);
                _tris.Add(b + 3);
            }
        }

        ApplyMesh(color);
    }

    void ApplyMesh(Color color)
    {
        _normals.Clear();
        for (int i = 0; i < _verts.Count; i++)
            _normals.Add(Vector3.up);

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_normals);
        _mesh.SetTriangles(_tris, 0, false);
        _mesh.RecalculateBounds();

        _mpb.Clear();
        if (_runtimeMat.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, color);
        if (_runtimeMat.HasProperty(ColorId))
            _mpb.SetColor(ColorId, color);
        _meshRenderer.SetPropertyBlock(_mpb);
        _meshRenderer.enabled = true;
    }

    bool BuildCenterline(Vector3[] path)
    {
        _centers.Clear();
        if (path == null || path.Length == 0)
            return false;

        float y = FactoryLayout.FloorY + PATH_GROUND_CLEARANCE;
        for (int i = 0; i < path.Length; i++)
        {
            var p = new Vector3(path[i].x, y, path[i].z);
            if (_centers.Count == 0
                || (p - _centers[_centers.Count - 1]).sqrMagnitude > MinPointSpacing * MinPointSpacing)
                _centers.Add(p);
        }

        if (_centers.Count == 0)
            return false;

        Vector3 forward = _centers.Count >= 2
            ? FlatDir(_centers[1] - _centers[0])
            : GetFallbackForward();
        _centers[0] += forward * ResolveFootprint().halfLength;

        for (int i = 1; i < _centers.Count;)
        {
            if ((_centers[i] - _centers[i - 1]).sqrMagnitude < MinPointSpacing * MinPointSpacing)
                _centers.RemoveAt(i);
            else
                i++;
        }

        if (_centers.Count == 1)
        {
            Vector3 tip = new Vector3(path[path.Length - 1].x, y, path[path.Length - 1].z);
            if ((tip - _centers[0]).sqrMagnitude > MinPointSpacing * MinPointSpacing)
                _centers.Add(tip);
            else
                _centers.Add(_centers[0] + forward * 0.5f);
        }

        return _centers.Count > 0;
    }

    static Vector3 FlatDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
    }

    Vector3 GetFallbackForward()
    {
        if (agv != null)
        {
            Vector3 fwd = agv.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f)
                return fwd.normalized;
        }
        Vector3 tfwd = transform.forward;
        tfwd.y = 0f;
        return tfwd.sqrMagnitude > 1e-4f ? tfwd.normalized : Vector3.forward;
    }

    (float halfWidth, float halfLength) ResolveFootprint()
    {
        if (_cachedWidth > 0f && _cachedHalfLength > 0f)
            return (_cachedWidth * 0.5f, _cachedHalfLength);

        float width = FactoryLayout.AgvFootprintM;
        float length = FactoryLayout.AgvFootprintM;
        Transform root = agv != null ? agv.transform : transform.parent;
        if (root != null && TryMeasureFootprint(root, out float w, out float len))
        {
            width = w;
            length = len;
        }

        _cachedWidth = Mathf.Max(0.05f, width);
        _cachedHalfLength = Mathf.Max(0.025f, length * 0.5f);
        return (_cachedWidth * 0.5f, _cachedHalfLength);
    }

    /// <summary>
    /// 経路幅は AGV 本体のみ。積載箱 (PlasticBox / Cargo) は含めない。
    /// </summary>
    static bool TryMeasureFootprint(Transform root, out float width, out float length)
    {
        width = 0f;
        length = 0f;
        var body = root.Find("Body");
        if (body != null)
        {
            width = Mathf.Abs(body.lossyScale.x);
            length = Mathf.Abs(body.lossyScale.z);
            return width > 0.05f && length > 0.05f;
        }

        // Palletrobot 本体メッシュを優先（タイヤより車体に合わせる）
        Renderer chassis = null;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is LineRenderer) continue;
            if (string.Equals(r.gameObject.name, "Palletrobot", System.StringComparison.OrdinalIgnoreCase))
            {
                chassis = r;
                break;
            }
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        bool any = false;

        if (chassis != null)
        {
            any = AccumulateRendererLocalXZ(root, chassis, ref minX, ref maxX, ref minZ, ref maxZ);
        }
        else
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is LineRenderer) continue;
                if (IsExcludedFromFootprint(r.gameObject.name)) continue;
                if (AccumulateRendererLocalXZ(root, r, ref minX, ref maxX, ref minZ, ref maxZ))
                    any = true;
            }
        }

        if (!any) return false;

        float localW = maxX - minX;
        float localL = maxZ - minZ;
        width = root.TransformVector(new Vector3(localW, 0f, 0f)).magnitude;
        length = root.TransformVector(new Vector3(0f, 0f, localL)).magnitude;
        return width > 0.05f && length > 0.05f;
    }

    static bool AccumulateRendererLocalXZ(
        Transform root, Renderer r,
        ref float minX, ref float maxX, ref float minZ, ref float maxZ)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 c = mb.center;
            Vector3 e = mb.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 lp = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = root.InverseTransformPoint(r.transform.TransformPoint(lp));
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.z < minZ) minZ = local.z;
                if (local.z > maxZ) maxZ = local.z;
            }
            return true;
        }

        Bounds b = r.bounds;
        FillBoundCorners(b);
        for (int i = 0; i < 8; i++)
        {
            Vector3 local = root.InverseTransformPoint(_boundCorners[i]);
            if (local.x < minX) minX = local.x;
            if (local.x > maxX) maxX = local.x;
            if (local.z < minZ) minZ = local.z;
            if (local.z > maxZ) maxZ = local.z;
        }
        return true;
    }

    static bool IsExcludedFromFootprint(string n) =>
        ContainsIgnoreCase(n, "Path")
        || ContainsIgnoreCase(n, "Stop")
        || ContainsIgnoreCase(n, "shadow")
        || ContainsIgnoreCase(n, "Cargo")
        || ContainsIgnoreCase(n, "PlasticBox")
        || ContainsIgnoreCase(n, "Box");

    static void FillBoundCorners(Bounds b)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        _boundCorners[0] = c + new Vector3(-e.x, -e.y, -e.z);
        _boundCorners[1] = c + new Vector3(-e.x, -e.y, e.z);
        _boundCorners[2] = c + new Vector3(-e.x, e.y, -e.z);
        _boundCorners[3] = c + new Vector3(-e.x, e.y, e.z);
        _boundCorners[4] = c + new Vector3(e.x, -e.y, -e.z);
        _boundCorners[5] = c + new Vector3(e.x, -e.y, e.z);
        _boundCorners[6] = c + new Vector3(e.x, e.y, -e.z);
        _boundCorners[7] = c + new Vector3(e.x, e.y, e.z);
    }

    static bool ContainsIgnoreCase(string s, string token) =>
        s.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;

    void EnsureMaterial()
    {
        if (_runtimeMat != null)
        {
            if (_meshRenderer.sharedMaterial != _runtimeMat)
                _meshRenderer.sharedMaterial = _runtimeMat;
            return;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Sprites/Default");
        _runtimeMat = new Material(shader) { name = "AGV_PathRibbonMat" };
        if (_runtimeMat.HasProperty("_Surface"))
        {
            _runtimeMat.SetFloat("_Surface", 1f);
            _runtimeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        if (_runtimeMat.HasProperty("_SrcBlend"))
            _runtimeMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (_runtimeMat.HasProperty("_DstBlend"))
            _runtimeMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_runtimeMat.HasProperty("_ZWrite"))
            _runtimeMat.SetInt("_ZWrite", 0);
        // 床面デカールは両面表示（巻き順ミスや斜め視でも欠けない）
        if (_runtimeMat.HasProperty("_Cull"))
            _runtimeMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        _runtimeMat.doubleSidedGI = true;
        _runtimeMat.renderQueue = 3000;
        if (_runtimeMat.HasProperty(BaseColorId))
            _runtimeMat.SetColor(BaseColorId, Color.white);
        if (_runtimeMat.HasProperty(ColorId))
            _runtimeMat.SetColor(ColorId, Color.white);
        _meshRenderer.sharedMaterial = _runtimeMat;
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
    }
}
