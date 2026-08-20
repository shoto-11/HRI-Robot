using UnityEngine;

/// <summary>
/// 危険度スコア R を色相・彩度・明度・不透明度に変換する。
/// 色相は赤(0°=危険)〜薄い青緑(180°=安全)。
/// </summary>
public static class RiskToVisualMapper
{
    public static (Color color, float alpha) Map(float r)
    {
        float alpha = Quantize(r);
        float hue = Mathf.Lerp(180f, 0f, r) / 360f;
        float saturation = 0.4f + 0.6f * r;
        float value = 0.5f + 0.3f * r;
        Color color = Color.HSVToRGB(hue, saturation, value);
        return (color, alpha);
    }

    static float Quantize(float r)
    {
        if (r < 0.25f) return 0.35f;
        if (r < 0.50f) return 0.55f;
        if (r < 0.75f) return 0.80f;
        return 1.0f;
    }
}
