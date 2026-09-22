using Nody.AndroidApp.Editor;
using Nody.AndroidApp.Services;
using Nody.Models;

namespace Nody.AndroidApp;

public partial class ScriptPage : ContentPage
{
    private readonly EditorController _editor;
    private readonly NodeModel _node;
    private readonly string[] _headers;
    private List<Dictionary<string, string>> _rows;

    public ScriptPage(EditorController editor, NodeModel node)
    {
        InitializeComponent();
        _editor = editor;
        _node = node;
        Title = node.Name;
        _headers = ExcelService.HeaderFor(node.Type);
        var path = editor.FullPath(node);
        if (!File.Exists(path))
            ExcelService.CreateTemplate(path, node.Type);
        _rows = ExcelService.ReadRows(path, node.Type);
        if (_rows.Count == 0)
            _rows.Add(NewRow());
        HintLabel.Text = node.Type == NodeType.Dialogue
            ? "대사 파일 · Content / Speaker 를 중심으로 편집합니다."
            : "선택지 파일 · Content / NextFile 을 중심으로 편집합니다.";
        Rebuild();
    }

    private Dictionary<string, string> NewRow()
    {
        var map = _headers.ToDictionary(h => h, _ => "", StringComparer.OrdinalIgnoreCase);
        map["Index"] = (_rows?.Count + 1 ?? 1).ToString();
        return map;
    }

    private void Rebuild()
    {
        RowsBox.Children.Clear();
        var keys = _node.Type == NodeType.Dialogue
            ? new[] { "Index", "Speaker", "Content", "NextFile" }
            : new[] { "Index", "Content", "NextFile", "Reward" };

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var box = new VerticalStackLayout { Spacing = 4 };
            box.Add(new Label { Text = $"행 {i + 1}", FontAttributes = FontAttributes.Bold });
            foreach (var key in keys)
            {
                if (!_headers.Contains(key)) continue;
                row.TryGetValue(key, out var value);
                var entry = new Entry { Text = value ?? "", Placeholder = key };
                var captured = key;
                entry.TextChanged += (_, ev) => row[captured] = ev.NewTextValue ?? "";
                box.Add(new Label { Text = key, FontSize = 11, TextColor = Colors.Gray });
                box.Add(entry);
            }
            RowsBox.Add(box);
        }
    }

    private void AddRow_Clicked(object sender, EventArgs e)
    {
        _rows.Add(NewRow());
        Rebuild();
    }

    private void Save_Clicked(object sender, EventArgs e) => Save();

    protected override void OnDisappearing()
    {
        Save();
        base.OnDisappearing();
    }

    private void Save()
    {
        try
        {
            ExcelService.WriteRows(_editor.FullPath(_node), _node.Type, _rows);
            _editor.RefreshPreview(_node, true);
            _editor.MarkDirty();
            _editor.SaveNow();
            _editor.Notify();
        }
        catch (Exception ex)
        {
            _editor.Status = "저장 실패: " + ex.Message;
        }
    }
}
