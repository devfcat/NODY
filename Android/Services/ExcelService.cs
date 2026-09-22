using ClosedXML.Excel;
using Nody.Models;
using IOPath = System.IO.Path;

namespace Nody.AndroidApp.Services;

public static class ExcelService
{
    public static string[] HeaderFor(NodeType type) => type == NodeType.Dialogue
        ? new[]
        {
            "Index", "Speaker", "SpriteID", "EmoteID", "DecoID", "SOFTY", "Position",
            "Content", "ContentType", "NextFile", "Reward", "FunctionID",
            "VOICEID", "BGID", "ECGID", "SFXID", "BGMID"
        }
        : new[] { "Index", "Content", "NextFile", "Reward", "FunctionID", "ContentType" };

    public static void CreateTemplate(string path, NodeType type)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Script");
        var headers = HeaderFor(type);

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Column(i + 1).Width = headers[i] is "Content"
                ? 50
                : Math.Max(headers[i].Length + 3, 10);
        }

        var headerRange = ws.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Cell(2, 1).Value = 1;
        wb.SaveAs(path);
    }

    public static void ReplaceWithTemplate(string path, NodeType type)
    {
        var tmp = IOPath.Combine(IOPath.GetDirectoryName(path)!, "~nody_" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            CreateTemplate(tmp, type);
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public static bool IsUntouched(string path)
    {
        using var fs = OpenShared(path);
        using var wb = new XLWorkbook(fs);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws == null) return true;

        var last = ws.LastRowUsed();
        if (last == null) return true;
        if (last.RowNumber() > 2) return false;
        return !ws.Row(2).CellsUsed().Any(c => c.Address.ColumnNumber > 1);
    }

    public static List<string> ReadPreview(string path, int maxRows = 3)
    {
        var lines = new List<string>();
        using var fs = OpenShared(path);
        using var wb = new XLWorkbook(fs);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws == null) return lines;

        var firstHeader = ws.Cell(1, 1).GetString().Trim();
        int startCol = firstHeader is "번호" or "Index" ? 2 : 1;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var texts = row.CellsUsed()
                .Where(c => c.Address.ColumnNumber >= startCol)
                .Select(c => c.GetString().Replace("\r", " ").Replace("\n", " ").Trim())
                .Where(s => s.Length > 0);

            var line = string.Join(" | ", texts);
            if (line.Length == 0) continue;
            lines.Add(line);
            if (lines.Count >= maxRows) break;
        }

        if (lines.Count == 0) lines.Add("(비어 있음)");
        return lines;
    }

    public static List<Dictionary<string, string>> ReadRows(string path, NodeType type)
    {
        var headers = HeaderFor(type);
        var rows = new List<Dictionary<string, string>>();
        if (!File.Exists(path)) return rows;

        using var fs = OpenShared(path);
        using var wb = new XLWorkbook(fs);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws == null) return rows;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                map[headers[i]] = row.Cell(i + 1).GetString();
            if (map.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(map);
        }

        return rows;
    }

    public static void WriteRows(string path, NodeType type, List<Dictionary<string, string>> rows)
    {
        var headers = HeaderFor(type);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Script");

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Column(i + 1).Width = headers[i] is "Content" ? 50 : Math.Max(headers[i].Length + 3, 10);
        }

        var headerRange = ws.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        for (int r = 0; r < rows.Count; r++)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                rows[r].TryGetValue(headers[i], out var value);
                if (headers[i] == "Index" && string.IsNullOrWhiteSpace(value))
                    value = (r + 1).ToString();
                ws.Cell(r + 2, i + 1).Value = value ?? "";
            }
        }

        if (rows.Count == 0)
            ws.Cell(2, 1).Value = 1;

        var tmp = path + ".tmp";
        wb.SaveAs(tmp);
        File.Move(tmp, path, true);
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
