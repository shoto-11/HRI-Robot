using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public class MeasurementHub : MonoBehaviour
{
    public static MeasurementHub Instance { get; private set; }

    [Header("評価データ")]
    [Tooltip("ON のとき、各ケース完了ごとに 1 つの Excel ファイルへ追記保存する。")]
    [SerializeField] bool evaluationEnabled = true;
    public bool EvaluationEnabled => evaluationEnabled;

    public CollisionCounter Collision { get; private set; }
    public TaskTimer Timer { get; private set; }
    public PathDeviationTracker PathTracker { get; private set; }

    string _sessionId;
    string _xlsxPath;
    readonly List<string[]> _rows = new();
    readonly List<ExperimentWorkbook.TrajectorySheet> _sheets = new();

    static readonly string[] CsvHeader =
    {
        "SessionID", "Timestamp", "Condition", "CaseIndex",
        "CompletionTime_s", "Collisions", "TraveledPath_m", "CollisionEvents"
    };

    public string LastExportPath => _xlsxPath;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _xlsxPath = Path.Combine(Application.persistentDataPath, $"HRI_AGV_Result_{_sessionId}.xlsx");
    }

    void Start()
    {
        Collision = GetOrAdd<CollisionCounter>();
        Timer = GetOrAdd<TaskTimer>();
        PathTracker = GetOrAdd<PathDeviationTracker>();
    }

    T GetOrAdd<T>() where T : MonoBehaviour => GetComponent<T>() ?? gameObject.AddComponent<T>();

    public void OnCaseStart(string conditionLabel, int caseIndex)
    {
        if (!evaluationEnabled) return;
        Collision.ResetCount();
        Timer.ResetTimer();
        PathTracker.ResetTracker();
        Timer.StartTiming();
        PathTracker.StartTracking();
        Debug.Log($"[MeasurementHub] 計測開始: {conditionLabel} ケース {caseIndex + 1}");
    }

    public void OnCaseComplete(string conditionLabel, int caseIndex)
    {
        if (!evaluationEnabled) return;
        Timer.StopTiming();
        PathTracker.StopTracking();
        _rows.Add(new[]
        {
            _sessionId,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            conditionLabel,
            (caseIndex + 1).ToString(),
            Timer.ElapsedTime.ToString("F2"),
            Collision.Count.ToString(),
            PathTracker.TotalPathLength.ToString("F2"),
            Collision.FormatEvents(),
        });

        string sheetName = $"{conditionLabel}_Case{(caseIndex + 1):00}";
        int dup = 1;
        string unique = sheetName;
        while (_sheets.Exists(s => s.SheetName == unique))
        {
            dup++;
            unique = $"{sheetName}_{dup}";
        }
        _sheets.Add(new ExperimentWorkbook.TrajectorySheet
        {
            SheetName = unique,
            Samples = PathTracker.CopySamples(),
        });

        Debug.Log($"[MeasurementHub] 記録: {conditionLabel} ケース{caseIndex + 1} | 時間={Timer.ElapsedTime:F2}s 衝突={Collision.Count} 移動距離={PathTracker.TotalPathLength:F2}m 点={PathTracker.Samples.Count} events={Collision.FormatEvents()}");
        ExportWorkbook();
    }

    public void ExportCSV() => ExportWorkbook();

    public void ExportWorkbook()
    {
        if (!evaluationEnabled || _rows.Count == 0)
        {
            Debug.Log("[MeasurementHub] 保存スキップ");
            return;
        }

        try
        {
            ExperimentWorkbook.Write(_xlsxPath, _rows.ToArray(), _sheets);
            Debug.Log($"[MeasurementHub] 結果保存: {_xlsxPath}  (Summary + {_sheets.Count} ケースシート)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MeasurementHub] xlsx 保存失敗: {e.Message}");
        }
    }

    [ContextMenu("結果ファイルのフォルダを開く")]
    void RevealCsvFolder()
    {
        string dir = Application.persistentDataPath;
        Debug.Log($"[MeasurementHub] 保存先: {dir}");
#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(string.IsNullOrEmpty(_xlsxPath) ? dir : _xlsxPath);
#endif
    }

    [ContextMenu("Write Dummy Xlsx")]
    void WriteDummyXlsx()
    {
        _rows.Add(new[] { _sessionId, "dummy", "Baseline", "1", "1.50", "1", "0.12", "((0.50,3,10.000,0.200,22.000))" });
        _sheets.Add(new ExperimentWorkbook.TrajectorySheet
        {
            SheetName = "Baseline_Case01",
            Samples = new List<PathDeviationTracker.PathSample>
            {
                new PathDeviationTracker.PathSample(0f, new Vector3(22f, 1.6f, 9f)),
                new PathDeviationTracker.PathSample(0.5f, new Vector3(21.5f, 1.6f, 12f)),
            }
        });
        ExportWorkbook();
    }

    public void ToggleEvaluation()
    {
        evaluationEnabled = !evaluationEnabled;
        Debug.Log($"[MeasurementHub] 評価モード: {(evaluationEnabled ? "ON" : "OFF")}");
    }
}
