using UnityEngine;
using UnityEngine.UI;

namespace HRIRobot.Experiment
{
    /// <summary>
    /// VR酔い対策として視野端にビネット（周辺減光）を適用する（仕様書 5.2）。
    /// ロコモーションは発生しない設計だが、頭部回転時の周辺視野刺激を抑えるため
    /// 常時弱めのビネットを掛け、必要に応じて回転速度に応じて強調する。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class ComfortVignette : MonoBehaviour
    {
        [Header("ビネット強度")]
        [Range(0f, 1f)] public float baseIntensity = 0.25f;
        [Range(0f, 1f)] public float maxIntensity = 0.6f;
        [Tooltip("この角速度(deg/s)以上でmaxIntensityに達する")]
        public float rotationSpeedForMaxIntensity = 180f;
        [Range(0f, 1f)] public float innerRadius = 0.5f;
        [Range(0f, 1f)] public float outerRadius = 1.0f;

        RawImage image;
        Texture2D vignetteTexture;
        Transform headTransform;
        Quaternion lastRotation;

        void Awake()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var imgGO = new GameObject("VignetteImage", typeof(RectTransform), typeof(RawImage));
            imgGO.transform.SetParent(transform, false);
            var rt = imgGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            image = imgGO.GetComponent<RawImage>();
            image.raycastTarget = false;

            vignetteTexture = GenerateVignetteTexture(256, innerRadius, outerRadius);
            image.texture = vignetteTexture;

            headTransform = Camera.main != null ? Camera.main.transform : transform;
            lastRotation = headTransform.rotation;
        }

        void Update()
        {
            float angularSpeed = Quaternion.Angle(lastRotation, headTransform.rotation) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastRotation = headTransform.rotation;

            float t = Mathf.Clamp01(angularSpeed / rotationSpeedForMaxIntensity);
            float intensity = Mathf.Lerp(baseIntensity, maxIntensity, t);

            image.color = new Color(0f, 0f, 0f, intensity);
        }

        static Texture2D GenerateVignetteTexture(int size, float inner, float outer)
        {
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                    float a = Mathf.Clamp01(Mathf.InverseLerp(inner, outer, dist));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
