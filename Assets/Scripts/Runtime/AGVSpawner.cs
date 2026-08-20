using System.Collections.Generic;
using UnityEngine;

/// <summary>シードから台数・速度を決定論的に算出し、10〜20 台の AGV を生成する。</summary>
public class AGVSpawner : MonoBehaviour
{
    [Header("台数")]
    [SerializeField] int minCount = 10;
    [SerializeField] int maxCount = 20;

    [Header("速度 (m/s)")]
    [SerializeField] float minSpeed = 1.0f;
    [SerializeField] float maxSpeed = 2.0f;

    [Header("乱数シード")]
    [Tooltip("実験メニューを使わないとき（単体 Play）に使うシード。実験中は ExperimentManager の Seeds が上書きする。")]
    [SerializeField] int seed = 42;

    [Header("見た目")]
    [SerializeField] GameObject agvPrefab;
    [Tooltip("Palletrobot 等に掛けるスケール。既定は約 1.0×0.2×1.0 m（幅×高×奥行）。")]
    [SerializeField] Vector3 agvVisualScale = new Vector3(1f / 1.5f, 0.2f / 0.13f, 1f / 1.5f);

    public int ActiveCount { get; private set; }
    Coroutine _respawn;
    Transform _agvRoot;

    Transform AgvRoot
    {
        get
        {
            if (_agvRoot == null)
            {
                var existing = transform.Find("AGVRoot");
                if (existing != null) _agvRoot = existing;
                else
                {
                    var go = new GameObject("AGVRoot");
                    go.transform.SetParent(transform, false);
                    _agvRoot = go.transform;
                }
            }
            return _agvRoot;
        }
    }

    void Awake()
    {
        if (BoxPickupPool.Instance == null)
            new GameObject("BoxPickupPool").AddComponent<BoxPickupPool>();
        if (AGVFleetOrchestrator.Instance == null)
            gameObject.AddComponent<AGVFleetOrchestrator>();
    }

    void Start()
    {
        if (FindFirstObjectByType<ExperimentManager>() != null)
            return;
        SpawnSession(seed);
    }

    public void ClearSession()
    {
        if (_respawn != null) StopCoroutine(_respawn);
        _respawn = null;
        var root = AgvRoot;
        for (int i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);
        ActiveCount = 0;
        AGVFleetOrchestrator.Instance?.BeginSession(null);
        FindFirstObjectByType<AGVPathVisualizer>()?.RefreshAgents();
    }

    public void RestartWithSeed(int newSeed)
    {
        seed = newSeed;
        BoxPickupPool.Instance?.ResetAll();
        ClearSession();
        _respawn = StartCoroutine(RespawnNextFrame());
    }

    System.Collections.IEnumerator RespawnNextFrame()
    {
        yield return null;
        SpawnSession(seed);
        FindFirstObjectByType<AGVPathVisualizer>()?.RefreshAgents();
        _respawn = null;
    }

    void SpawnSession(int sessionSeed)
    {
        BoxPickupPool.Instance?.Collect();
        int count = ResolveCount(sessionSeed);
        int added = BoxPickupPool.Instance != null ? BoxPickupPool.Instance.EnsureCount(count) : 0;
        if (added > 0)
            Debug.Log($"[AGVSpawner] AGV {count} 台に合わせて箱を {added} 個追加しました。");
        var agents = new List<AGVAgent>(count);
        for (int i = 0; i < count; i++)
            agents.Add(SpawnOne(i, count, sessionSeed));
        ActiveCount = count;
        AGVFleetOrchestrator.Instance?.BeginSession(agents);
        Debug.Log($"[AGVSpawner] seed={sessionSeed} で {count} 台を生成。");
    }

    public int PreviewCount(int sessionSeed) => ResolveCount(sessionSeed);

    int ResolveCount(int sessionSeed)
    {
        int min = Mathf.Clamp(Mathf.Min(minCount, maxCount), 1, 64);
        int max = Mathf.Clamp(Mathf.Max(minCount, maxCount), min, 64);
        if (min == max) return min;
        return min + (Mathf.Abs(sessionSeed) % (max - min + 1));
    }

    AGVAgent SpawnOne(int index, int total, int sessionSeed)
    {
        Vector3 pos = ResolveSpawn(index);
        GameObject root;
        if (agvPrefab != null)
        {
            root = Instantiate(agvPrefab, pos, Quaternion.identity, AgvRoot);
            root.name = $"AGV_{index + 1:D2}";
            root.transform.localScale = agvVisualScale;
            PrepareImportedAgv(root);
        }
        else
        {
            root = BuildPrimitiveAgv(index, pos);
        }

        var agent = root.GetComponent<AGVAgent>() ?? root.AddComponent<AGVAgent>();
        float spd = ResolveSpeed(index, sessionSeed);
        agent.Init(index, sessionSeed, spd);

        var risk = root.GetComponent<VehicleRiskCalculator>() ?? root.AddComponent<VehicleRiskCalculator>();
        risk.agv = agent;
        var tracker = FindFirstObjectByType<DynamicCrossingLineTracker>();
        if (tracker != null) risk.crossingLine = tracker;

        EnsurePathRenderers(root, risk, agent);
        return agent;
    }

