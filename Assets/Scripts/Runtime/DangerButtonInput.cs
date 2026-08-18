using UnityEngine;
using UnityEngine.InputSystem;

namespace HRIRobot.Experiment
{
    /// <summary>
    /// 被験者が「危険と感じた」と判断したタイミングを記録するためのボタン入力
    /// （仕様書 5.3）。XRコントローラーのトリガー/プライマリボタンに割り当てる。
    /// </summary>
    public class DangerButtonInput : MonoBehaviour
    {
        [Tooltip("XRコントローラーのボタン（例: <XRController>{RightHand}/primaryButton）")]
        public InputActionReference dangerButtonAction;

        public static bool WasPressedThisFrame { get; private set; }

        void OnEnable()
        {
            if (dangerButtonAction != null)
                dangerButtonAction.action.Enable();
        }

        void OnDisable()
        {
            if (dangerButtonAction != null)
                dangerButtonAction.action.Disable();
        }

        void Update()
        {
            WasPressedThisFrame = dangerButtonAction != null
                && dangerButtonAction.action.WasPressedThisFrame();
        }
    }
}
