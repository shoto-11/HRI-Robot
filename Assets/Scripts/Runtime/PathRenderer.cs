using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 危険度に応じて経路（走行中は AGV 幅の長方形リボン）または停止線を描画する。
/// 表示制限は水平距離 d ≤ D_max（FactoryLayout.DisplayDistanceMax）のみ。
/// 曲がり角は単一メッシュのマイター接合で描くため、半透明の二重描画で濃くならない。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PathRenderer : MonoBehaviour
{
    public VehicleRiskCalculator risk;
    public AGVAgent agv;
    public LineRenderer stopLineRenderer;

    /// <summary>経路表示幅 = AGV 床面一辺（長方形・テーパーなし）。</summary>
    const float PathWidth = FactoryLayout.AgvFootprintM;
    const float PATH_GROUND_CLEARANCE = 0.05f;
    const float ROBOT_HALF_LENGTH = FactoryLayout.AgvFootprintM * 0.5f;
    const float STOP_LINE_HALF_WIDTH = 0.5f;
    const float DISPLAY_PLAYER_DISTANCE = FactoryLayout.DisplayDistanceMax;
    const float MinPointSpacing = 0.05f;
    const float MiterLimit = 2.5f;
    static readonly Color BaselineColor = new Color(0.25f, 0.55f, 1f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;
    Mesh _mesh;
    Material _runtimeMat;
    MaterialPropertyBlock _mpb;
    static readonly List<Vector3> _centers = new(64);
    static readonly List<Vector3> _verts = new(128);
    static readonly List<int> _tris = new(256);

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "AGV_PathRibbon" };
        _mesh.MarkDynamic();
        _meshFilter.sharedMesh = _mesh;
        _mpb = new MaterialPropertyBlock();

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

        if (risk.IsStopped)
        {
            ClearRibbon();
            DrawStopLine(new Color(color.r, color.g, color.b, alpha), path, sortingOrder);
        }
        else
        {
            if (stopLineRenderer != null) stopLineRenderer.enabled = false;
            BuildRibbon(path, new Color(color.r, color.g, color.b, alpha));
        }
    }

    void Hide()
    {
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

    void DrawStopLine(Color color, Vector3[] path, int sortingOrder)
    {
        if (stopLineRenderer == null) return;
        stopLineRenderer.enabled = true;
        Vector3[] display = BuildCenterline(path);
        if (display.Length < 1) return;
        Vector3 frontPos = display[0];
        Vector3 forwardDir = display.Length > 1
            ? FlatDir(display[1] - display[0])
            : GetFallbackForward();
        Vector3 perpendicular = Vector3.Cross(Vector3.up, forwardDir).normalized;
        stopLineRenderer.positionCount = 2;
        stopLineRenderer.SetPosition(0, frontPos + perpendicular * STOP_LINE_HALF_WIDTH);
        stopLineRenderer.SetPosition(1, frontPos - perpendicular * STOP_LINE_HALF_WIDTH);
        stopLineRenderer.startColor = stopLineRenderer.endColor = color;
        stopLineRenderer.startWidth = stopLineRenderer.endWidth = 0.18f;
        stopLineRenderer.sortingOrder = sortingOrder;
    }

    void BuildRibbon(Vector3[] path, Color color)
    {
        EnsureMaterial();
        Vector3[] centers = BuildCenterline(path);
        if (centers.Length < 2)
        {
            ClearRibbon();
            return;
        }

        float half = PathWidth * 0.5f;
        _verts.Clear();
        _tris.Clear();

        // centers はワールド座標。MeshFilter はローカル頂点なので InverseTransform する。
        // 三角の巻き順は上向き法線（+Y）になるよう CCW（上から見て）にする。
        for (int i = 0; i < centers.Length; i++)
        {
            Vector3 tan;
            if (i == 0)
                tan = FlatDir(centers[1] - centers[0]);
            else if (i == centers.Length - 1)
                tan = FlatDir(centers[i] - centers[i - 1]);
            else
            {
                Vector3 inT = FlatDir(centers[i] - centers[i - 1]);
                Vector3 outT = FlatDir(centers[i + 1] - centers[i]);
                tan = FlatDir(inT + outT);
                if (tan.sqrMagnitude < 1e-6f)
                    tan = outT;
            }

            Vector3 perp = Vector3.Cross(Vector3.up, tan).normalized;
            float miter = half;
            if (i > 0 && i < centers.Length - 1)
            {
                Vector3 inT = FlatDir(centers[i] - centers[i - 1]);
                Vector3 inPerp = Vector3.Cross(Vector3.up, inT).normalized;
                float cosHalf = Mathf.Clamp(Vector3.Dot(inPerp, perp), 0.25f, 1f);
                float scale = 1f / cosHalf;
                if (scale > MiterLimit) scale = MiterLimit;
                miter = half * scale;
            }

            Vector3 leftWorld = centers[i] - perp * miter;
            Vector3 rightWorld = centers[i] + perp * miter;
            _verts.Add(transform.InverseTransformPoint(leftWorld));
            _verts.Add(transform.InverseTransformPoint(rightWorld));

            if (i < centers.Length - 1)
            {
                int b = i * 2;
                // left0, left1, right0 / right0, left1, right1 → 法線 +Y
                _tris.Add(b);
                _tris.Add(b + 2);
                _tris.Add(b + 1);
                _tris.Add(b + 1);
                _tris.Add(b + 2);
                _tris.Add(b + 3);
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();

        _mpb.Clear();
        if (_runtimeMat.HasProperty(BaseColorId))
            _mpb.SetColor(BaseColorId, color);
        if (_runtimeMat.HasProperty(ColorId))
            _mpb.SetColor(ColorId, color);
        _meshRenderer.SetPropertyBlock(_mpb);
        _meshRenderer.enabled = true;
    }

    Vector3[] BuildCenterline(Vector3[] path)
    {
        _centers.Clear();
        if (path == null || path.Length == 0)
            return System.Array.Empty<Vector3>();

        float y = FactoryLayout.FloorY + PATH_GROUND_CLEARANCE;
        for (int i = 0; i < path.Length; i++)
        {
            var p = new Vector3(path[i].x, y, path[i].z);
            if (_centers.Count == 0
                || (p - _centers[_centers.Count - 1]).sqrMagnitude > MinPointSpacing * MinPointSpacing)
                _centers.Add(p);
        }

        if (_centers.Count == 0)
            return System.Array.Empty<Vector3>();

        Vector3 forward = _centers.Count >= 2
            ? FlatDir(_centers[1] - _centers[0])
            : GetFallbackForward();
        _centers[0] += forward * ROBOT_HALF_LENGTH;

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

        return _centers.ToArray();
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
