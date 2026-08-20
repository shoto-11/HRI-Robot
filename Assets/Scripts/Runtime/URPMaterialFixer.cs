using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// HDRP→URP 変換後に残る非表示マテリアル（ShaderGraph / プロパティ未移行）を修復する。
/// ExperimentManager から Play 開始時に自動追加される。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class URPMaterialFixer : MonoBehaviour
{
    private static readonly Color CardboardFallback = new Color(0.75f, 0.65f, 0.50f);
    private static readonly Color ShelfSteelFallback = new Color(0.45f, 0.45f, 0.48f);

    private static Material _cardboardFallbackMat;
    private static Material _shelfSteelFallbackMat;

    public static int FixAllRenderers()
    {
        var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return FixRenderers(renderers);
    }

    public static int FixRenderers(IEnumerable<Renderer> renderers)
    {
        var urpLit = FindUrpLitShader();
        if (urpLit == null) return 0;

        int fixedCount = 0;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null)
                {
                    mats[i] = CreateFallbackForRenderer(r, urpLit);
                    changed = true;
                    fixedCount++;
                    continue;
                }

                if (NeedsFix(mat))
                {
                    TryRepairMaterialAsset(mat, urpLit);
                    FixShellDoubleSided(mat);
                    changed = true;
                    fixedCount++;
                }
                else if (IsBrokenURP(mat) || HasMissingAlbedoMigration(mat) || ClampHdrColor(mat))
                {
                    if (IsBrokenURP(mat) || HasMissingAlbedoMigration(mat))
                        RepairURPProperties(mat);
                    RepairNormalMap(mat);
                    FixDarkTexturedMaterial(mat);
                    FixShellDoubleSided(mat);
                    ClearInvalidUrpKeywords(mat);
                    changed = true;
                    fixedCount++;
                }
                else
                {
                    if (EnsureVisibleAlbedo(mat)) changed = true;
                    if (FixDarkTexturedMaterial(mat)) changed = true;
                    if (RepairNormalMap(mat)) changed = true;
                    if (FixShellDoubleSided(mat)) changed = true;
                    if (ClearInvalidUrpKeywords(mat)) changed = true;
                }
            }

            if (changed) r.sharedMaterials = mats;
        }

        return fixedCount;
    }

    /// <summary>マテリアルアセットをその場で URP 化（シーンにランタイム Material を書かない）</summary>
    public static bool TryRepairMaterialAsset(Material mat, Shader urpLit = null)
    {
        if (mat == null) return false;
        urpLit ??= FindUrpLitShader();
        if (urpLit == null) return false;

        bool changed = false;
        if (NeedsFix(mat))
        {
            ApplyUrpConversion(mat, urpLit, mat);
            changed = true;
        }
        else if (IsBrokenURP(mat) || HasMissingAlbedoMigration(mat))
        {
            RepairURPProperties(mat);
            changed = true;
        }

        if (EnsureVisibleAlbedo(mat))
            changed = true;
        if (FixDarkTexturedMaterial(mat))
            changed = true;
        if (RepairNormalMap(mat))
            changed = true;
        if (FixShellDoubleSided(mat))
            changed = true;
        if (ClearInvalidUrpKeywords(mat))
            changed = true;
        if (ClampHdrColor(mat))
            changed = true;

        return changed;
    }

    /// <summary>倉庫の外殻（壁・床・天井）は内側からも見えるよう両面描画にする。</summary>
    public static bool FixShellDoubleSided(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        if (!mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;
        if (!IsWarehouseShellMaterial(mat.name)) return false;
        if (!mat.HasProperty("_Cull")) return false;
        if (mat.GetInt("_Cull") == 0) return false;

        mat.SetInt("_Cull", 0);
        return true;
    }

    public static bool IsWarehouseShellMaterial(string materialName)
    {
        if (string.IsNullOrEmpty(materialName)) return false;
        string n = materialName.ToLowerInvariant();
        return n.Contains("outside") || n.Contains("ceiling") || n.Contains("floor") ||
               n.Contains("skirt") || n.Contains("roof") || n.Contains("steelframe") ||
               n.Contains("gap") || n.Contains("bentplate") || n.StartsWith("door") ||
               n.Contains("wall") || n.Contains("building") || n.Contains("concrete") ||
               n.Contains("corrugated") || n.Contains("facade");
    }

    public static Shader FindUrpLitShader() =>
        Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

    void Awake()
    {
        int fixedCount = FixAllRenderers();
        if (fixedCount > 0)
            Debug.Log($"[URPMaterialFixer] {fixedCount} 件のマテリアルを修復しました。");
    }

    public static bool NeedsFix(Material mat)
    {
        if (mat == null) return true;
        if (mat.shader == null) return true;
        string sn = mat.shader.name;
        if (sn.StartsWith("Universal Render Pipeline/")) return false;
        if (sn == "Standard" || sn == "Standard (Specular setup)") return false;
        if (sn.StartsWith("Sprites/") || sn.StartsWith("UI/")) return false;
        if (sn == "Hidden/InternalErrorShader") return true;
        // 倉庫アセットは "Unity Warehouse Scene - HDRP/Floor" のように
        // HDRP/ で始まらない ShaderGraph 名を使う。
        if (sn.Contains("HDRP") || sn.Contains("HDRenderPipeline")) return true;
        if (sn.Contains("Shader Graph") || sn.Contains("Unity Warehouse Scene")) return true;
#if UNITY_EDITOR
        string sp = AssetDatabase.GetAssetPath(mat.shader);
        if (!string.IsNullOrEmpty(sp) && sp.Contains("UnityWarehouseSceneHDRP")) return true;
#endif
        return false;
    }

    static bool IsBrokenURP(Material mat)
    {
        if (mat.shader == null || !mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;
        if (HasAssignedBaseMap(mat)) return false;

        Color col = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") :
                    mat.HasProperty("_Color")     ? mat.GetColor("_Color")     : Color.white;
        return col.maxColorComponent < 0.15f;
    }

    static bool HasMissingAlbedoMigration(Material mat)
    {
        if (mat.shader == null || !mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;
        if (HasAssignedBaseMap(mat)) return false;
        return FindAlbedoTexture(mat) != null;
    }

    static bool HasAssignedBaseMap(Material mat)
    {
        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) return true;
        return mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null;
    }

    static void RepairURPProperties(Material mat)
    {
        Texture tex = FindAlbedoTexture(mat);
        Color col = tex != null ? Color.white : GuessColorFromName(mat.name);
        col.a = 1f;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", col);
        if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
    }

    static void ApplyUrpConversion(Material dst, Shader urpLit, Material src)
    {
        dst.shader = urpLit;
        dst.shaderKeywords = System.Array.Empty<string>();

        Texture tex = FindAlbedoTexture(src);
        Color col = tex != null ? Color.white :
                    src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") :
                    src.HasProperty("_Color")     ? src.GetColor("_Color")     :
                    GuessColorFromName(src.name);
        col.a = 1f;
        if (col.maxColorComponent < 0.15f || col.maxColorComponent > 1.05f)
            col = tex != null ? Color.white : GuessColorFromName(src.name);

        if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", col);
        if (dst.HasProperty("_Color"))     dst.SetColor("_Color", col);
        if (tex != null && dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", tex);
        if (dst.HasProperty("_Smoothness")) dst.SetFloat("_Smoothness", 0.25f);
        if (dst.HasProperty("_EmissionColor")) dst.SetColor("_EmissionColor", Color.black);
    }

    static Material CreateFixedMaterial(Material src, Shader urpLit)
    {
        var mat = new Material(urpLit) { name = src.name + "_Fixed" };
        ApplyUrpConversion(mat, urpLit, src);
        EnsureVisibleAlbedo(mat);
        return mat;
    }

    static Material CreateFallbackForRenderer(Renderer r, Shader urpLit)
    {
        string n = r.gameObject.name.ToLowerInvariant();
        if (n.Contains("cardboard") || n.Contains("box"))
            return GetCardboardFallback(urpLit);
        return GetShelfSteelFallback(urpLit);
    }

    static Material GetCardboardFallback(Shader urpLit)
    {
        if (_cardboardFallbackMat != null) return _cardboardFallbackMat;
        _cardboardFallbackMat = new Material(urpLit) { name = "HRI_Cardboard_Fallback" };
        _cardboardFallbackMat.SetColor("_BaseColor", CardboardFallback);
        _cardboardFallbackMat.SetFloat("_Smoothness", 0.2f);
        return _cardboardFallbackMat;
    }

    static Material GetShelfSteelFallback(Shader urpLit)
    {
        if (_shelfSteelFallbackMat != null) return _shelfSteelFallbackMat;
        _shelfSteelFallbackMat = new Material(urpLit) { name = "HRI_ShelfSteel_Fallback" };
        _shelfSteelFallbackMat.SetColor("_BaseColor", ShelfSteelFallback);
        _shelfSteelFallbackMat.SetFloat("_Metallic", 0.35f);
        _shelfSteelFallbackMat.SetFloat("_Smoothness", 0.45f);
        return _shelfSteelFallbackMat;
    }

    /// <summary>テクスチャを持つのに _BaseColor が黒に近い（HDRP 変換で失われた色）を白に戻す。</summary>
    public static bool FixDarkTexturedMaterial(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        if (!mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;
        if (!mat.HasProperty("_BaseColor")) return false;
        if (!HasAssignedBaseMap(mat)) return false;

        Color c = mat.GetColor("_BaseColor");
        if (c.maxColorComponent >= 0.15f) return false;

        mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        return true;
    }

    /// <summary>HDRP 由来の HDR 白色（数万倍）を URP 向けに 0〜1 へ戻す。</summary>
    public static bool ClampHdrColor(Material mat)
    {
        if (mat == null) return false;
        bool changed = false;

        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            if (c.maxColorComponent > 1.05f)
            {
                mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                changed = true;
            }
        }

        if (mat.HasProperty("_EmissionColor"))
        {
            Color e = mat.GetColor("_EmissionColor");
            bool hdr = e.maxColorComponent > 1.05f;
            bool robot = mat.name.IndexOf("palletrobot", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (hdr || robot)
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                changed = true;
            }
        }

        return changed;
    }

    public static bool EnsureVisibleAlbedo(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        if (!mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;

        Texture existing = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
        if (existing != null) return false;

        Texture tex = FindAlbedoTexture(mat);
        if (tex == null) return false;

        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
        return true;
    }

    static Texture FindAlbedoTexture(Material mat)
    {
        string[] props =
        {
            "_BaseMap", "_BaseColorMap", "_MainTex", "_Albedo", "_AlbedoMap"
        };
        foreach (var p in props)
            if (mat.HasProperty(p) && mat.GetTexture(p) != null)
                return mat.GetTexture(p);

#if UNITY_EDITOR
        return FindSerializedTexture(mat, props, new[] { "albedo", "basecolor", "diffuse", "main" });
#else
        return null;
#endif
    }

    static Texture FindNormalTexture(Material mat)
    {
        string[] props = { "_BumpMap", "_NormalMap", "_Normal_Map", "_DetailNormalMap" };
        foreach (var p in props)
            if (mat.HasProperty(p) && mat.GetTexture(p) != null)
                return mat.GetTexture(p);

#if UNITY_EDITOR
        return FindSerializedTexture(mat, props, new[] { "normal", "bump" });
#else
        return null;
#endif
    }

#if UNITY_EDITOR
    static Texture FindSerializedTexture(Material mat, string[] priorityNames, string[] hintWords)
    {
        var so = new SerializedObject(mat);
        var texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
        if (texEnvs == null || !texEnvs.isArray) return null;

        var all = new Dictionary<string, Texture>();
        for (int i = 0; i < texEnvs.arraySize; i++)
        {
            var elem = texEnvs.GetArrayElementAtIndex(i);
            string name = elem.FindPropertyRelative("first").stringValue;
            var tex = elem.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            if (tex != null) all[name] = tex;
        }

        foreach (var p in priorityNames)
            if (all.TryGetValue(p, out var t)) return t;

        foreach (var kv in all)
            foreach (var hint in hintWords)
                if (kv.Key.ToLowerInvariant().Contains(hint)) return kv.Value;

        bool isNormalSearch = System.Array.Exists(hintWords, h => h == "normal" || h == "bump");
        if (!isNormalSearch)
        {
            foreach (var kv in all)
            {
                string lower = kv.Key.ToLowerInvariant();
                if (!lower.Contains("normal") && !lower.Contains("bump") &&
                    !lower.Contains("occlusion") && !lower.Contains("mask") &&
                    !lower.Contains("smooth") && !lower.Contains("detail"))
                    return kv.Value;
            }
        }

        return null;
    }
#endif

    static bool RepairNormalMap(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        if (!mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;
        if (!mat.HasProperty("_BumpMap")) return false;
        if (mat.GetTexture("_BumpMap") != null) return false;

        Texture normal = FindNormalTexture(mat);
        if (normal == null) return false;

        mat.SetTexture("_BumpMap", normal);
        mat.EnableKeyword("_NORMALMAP");
        return true;
    }

    static bool ClearInvalidUrpKeywords(Material mat)
    {
        if (mat == null || mat.shader == null) return false;
        if (!mat.shader.name.StartsWith("Universal Render Pipeline/")) return false;

        bool changed = false;
        foreach (var kw in mat.shaderKeywords)
        {
            if (kw.StartsWith("_DISABLE_") || kw.Contains("SSR") || kw.Contains("TANGENT_SPACE"))
            {
                mat.DisableKeyword(kw);
                changed = true;
            }
        }
        return changed;
    }

    static Color GuessColorFromName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("cardboard")) return CardboardFallback;
        if (n.Contains("plastic"))   return new Color(0.55f, 0.72f, 0.85f);
        if (n.Contains("shelf") || n.Contains("steel") || n.Contains("beam") || n.Contains("pillar"))
            return ShelfSteelFallback;
        return CardboardFallback;
    }
}
