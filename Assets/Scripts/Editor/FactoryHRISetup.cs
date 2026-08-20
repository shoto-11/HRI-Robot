using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// HRI > Setup Factory Scene
/// Unity Warehouse / Factory アセットがあれば実倉庫シーンを URP 化して AGV 実験シーンを構築する。
/// 未インポート時は FactoryLayout 座標のホワイトボックス工場にフォールバックする。
/// </summary>
public static class FactoryHRISetup
{
    const string ScenePath = "Assets/Scenes/FactoryExperiment.unity";
    const string WarehouseRoot = "Assets/UnityWarehouseSceneHDRP";
    const string WarehouseScene = "Assets/UnityWarehouseSceneHDRP/Scene_Warehouse/WarehouseSceneSample.unity";
    const string PrefabRoot = "Assets/UnityWarehouseSceneHDRP/Scene_Warehouse/Background/Prefabs";
    const string AgvPrefabPath = "Assets/UnityWarehouseSceneHDRP/Scene_Warehouse/Movable/Prefabs/Palletrobot.prefab";
    const string LightingSettingsPath = "Assets/Scenes/HRI_LightingSettings.asset";

    [MenuItem("HRI/Setup Factory Scene", false, 1)]
    public static void Setup() => SetupInternal(interactive: true);

    /// <summary>MCP / 自動化用。保存確認ダイアログと完了ダイアログを出さない。</summary>
    public static void SetupNoDialog() => SetupInternal(interactive: false);

    static bool _interactive = true;

