using UnityEngine;

/// <summary>
/// 危険度スコア R を色相・彩度・明度・不透明度に変換する。
/// 色・不透明度はともに R に対して連続変化する。
/// 色相は赤(0°=危険)〜薄い青緑(180°=安全)。
/// </summary>
public static class RiskToVisualMapper
{
    const float AlphaMin = 0.35f;
    const float AlphaMax = 1.0f;

    public static (Color color, float alpha) Map(float r)
    {
        r = Mathf.Clamp01(r);
        float alpha = Mathf.Lerp(AlphaMin, AlphaMax, r);
        float hue = Mathf.Lerp(180f, 0f, r) / 360f;
        float saturation = 0.4f + 0.6f * r;
        float value = 0.5f + 0.3f * r;
        Color color = Color.HSVToRGB(hue, saturation, value);
        return (color, alpha);
    }
}
