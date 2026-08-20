using UnityEngine;
using SysRandom = System.Random;

/// <summary>
/// ピックアップ→滞在→ドロップ→滞在 を反復する地上 AGV。
/// VehicleRiskCalculator が参照する IsStopped / currentSpeed / plannedPath を公開する。
/// </summary>
[DisallowMultipleComponent]
public class AGVAgent : MonoBehaviour
{
    public const float TrajectoryStepSeconds = 0.1f;

    [Header("移動")]
    [Tooltip("Init 時に AGVSpawner の min/max Speed から上書きされる。")]
    [SerializeField] float speed = 0.8f;
    [SerializeField] float turnSpeed = 2.5f;

    public AGVPhase currentPhase;
    public bool IsStopped => currentPhase == AGVPhase.DwellAtPickup || currentPhase == AGVPhase.DwellAtDrop;
    public float currentSpeed;
    public Vector3[] plannedPath;

    public float MoveSpeed => speed;
    public float TurnSpeed => turnSpeed;
    public Vector3 Velocity => _velocity;
    public int Index => _agvIndex;
    public AGVMissionPlan ActivePlan { get; private set; }
    public int PathSegment { get; private set; }

    Vector3 _velocity;
    SysRandom _rng;
    int _agvIndex;
    int _missionOrdinal;
    Transform _cargoBox;
    Transform _pendingBox;
    Vector3 _boxOrigin;
    GameObject _cargoVisual;
    static Material _cargoMat;

    const string CargoVisualName = "CargoVisual";
    static readonly Vector3 CargoSize = new Vector3(0.70f, 0.42f, 0.55f);

    void Awake()
    {
        _rng = new SysRandom(0);
        currentPhase = AGVPhase.MovingToPickup;
        plannedPath = System.Array.Empty<Vector3>();
    }

    void OnDestroy()
    {
        AGVFleetOrchestrator.Instance?.Unregister(this);
        var pool = BoxPickupPool.Instance;
        if (_cargoBox != null)
        {
            SetRenderersEnabled(_cargoBox, true);
            pool?.Return(_cargoBox, _boxOrigin);
        }
        else if (_pendingBox != null)
            pool?.ReleaseReservation(_pendingBox);
    }

    public void Init(int index, int seed, float moveSpeed)
    {
        _agvIndex = index;
        speed = moveSpeed;
        _rng = new SysRandom(unchecked(seed * 1000 + index));
        _missionOrdinal = 0;
        transform.position = FactoryLayout.Flatten(transform.position);
    }

    public void SetSpeed(float value) => speed = value;

    public bool TryPlanMission(out AGVMissionPlan plan)
    {
        plan = null;
        var pool = BoxPickupPool.Instance;
        if (pool == null || pool.AvailableCount == 0)
        {
            EnterWaitingForCargo();
            return false;
        }

        var tried = new System.Collections.Generic.HashSet<Transform>();
        for (int attempt = 0; attempt < pool.AvailableCount; attempt++)
        {
            if (!pool.TryClaimNearest(transform.position, out Transform box) || box == null)
                break;
            if (!tried.Add(box))
            {
                pool.ReleaseReservation(box);
                continue;
            }

            _pendingBox = box;
            _boxOrigin = box.position;

            var drops = FactoryLayout.DropZones;
            for (int d = 0; d < drops.Count; d++)
            {
                var drop = drops[(Mathf.Abs(_rng.Next()) + d) % drops.Count];
                Vector3 dropPos = FactoryLayout.Flatten(drop.FloorCenter);
                Vector3[] toPickup = AGVRoutePlanner.CalculatePath(transform.position, box.position);
                Vector3[] toDrop = AGVRoutePlanner.CalculatePath(box.position, dropPos);
                if (toPickup.Length < 2 || toDrop.Length < 2)
                    continue;

                plan = new AGVMissionPlan
                {
                    pathToPickup = toPickup,
                    pathToDrop = toDrop,
                    dwellDurationAtPickup = 2.0f,
                    dwellDurationAtDrop = 2.0f,
                    Box = box,
                    PlacePos = dropPos,
                };
                ActivePlan = plan;
                PathSegment = 0;
                currentPhase = AGVPhase.MovingToPickup;
                _velocity = transform.forward * speed;
                RefreshPlannedPath();
                _missionOrdinal++;
                return true;
            }

            pool.ReleaseReservation(box);
            _pendingBox = null;
        }

        EnterWaitingForCargo();
        return false;
    }

    void EnterWaitingForCargo()
    {
        ActivePlan = null;
        PathSegment = 0;
        plannedPath = System.Array.Empty<Vector3>();
        currentSpeed = 0f;
        _velocity = Vector3.zero;
        currentPhase = AGVPhase.MovingToPickup;
    }

