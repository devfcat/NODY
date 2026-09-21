using System.Diagnostics;
using System.IO;
using ClosedXML.Excel;
using Nody.Models;
using IOPath = System.IO.Path;

namespace Nody.Services;

/// <summary>노드 1개 = 엑셀 파일 1개. 템플릿 생성 / 미리보기 / 열기를 담당한다.</summary>
public static class ExcelService
{
    // 클라이언트 파서와 맞춰야 하는 컬럼 구성 (필요하면 여기만 수정)
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

    /// <summary>임시 파일로 만든 뒤 교체 (엑셀이 열고 있으면 예외 → 호출 쪽에서 무시)</summary>
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

    /// <summary>템플릿 그대로(헤더 + 빈 1행)인지. 타입을 바꿀 때 템플릿을 다시 만들어도 되는지 판단한다.</summary>
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

    /// <summary>노드에 표시할 스크립트 앞부분 (최대 maxRows 줄)</summary>
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

    public static void Open(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // 엑셀이 열고 있는 파일도 읽을 수 있도록 공유 모드로 연다
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
