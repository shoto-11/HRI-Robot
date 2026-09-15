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
    [SerializeField] float stickDeadzone = 0.15f;

    public float MoveSpeed => moveSpeed;

    /// <summary>水平移動速度（m/s）。PTTC の相対速度計算用。</summary>
    public Vector3 HorizontalVelocity => _horizontalVelocity;

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
    InputAction _moveAction;
    InputAction _turnAction;
    float _pitch;
    Vector3 _horizontalVelocity;

    void Awake()
    {
        if (GetComponent<PlayerXrRig>() == null)
            gameObject.AddComponent<PlayerXrRig>();
        EnsureStickActions();
    }

    void OnEnable()
    {
        EnsureStickActions();
        _moveAction?.Enable();
        _turnAction?.Enable();
    }

    void OnDisable()
    {
        _moveAction?.Disable();
        _turnAction?.Disable();
    }

    void OnDestroy()
    {
        _moveAction?.Dispose();
        _turnAction?.Dispose();
        _moveAction = null;
        _turnAction = null;
    }

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        float h = FactoryLayout.StandingHeightM;
        _cc.height = h;
        _cc.center = new Vector3(0f, h * 0.5f, 0f);
        _cc.radius = FactoryLayout.PedestrianBodyRadiusM;
        _cc.skinWidth = 0.08f;
        _cc.minMoveDistance = 0f;
        _rig = GetComponent<PlayerXrRig>();
        BindHead();
        ResetView();
        PlayerWalkabilityUtility.ApplyWalkabilityRules();
    }

    void EnsureStickActions()
    {
        if (_moveAction == null)
        {
            _moveAction = new InputAction("XRMove", InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddBinding("<XRController>{LeftHand}/thumbstick");
            _moveAction.AddBinding("<XRController>{LeftHand}/joystick");
            _moveAction.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            _moveAction.AddBinding("<Gamepad>/leftStick");
        }

        if (_turnAction == null)
        {
            _turnAction = new InputAction("XRTurn", InputActionType.Value, expectedControlType: "Vector2");
            _turnAction.AddBinding("<XRController>{RightHand}/thumbstick");
            _turnAction.AddBinding("<XRController>{RightHand}/joystick");
            _turnAction.AddBinding("<XRController>{RightHand}/primary2DAxis");
            _turnAction.AddBinding("<Gamepad>/rightStick");
        }
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
        {
            _horizontalVelocity = Vector3.zero;
            return;
        }

        BindHead();
        if (_cc == null)
            _cc = GetComponent<CharacterController>();
        if (_cc == null || !_cc.enabled)
        {
            _horizontalVelocity = Vector3.zero;
            return;
        }

        if (!PlayerXrRig.XrActive)
            ApplyDesktopLook();

        Vector3 horizontal = ReadMoveInput();
        _horizontalVelocity = horizontal;
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
        float dz = stickDeadzone * stickDeadzone;
        if (stick.sqrMagnitude > dz)
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
        if (Mathf.Abs(turn.x) < stickDeadzone) return;
        transform.Rotate(0f, turn.x * lookSpeed * Time.deltaTime, 0f);
    }

    Vector2 ReadMoveStick()
    {
        Vector2 v = ReadActionStick(_moveAction);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadThumbstick(XRController.leftHand);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadAnyHandStick(isLeft: true);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadLegacyStick(XRNode.LeftHand);
        if (v.sqrMagnitude < 0.0001f && Gamepad.current != null)
            v = Gamepad.current.leftStick.ReadValue();
        return v;
    }

    Vector2 ReadTurnStick()
    {
        Vector2 v = ReadActionStick(_turnAction);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadThumbstick(XRController.rightHand);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadAnyHandStick(isLeft: false);
        if (v.sqrMagnitude < 0.0001f)
            v = ReadLegacyStick(XRNode.RightHand);
        if (v.sqrMagnitude < 0.0001f && Gamepad.current != null)
            v = Gamepad.current.rightStick.ReadValue();
        return v;
    }

    static Vector2 ReadActionStick(InputAction action)
    {
        if (action == null) return Vector2.zero;
        try
        {
            return action.ReadValue<Vector2>();
        }
        catch
        {
            return Vector2.zero;
        }
    }

    static Vector2 ReadThumbstick(XRController controller)
    {
        if (controller == null) return Vector2.zero;
        var stick = controller.TryGetChildControl<Vector2Control>("thumbstick")
                    ?? controller.TryGetChildControl<Vector2Control>("joystick")
                    ?? controller.TryGetChildControl<Vector2Control>("primary2DAxis");
        return stick != null ? stick.ReadValue() : Vector2.zero;
    }

    static Vector2 ReadAnyHandStick(bool isLeft)
    {
        foreach (var device in InputSystem.devices)
        {
            if (device == null || !device.added) continue;
            bool match = HasUsage(device, isLeft
                ? UnityEngine.InputSystem.CommonUsages.LeftHand
                : UnityEngine.InputSystem.CommonUsages.RightHand);
            if (!match)
            {
                string n = device.name ?? "";
                match = isLeft
                    ? n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0
                    : n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (!match) continue;

            var stick = device.TryGetChildControl<Vector2Control>("thumbstick")
                        ?? device.TryGetChildControl<Vector2Control>("joystick")
                        ?? device.TryGetChildControl<Vector2Control>("primary2DAxis");
            if (stick == null) continue;
            Vector2 v = stick.ReadValue();
            if (v.sqrMagnitude > 0.0001f)
                return v;
        }
        return Vector2.zero;
    }

    static bool HasUsage(InputDevice device, UnityEngine.InputSystem.Utilities.InternedString usage)
    {
        var usages = device.usages;
        for (int i = 0; i < usages.Count; i++)
        {
            if (usages[i] == usage)
                return true;
        }
        return false;
    }

    static Vector2 ReadLegacyStick(XRNode node)
    {
        var list = new System.Collections.Generic.List<XRInputDevice>();
        InputDevices.GetDevicesAtXRNode(node, list);
        foreach (var device in list)
        {
            if (!device.isValid) continue;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick)
                && stick.sqrMagnitude > 0.0001f)
                return stick;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondary2DAxis, out stick)
                && stick.sqrMagnitude > 0.0001f)
                return stick;
        }
        return Vector2.zero;
    }
}