    public void TickMotion(float dt)
    {
        if (IsStopped || ActivePlan == null)
        {
            currentSpeed = 0f;
            _velocity = Vector3.zero;
            RefreshPlannedPath();
            return;
        }

        Vector3[] route = currentPhase == AGVPhase.MovingToDrop
            ? ActivePlan.pathToDrop
            : ActivePlan.pathToPickup;

        Vector3 pos = transform.position;
        int seg = PathSegment;
        AGVMotionSimulator.Advance(ref pos, ref _velocity, ref seg, route, speed, turnSpeed, dt);
        PathSegment = seg;
        transform.position = pos;
        if (_velocity.sqrMagnitude > 0.001f)
            transform.forward = new Vector3(_velocity.x, 0f, _velocity.z).normalized;
        currentSpeed = _velocity.magnitude;
        RefreshPlannedPath();
    }

    public bool HasArrivedCurrentLeg()
    {
        if (ActivePlan == null) return false;
        Vector3[] route = currentPhase == AGVPhase.MovingToDrop
            ? ActivePlan.pathToDrop
            : ActivePlan.pathToPickup;
        return AGVMotionSimulator.HasArrived(transform.position, route, PathSegment);
    }

    public void BeginDwellPickup()
    {
        currentPhase = AGVPhase.DwellAtPickup;
        currentSpeed = 0f;
        AttachBox();
        RefreshPlannedPath();
    }

    public void BeginMoveToDrop()
    {
        currentPhase = AGVPhase.MovingToDrop;
        PathSegment = 0;
        if (ActivePlan != null)
            ActivePlan.pathToDrop = AGVRoutePlanner.CalculatePath(transform.position, ActivePlan.PlacePos);
        PlaceCargoOnDeck();
        RefreshPlannedPath();
    }

    public void BeginDwellDrop()
    {
        currentPhase = AGVPhase.DwellAtDrop;
        currentSpeed = 0f;
        DropBox();
        RefreshPlannedPath();
    }

    public void EndMission()
    {
        EnterWaitingForCargo();
    }

    void AttachBox()
    {
        if (_pendingBox == null) return;
        BoxPickupPool.Instance?.ClaimSpecific(_pendingBox);
        _cargoBox = _pendingBox;
        _pendingBox = null;
        _cargoBox.SetParent(transform, false);
        _cargoBox.localPosition = Vector3.zero;
        _cargoBox.localRotation = Quaternion.identity;
        _cargoBox.localScale = Vector3.one;
        SetRenderersEnabled(_cargoBox, false);
        ShowCargoVisual(true);
    }

    void PlaceCargoOnDeck() => ShowCargoVisual(_cargoBox != null);

    void ShowCargoVisual(bool on)
    {
        if (_cargoVisual == null)
            _cargoVisual = BuildCargoVisual();

        float deck = MeasureDeckLocalY();
        _cargoVisual.transform.localPosition = new Vector3(0f, deck + CargoSize.y * 0.5f + 0.02f, 0f);
        _cargoVisual.transform.localRotation = Quaternion.identity;
        _cargoVisual.SetActive(on);
    }

    GameObject BuildCargoVisual()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = CargoVisualName;
        go.transform.SetParent(transform, false);
        go.transform.localScale = CargoSize;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = CargoMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    float MeasureDeckLocalY()
    {
        float y = 0.36f;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || r is LineRenderer) continue;
            if (r.gameObject == _cargoVisual) continue;
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("shadow") || n.Contains("pathline") || n.Contains("stopline")) continue;
            y = Mathf.Max(y, r.bounds.max.y - transform.position.y);
        }
        return y;
    }

    static void SetRenderersEnabled(Transform root, bool enabled)
    {
        if (root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is LineRenderer) continue;
            r.enabled = enabled;
        }
    }

    static Material CargoMaterial()
    {
        if (_cargoMat != null) return _cargoMat;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _cargoMat = new Material(shader) { name = "AGV_CargoBox" };
        var c = new Color(0.82f, 0.58f, 0.28f);
        _cargoMat.color = c;
        if (_cargoMat.HasProperty("_BaseColor")) _cargoMat.SetColor("_BaseColor", c);
        if (_cargoMat.HasProperty("_Smoothness")) _cargoMat.SetFloat("_Smoothness", 0.2f);
        return _cargoMat;
    }

    void DropBox()
    {
        ShowCargoVisual(false);
        if (_cargoBox == null || ActivePlan == null) return;
        SetRenderersEnabled(_cargoBox, true);
        BoxPickupPool.Instance?.Return(_cargoBox, ActivePlan.PlacePos + Vector3.up * 0.15f);
        _cargoBox = null;
    }

    void RefreshPlannedPath()
    {
        if (ActivePlan == null)
        {
            plannedPath = System.Array.Empty<Vector3>();
            return;
        }

        Vector3[] src = (currentPhase == AGVPhase.MovingToDrop || currentPhase == AGVPhase.DwellAtDrop)
            ? ActivePlan.pathToDrop
            : ActivePlan.pathToPickup;
        plannedPath = AGVRoutePlanner.TrimToRemainingRoute(src, transform.position);
    }
}