    static void SetupInternal(bool interactive)
    {
        _interactive = interactive;
        if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        try
        {
            if (WarehouseAvailable())
                SetupFromWarehouse();
            else
                SetupGraybox();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("HRI/Setup Factory Scene (Graybox)", false, 11)]
    public static void SetupGrayboxMenu()
    {
        _interactive = true;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try { SetupGraybox(); }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("HRI/Restore Warehouse Building", false, 18)]
    public static void RestoreWarehouseBuildingMenu()
    {
        int n = EnsureWarehouseBuilding();
        if (n > 0)
        {
            RepairAllWarehouseMaterialAssets();
            RestoreOriginalWarehouseColors();
            FixWarehouseShellMaterials();
            SetupWarehouseInteriorLighting();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
        EditorUtility.DisplayDialog(
            "Restore Warehouse Building",
            n > 0
                ? "床・壁・天井の Building をシーンに戻しました。"
                : "Building は既にシーンにあります。",
            "OK");
    }

    [MenuItem("HRI/Restore Original Warehouse Look", false, 19)]
    public static void RestoreOriginalWarehouseLook()
    {
        string summary = RestoreOriginalWarehouseLookInternal();
        EditorUtility.DisplayDialog("Restore Original Warehouse Look", summary, "OK");
    }

    /// <summary>MCP / 自動化用。保存確認・完了ダイアログなし。</summary>
    public static void RestoreOriginalWarehouseLookNoDialog()
    {
        RestoreOriginalWarehouseLookInternal();
    }

    const string PendingRestoreFlag = "Assets/Temp/pending_warehouse_restore.flag";

    [InitializeOnLoadMethod]
    static void RunPendingWarehouseRestore()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(PendingRestoreFlag)) return;
            File.Delete(PendingRestoreFlag);
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[FactoryHRISetup] 自動修復を開始します…");
            RestoreOriginalWarehouseLookNoDialog();
        };
    }

    static string RestoreOriginalWarehouseLookInternal()
    {
        int building = EnsureWarehouseBuilding();
        int converted = ConvertAllHDRPMaterials();
        int shelves = RevertBrokenShelfPrefabs();
        RemoveWarehouseLabelScripts();
        int assets = RepairAllWarehouseMaterialAssets();
        RestoreOriginalWarehouseColors();
        int shells = FixWarehouseShellMaterials();
        RepairSceneMaterialsInternal(silent: true);
        RepairShelfCargoVisuals();
        URPMaterialFixer.FixAllRenderers();
        SetupWarehouseInteriorLighting();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        string summary =
            "HRI-Drone と同じ修復手順を適用しました。\n\n" +
            $"Building復元: {building}\nHDRP→URP 変換: {converted}\n棚プレハブ復元: {shelves}\n" +
            $"マテリアル修復: {assets}\n外殻両面化: {shells}\n" +
            "天井ライト強度: 15（Drone 同等）";
        Debug.Log($"[FactoryHRISetup] {summary.Replace("\n", " ")}");
        return summary;
    }

    [MenuItem("HRI/Fix URP Materials", false, 20)]
    public static void FixMaterials()
    {
        int assets = RepairAllWarehouseMaterialAssets();
        int nullFixed = FixNullMaterialOverrides();
        int renderers = URPMaterialFixer.FixAllRenderers();
        SetupWarehouseInteriorLighting();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[FactoryHRISetup] URP 修復: アセット {assets} / null {nullFixed} / Renderer {renderers}");
    }

    static void RestoreOriginalWarehouseColors()
    {
        var untexturedColors = new Dictionary<string, Color>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Skirt", new Color(0.21f, 0.21f, 0.21f) },
            { "Outside", new Color(0.78f, 0.77f, 0.74f) },
            { "Prefab_Roof", new Color(0.75f, 0.65f, 0.5f) },
            { "ShelfSteel_Beam", new Color(0.16f, 0.16f, 0.16f) },
            { "ShelfSteel_Pillar", new Color(0.45f, 0.45f, 0.48f) },
            { "ShelfSteel_Guard", new Color(0.91f, 0.61f, 0f) },
            { "Handrail", new Color(1f, 0.74f, 0f) },
            { "Ceiling", new Color(0.86f, 0.86f, 0.86f) },
            { "BentPlate", new Color(0.86f, 0.86f, 0.86f) },
        };

        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { WarehouseRoot }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null) continue;

            if (URPMaterialFixer.NeedsFix(mat))
                URPMaterialFixer.TryRepairMaterialAsset(mat);

            if (!mat.HasProperty("_BaseColor") && !mat.HasProperty("_Color"))
                continue;

            bool hasTex = mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null;
            Color existing = Color.white;
            if (mat.HasProperty("_BaseColor")) existing = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) existing = mat.GetColor("_Color");

            Color col = hasTex
                ? Color.white
                : (untexturedColors.TryGetValue(mat.name, out var c) ? c : existing);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", hasTex ? 0.5f : 0.25f);
            if (mat.HasProperty("_Metallic"))
            {
                bool metal = mat.name.IndexOf("steel", System.StringComparison.OrdinalIgnoreCase) >= 0
                             || mat.name.IndexOf("frame", System.StringComparison.OrdinalIgnoreCase) >= 0;
                mat.SetFloat("_Metallic", metal ? 0.35f : 0f);
            }
            if (mat.HasProperty("_Cull") && URPMaterialFixer.IsWarehouseShellMaterial(mat.name))
                mat.SetInt("_Cull", 0);

            EditorUtility.SetDirty(mat);
            count++;
        }
        Debug.Log($"[FactoryHRISetup] 倉庫の元色を復元: {count} 件");
    }

    static int RevertBrokenShelfPrefabs()
    {
        int count = 0;
        var done = new HashSet<GameObject>();

        foreach (var rootName in new[] { "Cardboard_Shelf", "Pallet_Shelf", "Shelf", "=== Cargo on Shelves ===" })
        {
            var root = GameObject.Find(rootName);
            if (root == null) continue;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (!PrefabUtility.IsPartOfPrefabInstance(go)) continue;

                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                if (outer == null || !done.Add(outer)) continue;

                bool hasMesh = outer.GetComponentInChildren<MeshRenderer>(true) != null
                            || outer.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
                if (hasMesh) continue;

                PrefabUtility.RevertPrefabInstance(outer, InteractionMode.AutomatedAction);
                count++;
                Debug.Log($"[FactoryHRISetup] 棚プレハブ復元: {outer.name}");
            }
        }

        return count;
    }

    static void RemoveWarehouseLabelScripts(HashSet<GameObject> destroyGos = null)
    {
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string ns = mb.GetType().Namespace ?? "";
            if (!ns.StartsWith("UnityWarehouseSceneHDRP")) continue;

            string typeName = mb.GetType().Name;
            if (typeName == "CameraMove" || typeName == "PlayerInput")
            {
                destroyGos?.Add(mb.gameObject);
                continue;
            }

            Object.DestroyImmediate(mb);
        }
    }

    static bool RepairSceneMaterialsInternal(bool silent)
    {
        var urpLit = URPMaterialFixer.FindUrpLitShader();
        if (urpLit == null)
        {
            if (!silent)
                EditorUtility.DisplayDialog("エラー", "URP/Lit シェーダーが見つかりません。", "OK");
            return false;
        }

        int nullFixed = FixNullMaterialOverrides();
        int matCount = 0;
        int goCount = 0;

        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;

                string sn = mats[i].shader?.name ?? "";
                if (sn.StartsWith("Universal Render Pipeline/"))
                {
                    if (URPMaterialFixer.EnsureVisibleAlbedo(mats[i])
                        || URPMaterialFixer.FixDarkTexturedMaterial(mats[i])
                        || URPMaterialFixer.FixShellDoubleSided(mats[i]))
                    {
                        EditorUtility.SetDirty(mats[i]);
                        matCount++;
                        changed = true;
                    }
                    continue;
                }
                if (sn == "Standard" || sn == "Standard (Specular setup)") continue;
                if (sn.StartsWith("Sprites/") || sn.StartsWith("UI/")) continue;

                if (URPMaterialFixer.TryRepairMaterialAsset(mats[i], urpLit))
                {
                    EditorUtility.SetDirty(mats[i]);
                    matCount++;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(r);
                goCount++;
            }
        }

        if (matCount > 0) AssetDatabase.SaveAssets();
        Debug.Log($"[FactoryHRISetup] シーンマテリアル修復: null={nullFixed} mat={matCount} renderer={goCount}");
        return true;
    }

    [MenuItem("HRI/Fix See-Through Walls", false, 24)]
    public static void FixSeeThroughWalls()
    {
        int mats = FixWarehouseShellMaterials();
        int renderers = URPMaterialFixer.FixAllRenderers();
        SetupWarehouseInteriorLighting();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Fix See-Through Walls",
            $"外殻マテリアル {mats} 件を両面描画に変更しました。\nRenderer 修復: {renderers} 件",
            "OK");
    }

    static int FixWarehouseShellMaterials()
    {
        int count = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { WarehouseRoot }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || !URPMaterialFixer.IsWarehouseShellMaterial(mat.name)) continue;
                if (URPMaterialFixer.TryRepairMaterialAsset(mat))
                {
                    EditorUtility.SetDirty(mat);
                    count++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            if (count > 0) AssetDatabase.SaveAssets();
        }
        return count;
    }

    [MenuItem("HRI/Fix Interior Lighting", false, 23)]
    public static void FixInteriorLightingMenu()
    {
        SetupWarehouseInteriorLighting();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog(
            "Fix Interior Lighting",
            "倉庫内部の照明設定を更新しました。\n\n" +
            "・環境光を明るく\n" +
            "・天井ライトのエミッション復元\n" +
            "・Realtime 照明 + 反射プローブ\n" +
            "・URP 追加ライト上限 12",
            "OK");
    }

    [MenuItem("HRI/Fix Pink Shelves", false, 22)]
    public static void FixPinkShelves()
    {
        int assets = RepairAllWarehouseMaterialAssets();
        int nullFixed = FixNullMaterialOverrides();
        RepairShelfCargoVisuals();
        URPMaterialFixer.FixAllRenderers();
        SetupWarehouseInteriorLighting();
        EditorSceneManager.SaveOpenScenes();
        string msg = $"アセット {assets} / null {nullFixed} 件を修復しました。";
        Debug.Log($"[FactoryHRISetup] {msg}");
        EditorUtility.DisplayDialog("Fix Pink Shelves", msg, "OK");
    }

    [MenuItem("HRI/Fix Missing Scripts", false, 21)]
    public static void FixMissingScripts()
    {
        int removed = 0;
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !go.scene.IsValid() || !go.scene.isLoaded) continue;
            if (EditorUtility.IsPersistent(go)) continue;
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        }
        if (removed > 0)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[FactoryHRISetup] Missing Script {removed} 件除去");
        }
        else
        {
            Debug.Log("[FactoryHRISetup] Missing Script は見つかりませんでした。");
        }
    }

    static bool WarehouseAvailable()
    {
        string abs = Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? "",
            WarehouseScene.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(abs) || AssetDatabase.LoadAssetAtPath<SceneAsset>(WarehouseScene) != null;
    }

    static void SetupFromWarehouse()
    {
        EditorUtility.DisplayProgressBar("HRI Factory", "倉庫シーンを開いています...", 0.05f);
        var scene = EditorSceneManager.OpenScene(WarehouseScene, OpenSceneMode.Single);

        EditorUtility.DisplayProgressBar("HRI Factory", "マテリアルを URP に変換中...", 0.15f);
        int converted = ConvertAllHDRPMaterials();
        Debug.Log($"[FactoryHRISetup] マテリアル変換: {converted} 件");

        EditorUtility.DisplayProgressBar("HRI Factory", "Lightmap をクリア中...", 0.28f);
        ClearHDRPLightmaps();

        EditorUtility.DisplayProgressBar("HRI Factory", "HDRP コンポーネントを削除中...", 0.36f);
        RemoveHDRPObjects();
        EnsureWarehouseBuilding();

        EditorUtility.DisplayProgressBar("HRI Factory", "棚に荷物を配置中...", 0.50f);
        PopulateShelvesWithCargo();

        EditorUtility.DisplayProgressBar("HRI Factory", "レンダー設定中...", 0.58f);
        SetupRenderSettings();

        EditorUtility.DisplayProgressBar("HRI Factory", "実験オブジェクトを配置中...", 0.72f);
        AddStationsAndCargoZones();
        AddFactoryData();
        BuildPlayer();
        BuildSystems();

        EditorUtility.DisplayProgressBar("HRI Factory", "マテリアル修復中...", 0.82f);
        DisableSceneEmissionExceptWarehouseLights();
        RevertBrokenShelfPrefabs();
        RemoveWarehouseLabelScripts();
        RepairAllWarehouseMaterialAssets();
        RestoreOriginalWarehouseColors();
        FixWarehouseShellMaterials();
        FixNullMaterialOverrides();
        RepairSceneMaterialsInternal(silent: true);
        RepairShelfCargoVisuals();
        URPMaterialFixer.FixAllRenderers();
        SetupWarehouseInteriorLighting();
        DisableDecorativeCollidersInScene();

        EditorUtility.DisplayProgressBar("HRI Factory", "NavMesh をベイク中...", 0.90f);
        MarkWalkableSurfaces();
        BakeNavMesh();

        EditorUtility.DisplayProgressBar("HRI Factory", "保存中...", 0.96f);
        EnsureScenesFolder();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log($"[FactoryHRISetup] Unity Factory 倉庫シーンを保存: {ScenePath}");
        if (_interactive)
        {
            EditorUtility.DisplayDialog(
                "完了",
                "Unity Factory 倉庫をベースに実験シーンを構築しました。\n\n" +
                $"保存先: {ScenePath}\n" +
                $"変換マテリアル: {converted} 件\n\n" +
                "AGV は Palletrobot プレハブを使用します。\n" +
                "床が青く見える場合は Scene ビューの Navigation オーバーレイをオフにしてください。",
                "OK");
        }
    }

    static void SetupGraybox()
    {
        EditorUtility.DisplayProgressBar("HRI Factory", "ホワイトボックス工場を生成中...", 0.2f);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildGrayboxEnvironment();
        BuildPlayer();
        BuildSystems();
        BuildGrayboxLighting();
        BakeNavMesh();
        EnsureScenesFolder();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[FactoryHRISetup] グレイボックスシーンを保存: {ScenePath}");
        if (_interactive)
        {
            EditorUtility.DisplayDialog(
                "完了（グレイボックス）",
                "Unity Factory アセットが見つからなかったため、簡易工場を生成しました。\n" +
                "アセット導入後に HRI > Setup Factory Scene を再実行してください。",
                "OK");
        }
    }

    static void EnsureScenesFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // HDRP → URP
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static int ConvertAllHDRPMaterials()
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[FactoryHRISetup] URP/Lit シェーダーが見つかりません。");
            return 0;
        }

        AssetDatabase.StartAssetEditing();
        int count = 0;
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { WarehouseRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (NeedsConversion(mat))
                {
                    Color col = GetColor(mat, "_BaseColor", "_Color", Color.white);
                    if (col.maxColorComponent > 1.05f) col = Color.white;
                    float metal = GetFloat(mat, "_Metallic", null, 0f);
                    float smooth = GetFloat(mat, "_Smoothness", "_Glossiness", 0.5f);
                    Texture albedo = GetTex(mat,
                        new[] { "_BaseColorMap", "_MainTex", "_AlbedoMap", "_BaseMap", "_Albedo" },
                        new[] { "albedo", "base", "color", "diffuse", "main" });
                    Texture normal = GetTex(mat,
                        new[] { "_BumpMap", "_NormalMap", "_Normal_Map", "_DetailNormalMap" },
                        new[] { "normal", "bump" });

                    mat.shader = urpLit;
                    mat.shaderKeywords = System.Array.Empty<string>();

                    Color finalCol = albedo != null ? Color.white : col;
                    float finalSmooth = Mathf.Min(Mathf.Clamp01(smooth), 0.45f);

                    mat.SetColor("_BaseColor", finalCol);
                    mat.SetColor("_Color", finalCol);
                    mat.SetFloat("_Metallic", Mathf.Clamp01(metal));
                    mat.SetFloat("_Smoothness", finalSmooth);
                    if (albedo != null) mat.SetTexture("_BaseMap", albedo);
                    if (normal != null)
                    {
                        mat.SetTexture("_BumpMap", normal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    URPMaterialFixer.EnsureVisibleAlbedo(mat);
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    EditorUtility.SetDirty(mat);
                    count++;
                }

                if (URPMaterialFixer.TryRepairMaterialAsset(mat, urpLit))
                {
                    EditorUtility.SetDirty(mat);
                    count++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }
        return count;
    }

    static bool NeedsConversion(Material mat)
    {
        if (mat.shader == null) return true;
        string sn = mat.shader.name;
        if (sn.StartsWith("Universal Render Pipeline/")) return false;
        if (sn == "Standard" || sn == "Standard (Specular setup)") return false;
        if (sn.StartsWith("Sprites/") || sn.StartsWith("UI/")) return false;
        if (sn.Contains("HDRP") || sn.Contains("HDRenderPipeline") ||
            sn.Contains("Shader Graph") || sn.Contains("Unity Warehouse Scene") ||
            sn == "Hidden/InternalErrorShader") return true;
        string mp = AssetDatabase.GetAssetPath(mat);
        if (!string.IsNullOrEmpty(mp) && mp.StartsWith(WarehouseRoot)) return true;
        string sp = AssetDatabase.GetAssetPath(mat.shader);
        return !string.IsNullOrEmpty(sp) && sp.StartsWith(WarehouseRoot);
    }

    static void ClearHDRPLightmaps()
    {
        Lightmapping.Cancel();
        AssetDatabase.DeleteAsset(LightingSettingsPath);
        var ls = new LightingSettings
        {
#pragma warning disable CS0618
            autoGenerate = false,
#pragma warning restore CS0618
            realtimeGI = false,
            bakedGI = false,
        };
        EnsureScenesFolder();
        AssetDatabase.CreateAsset(ls, LightingSettingsPath);
        EditorUtility.SetDirty(ls);
        AssetDatabase.SaveAssets();
        Lightmapping.lightingSettings = ls;
        Lightmapping.Clear();
        Lightmapping.lightingDataAsset = null;
        LightmapSettings.lightmaps = System.Array.Empty<LightmapData>();
        LightmapSettings.lightProbes = null;

        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            r.receiveGI = ReceiveGI.LightProbes;
            var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject)
                        & ~StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
        }
    }

    static void RemoveHDRPObjects()
    {
        var deleteNames = new HashSet<string>
        {
            "Global Volume", "StaticLightingSky",
            "Reflection Probe Main1", "Reflection Probe Main2", "Reflection Probe Main3",
            "Reflection Probe Sub1", "Reflection Probe Sub2",
            "Reflection Probe Sub3", "Reflection Probe Sub4",
            "Reflection Proxy Volume",
            "HeadLight", "LineLight", "Main Camera", "SceneIDMap",
        };

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || t.gameObject == null) continue;
            if (deleteNames.Contains(t.name))
                Object.DestroyImmediate(t.gameObject);
        }

        var destroyGos = new HashSet<GameObject>();
        RemoveWarehouseLabelScripts(destroyGos);
        foreach (var go in destroyGos)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l == null) continue;
            if (l.type == LightType.Directional)
            {
                if (deleteNames.Contains(l.gameObject.name))
                    Object.DestroyImmediate(l.gameObject);
                continue;
            }
            l.shadows = LightShadows.None;
            l.intensity = 15f;
        }

        var animSample = GameObject.Find("Animation Sample");
        if (animSample != null) animSample.SetActive(false);
    }

    static void PopulateShelvesWithCargo()
    {
        var cardboardPrefabs = new List<GameObject>();
        foreach (var n in new[]
        {
            "Cardboard_A1", "Cardboard_A2", "Cardboard_A3",
            "Cardboard_B1", "Cardboard_B2", "Cardboard_B3",
            "Cardboard_C1", "Cardboard_C2", "Cardboard_C3"
        })
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{n}.prefab");
            if (p != null) cardboardPrefabs.Add(p);
        }

        if (cardboardPrefabs.Count == 0)
        {
            Debug.LogWarning("[FactoryHRISetup] Cardboard プレハブが見つかりません。");
            return;
        }

        var existing = GameObject.Find("=== Cargo on Shelves ===");
        if (existing != null) Object.DestroyImmediate(existing);

        var cargoRoot = new GameObject("=== Cargo on Shelves ===");
        int cardboardIdx = 0;
        float[] rowY = { 0.85f, 1.45f };
        int[] rowCols = { 6, 5 };
        const float colSpacingZ = 0.52f;

        foreach (var wb in FactoryLayout.WorkbenchPositions)
        {
            bool isLeft = wb.x < FactoryLayout.CenterX;
            float shelfX = isLeft ? wb.x - 2.0f : wb.x + 2.0f;
            for (int row = 0; row < rowY.Length; row++)
            {
                int cols = rowCols[row];
                for (int col = 0; col < cols; col++)
                {
                    var prefab = cardboardPrefabs[cardboardIdx % cardboardPrefabs.Count];
                    cardboardIdx++;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cargoRoot.transform);
                    float zOffset = (col - (cols - 1) * 0.5f) * colSpacingZ;
                    float xDepth = col % 2 == 0 ? 0f : (isLeft ? -0.30f : 0.30f);
                    go.transform.position = new Vector3(shelfX + xDepth, rowY[row], wb.z + zOffset);
                    go.transform.rotation = Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f);
                    go.isStatic = false;
                    ForceURPMaterials(go);
                    DisableDecorativeCollidersOn(go);
                }
            }
        }
        Debug.Log($"[FactoryHRISetup] 棚配置完了: Cardboard×{cardboardIdx}");
    }

    static void DisableDecorativeCollidersOn(GameObject go)
    {
        if (go == null) return;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || col.isTrigger) continue;
            col.enabled = false;
        }
    }

    static void DisableDecorativeCollidersInScene()
    {
        PlayerWalkabilityUtility.ApplyWalkabilityRules();
    }

    static void RepairShelfCargoVisuals()
    {
        var targets = new HashSet<string>
        {
            "Cardboard_Shelf", "Shelf", "=== Cargo on Shelves ===", "Pallet_Shelf"
        };
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (targets.Contains(t.name) || t.name.StartsWith("Cardboard_") || t.name.StartsWith("PlasticBox"))
                ForceURPMaterials(t.gameObject);
        }
    }

    static void ForceURPMaterials(GameObject go)
    {
        var urpLit = URPMaterialFixer.FindUrpLitShader();
        if (urpLit == null) return;

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                {
                    mats[i] = LoadShelfFallbackMaterial(r.gameObject.name, urpLit);
                    changed = true;
                    continue;
                }
                if (URPMaterialFixer.TryRepairMaterialAsset(mats[i], urpLit))
                {
                    EditorUtility.SetDirty(mats[i]);
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    static Material LoadShelfFallbackMaterial(string objectName, Shader urpLit)
    {
        string n = objectName.ToLowerInvariant();
        string matRoot = PrefabRoot.Replace("/Prefabs", "/Materials");
        string path;
        if (n.Contains("cardboard") || n.Contains("box"))
            path = $"{matRoot}/Cardboard_A1.mat";
        else if (n.Contains("pillar"))
            path = $"{matRoot}/ShelfSteel_Pillar.mat";
        else if (n.Contains("guard"))
            path = $"{matRoot}/ShelfSteel_Guard.mat";
        else
            path = $"{matRoot}/ShelfSteel_Beam.mat";
        var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (asset != null) return asset;
        var mat = new Material(urpLit) { name = "Shelf_Fallback" };
        mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.48f));
        return mat;
    }

    static void DisableSceneEmissionExceptWarehouseLights()
    {
        var keepEmissive = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Toplight_Glass", "Toplight_Wall", "TopLight_Frame", "Light_Prefab", "Light", "Exit"
        };

        var seen = new HashSet<Material>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null || !seen.Add(mat)) continue;
                if (keepEmissive.Contains(mat.name)) continue;
                if (!mat.IsKeywordEnabled("_EMISSION")) continue;

                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(mat);
            }
        }
        AssetDatabase.SaveAssets();
    }

    static void SetupWarehouseInteriorLighting()
    {
        SetupRenderSettings();
        EnsureWarehouseFillLight();
        EnsureWarehouseReflectionProbe();
        EnableWarehouseRealtimeLighting();
        RestoreToplightEmission();
        TuneWarehousePointLights();
    }

    static void EnableWarehouseRealtimeLighting()
    {
        int count = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool changed = false;
            if (r.receiveGI != ReceiveGI.LightProbes)
            {
                r.receiveGI = ReceiveGI.LightProbes;
                changed = true;
            }
            if (r.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.Off)
            {
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(r);
                count++;
            }
        }
        Debug.Log($"[FactoryHRISetup] 倉庫メッシュ {count} 件を Realtime 照明対象に設定");
    }

    /// <summary>
    /// FactoryExperiment から欠落した倉庫本体（床・壁・天井）を元プレハブから戻す。
    /// </summary>
    static int EnsureWarehouseBuilding()
    {
        if (GameObject.Find("Building") != null) return 0;

        const string prefabPath = WarehouseRoot + "/Scene_Warehouse/Background/Prefabs/Building.prefab";
        const string fbxPath = WarehouseRoot + "/Scene_Warehouse/Background/Models/Building.fbx";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)
                     ?? AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (prefab == null)
        {
            Debug.LogError("[FactoryHRISetup] Building プレハブが見つかりません。");
            return 0;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "Building";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            r.receiveGI = ReceiveGI.LightProbes;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
        ForceURPMaterials(instance);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[FactoryHRISetup] 欠落していた Building（床・壁・天井）を復元しました。");
        return 1;
    }

    static void EnsureWarehouseReflectionProbe()
    {
        if (GameObject.Find("HRI_WarehouseReflectionProbe") != null) return;

        var go = new GameObject("HRI_WarehouseReflectionProbe");
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
        probe.size = new Vector3(120f, 30f, 120f);
        probe.center = Vector3.zero;
        probe.intensity = 0.85f;
        probe.boxProjection = true;
        probe.resolution = 128;
        probe.backgroundColor = new Color(0.18f, 0.17f, 0.16f);
        go.transform.position = Vector3.zero;
    }

    static void RestoreToplightEmission()
    {
        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Toplight_Glass", "Toplight_Wall", "TopLight_Frame", "Light_Prefab", "Light", "Exit"
        };

        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { WarehouseRoot }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null || !names.Contains(mat.name)) continue;

            var emissive = new Color(0.95f, 0.92f, 0.82f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissive);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            count++;
        }
        if (count > 0) AssetDatabase.SaveAssets();
    }

    static void TuneWarehousePointLights()
    {
        int count = 0;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l == null || l.type != LightType.Point) continue;
            l.intensity = 15f;
            l.range = Mathf.Max(l.range, 10f);
            l.shadows = LightShadows.None;
            count++;
        }
        Debug.Log($"[FactoryHRISetup] 倉庫ポイントライト {count} 件を調整 (intensity=15, Drone同等)");
    }

    static void EnsureWarehouseFillLight()
    {
        var existing = GameObject.Find("HRI_WarehouseFillLight");
        Light light;
        if (existing != null)
        {
            light = existing.GetComponent<Light>();
        }
        else
        {
            var go = new GameObject("HRI_WarehouseFillLight");
            light = go.AddComponent<Light>();
            go.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
        }

        if (light == null) return;
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.shadows = LightShadows.Soft;
    }

    static int RepairAllWarehouseMaterialAssets()
    {
        var urpLit = URPMaterialFixer.FindUrpLitShader();
        if (urpLit == null) return 0;

        int count = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { WarehouseRoot }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null) continue;
                if (URPMaterialFixer.TryRepairMaterialAsset(mat, urpLit))
                {
                    EditorUtility.SetDirty(mat);
                    count++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            if (count > 0) AssetDatabase.SaveAssets();
        }
        return count;
    }

    static int FixNullMaterialOverrides()
    {
        int count = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || !HasNullSharedMaterial(r)) continue;

            if (PrefabUtility.IsPartOfAnyPrefab(r.gameObject))
            {
                PrefabUtility.RevertObjectOverride(r, InteractionMode.AutomatedAction);
                count++;
                continue;
            }

            var urpLit = URPMaterialFixer.FindUrpLitShader();
            if (urpLit == null) continue;

            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null) continue;
                mats[i] = LoadShelfFallbackMaterial(r.gameObject.name, urpLit);
            }
            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
            count++;
        }
        return count;
    }

    static bool HasNullSharedMaterial(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
            if (m == null) return true;
        return false;
    }

    static void SetupRenderSettings()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.11f, 0.10f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 25f;
        RenderSettings.fogEndDistance = 55f;
        RenderSettings.fogColor = new Color(0.15f, 0.14f, 0.13f);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 実験オブジェクト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    static void AddStationsAndCargoZones()
    {
        var existing = GameObject.Find("=== Stations ===");
        if (existing != null) Object.DestroyImmediate(existing);
        var existingZones = GameObject.Find("=== Cargo Zones ===");
        if (existingZones != null) Object.DestroyImmediate(existingZones);

        var root = new GameObject("=== Stations ===");
        BuildStation(root.transform, "Station_A", FactoryLayout.StationA, new Color(0.10f, 0.90f, 0.30f));
        BuildStation(root.transform, "Station_B", FactoryLayout.StationB, new Color(0.85f, 0.18f, 0.14f));

        int i = 0;
        foreach (var wb in FactoryLayout.WorkbenchPositions)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m.name = $"WB_{++i}";
            m.transform.SetParent(root.transform, false);
            m.transform.position = wb + Vector3.up * 0.3f;
            m.transform.localScale = new Vector3(0.20f, 0.30f, 0.20f);
            ApplyMat(m, new Color(0.85f, 0.70f, 0.10f), 0.5f, 0.7f);
            Object.DestroyImmediate(m.GetComponent<Collider>());
        }

        var zoneRoot = new GameObject("=== Cargo Zones ===");
        var plasticA = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/PlasticBox_A.prefab");
        var plasticB = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/PlasticBox_B.prefab");

        int zoneI = 0;
        foreach (var zone in FactoryLayout.PickupZones)
        {
            var markerGo = new GameObject(zone.Id);
            markerGo.transform.SetParent(zoneRoot.transform);
            markerGo.transform.position = zone.FloorCenter;
            var marker = markerGo.AddComponent<CargoZoneMarker>();
            marker.Kind = FactoryLayout.CargoZoneKind.Pickup;

            var prefab = (zoneI % 2 == 0 ? plasticA : plasticB) ?? plasticA;
            zoneI++;
            if (prefab != null)
            {
                var box = (GameObject)PrefabUtility.InstantiatePrefab(prefab, markerGo.transform);
                box.name = $"PickupBox_{zoneI:D2}";
                box.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                box.isStatic = false;
                ForceURPMaterials(box);
                DisableDecorativeCollidersOn(box);
            }
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"PickupBox_{zoneI:D2}";
                box.transform.SetParent(markerGo.transform, false);
                box.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                box.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
                ApplyMat(box, new Color(0.72f, 0.45f, 0.18f), 0f, 0.3f);
                Object.DestroyImmediate(box.GetComponent<Collider>());
            }
        }

        foreach (var zone in FactoryLayout.DropZones)
        {
            var markerGo = new GameObject(zone.Id);
            markerGo.transform.SetParent(zoneRoot.transform);
            markerGo.transform.position = zone.FloorCenter;
            var marker = markerGo.AddComponent<CargoZoneMarker>();
            marker.Kind = FactoryLayout.CargoZoneKind.Drop;
        }
    }

    static void BuildStation(Transform parent, string name, Vector3 pos, Color color)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;

        var plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plat.name = "Platform";
        plat.transform.SetParent(root.transform, false);
        plat.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        plat.transform.localScale = new Vector3(4f, 0.04f, 4f);
        ApplyMat(plat, color * 0.5f, 0.3f, 0.6f);
        Object.DestroyImmediate(plat.GetComponent<Collider>());

        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(root.transform, false);
        pillar.transform.localPosition = new Vector3(-1.6f, 1.2f, -1.2f);
        pillar.transform.localScale = new Vector3(0.12f, 1.2f, 0.12f);
        ApplyMat(pillar, color, 0.6f, 0.75f);
        Object.DestroyImmediate(pillar.GetComponent<Collider>());
    }

    static void AddFactoryData()
    {
        var existing = Object.FindFirstObjectByType<FactoryData>();
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = new GameObject("FactoryData");
        var data = go.AddComponent<FactoryData>();
        data.WorkbenchPositions.AddRange(FactoryLayout.WorkbenchPositions);
    }

    static void BuildPlayer()
    {
        var old = GameObject.Find("XR Origin");
        if (old != null) Object.DestroyImmediate(old);

        var origin = new GameObject("XR Origin");
        origin.transform.position = FactoryLayout.PlayerSpawnPosition;
        var cc = origin.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.radius = 0.3f;
        origin.AddComponent<PlayerLocomotion>();
        origin.AddComponent<DynamicCrossingLineTracker>();

        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(origin.transform, false);
        offset.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(offset.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        camGo.AddComponent<AudioListener>();

        var vig = new GameObject("ComfortVignette");
        vig.transform.SetParent(origin.transform, false);
        vig.AddComponent<Canvas>();
        vig.AddComponent<HRIRobot.Experiment.ComfortVignette>();
    }

    static void BuildSystems()
    {
        var old = GameObject.Find("ExperimentSystems");
        if (old != null) Object.DestroyImmediate(old);

        var systems = new GameObject("ExperimentSystems");
        var spawner = systems.AddComponent<AGVSpawner>();
        AssignAgvPrefab(spawner);
        systems.AddComponent<AGVPathVisualizer>();
        systems.AddComponent<ExperimentManager>();
        systems.AddComponent<ExperimentStartMenu>();
        systems.AddComponent<MeasurementHub>();
        var tracker = systems.GetComponent<PathDeviationTracker>() ?? systems.AddComponent<PathDeviationTracker>();
        tracker.optimalPathWaypoints = BuildCorridorWaypoints(FactoryLayout.StationA, FactoryLayout.StationB);
    }

    static void AssignAgvPrefab(AGVSpawner spawner)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AgvPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[FactoryHRISetup] AGV プレハブが見つかりません: {AgvPrefabPath}");
            return;
        }
        var so = new SerializedObject(spawner);
        var p = so.FindProperty("agvPrefab");
        if (p == null) return;
        p.objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawner);
    }

    static Vector3[] BuildCorridorWaypoints(Vector3 a, Vector3 b)
    {
        float y = a.y;
        return new[]
        {
            a,
            new Vector3(21.475f, y, 12f),
            new Vector3(21.475f, y, 30f),
            new Vector3(21.475f, y, 48f),
            new Vector3(12f, b.y, 55f),
            b,
        };
    }

    static void MarkWalkableSurfaces()
    {
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (col == null) continue;
            string n = col.gameObject.name.ToLowerInvariant();
            if (!(n.Contains("floor") || n.Contains("building") || n.Contains("ground") || n.Contains("sunoko")))
                continue;
            var flags = GameObjectUtility.GetStaticEditorFlags(col.gameObject) | StaticEditorFlags.NavigationStatic;
            GameObjectUtility.SetStaticEditorFlags(col.gameObject, flags);
        }
    }

    static void BakeNavMesh()
    {
        EditorNavMeshUtility.ClearNavMeshes();
        EditorNavMeshUtility.BakeNavMesh();
        Debug.Log("[FactoryHRISetup] NavMesh をベイクしました。");
    }

    static void BuildGrayboxEnvironment()
    {
        var root = new GameObject("Factory_Environment");
        var data = root.AddComponent<FactoryData>();
        data.WorkbenchPositions.AddRange(FactoryLayout.WorkbenchPositions);

        var floorMat = Mat(new Color(0.22f, 0.23f, 0.24f), 0.05f, 0.4f);
        var wallMat = Mat(new Color(0.35f, 0.36f, 0.37f), 0f, 0.15f);
        var shelfMat = Mat(new Color(0.42f, 0.38f, 0.32f), 0.1f, 0.25f);
        var markA = Mat(new Color(0.1f, 0.7f, 0.2f), 0f, 0.2f);
        var markB = Mat(new Color(0.8f, 0.15f, 0.12f), 0f, 0.2f);
        var boxMat = Mat(new Color(0.72f, 0.45f, 0.18f), 0f, 0.3f);

        float w = FactoryLayout.BuildingXMax - FactoryLayout.BuildingXMin;
        float l = FactoryLayout.BuildingZMax - FactoryLayout.BuildingZMin;
        Vector3 center = new Vector3(
            (FactoryLayout.BuildingXMin + FactoryLayout.BuildingXMax) * 0.5f,
            0f,
            (FactoryLayout.BuildingZMin + FactoryLayout.BuildingZMax) * 0.5f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root.transform);
        floor.transform.position = center;
        floor.transform.localScale = new Vector3(w / 10f, 1f, l / 10f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

        float wallH = 5f;
        MakeWall("Wall_N", root.transform, new Vector3(center.x, wallH * 0.5f, FactoryLayout.BuildingZMax), new Vector3(w, wallH, 0.3f), wallMat);
        MakeWall("Wall_S", root.transform, new Vector3(center.x, wallH * 0.5f, FactoryLayout.BuildingZMin), new Vector3(w, wallH, 0.3f), wallMat);
        MakeWall("Wall_E", root.transform, new Vector3(FactoryLayout.BuildingXMax, wallH * 0.5f, center.z), new Vector3(0.3f, wallH, l), wallMat);
        MakeWall("Wall_W", root.transform, new Vector3(FactoryLayout.BuildingXMin, wallH * 0.5f, center.z), new Vector3(0.3f, wallH, l), wallMat);

        foreach (var wb in FactoryLayout.WorkbenchPositions)
        {
            bool left = wb.x < FactoryLayout.CenterX;
            Vector3 shelfPos = wb + new Vector3(left ? -2.2f : 2.2f, 1.1f, 0f);
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = "Shelf";
            shelf.transform.SetParent(root.transform);
            shelf.transform.position = shelfPos;
            shelf.transform.localScale = new Vector3(1.2f, 2.2f, 3.6f);
            shelf.GetComponent<Renderer>().sharedMaterial = shelfMat;
            GameObjectUtility.SetStaticEditorFlags(shelf, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);

            var bench = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bench.name = "Workbench";
            bench.transform.SetParent(root.transform);
            bench.transform.position = new Vector3(wb.x, 0.45f, wb.z);
            bench.transform.localScale = new Vector3(1.6f, 0.9f, 1.2f);
            bench.GetComponent<Renderer>().sharedMaterial = shelfMat;
            GameObjectUtility.SetStaticEditorFlags(bench, StaticEditorFlags.NavigationStatic);
        }

        MakePad("Station_A", root.transform, FactoryLayout.StationA, markA);
        MakePad("Station_B", root.transform, FactoryLayout.StationB, markB);

        int zoneI = 0;
        foreach (var zone in FactoryLayout.PickupZones)
        {
            var markerGo = new GameObject(zone.Id);
            markerGo.transform.SetParent(root.transform);
            markerGo.transform.position = zone.FloorCenter;
            var marker = markerGo.AddComponent<CargoZoneMarker>();
            marker.Kind = FactoryLayout.CargoZoneKind.Pickup;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"PickupBox_{++zoneI:D2}";
            box.transform.SetParent(markerGo.transform, false);
            box.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            box.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
            box.GetComponent<Renderer>().sharedMaterial = boxMat;
            Object.DestroyImmediate(box.GetComponent<Collider>());
        }

        foreach (var zone in FactoryLayout.DropZones)
        {
            var markerGo = new GameObject(zone.Id);
            markerGo.transform.SetParent(root.transform);
            markerGo.transform.position = zone.FloorCenter;
            var marker = markerGo.AddComponent<CargoZoneMarker>();
            marker.Kind = FactoryLayout.CargoZoneKind.Drop;
        }
    }

    static void BuildGrayboxLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.19f, 0.21f);
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void MakeWall(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
    }

    static void MakePad(string name, Transform parent, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = new Vector3(pos.x, 0.02f, pos.z);
        go.transform.localScale = new Vector3(2.4f, 0.02f, 2.4f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static void ApplyMat(GameObject go, Color c, float metallic, float smooth)
    {
        var r = go.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = Mat(c, metallic, smooth);
    }

    static Material Mat(Color c, float metallic, float smooth)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { color = c };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
        return mat;
    }

    static Color GetColor(Material m, string p1, string p2 = null, Color def = default)
    {
        if (m.HasProperty(p1)) return m.GetColor(p1);
        if (p2 != null && m.HasProperty(p2)) return m.GetColor(p2);
        return def == default ? Color.white : def;
    }

    static float GetFloat(Material m, string p1, string p2 = null, float def = 0f)
    {
        if (m.HasProperty(p1)) return m.GetFloat(p1);
        if (p2 != null && m.HasProperty(p2)) return m.GetFloat(p2);
        return def;
    }

    static Texture GetTex(Material m, string[] priorityNames, string[] hintWords)
    {
        foreach (var p in priorityNames)
            if (m.HasProperty(p))
            {
                var t = m.GetTexture(p);
                if (t != null) return t;
            }

        var so = new SerializedObject(m);
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
            if (all.TryGetValue(p, out var t2)) return t2;

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
                    !lower.Contains("detail") && !lower.Contains("occlusion") &&
                    !lower.Contains("mask") && !lower.Contains("smooth"))
                    return kv.Value;
            }
        }
        return null;
    }
}
