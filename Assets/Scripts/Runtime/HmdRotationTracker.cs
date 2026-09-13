using UnityEngine;

/// <summary>
/// ケース中の HMD ヨー（左右）・ピッチ（上下）回転量を、
/// それぞれ絶対角変化の累積（度）として計測する。
/// </summary>
[DisallowMultipleComponent]
public class HmdRotationTracker : MonoBehaviour
{
    [SerializeField] Transform hmdTransform;

    public float CumulativeYawDegrees { get; private set; }
    public float CumulativePitchDegrees { get; private set; }
    public bool IsTracking { get; private set; }

    float _lastYaw;
    float _lastPitch;
    bool _hasLast;

    void Start() => ResolveHmd();

    public void StartTracking()
    {
        ResetTracker();
        ResolveHmd();
        if (hmdTransform != null)
        {
            CaptureAngles(out _lastYaw, out _lastPitch);
            _hasLast = true;
        }
        IsTracking = true;
    }

    public void StopTracking() => IsTracking = false;

    public void ResetTracker()
    {
        StopTracking();
        CumulativeYawDegrees = 0f;
        CumulativePitchDegrees = 0f;
        _hasLast = false;
    }

    void Update()
    {
        if (!IsTracking) return;
        if (hmdTransform == null && !ResolveHmd()) return;

        CaptureAngles(out float yaw, out float pitch);
        if (!_hasLast)
        {
            _lastYaw = yaw;
            _lastPitch = pitch;
            _hasLast = true;
            return;
        }

        CumulativeYawDegrees += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
        CumulativePitchDegrees += Mathf.Abs(Mathf.DeltaAngle(_lastPitch, pitch));
        _lastYaw = yaw;
        _lastPitch = pitch;
    }

    void CaptureAngles(out float yaw, out float pitch)
    {
        Vector3 e = hmdTransform.eulerAngles;
        yaw = e.y;
        pitch = e.x; // DeltaAngle が 0–360 のラップを処理
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
