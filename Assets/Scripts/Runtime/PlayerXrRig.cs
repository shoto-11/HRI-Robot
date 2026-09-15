using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Quest / OpenXR 用にカメラ追跡を付け、PC のみのときは従来の固定視点を残す。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class PlayerXrRig : MonoBehaviour
{
    public static bool XrActive { get; private set; }

    const float XrEyeTextureScale = 0.72f;
    const float XrRenderScale = 0.75f;
    const float XrShadowDistance = 12f;
    const int XrAdditionalLightsPerObject = 1;

    Transform _cameraOffset;
    Camera _camera;
    TrackedPoseDriver _poseDriver;
    bool _xrPerfApplied;

    void Awake()
    {
        EnsureCamera();
        XrActive = DetectXrDisplay();
        ConfigureForMode();
        ApplyXrPerformance();
    }

    void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
    {
        if (change != InputDeviceChange.Added && change != InputDeviceChange.Reconnected)
            return;
        if (XrActive) return;
        XrActive = DetectXrDisplay();
        if (!XrActive) return;
        ConfigureForMode();
        ApplyXrPerformance();
    }

    public Camera HeadCamera => _camera;

    public Transform CameraOffset
    {
        get
        {
            if (_cameraOffset == null)
                EnsureCamera();
            return _cameraOffset;
        }
    }

    void EnsureCamera()
    {
        _cameraOffset = transform.Find("Camera Offset");
        if (_cameraOffset == null)
        {
            var go = new GameObject("Camera Offset");
            go.transform.SetParent(transform, false);
            _cameraOffset = go.transform;
        }

        var camTf = _cameraOffset.Find("Main Camera");
        if (camTf == null && Camera.main != null && Camera.main.transform.IsChildOf(transform))
            camTf = Camera.main.transform;

        if (camTf == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(_cameraOffset, false);
            camTf = camGo.transform;
        }

        _camera = camTf.GetComponent<Camera>();
        if (_camera == null)
            _camera = camTf.gameObject.AddComponent<Camera>();
        if (camTf.GetComponent<AudioListener>() == null)
            camTf.gameObject.AddComponent<AudioListener>();

        _camera.nearClipPlane = 0.05f;
        _camera.farClipPlane = 80f;
        _camera.stereoTargetEye = StereoTargetEyeMask.Both;
        if (string.IsNullOrEmpty(camTf.gameObject.tag) || camTf.gameObject.tag != "MainCamera")
            camTf.gameObject.tag = "MainCamera";
    }

    void ConfigureForMode()
    {
        EnsureCamera();
        var origin = GetComponent<XROrigin>();
        if (origin == null)
            origin = gameObject.AddComponent<XROrigin>();
        origin.Origin = gameObject;
        origin.CameraFloorOffsetObject = _cameraOffset.gameObject;
        origin.Camera = _camera;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        if (XrActive)
        {
            _cameraOffset.localPosition = Vector3.zero;
            _cameraOffset.localRotation = Quaternion.identity;
            EnsureTrackedPose();
            DisableVrOverlays();
        }
        else
        {
            // Desktop: fixed eye height for ~170 cm person.
            _cameraOffset.localPosition = new Vector3(0f, FactoryLayout.EyeHeightM, 0f);
            _cameraOffset.localRotation = Quaternion.identity;
        }
    }

    /// <summary>
    /// ケース開始時などに、現在の HMD 追跡を基準に目線を EyeHeightM（約 170 cm の人）へ合わせる。
    /// </summary>
    public void CalibrateEyeHeightToNominal()
    {
        EnsureCamera();
        if (_cameraOffset == null || _camera == null) return;

        if (!XrActive)
        {
            _cameraOffset.localPosition = new Vector3(0f, FactoryLayout.EyeHeightM, 0f);
            _cameraOffset.localRotation = Quaternion.identity;
            return;
        }

        // Floor 追跡の実測高さにオフセットを足し、ワールド目線を公称値へ揃える。
        _cameraOffset.localPosition = Vector3.zero;
        _cameraOffset.localRotation = Quaternion.identity;
        Physics.SyncTransforms();

        float tracked = _camera.transform.position.y - transform.position.y;
        if (tracked < 0.4f || tracked > 2.4f)
            tracked = FactoryLayout.EyeHeightM;

        float dy = FactoryLayout.EyeHeightM - tracked;
        _cameraOffset.localPosition = new Vector3(0f, dy, 0f);
    }

    void EnsureTrackedPose()
    {
        _poseDriver = _camera.GetComponent<TrackedPoseDriver>();
        if (_poseDriver == null)
            _poseDriver = _camera.gameObject.AddComponent<TrackedPoseDriver>();

        _poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        _poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        _poseDriver.positionInput = NewPoseAction("HMDPosition", "<XRHMD>/centerEyePosition", "Vector3");
        _poseDriver.rotationInput = NewPoseAction("HMDRotation", "<XRHMD>/centerEyeRotation", "Quaternion");
    }

    static InputActionProperty NewPoseAction(string name, string binding, string controlType)
    {
        var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: controlType);
        action.Enable();
        return new InputActionProperty(action);
    }

    static void DisableVrOverlays()
    {
        foreach (var v in FindObjectsByType<HRIRobot.Experiment.ComfortVignette>(FindObjectsSortMode.None))
        {
            if (v != null)
                v.gameObject.SetActive(false);
        }
    }

    static bool DetectXrDisplay()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
        {
            if (d != null && d.running)
                return true;
        }

        var mgr = XRGeneralSettings.Instance?.Manager;
        if (mgr != null && mgr.isInitializationComplete && mgr.activeLoader != null)
            return true;

        return XRSettings.isDeviceActive;
    }

    void ApplyXrPerformance()
    {
        // Editor の Game ビュー単体でも重くなりすぎないよう、常に軽い側へ寄せる。
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadowDistance = XrShadowDistance;
        QualitySettings.shadows = UnityEngine.ShadowQuality.HardOnly;
        QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Low;
        QualitySettings.shadowCascades = 1;
        QualitySettings.lodBias = 0.55f;
        QualitySettings.maximumLODLevel = 0;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.particleRaycastBudget = 16;
        QualitySettings.antiAliasing = 2;

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            urp.supportsHDR = false;
            urp.renderScale = XrRenderScale;
            urp.shadowDistance = XrShadowDistance;
            urp.mainLightShadowmapResolution = 1024;
            urp.msaaSampleCount = 2;
            urp.maxAdditionalLightsCount = XrAdditionalLightsPerObject;
        }

        if (!DetectXrDisplay() && !XRSettings.enabled)
            return;
        if (_xrPerfApplied) return;
        _xrPerfApplied = true;

        if (XRSettings.enabled)
            XRSettings.eyeTextureResolutionScale = XrEyeTextureScale;

        TrimRealtimeLightsForXr();
        EnableFixedFoveatedRendering();
        Debug.Log("[PlayerXrRig] XR performance mode: lower scale, hard shadows, point lights off.");
    }

    static void TrimRealtimeLightsForXr()
    {
        // 倉庫アセット由来のポイントライト多数が Quest / Link の主ボトルネック。
        int disabled = 0;
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light == null) continue;
            if (light.type == LightType.Directional)
            {
                light.shadows = LightShadows.Hard;
                continue;
            }

            if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                if (!light.enabled) continue;
                light.enabled = false;
                disabled++;
            }
        }

        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat
            || RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight)
        {
            RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, 1.15f);
        }
        else
        {
            RenderSettings.ambientIntensity = Mathf.Max(RenderSettings.ambientIntensity, 1.05f);
        }

        if (disabled > 0)
            Debug.Log($"[PlayerXrRig] Disabled {disabled} point/spot lights for XR frame time.");
    }

    static void EnableFixedFoveatedRendering()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays)
        {
            if (display == null || !display.running) continue;
            try
            {
                display.foveatedRenderingLevel = 1f;
                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PlayerXrRig] Foveated rendering unavailable ({e.Message}).");
            }
        }
    }
}
