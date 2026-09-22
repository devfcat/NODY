using Nody.AndroidApp.Editor;
using Nody.AndroidApp.Services;
using Nody.Models;

namespace Nody.AndroidApp;

public partial class EditorPage : ContentPage
{
    private readonly EditorController _editor = new();
    private readonly EditorDrawable _drawable = new();
    private PointF _last;
    private PointF _start;
    private NodeModel _dragNode;
    private bool _panning, _minimapDrag, _moved;
    private float _pinchStartDist;
    private float _pinchStartZoom;
    private DateTime _lastTap;
    private NodeModel _lastTapNode;

    public EditorPage()
    {
        InitializeComponent();
        _drawable.Editor = _editor;
        CanvasView.Drawable = _drawable;
        _editor.Changed += () => MainThread.BeginInvokeOnMainThread(RefreshUi);
        _editor.LoadDefaultProject();
        BuildToolbar();
        RefreshUi();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        foreach (var n in _editor.Data.Nodes)
            _editor.RefreshPreview(n, true);
        RefreshUi();
    }

    private void RefreshUi()
    {
        _drawable.Width = (float)CanvasView.Width;
        _drawable.Height = (float)CanvasView.Height;
        CanvasView.Invalidate();
        StatusLabel.Text = _editor.Status;
        BackgroundColor = _editor.Dark ? Color.FromArgb("#14161B") : Color.FromArgb("#F3F2F1");
        StatusLabel.TextColor = _editor.Dark ? Color.FromArgb("#9AA3B2") : Color.FromArgb("#6B7280");
        BuildToolbar();
    }

    private void BuildToolbar()
    {
        Toolbar.Children.Clear();
        Toolbar.Add(Chip("노드 생성", _ =>
        {
            _editor.AddNode((float)Math.Max(CanvasView.Width, 1), (float)Math.Max(CanvasView.Height, 1));
        }));
        Toolbar.Add(Chip("삭제", async _ =>
        {
            if (_editor.Selected() == null)
            {
                await DisplayAlert("삭제", "노드를 먼저 선택하세요.", "확인");
                return;
            }
            if (await DisplayAlert("노드 삭제", $"'{_editor.Selected().Name}'을 삭제할까요? 엑셀은 _trash 로 이동합니다.", "삭제", "취소"))
                _editor.DeleteSelected();
        }));
        Toolbar.Add(Chip(_editor.Mode == ToolMode.DrawFlow ? "긋기 ●" : "플로우 긋기",
            _ => _editor.SetMode(ToolMode.DrawFlow), _editor.Mode == ToolMode.DrawFlow));
        Toolbar.Add(Chip(_editor.Mode == ToolMode.DeleteFlow ? "삭제 ●" : "플로우 삭제",
            _ => _editor.SetMode(ToolMode.DeleteFlow), _editor.Mode == ToolMode.DeleteFlow));
        Toolbar.Add(Chip("스크립트", async _ =>
        {
            var n = _editor.Selected();
            if (n == null) { await DisplayAlert("스크립트", "노드를 먼저 선택하세요.", "확인"); return; }
            await Navigation.PushAsync(new ScriptPage(_editor, n));
        }));
        Toolbar.Add(Chip("이름", async _ =>
        {
            var n = _editor.Selected();
            if (n == null) { await DisplayAlert("이름", "노드를 먼저 선택하세요.", "확인"); return; }
            var name = await DisplayPromptAsync("이름 수정", "엑셀 파일명", initialValue: n.Name);
            if (name != null) _editor.Rename(n, name);
        }));
        Toolbar.Add(Chip("타입", async _ =>
        {
            var n = _editor.Selected();
            if (n == null) { await DisplayAlert("타입", "노드를 먼저 선택하세요.", "확인"); return; }
            var pick = await DisplayActionSheet("노드 타입", "취소", null, "대사 파일", "선택지 파일");
            if (pick == "대사 파일") _editor.ChangeType(n, NodeType.Dialogue);
            if (pick == "선택지 파일") _editor.ChangeType(n, NodeType.Choice);
        }));
        Toolbar.Add(Chip("내보내기", async _ =>
        {
            try
            {
                var zip = _editor.ExportZipPath();
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "NODY 프로젝트",
                    File = new ShareFile(zip)
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("내보내기", ex.Message, "확인");
            }
        }));
        Toolbar.Add(Chip(_editor.Dark ? "라이트" : "다크", _ => _editor.ToggleTheme()));
        Toolbar.Add(Chip("정보", async _ =>
        {
            await VersionService.EnsureLoaded();
            var v = VersionService.Current;
            await DisplayAlert($"{v.Name} {v.Version}",
                $"{(string.IsNullOrWhiteSpace(v.Date) ? "" : "릴리스 " + v.Date + "\n")}{v.Description}", "확인");
        }));
    }

