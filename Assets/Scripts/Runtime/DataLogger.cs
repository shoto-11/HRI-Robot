using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using HRIRobot.Risk;

namespace HRIRobot.Experiment
{
    /// <summary>
    /// 毎フレームのデータをCSVに記録する（仕様書 5.3）。頭部の位置・向き、
    /// 「危険と感じた」ボタン押下タイミング、試行条件ラベル、各車両のスコア推移を記録し、
    /// セッション後にADB経由でPCへ吸い出す運用を想定する。
    /// </summary>
    public class DataLogger : MonoBehaviour
    {
        [Header("記録対象")]
        public Transform headTransform; // OVRCameraRig / XR Origin の Main Camera
        public VehicleRiskCalculator[] trackedVehicles;

        [Header("試行ラベル")]
        public string participantId = "P00";
        public string trialLabel = "trial";

        StreamWriter writer;
        string filePath;

        void Start()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Logs");
            Directory.CreateDirectory(dir);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            filePath = Path.Combine(dir, $"{participantId}_{trialLabel}_{timestamp}.csv");

            writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine(BuildHeader());
        }

        string BuildHeader()
        {
            var sb = new StringBuilder();
            sb.Append("time,condition,trialLabel,headPosX,headPosY,headPosZ,headRotX,headRotY,headRotZ,dangerButtonPressed");
            if (trackedVehicles != null)
            {
                for (int v = 0; v < trackedVehicles.Length; v++)
                {
                    var lines = trackedVehicles[v]?.crossingLines;
                    int lineCount = lines != null ? lines.Length : 0;
                    for (int l = 0; l < lineCount; l++)
                        sb.Append($",veh{v}_line{l}_ttc,veh{v}_line{l}_score");
                }
            }
            return sb.ToString();
        }

        public void LogFrame(bool dangerButtonPressed)
        {
            if (writer == null) return;

            var sb = new StringBuilder();
            var condition = ExperimentConditionManager.Instance != null
                ? ExperimentConditionManager.Instance.currentCondition.ToString()
                : "Unknown";

            Vector3 pos = headTransform != null ? headTransform.position : Vector3.zero;
            Vector3 rot = headTransform != null ? headTransform.eulerAngles : Vector3.zero;

            sb.Append(Time.time.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(condition).Append(',');
            sb.Append(trialLabel).Append(',');
            sb.Append(pos.x).Append(',').Append(pos.y).Append(',').Append(pos.z).Append(',');
            sb.Append(rot.x).Append(',').Append(rot.y).Append(',').Append(rot.z).Append(',');
            sb.Append(dangerButtonPressed ? 1 : 0);

            if (trackedVehicles != null)
            {
                foreach (var veh in trackedVehicles)
                {
                    var lines = veh?.crossingLines;
                    if (lines == null) continue;
                    foreach (var line in lines)
                    {
                        float ttc = line != null && !float.IsInfinity(line.currentTTC) ? line.currentTTC : -1f;
                        float score = line != null ? line.currentScore : 0f;
                        sb.Append(',').Append(ttc.ToString(CultureInfo.InvariantCulture));
                        sb.Append(',').Append(score.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            writer.WriteLine(sb.ToString());
        }

        void Update()
        {
            LogFrame(DangerButtonInput.WasPressedThisFrame);
        }

        void OnDestroy()
        {
            writer?.Flush();
            writer?.Dispose();
        }

        void OnApplicationQuit()
        {
            writer?.Flush();
            writer?.Dispose();
        }
    }
}
