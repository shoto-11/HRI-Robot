using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Excel が警告なしで開ける本物の .xlsx（Office Open XML）を書き出す。
/// </summary>
public static class ExperimentWorkbook
{
    public struct TrajectorySheet
    {
        public string SheetName;
        public List<PathDeviationTracker.PathSample> Samples;
    }

    public static void Write(string path, string[][] summaryRows, List<TrajectorySheet> sheets)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var parts = new List<(string Name, string Xml)>();
        parts.Add(("Summary", SheetXml(new[]
        {
            "SessionID", "Timestamp", "Condition", "CaseIndex",
            "CompletionTime_s", "Collisions", "TraveledPath_m", "CollisionEvents"
        }, summaryRows)));

        if (sheets != null)
        {
            for (int i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                string name = SanitizeSheetName(sheet.SheetName, i);
                var rows = new List<string[]>();
                if (sheet.Samples != null)
                {
                    foreach (var s in sheet.Samples)
                    {
                        rows.Add(new[]
                        {
                            s.Time.ToString("F2", CultureInfo.InvariantCulture),
                            s.Position.x.ToString("F3", CultureInfo.InvariantCulture),
                            s.Position.y.ToString("F3", CultureInfo.InvariantCulture),
                            s.Position.z.ToString("F3", CultureInfo.InvariantCulture),
                        });
                    }
                }
                parts.Add((name, SheetXml(new[] { "Time_s", "PosX", "PosY", "PosZ" }, rows.ToArray())));
            }
        }

        string tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml(parts.Count));
            WriteEntry(zip, "_rels/.rels", RelsXml());
            WriteEntry(zip, "xl/workbook.xml", WorkbookXml(parts));
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(parts.Count));
            for (int i = 0; i < parts.Count; i++)
                WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", parts[i].Xml);
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(content);
    }

    static string ContentTypesXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (int i = 1; i <= sheetCount; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    static string RelsXml() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    static string WorkbookXml(List<(string Name, string Xml)> parts)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheets>");
        for (int i = 0; i < parts.Count; i++)
            sb.Append($"<sheet name=\"{Xml(parts[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    static string WorkbookRelsXml(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 1; i <= sheetCount; i++)
            sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    static string SheetXml(string[] header, string[][] rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        AppendRowXml(sb, 1, header);
        if (rows != null)
        {
            for (int i = 0; i < rows.Length; i++)
                AppendRowXml(sb, i + 2, rows[i]);
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    static void AppendRowXml(StringBuilder sb, int row, string[] cells)
    {
        sb.Append($"<row r=\"{row}\">");
        if (cells != null)
        {
            for (int c = 0; c < cells.Length; c++)
            {
                string refer = CellRef(c, row);
                string val = cells[c] ?? "";
                if (IsNumber(val))
                    sb.Append($"<c r=\"{refer}\"><v>{val}</v></c>");
                else
                    sb.Append($"<c r=\"{refer}\" t=\"inlineStr\"><is><t>{Xml(val)}</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    static string CellRef(int colZero, int row)
    {
        int col = colZero + 1;
        var sb = new StringBuilder();
        while (col > 0)
        {
            col--;
            sb.Insert(0, (char)('A' + col % 26));
            col /= 26;
        }
        return sb.ToString() + row;
    }

    static bool IsNumber(string s) =>
        !string.IsNullOrEmpty(s) &&
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    static string SanitizeSheetName(string name, int index)
    {
        if (string.IsNullOrEmpty(name)) name = $"Case{index + 1}";
        var sb = new StringBuilder();
        foreach (char c in name)
            sb.Append(c is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '_' : c);
        string s = sb.ToString();
        return s.Length > 31 ? s.Substring(0, 31) : s;
    }

    static string Xml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
