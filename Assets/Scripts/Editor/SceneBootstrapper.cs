using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using HRIRobot.Risk;
using HRIRobot.Experiment;

namespace HRIRobot.EditorTools
{
    /// <summary>
    /// 仕様書 2.1 のレイアウト（スタート→横断歩道1→リセット1→横断歩道2→リセット2→横断歩道3→ゴール）を
    /// プリミティブ（Cube/Plane等）で構築するプロトタイプシーンジェネレータ。
    /// 街区間・横断歩道幅はいずれも15mとし、経路の進行方向をZ軸、横断方向をX軸とする。
    /// 実物のモデル・道路プレハブ等は含まないため、後日アセットに差し替える前提のホワイトボックス。
    /// </summary>
    public static class SceneBootstrapper
    {
        const float BLOCK_SPACING = 15f;
        const float CROSSING_WIDTH = 15f;
        const float ROAD_HALF_WIDTH = 4f;

        [MenuItem("HRI/Scene/Build Prototype Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("HRI_Environment");
            var pedestrian = BuildPedestrianRig(root.transform);

            float z = 0f;
            var startMarker = CreateMarker("Start", root.transform, new Vector3(0, 0, z));
            z += BLOCK_SPACING;

            var crossing1 = BuildCrossingZone("Crosswalk_1", root.transform, ref z);
            var reset1 = BuildResetPoint("ResetPoint_1", root.transform, ref z);
            var crossing2 = BuildCrossingZone("Crosswalk_2", root.transform, ref z);
            var reset2 = BuildResetPoint("ResetPoint_2", root.transform, ref z);
            var crossing3 = BuildCrossingZone("Crosswalk_3", root.transform, ref z);

            var goalMarker = CreateMarker("Goal", root.transform, new Vector3(0, 0, z));

            BuildCityBlocks(root.transform, z);

            var condManager = new GameObject("ExperimentConditionManager").AddComponent<ExperimentConditionManager>();
            condManager.transform.SetParent(root.transform);

            var vehiclesRoot = new GameObject("Vehicles").transform;
            vehiclesRoot.SetParent(root.transform);

            var v1 = BuildSampleVehicle("Vehicle_A_Sedan", vehiclesRoot, crossing1, pedestrian);
            var v2 = BuildSampleVehicle("Vehicle_B_Truck", vehiclesRoot, crossing2, pedestrian);
            var v3 = BuildSampleVehicle("Vehicle_C_Sedan", vehiclesRoot, crossing3, pedestrian);

            reset1.GetComponent<ResetPointManager>().pedestrian = pedestrian;
            reset1.GetComponent<ResetPointManager>().managedVehicles.Add(new ResetPointManager.VehicleState { vehicle = v1.transform });
            reset2.GetComponent<ResetPointManager>().pedestrian = pedestrian;
            reset2.GetComponent<ResetPointManager>().managedVehicles.Add(new ResetPointManager.VehicleState { vehicle = v2.transform });

            BuildLighting(root.transform);
            BuildComfortVignette(root.transform);

            var logger = new GameObject("DataLogger").AddComponent<DataLogger>();
            logger.transform.SetParent(root.transform);
            logger.headTransform = pedestrian;
            logger.trackedVehicles = new[]
            {
                v1.GetComponent<VehicleRiskCalculator>(),
                v2.GetComponent<VehicleRiskCalculator>(),
                v3.GetComponent<VehicleRiskCalculator>(),
            };

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/PrototypeExperiment.unity");

            Debug.Log("[HRI SceneBootstrapper] Prototype scene built at Assets/Scenes/PrototypeExperiment.unity");
        }

        static Transform BuildPedestrianRig(Transform parent)
        {
            var rig = new GameObject("Pedestrian");
            rig.tag = "Player";
            rig.transform.SetParent(parent);
            rig.transform.position = new Vector3(0, 0, 0);

            var cam = new GameObject("Main Camera");
            cam.transform.SetParent(rig.transform);
            cam.transform.localPosition = new Vector3(0, 1.6f, 0);
            var camera = cam.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            cam.AddComponent<AudioListener>();
            cam.tag = "MainCamera";

            var tpd = cam.AddComponent<TrackedPoseDriver>();
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;

            return rig.transform;
        }

        static GameObject CreateMarker(string name, Transform parent, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos + Vector3.up * 0.05f;
            go.transform.localScale = new Vector3(1f, 0.05f, 1f);
            return go;
        }

        static Transform BuildCrossingZone(string name, Transform parent, ref float z)
        {
            var zoneRoot = new GameObject(name);
            zoneRoot.transform.SetParent(parent);

            float zStart = z;
            float zEnd = z + CROSSING_WIDTH;
            float zCenter = (zStart + zEnd) * 0.5f;

            var lineStart = new GameObject("CrossingLine_Start").transform;
            lineStart.SetParent(zoneRoot.transform);
            lineStart.position = new Vector3(-CROSSING_WIDTH * 0.5f, 0, zCenter);

            var lineEnd = new GameObject("CrossingLine_End").transform;
            lineEnd.SetParent(zoneRoot.transform);
            lineEnd.position = new Vector3(CROSSING_WIDTH * 0.5f, 0, zCenter);

            // 横断歩道の路面（視覚化のみ・当たり判定なし）
            var stripes = GameObject.CreatePrimitive(PrimitiveType.Plane);
            stripes.name = "CrosswalkSurface";
            stripes.transform.SetParent(zoneRoot.transform);
            stripes.transform.position = new Vector3(0, 0.01f, zCenter);
            stripes.transform.localScale = new Vector3(CROSSING_WIDTH / 10f, 1f, CROSSING_WIDTH / 10f);
            var stripeMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.85f, 0.85f) };
            stripes.GetComponent<Renderer>().sharedMaterial = stripeMat;

