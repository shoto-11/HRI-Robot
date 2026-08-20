using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

public static class ExperimentDataTest
{
    [MenuItem("HRI/Test Experiment Workbook", false, 40)]
    public static void WriteTestWorkbook()
    {
        string path = Path.Combine(Application.persistentDataPath, "HRI_AGV_Result_TEST.xlsx");
        var summary = new[]
        {
            new[] { "test", "2026-08-19 00:00:00", "Baseline", "1", "12.50", "1", "3.20", "((4.50,2,18.100,0.200,22.000))" },
            new[] { "test", "2026-08-19 00:01:00", "Proposed", "1", "11.00", "0", "2.10", "()" },
        };
        var sheets = new List<ExperimentWorkbook.TrajectorySheet>
        {
            new ExperimentWorkbook.TrajectorySheet
            {
                SheetName = "Baseline_Case01",
                Samples = new List<PathDeviationTracker.PathSample>
                {
                    new PathDeviationTracker.PathSample(0f, new Vector3(22f, 1.6f, 9f)),
                    new PathDeviationTracker.PathSample(0.5f, new Vector3(21f, 1.6f, 15f)),
                }
            },
            new ExperimentWorkbook.TrajectorySheet
            {
                SheetName = "Proposed_Case01",
                Samples = new List<PathDeviationTracker.PathSample>
                {
                    new PathDeviationTracker.PathSample(0f, new Vector3(22f, 1.6f, 9f)),
                    new PathDeviationTracker.PathSample(0.5f, new Vector3(20f, 1.6f, 18f)),
                }
            }
        };

        ExperimentWorkbook.Write(path, summary, sheets);
        bool ok = false;
        using (var fs = File.OpenRead(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            var wb = zip.GetEntry("xl/workbook.xml");
            if (wb != null)
            {
                using (var s = wb.Open())
                using (var r = new StreamReader(s))
                {
                    string xml = r.ReadToEnd();
                    ok = xml.Contains("name=\"Summary\"")
                         && xml.Contains("name=\"Baseline_Case01\"")
                         && xml.Contains("name=\"Proposed_Case01\"");
                }
            }
        }
        Debug.Log($"[ExperimentDataTest] wrote {path} ok={ok}");
        if (!ok) throw new System.Exception("xlsx が不正です。");
        EditorUtility.RevealInFinder(path);
    }
}
