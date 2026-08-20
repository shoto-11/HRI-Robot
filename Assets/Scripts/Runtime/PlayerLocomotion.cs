using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class PlayerLocomotion : MonoBehaviour
{
    [SerializeField] float moveSpeed = 1.4f;
    [SerializeField] float lookSpeed = 90f;
    [SerializeField] float minPitch = -40f;
    [SerializeField] float maxPitch = 50f;
    [SerializeField] Transform hmdTransform;
    [SerializeField] Transform pitchPivot;

    public float MoveSpeed => moveSpeed;

    public void ResetView()
    {
        _pitch = 0f;
        if (PlayerXrRig.XrActive)
            return;

        if (pitchPivot == null)
        {
            var offset = transform.Find("Camera Offset");
            pitchPivot = offset != null ? offset : (hmdTransform != null ? hmdTransform : transform);
        }
        if (pitchPivot != null)
            pitchPivot.localRotation = Quaternion.identity;
    }

    CharacterController _cc;
    PlayerXrRig _rig;
    XRInputDevice _leftDevice;
    bool _deviceValid;
    float _pitch;

    void Awake()
    {
        if (GetComponent<PlayerXrRig>() == null)
            gameObject.AddComponent<PlayerXrRig>();
    }

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        _cc.height = 1.8f;
        _cc.center = new Vector3(0f, 0.9f, 0f);
        _cc.radius = 0.3f;
        _rig = GetComponent<PlayerXrRig>();
        BindHead();
        ResetView();
        TryInitDevice();
        PlayerWalkabilityUtility.ApplyWalkabilityRules();
    }

    void BindHead()
    {
        if (_rig != null && _rig.HeadCamera != null)
            hmdTransform = _rig.HeadCamera.transform;
        if (hmdTransform == null && Camera.main != null)
            hmdTransform = Camera.main.transform;
        if (pitchPivot == null)
        {
            var offset = _rig != null ? _rig.CameraOffset : transform.Find("Camera Offset");
            pitchPivot = offset != null ? offset : (hmdTransform != null ? hmdTransform : transform);
        }
    }

    void Update()
    {
        if (ExperimentStartMenu.Instance != null && ExperimentStartMenu.Instance.IsVisible)
            return;
        if (!_deviceValid) TryInitDevice();
        BindHead();

        if (!PlayerXrRig.XrActive)
            ApplyDesktopLook();

        Vector3 horizontal = ReadMoveInput();
        ApplyStickTurn();
        _cc.Move(Vector3.down * 9.8f * Time.deltaTime);
        if (horizontal.sqrMagnitude > 0.0001f)
            _cc.Move(horizontal * Time.deltaTime);
    }

    void ApplyDesktopLook()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float yaw = 0f;
        float pitchDelta = 0f;
        if (kb.leftArrowKey.isPressed) yaw -= 1f;
        if (kb.rightArrowKey.isPressed) yaw += 1f;
        if (kb.upArrowKey.isPressed) pitchDelta -= 1f;
        if (kb.downArrowKey.isPressed) pitchDelta += 1f;
        if (Mathf.Abs(yaw) > 0.01f)
            transform.Rotate(0f, yaw * lookSpeed * Time.deltaTime, 0f);
        if (Mathf.Abs(pitchDelta) > 0.01f && pitchPivot != null)
        {
            _pitch = Mathf.Clamp(_pitch + pitchDelta * lookSpeed * Time.deltaTime, minPitch, maxPitch);
            var e = pitchPivot.localEulerAngles;
            pitchPivot.localEulerAngles = new Vector3(_pitch, e.y, 0f);
        }
    }

    Vector3 ReadMoveInput()
    {
        Vector3 dir = Vector3.zero;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed) dir += Vector3.forward;
            if (kb.sKey.isPressed) dir += Vector3.back;
            if (kb.aKey.isPressed) dir += Vector3.left;
            if (kb.dKey.isPressed) dir += Vector3.right;
        }

        Vector2 stick = ReadMoveStick();
        if (stick.sqrMagnitude > 0.0225f)
            dir += new Vector3(stick.x, 0f, stick.y);

        if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
        dir.Normalize();

        Vector3 forward = hmdTransform != null ? hmdTransform.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return (forward * dir.z + right * dir.x) * moveSpeed;
    }

    void ApplyStickTurn()
    {
        Vector2 turn = ReadTurnStick();
        if (Mathf.Abs(turn.x) < 0.25f) return;
        transform.Rotate(0f, turn.x * lookSpeed * Time.deltaTime, 0f);
    }

    Vector2 ReadMoveStick()
    {
        Vector2 v = ReadThumbstick(XRController.leftHand);
        if (v.sqrMagnitude < 0.01f)
            v = ReadLegacyStick(XRNode.LeftHand);
        if (v.sqrMagnitude < 0.01f && Gamepad.current != null)
            v = Gamepad.current.leftStick.ReadValue();
        return v;
    }

    Vector2 ReadTurnStick()
    {
        Vector2 v = ReadThumbstick(XRController.rightHand);
        if (v.sqrMagnitude < 0.01f)
            v = ReadLegacyStick(XRNode.RightHand);
        if (v.sqrMagnitude < 0.01f && Gamepad.current != null)
            v = Gamepad.current.rightStick.ReadValue();
        return v;
    }

    static Vector2 ReadThumbstick(UnityEngine.InputSystem.XR.XRController controller)
    {
        if (controller == null) return Vector2.zero;
        var stick = controller.TryGetChildControl<Vector2Control>("thumbstick")
                    ?? controller.TryGetChildControl<Vector2Control>("joystick")
                    ?? controller.TryGetChildControl<Vector2Control>("primary2DAxis");
        return stick != null ? stick.ReadValue() : Vector2.zero;
    }

    Vector2 ReadLegacyStick(XRNode node)
    {
        var list = new System.Collections.Generic.List<XRInputDevice>();
        InputDevices.GetDevicesAtXRNode(node, list);
        foreach (var device in list)
        {
            if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick)
                && stick.sqrMagnitude > 0.01f)
                return stick;
        }
        return Vector2.zero;
    }

    void TryInitDevice()
    {
        var list = new System.Collections.Generic.List<XRInputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, list);
        if (list.Count > 0)
        {
            _leftDevice = list[0];
            _deviceValid = _leftDevice.isValid;
        }
        if (!_deviceValid && XRController.leftHand != null)
            _deviceValid = true;
    }
}
