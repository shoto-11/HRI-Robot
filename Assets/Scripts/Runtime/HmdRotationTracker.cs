using UnityEngine;

/// <summary>ケース中の HMD ヨー回転量（絶対角変化の累積、度）を計測する。</summary>
[DisallowMultipleComponent]
public class HmdRotationTracker : MonoBehaviour
{
    [SerializeField] Transform hmdTransform;

    public float CumulativeYawDegrees { get; private set; }
    public bool IsTracking { get; private set; }

    float _lastYaw;
    bool _hasLast;

    void Start() => ResolveHmd();

    public void StartTracking()
    {
        ResetTracker();
        ResolveHmd();
        if (hmdTransform != null)
        {
            _lastYaw = hmdTransform.eulerAngles.y;
            _hasLast = true;
        }
        IsTracking = true;
    }

    public void StopTracking() => IsTracking = false;

    public void ResetTracker()
    {
        StopTracking();
        CumulativeYawDegrees = 0f;
        _hasLast = false;
    }

    void Update()
    {
        if (!IsTracking) return;
        if (hmdTransform == null && !ResolveHmd()) return;

        float yaw = hmdTransform.eulerAngles.y;
        if (!_hasLast)
        {
            _lastYaw = yaw;
            _hasLast = true;
            return;
        }

        CumulativeYawDegrees += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
        _lastYaw = yaw;
    }

    bool ResolveHmd()
    {
        if (hmdTransform != null) return true;
        if (Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
            return true;
        }
        var rig = FindFirstObjectByType<PlayerXrRig>();
        if (rig != null && rig.HeadCamera != null)
        {
            hmdTransform = rig.HeadCamera.transform;
            return true;
        }
        return false;
    }
}
