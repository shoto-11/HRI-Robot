using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace HRIRobot.EditorTools
{
    /// <summary>
    /// URP・XR Plug-in Management (OpenXR) をスクリプトから初期設定する（仕様書 5.1）。
    /// 対象ビルドターゲット: Standalone（デバッグ用Link/Air Link）と Android（Quest 3実機）。
    /// </summary>
    public static class ProjectSetup
    {
        const string URP_ASSET_PATH = "Assets/Settings/HRI_URP_Asset.asset";
        const string URP_RENDERER_PATH = "Assets/Settings/HRI_URP_Renderer.asset";
        const string XR_SETTINGS_PATH = "Assets/XR/HRI_XRGeneralSettings.asset";

        [MenuItem("HRI/Setup/Run All Setup")]
        public static void RunAll()
        {
            CreateAndAssignURP();
            ConfigureXRPluginManagement();
            AssetDatabase.SaveAssets();
            Debug.Log("[HRI ProjectSetup] RunAll complete.");
        }

        [MenuItem("HRI/Setup/1. Create URP Asset And Assign")]
        public static void CreateAndAssignURP()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(URP_RENDERER_PATH);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, URP_RENDERER_PATH);
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(URP_ASSET_PATH);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipelineAsset, URP_ASSET_PATH);
            }

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;

            AssetDatabase.SaveAssets();
            Debug.Log("[HRI ProjectSetup] URP asset created and assigned as default render pipeline.");
        }

        [MenuItem("HRI/Setup/2. Configure XR Plugin Management (OpenXR)")]
        public static void ConfigureXRPluginManagement()
        {
            ConfigureForBuildTargetGroup(BuildTargetGroup.Standalone);
            ConfigureForBuildTargetGroup(BuildTargetGroup.Android);
        }

        static void ConfigureForBuildTargetGroup(BuildTargetGroup group)
        {
            try
            {
                if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                        out XRGeneralSettingsPerBuildTarget buildTargetSettings) || buildTargetSettings == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/XR"))
                        AssetDatabase.CreateFolder("Assets", "XR");

                    buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(buildTargetSettings, XR_SETTINGS_PATH);
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
                }

                XRGeneralSettings settings = buildTargetSettings.SettingsForBuildTarget(group);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                    settings.name = $"XR Settings {group}";
                    AssetDatabase.AddObjectToAsset(settings, buildTargetSettings);
                    buildTargetSettings.SetSettingsForBuildTarget(group, settings);
                }

                if (settings.Manager == null)
                {
                    var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                    manager.name = $"XR Manager Settings {group}";
                    AssetDatabase.AddObjectToAsset(manager, buildTargetSettings);
                    settings.Manager = manager;
                }

                bool assigned = XRPackageMetadataStore.AssignLoader(settings.Manager, typeof(OpenXRLoader).FullName, group);
                Debug.Log($"[HRI ProjectSetup] OpenXR loader for {group}: {(assigned ? "assigned" : "already assigned or unavailable")}");

                EditorUtility.SetDirty(buildTargetSettings);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HRI ProjectSetup] XR setup for {group} failed ({e.Message}). Configure manually via Project Settings > XR Plug-in Management.");
            }
        }
    }
}