    private View Chip(string text, EventHandler onTap, bool active = false)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(12, 6),
            CornerRadius = 8,
            BackgroundColor = active ? Color.FromArgb("#C6E0B4") : Color.FromArgb("#FFFFFF"),
            TextColor = active ? Color.FromArgb("#0D5C32") : Color.FromArgb("#1F2430"),
            BorderColor = Color.FromArgb("#E1DFDD"),
            BorderWidth = 1
        };
        btn.Clicked += onTap;
        return btn;
    }

    private void OnStart(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        _drawable.Width = (float)CanvasView.Width;
        _drawable.Height = (float)CanvasView.Height;
        _editor.UpdateMinimap(_drawable.Width, _drawable.Height);

        _start = e.Touches[0];
        _last = _start;
        _moved = false;
        _dragNode = null;
        _panning = false;
        _minimapDrag = false;

        if (e.Touches.Length >= 2)
        {
            _pinchStartDist = Dist(e.Touches[0], e.Touches[1]);
            _pinchStartZoom = _editor.Zoom;
            return;
        }

        if (_editor.HitMinimap(_start))
        {
            _minimapDrag = true;
            _editor.CenterOn(_editor.MiniToWorld(_start), _drawable.Width, _drawable.Height);
            return;
        }

        if (_editor.TryDeleteFlowAt(_editor.ScreenToWorld(_start)))
            return;

        var node = _editor.HitNode(_editor.ScreenToWorld(_start));
        if (node != null)
        {
            var now = DateTime.Now;
            if (_lastTapNode == node && (now - _lastTap).TotalMilliseconds < 350)
            {
                _ = Navigation.PushAsync(new ScriptPage(_editor, node));
                _lastTapNode = null;
                return;
            }
            _lastTap = now;
            _lastTapNode = node;
            _editor.HandleNodeTap(node);
            if (_editor.Mode == ToolMode.None)
                _dragNode = node;
            return;
        }

        if (_editor.Mode == ToolMode.DrawFlow)
            _editor.Select(null);
        else
            _editor.Select(null);
        _panning = true;
    }

    private void OnDrag(object sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;

        if (e.Touches.Length >= 2)
        {
            var d = Dist(e.Touches[0], e.Touches[1]);
            if (_pinchStartDist > 1)
            {
                var mid = new PointF((e.Touches[0].X + e.Touches[1].X) / 2, (e.Touches[0].Y + e.Touches[1].Y) / 2);
                var factor = d / _pinchStartDist;
                var target = Math.Clamp(_pinchStartZoom * factor, 0.3f, 2.5f);
                _editor.ZoomAt(mid, target / _editor.Zoom);
            }
            return;
        }

        var p = e.Touches[0];
        var dx = p.X - _last.X;
        var dy = p.Y - _last.Y;
        if (Math.Abs(p.X - _start.X) > 4 || Math.Abs(p.Y - _start.Y) > 4) _moved = true;
        _last = p;

        if (_minimapDrag)
        {
            _editor.CenterOn(_editor.MiniToWorld(p), (float)CanvasView.Width, (float)CanvasView.Height);
            return;
        }

        if (_dragNode != null && _editor.Mode == ToolMode.None)
        {
            _editor.MoveNode(_dragNode, dx / _editor.Zoom, dy / _editor.Zoom);
            return;
        }

        if (_editor.Mode == ToolMode.DrawFlow && _editor.FlowSourceId != null)
        {
            _editor.RubberWorld = _editor.ScreenToWorld(p);
            _editor.Notify();
            return;
        }

        if (_panning)
            _editor.Pan(dx, dy);
    }

    private void OnEnd(object sender, TouchEventArgs e)
    {
        if (_moved || _dragNode != null || _minimapDrag || _panning)
            _editor.EndGesture();
        _dragNode = null;
        _panning = false;
        _minimapDrag = false;
        _editor.RubberWorld = null;
    }

    private void OnHoverEnd(object sender, EventArgs e) { }

    private static float Dist(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