    Vector3 ResolveSpawn(int index)
    {
        var zones = FactoryLayout.PickupZones;
        if (zones.Count == 0)
            return FactoryLayout.Flatten(FactoryLayout.StationA);

        var zone = zones[index % zones.Count];
        int ring = index / zones.Count;
        float jx = ((ring % 3) - 1) * 0.8f;
        float jz = (((ring / 3) % 3) - 1) * 0.8f;
        return FactoryLayout.Flatten(zone.Center + new Vector3(jx, 0f, jz));
    }

    float ResolveSpeed(int index, int sessionSeed)
    {
        var rng = new System.Random(unchecked(sessionSeed * 7919 + index * 104729));
        return minSpeed + (float)rng.NextDouble() * (maxSpeed - minSpeed);
    }

    static void PrepareImportedAgv(GameObject root)
    {
        try { root.tag = "AGV"; } catch { /* tag 未登録時は無視 */ }
        foreach (var anim in root.GetComponentsInChildren<Animator>(true))
            anim.enabled = false;
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        foreach (var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            agent.enabled = false;
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col != null) col.enabled = false;
        }
        DisableBlobShadows(root);
        StripMissingScripts(root);
        URPMaterialFixer.FixRenderers(root.GetComponentsInChildren<Renderer>(true));
    }

    static void StripMissingScripts(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
#if UNITY_EDITOR
            UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
#endif
        }
    }

    static void DisableBlobShadows(GameObject root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is LineRenderer) continue;
            if (!IsBlobShadow(r)) continue;
            r.enabled = false;
            r.gameObject.SetActive(false);
        }
    }

    static bool IsBlobShadow(Renderer r)
    {
        string n = r.gameObject.name.ToLowerInvariant();
        if (n.Contains("shadow")) return true;
        foreach (var m in r.sharedMaterials)
        {
            if (m != null && m.name.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    GameObject BuildPrimitiveAgv(int index, Vector3 pos)
    {
        var root = new GameObject($"AGV_{index + 1:D2}");
        root.transform.SetParent(AgvRoot);
        root.transform.position = pos;
        var mat = CreateMat(new Color(0.18f, 0.55f, 0.85f));
        AddCube("Body", root.transform, new Vector3(0f, 0.1f, 0f), new Vector3(1.0f, 0.2f, 1.0f), mat);
        var dark = CreateMat(new Color(0.12f, 0.12f, 0.14f));
        AddCube("WheelFL", root.transform, new Vector3(-0.4f, 0.04f, 0.4f), new Vector3(0.08f, 0.08f, 0.08f), dark);
        AddCube("WheelFR", root.transform, new Vector3(0.4f, 0.04f, 0.4f), new Vector3(0.08f, 0.08f, 0.08f), dark);
        AddCube("WheelRL", root.transform, new Vector3(-0.4f, 0.04f, -0.4f), new Vector3(0.08f, 0.08f, 0.08f), dark);
        AddCube("WheelRR", root.transform, new Vector3(0.4f, 0.04f, -0.4f), new Vector3(0.08f, 0.08f, 0.08f), dark);
        return root;
    }

    static void EnsurePathRenderers(GameObject root, VehicleRiskCalculator risk, AGVAgent agent)
    {
        var pathGo = root.transform.Find("PathLine");
        if (pathGo == null)
        {
            pathGo = new GameObject("PathLine").transform;
            pathGo.SetParent(root.transform, false);
            pathGo.gameObject.AddComponent<LineRenderer>();
        }
        var pr = pathGo.GetComponent<PathRenderer>() ?? pathGo.gameObject.AddComponent<PathRenderer>();
        pr.risk = risk;
        pr.agv = agent;

        var stopGo = root.transform.Find("StopLine");
        if (stopGo == null)
        {
            stopGo = new GameObject("StopLine").transform;
            stopGo.SetParent(root.transform, false);
            stopGo.gameObject.AddComponent<LineRenderer>();
        }
        pr.stopLineRenderer = stopGo.GetComponent<LineRenderer>();
        ConfigureLine(pathGo.GetComponent<LineRenderer>());
        ConfigureLine(pr.stopLineRenderer);
    }

    static void ConfigureLine(LineRenderer lr)
    {
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.numCapVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
            lr.sharedMaterial = new Material(shader);
        lr.enabled = false;
    }

    static void AddCube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(go.GetComponent<BoxCollider>());
    }

    static Material CreateMat(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = c };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        return mat;
    }
}
