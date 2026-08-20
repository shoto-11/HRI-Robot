using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
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

    Transform _cameraOffset;
    Camera _camera;
    TrackedPoseDriver _poseDriver;

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
        if (XrActive)
            ConfigureForMode();
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
            if (_cameraOffset.localPosition.sqrMagnitude < 0.01f)
                _cameraOffset.localPosition = new Vector3(0f, 1.6f, 0f);
        }
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

    static void ApplyXrPerformance()
    {
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        QualitySettings.shadowDistance = 25f;
        QualitySettings.lodBias = 0.7f;
        QualitySettings.maximumLODLevel = 0;
        if (XRSettings.enabled)
            XRSettings.eyeTextureResolutionScale = 0.85f;
    }
}