            // 交差する側道（車両が通過する道路）
            var sideRoad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sideRoad.name = "SideRoad";
            sideRoad.transform.SetParent(zoneRoot.transform);
            sideRoad.transform.position = new Vector3(0, -0.05f, zCenter);
            sideRoad.transform.localScale = new Vector3(60f, 0.1f, ROAD_HALF_WIDTH * 2f);
            var roadMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.25f, 0.25f, 0.27f) };
            sideRoad.GetComponent<Renderer>().sharedMaterial = roadMat;

            z = zEnd + BLOCK_SPACING;
            return zoneRoot.transform; // child0/1 = lineStart/lineEnd
        }

        static GameObject BuildResetPoint(string name, Transform parent, ref float z)
        {
            var go = CreateMarker(name, parent, new Vector3(0, 0, z));
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.2f, 0.6f, 1f) };
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<ResetPointManager>();
            z += BLOCK_SPACING;
            return go;
        }

        static void BuildCityBlocks(Transform parent, float totalLength)
        {
            var blocksRoot = new GameObject("CityBlocks").transform;
            blocksRoot.SetParent(parent);

            float buildingSize = 10f;
            float xOffset = ROAD_HALF_WIDTH + BLOCK_SPACING * 0.5f + buildingSize * 0.5f;

            for (float z = BLOCK_SPACING * 0.5f; z < totalLength; z += BLOCK_SPACING)
            {
                foreach (float xSign in new[] { -1f, 1f })
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    b.name = $"Building_{z:F0}_{(xSign < 0 ? "L" : "R")}";
                    b.transform.SetParent(blocksRoot);
                    float height = Random.Range(4f, 12f);
                    b.transform.position = new Vector3(xSign * xOffset, height * 0.5f, z);
                    b.transform.localScale = new Vector3(buildingSize, height, buildingSize);
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                    {
                        color = new Color(Random.Range(0.5f, 0.8f), Random.Range(0.5f, 0.8f), Random.Range(0.55f, 0.85f))
                    };
                    b.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent);
            ground.transform.position = new Vector3(0, -0.05f, totalLength * 0.5f);
            ground.transform.localScale = new Vector3((totalLength + 40f) / 10f, 1f, (totalLength + 40f) / 10f);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.4f, 0.42f, 0.4f) };
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        }

        static GameObject BuildSampleVehicle(string name, Transform parent, Transform crossingZone, Transform pedestrian)
        {
            var lineStart = crossingZone.Find("CrossingLine_Start");
            var lineEnd = crossingZone.Find("CrossingLine_End");
            Vector3 crossCenter = (lineStart.position + lineEnd.position) * 0.5f;

            var vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.name = name;
            vehicle.transform.SetParent(parent);
            Vector3 startPos = crossCenter + new Vector3(-20f, 0.5f, 0f);
            vehicle.transform.position = startPos;
            vehicle.transform.localScale = new Vector3(1.8f, 1f, 4.2f);
            vehicle.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.7f, 0.1f, 0.1f) };
            vehicle.GetComponent<Renderer>().sharedMaterial = mat;

            var risk = vehicle.AddComponent<VehicleRiskCalculator>();
            risk.pedestrian = pedestrian;
            risk.currentSpeed = 8.3f; // 約30km/h
            risk.plannedPath = new[]
            {
                startPos,
                crossCenter,
                crossCenter + new Vector3(20f, 0.5f, 0f),
            };
            risk.crossingLines = new[]
            {
                new VehicleRiskCalculator.CrossingLine { label = crossingZone.name, start = lineStart, end = lineEnd },
            };

            var pathGO = new GameObject("PathVisual");
            pathGO.transform.SetParent(vehicle.transform);
            pathGO.transform.localPosition = Vector3.zero;
            var lr = pathGO.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;

            var stopLineGO = new GameObject("StopLine");
            stopLineGO.transform.SetParent(vehicle.transform);
            var stopLr = stopLineGO.AddComponent<LineRenderer>();
            stopLr.material = new Material(Shader.Find("Sprites/Default"));
            stopLr.useWorldSpace = true;

            var pr = pathGO.AddComponent<PathRenderer>();
            pr.risk = risk;
            pr.crossingLineIndex = 0;
            pr.futurePathPoints = risk.plannedPath;
            pr.stopLineRenderer = stopLr;

            return vehicle;
        }

        static void BuildLighting(Transform parent)
        {
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.SetParent(parent);
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        static void BuildComfortVignette(Transform parent)
        {
            var canvasGO = new GameObject("ComfortVignetteCanvas");
            canvasGO.transform.SetParent(parent);
            canvasGO.AddComponent<Canvas>();
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<ComfortVignette>();
        }
    }
}
