using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nody.Models;
using Nody.Services;
using Nody.Views;
using IOPath = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;

namespace Nody;

public partial class MainWindow : Window
{
    private enum ToolMode { None, DrawFlow, DeleteFlow }

    // ── 프로젝트 상태 ──
    private string _projectFolder;
    private ProjectData _data = new();
    private readonly Dictionary<Guid, NodeView> _views = new();
    private AppSettings _settings = new();

    // ── 편집 상태 ──
    private NodeView _selected;
    private NodeView _flowSource;
    private WpfPath _rubber;
    private ToolMode _mode = ToolMode.None;

    // ── 뷰(패닝/줌) ──
    private readonly MatrixTransform _worldTransform = new();
    private double _zoom = 1, _offX = 80, _offY = 80;
    private bool _panning, _panMoved;
    private MouseButton _panButton;
    private Point _panStart;
    private double _panOffX, _panOffY;

    // ── 미니맵 ──
    private bool _minimapDrag;
    private double _mmScale = 1, _mmOx, _mmOy, _mmWorldL, _mmWorldT;

    // ── 자동 저장 ──
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty, _loading;

    public MainWindow()
    {
        InitializeComponent();
        World.RenderTransform = _worldTransform;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        Loaded += MainWindow_Loaded;
        SizeChanged += (_, _) => { UpdateMinimapSize(); RedrawMinimap(); };
        Viewport.SizeChanged += (_, _) => { UpdateMinimapSize(); RedrawMinimap(); };
        BindAboutInfo();
    }

    private void BindAboutInfo()
    {
        var v = VersionService.Current;
        var date = string.IsNullOrWhiteSpace(v.Date) ? "" : $"  ·  릴리스 {v.Date}";
        var desc = string.IsNullOrWhiteSpace(v.Description) ? "" : $"  ·  {v.Description}";
        AboutVersionLine.Text = $"{v.Name} {v.Version}{date}{desc}";
    }

    // ════════════════════════ 시작 / 종료 ════════════════════════

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsService.Load();
        ThemeToggle.IsChecked = _settings.Theme == "Dark";
        ApplyView();

        if (!string.IsNullOrEmpty(_settings.LastProject) && Directory.Exists(_settings.LastProject))
        {
            LoadProject(_settings.LastProject);
        }
        else
        {
            UpdateEmptyHint();
            OpenProjectDialog();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveNow();
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        // 엑셀에서 편집하고 돌아오면 노드의 스크립트 미리보기를 갱신
        foreach (var v in _views.Values) RefreshPreview(v);
    }

    // ════════════════════════ 프로젝트 ════════════════════════

    private bool EnsureProject()
    {
        if (_projectFolder != null) return true;
        OpenProjectDialog();
        return _projectFolder != null;
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => OpenProjectDialog();

    private void OpenProjectDialog()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "NODY 프로젝트 폴더 선택 (새 폴더를 만들어도 됩니다)"
        };
        if (!string.IsNullOrEmpty(_projectFolder)) dlg.InitialDirectory = _projectFolder;

        if (dlg.ShowDialog(this) == true)
            LoadProject(dlg.FolderName);
    }

    private void LoadProject(string folder)
    {
        SaveNow();          // 이전 프로젝트 저장
        _saveTimer.Stop();
        _loading = true;

        SetMode(ToolMode.None);
        NodeLayer.Children.Clear();
        FlowLayer.Children.Clear();
        OverlayLayer.Children.Clear();
        _views.Clear();
        _selected = null;
        _flowSource = null;
        _rubber = null;

        _projectFolder = folder;
        _data = ProjectService.Load(folder);

        foreach (var n in _data.Nodes) CreateView(n);
        _data.Flows.RemoveAll(f => !_views.ContainsKey(f.From) || !_views.ContainsKey(f.To));

        _zoom = Math.Clamp(_data.View.Zoom <= 0 ? 1 : _data.View.Zoom, 0.3, 2.0);
        _offX = _data.View.OffsetX;
        _offY = _data.View.OffsetY;
        ApplyView();
        RedrawFlows();
        UpdateEmptyHint();

        _loading = false;
        _dirty = false;

        Title = $"NODY - {IOPath.GetFileName(folder.TrimEnd('\\', '/'))}";
        StatusSave.Text = "";
        SetStatus($"프로젝트: {folder}");

        _settings.LastProject = folder;
        SettingsService.Save(_settings);
    }

    private string FullPath(NodeModel m) => IOPath.Combine(_projectFolder, m.FileName);

    // ════════════════════════ 자동 저장 ════════════════════════

    private void MarkDirty()
    {
        if (_loading || _projectFolder == null) return;
        _dirty = true;
        StatusSave.Text = "변경됨…";
        _saveTimer.Stop();
        _saveTimer.Start();      // 마지막 변경 후 0.6초 뒤 저장 (디바운스)
    }

    private void SaveNow(bool force = false)
    {
        if (_projectFolder == null || _loading) return;
        if (!force && !_dirty) return;

        _saveTimer.Stop();
        try
        {
            _data.View.Zoom = _zoom;
            _data.View.OffsetX = _offX;
            _data.View.OffsetY = _offY;
            ProjectService.Save(_projectFolder, _data);
            _dirty = false;
            StatusSave.Text = $"자동 저장됨 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusSave.Text = "저장 실패: " + ex.Message;
        }
    }

    // ════════════════════════ 테마 ════════════════════════

    private void HomeTab_Checked(object sender, RoutedEventArgs e) => ShowRibbonTab(home: true);

    private void InfoTab_Checked(object sender, RoutedEventArgs e) => ShowRibbonTab(home: false);

    private void ShowRibbonTab(bool home)
    {
        if (HomeRibbon == null || InfoRibbon == null) return;

        HomeRibbon.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        InfoRibbon.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        var dark = ThemeToggle.IsChecked == true;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };

        _settings.Theme = dark ? "Dark" : "Light";
        SettingsService.Save(_settings);
    }

    // ════════════════════════ 뷰: 패닝 / 줌 ════════════════════════

    private Point ScreenToWorld(Point p) => new((p.X - _offX) / _zoom, (p.Y - _offY) / _zoom);

    private void ApplyView()
    {
        var m = new Matrix(_zoom, 0, 0, _zoom, _offX, _offY);
        _worldTransform.Matrix = m;
        GridBrush.Transform = new MatrixTransform(m);
        StatusZoom.Text = $"{_zoom * 100:0}%";
        RedrawMinimap();
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 노드/선에서 처리하지 않은 빈 배경 클릭만 여기까지 온다
        Viewport.Focus();
        StartPan(e);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) StartPan(e);
    }

    private void StartPan(MouseButtonEventArgs e)
    {
        _panning = true;
        _panMoved = false;
        _panButton = e.ChangedButton;
        _panStart = e.GetPosition(Viewport);
        _panOffX = _offX;
        _panOffY = _offY;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Viewport);

        if (_panning)
        {
            var dx = pos.X - _panStart.X;
            var dy = pos.Y - _panStart.Y;
            if (!_panMoved && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) _panMoved = true;
            if (_panMoved)
            {
                _offX = _panOffX + dx;
                _offY = _panOffY + dy;
                ApplyView();
            }
        }

        if (_mode == ToolMode.DrawFlow && _flowSource != null) UpdateRubber(pos);
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || e.ChangedButton != _panButton) return;

        _panning = false;
        Viewport.ReleaseMouseCapture();

        if (_panMoved)
        {
            MarkDirty();
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            // 드래그 없이 빈 곳 클릭 → 선택 해제 / 플로우 시작 노드 해제
            if (_mode == ToolMode.DrawFlow) SetFlowSource(null);
            else ClearSelection();
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(Viewport);
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.Clamp(_zoom * factor, 0.3, 2.0);
        factor = newZoom / _zoom;

        // 마우스 커서 위치를 기준으로 확대/축소
        _offX = p.X - (p.X - _offX) * factor;
        _offY = p.Y - (p.Y - _offY) * factor;
        _zoom = newZoom;

        ApplyView();
        MarkDirty();
    }

    private void UpdateEmptyHint()
    {
        if (_projectFolder == null)
        {
            EmptyHint.Text = "상단의 '프로젝트 열기'로 작업 폴더를 선택해 주세요";
            EmptyHint.Visibility = Visibility.Visible;
        }
        else if (_data.Nodes.Count == 0)
        {
            EmptyHint.Text = "상단의 '노드 생성'을 눌러 첫 노드를 만들어 보세요";
            EmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
        }
    }

    private void SetStatus(string text) => StatusMain.Text = text;

    // ════════════════════════ 미니맵 ════════════════════════

    private void UpdateMinimapSize()
    {
        if (MinimapHost == null) return;

        var ratio = Math.Max(ActualWidth, 1) / Math.Max(ActualHeight, 1);
        const double maxW = 200;
        var w = maxW;
        var h = w / ratio;

        var cap = Viewport.ActualHeight > 0 ? Viewport.ActualHeight * 0.38 : 160;
        if (h > cap)
        {
            h = cap;
            w = h * ratio;
        }

        MinimapHost.Width = w;
        MinimapHost.Height = h;
        MinimapCanvas.Width = Math.Max(w - 2, 1);
        MinimapCanvas.Height = Math.Max(h - 2, 1);
    }

    private void RedrawMinimap()
    {
        if (MinimapCanvas == null) return;
        UpdateMinimapSize();
        MinimapCanvas.Children.Clear();

        var mw = MinimapCanvas.Width;
        var mh = MinimapCanvas.Height;
        if (double.IsNaN(mw) || double.IsNaN(mh) || mw < 4 || mh < 4) return;

        var viewW = Math.Max(Viewport.ActualWidth, 1);
        var viewH = Math.Max(Viewport.ActualHeight, 1);
        var camL = -_offX / _zoom;
        var camT = -_offY / _zoom;
        var camR = camL + viewW / _zoom;
        var camB = camT + viewH / _zoom;

        double l = camL, t = camT, r = camR, b = camB;
        foreach (var n in _data.Nodes)
        {
            l = Math.Min(l, n.X);
            t = Math.Min(t, n.Y);
            r = Math.Max(r, n.X + NodeView.NodeWidth);
            b = Math.Max(b, n.Y + NodeView.NodeHeight);
        }

        var bw = Math.Max(r - l, 1);
        var bh = Math.Max(b - t, 1);
        var pad = Math.Max(24, Math.Max(bw, bh) * 0.08);
        l -= pad; t -= pad; r += pad; b += pad;
        bw = r - l; bh = b - t;

        _mmScale = Math.Min(mw / bw, mh / bh);
        _mmOx = (mw - bw * _mmScale) / 2;
        _mmOy = (mh - bh * _mmScale) / 2;
        _mmWorldL = l;
        _mmWorldT = t;

        foreach (var n in _data.Nodes)
        {
            var p = WorldToMini(n.X, n.Y);
            var rect = new Rectangle
            {
                Width = Math.Max(3, NodeView.NodeWidth * _mmScale),
                Height = Math.Max(2, NodeView.NodeHeight * _mmScale),
                RadiusX = 1.5,
                RadiusY = 1.5,
                IsHitTestVisible = false
            };
            var fillKey = n.Type == NodeType.Dialogue ? "DialogueAccent" : "ChoiceAccent";
            rect.SetResourceReference(Shape.FillProperty, fillKey);
            if (_selected != null && _selected.Model.Id == n.Id)
            {
                rect.StrokeThickness = 1.4;
                rect.SetResourceReference(Shape.StrokeProperty, "NodeSelected");
            }
            Canvas.SetLeft(rect, p.X);
            Canvas.SetTop(rect, p.Y);
            MinimapCanvas.Children.Add(rect);
        }

        var cam = WorldToMini(camL, camT);
        var camBox = new Rectangle
        {
            Width = Math.Max(4, (camR - camL) * _mmScale),
            Height = Math.Max(4, (camB - camT) * _mmScale),
            StrokeThickness = 1.4,
            IsHitTestVisible = false
        };
        camBox.SetResourceReference(Shape.StrokeProperty, "NodeSelected");
        camBox.SetResourceReference(Shape.FillProperty, "MinimapViewFill");
        Canvas.SetLeft(camBox, cam.X);
        Canvas.SetTop(camBox, cam.Y);
        MinimapCanvas.Children.Add(camBox);
    }

    private Point WorldToMini(double x, double y) =>
        new((x - _mmWorldL) * _mmScale + _mmOx, (y - _mmWorldT) * _mmScale + _mmOy);

    private Point MiniToWorld(Point p) =>
        new((p.X - _mmOx) / _mmScale + _mmWorldL, (p.Y - _mmOy) / _mmScale + _mmWorldT);

    private void CenterViewOnWorld(Point world)
    {
        _offX = Viewport.ActualWidth / 2 - world.X * _zoom;
        _offY = Viewport.ActualHeight / 2 - world.Y * _zoom;
        ApplyView();
    }

    private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _minimapDrag = true;
        MinimapHost.CaptureMouse();
        CenterViewOnWorld(MiniToWorld(e.GetPosition(MinimapCanvas)));
    }

    private void Minimap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_minimapDrag) return;
        CenterViewOnWorld(MiniToWorld(e.GetPosition(MinimapCanvas)));
    }

    private void Minimap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_minimapDrag) return;
        e.Handled = true;
        _minimapDrag = false;
        MinimapHost.ReleaseMouseCapture();
        MarkDirty();
    }

    private void Minimap_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var world = MiniToWorld(e.GetPosition(MinimapCanvas));
        var factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.Clamp(_zoom * factor, 0.3, 2.0);
        _zoom = newZoom;
        CenterViewOnWorld(world);
        MarkDirty();
    }

    // ════════════════════════ 노드 ════════════════════════

    private NodeView CreateView(NodeModel m)
    {
        var v = new NodeView(m);
        Canvas.SetLeft(v, m.X);
        Canvas.SetTop(v, m.Y);

        v.Pressed += NodePressed;
        v.Activated += n => { Viewport.Focus(); Select(n); };
        v.Moved += _ =>
        {
            RedrawFlows();
            RedrawMinimap();
            if (_flowSource != null) UpdateRubber(Mouse.GetPosition(Viewport));
        };
        v.MoveCompleted += _ => MarkDirty();
        v.TypeChanged += NodeTypeChanged;
        v.RenameRequested += NodeRenameRequested;

        NodeLayer.Children.Add(v);
        _views[m.Id] = v;
        RefreshPreview(v, true);
        RedrawMinimap();
        return v;
    }

    private void Select(NodeView v)
    {
        if (_selected == v) return;
        if (_selected != null) _selected.IsSelected = false;
        _selected = v;
        if (v != null) v.IsSelected = true;
        RedrawMinimap();
    }

    private void ClearSelection() => Select(null);

    private void NodePressed(NodeView v, int clickCount)
    {
        Viewport.Focus();       // 이름 편집 중이던 텍스트박스가 있으면 확정시킨다

        switch (_mode)
        {
            case ToolMode.DrawFlow:
                HandleFlowClick(v);
                return;

            case ToolMode.DeleteFlow:
                return;

            default:
                Select(v);
                if (clickCount >= 2) { OpenNodeFile(v); return; }   // 두 번 좌클릭 = 파일 열기
                v.BeginDrag();
                return;
        }
    }

    private static string UniqueBaseName(string folder, IEnumerable<NodeModel> nodes, string baseName)
    {
        var used = new HashSet<string>(nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var name = $"{baseName} {i}";
            if (!used.Contains(name) && !File.Exists(IOPath.Combine(folder, name + ".xlsx"))) return name;
        }
    }

    private void AddNode_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;

        var center = ScreenToWorld(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));
        var m = new NodeModel
        {
            Name = UniqueBaseName(_projectFolder, _data.Nodes, "새 노드"),
            Type = NodeType.Dialogue,
            X = Math.Round(center.X - NodeView.NodeWidth / 2),
            Y = Math.Round(center.Y - NodeView.NodeHeight / 2)
        };

        // 같은 자리에 겹치지 않게 살짝 비켜 놓는다
        while (_data.Nodes.Any(n => Math.Abs(n.X - m.X) < 20 && Math.Abs(n.Y - m.Y) < 20))
        {
            m.X += 30;
            m.Y += 30;
        }

        try
        {
            ExcelService.CreateTemplate(FullPath(m), m.Type);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "엑셀 파일을 만들지 못했습니다.\n\n" + ex.Message, "노드 생성", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _data.Nodes.Add(m);
        var v = CreateView(m);
        SetMode(ToolMode.None);
        Select(v);
        UpdateEmptyHint();
        MarkDirty();

        // 생성 직후 바로 이름을 입력할 수 있게 한다
        Dispatcher.BeginInvoke(new Action(v.BeginRename), DispatcherPriority.Loaded);
    }

    private void DeleteNode_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            SetStatus("삭제할 노드를 먼저 클릭해서 선택하세요.");
            return;
        }

        var v = _selected;
        var m = v.Model;

        var result = MessageBox.Show(this,
            $"노드 '{m.Name}'을(를) 삭제할까요?\n\n" +
            $"[예]  노드와 엑셀 파일을 함께 삭제 (파일은 '{ProjectService.TrashFolder}' 폴더로 이동)\n" +
            "[아니요]  노드만 삭제 (엑셀 파일은 그대로 유지)\n" +
            "[취소]  삭제하지 않음",
            "노드 삭제", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel) return;

        if (result == MessageBoxResult.Yes && !MoveFileToTrash(m)) return;

        _data.Flows.RemoveAll(f => f.From == m.Id || f.To == m.Id);
        _data.Nodes.Remove(m);
        _views.Remove(m.Id);
        NodeLayer.Children.Remove(v);

        _selected = null;
        if (_flowSource == v) SetFlowSource(null);

        RedrawFlows();
        UpdateEmptyHint();
        RedrawMinimap();
        MarkDirty();
        SetStatus($"'{m.Name}' 노드를 삭제했습니다.");
    }

    private bool MoveFileToTrash(NodeModel m)
    {
        var src = FullPath(m);
        if (!File.Exists(src)) return true;

        try
        {
            var trash = IOPath.Combine(_projectFolder, ProjectService.TrashFolder);
            Directory.CreateDirectory(trash);
            var dest = IOPath.Combine(trash, $"{m.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            File.Move(src, dest);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "파일을 삭제하지 못했습니다. 엑셀에서 열려 있다면 닫고 다시 시도해 주세요.\n\n" + ex.Message,
                "노드 삭제", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenNodeFile(NodeView v)
    {
        var path = FullPath(v.Model);
        try
        {
            if (!File.Exists(path)) ExcelService.CreateTemplate(path, v.Model.Type);
            ExcelService.Open(path);
            RefreshPreview(v, true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "파일을 열지 못했습니다.\n\n" + ex.Message, "파일 열기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool NodeRenameRequested(NodeView v, string newName)
    {
        newName = newName.Trim().TrimEnd('.');
        if (newName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) newName = newName[..^5];

        if (newName.Length == 0) return Warn("이름을 입력해 주세요.");
        if (newName.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0)
            return Warn("파일 이름에 사용할 수 없는 문자가 있습니다.  \\ / : * ? \" < > |");

        if (_data.Nodes.Any(n => n != v.Model && string.Equals(n.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return Warn("같은 이름의 노드가 이미 있습니다.");

        var oldPath = FullPath(v.Model);
        var newPath = IOPath.Combine(_projectFolder, newName + ".xlsx");

        if (File.Exists(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return Warn("같은 이름의 파일이 폴더에 이미 있습니다.");

        try
        {
            if (File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.Ordinal))
                File.Move(oldPath, newPath);
        }
        catch (Exception ex)
        {
            return Warn("파일 이름을 바꾸지 못했습니다. 엑셀에서 열려 있다면 닫고 다시 시도해 주세요.\n\n" + ex.Message);
        }

        v.Model.Name = newName;
        MarkDirty();
        RefreshPreview(v, true);
        return true;
    }

    private bool Warn(string message)
    {
        MessageBox.Show(this, message, "이름 수정", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void NodeTypeChanged(NodeView v, NodeType type)
    {
        // 아직 손대지 않은 템플릿이면 새 타입의 템플릿으로 교체 (내용이 있으면 절대 건드리지 않음)
        var path = FullPath(v.Model);
        try
        {
            if (File.Exists(path) && ExcelService.IsUntouched(path))
                ExcelService.ReplaceWithTemplate(path, type);
        }
        catch { /* 엑셀에서 열려 있는 경우 등: 파일은 그대로 둔다 */ }

        RefreshPreview(v, true);
        RedrawFlows();      // 선택지 노드에서 나가는 선은 색이 다르다
        MarkDirty();
    }

    private async void RefreshPreview(NodeView v, bool force = false)
    {
        if (_projectFolder == null) return;
        var path = FullPath(v.Model);

        if (!File.Exists(path))
        {
            v.SetPreview(new[] { "(파일 없음 - 더블클릭하면 새로 만듭니다)" }, true);
            v.LastWrite = default;
            return;
        }

        try
        {
            var write = File.GetLastWriteTimeUtc(path);
            if (!force && write == v.LastWrite) return;

            var lines = await Task.Run(() => ExcelService.ReadPreview(path));
            v.LastWrite = write;
            v.SetPreview(lines, false);
        }
        catch
        {
            // 다른 프로그램이 잠갔거나 읽기 실패 → 이전 미리보기 유지
        }
    }

    // ════════════════════════ 플로우 ════════════════════════

    private void DrawFlow_Click(object sender, RoutedEventArgs e) =>
        SetMode(DrawFlowToggle.IsChecked == true ? ToolMode.DrawFlow : ToolMode.None);

    private void DeleteFlow_Click(object sender, RoutedEventArgs e) =>
        SetMode(DeleteFlowToggle.IsChecked == true ? ToolMode.DeleteFlow : ToolMode.None);

    private void SetMode(ToolMode mode)
    {
        _mode = mode;
        DrawFlowToggle.IsChecked = mode == ToolMode.DrawFlow;
        DeleteFlowToggle.IsChecked = mode == ToolMode.DeleteFlow;
        SetFlowSource(null);

        Viewport.Cursor = mode == ToolMode.DrawFlow ? Cursors.Cross : null;

        switch (mode)
        {
            case ToolMode.DrawFlow:
                SetStatus("플로우 긋기: 시작 노드를 클릭한 뒤 도착 노드를 클릭하세요. (Esc: 종료)");
                break;
            case ToolMode.DeleteFlow:
                SetStatus("플로우 삭제: 지우려는 선을 클릭하세요. (Esc: 종료)");
                break;
            default:
                if (_projectFolder != null) SetStatus($"프로젝트: {_projectFolder}");
                break;
        }
    }

    private void SetFlowSource(NodeView v)
    {
        if (_flowSource != null) _flowSource.IsFlowSource = false;
        _flowSource = v;
        if (v != null) v.IsFlowSource = true;

        OverlayLayer.Children.Clear();
        _rubber = null;

        if (v != null)
        {
            _rubber = new WpfPath { StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
            _rubber.SetResourceReference(Shape.StrokeProperty, "FlowSource");
            OverlayLayer.Children.Add(_rubber);
            UpdateRubber(Mouse.GetPosition(Viewport));
        }
    }

    private void UpdateRubber(Point viewportPos)
    {
        if (_flowSource == null || _rubber == null) return;
        var s = OutPoint(_flowSource.Model);
        _rubber.Data = BuildCurve(s, ScreenToWorld(viewportPos));
    }

    private void HandleFlowClick(NodeView v)
    {
        if (_flowSource == null) { SetFlowSource(v); return; }
        if (_flowSource == v) { SetFlowSource(null); return; }

        AddFlow(_flowSource.Model, v.Model);
        SetFlowSource(null);
    }

    private void AddFlow(NodeModel from, NodeModel to)
    {
        if (_data.Flows.Any(f => f.From == from.Id && f.To == to.Id))
        {
            SetStatus("이미 연결된 노드입니다.");
            return;
        }

        _data.Flows.Add(new FlowModel { From = from.Id, To = to.Id });
        RedrawFlows();
        MarkDirty();
        SetStatus($"'{from.Name}' → '{to.Name}' 연결");
    }

    private static Point OutPoint(NodeModel m) => new(m.X + NodeView.NodeWidth, m.Y + NodeView.NodeHeight / 2);
    private static Point InPoint(NodeModel m) => new(m.X, m.Y + NodeView.NodeHeight / 2);

    private static Geometry BuildCurve(Point s, Point e)
    {
        var dx = Math.Max(60, Math.Abs(e.X - s.X) * 0.5);
        var fig = new PathFigure { StartPoint = s, IsClosed = false };
        fig.Segments.Add(new BezierSegment(new Point(s.X + dx, s.Y), new Point(e.X - dx, e.Y), e, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private void RedrawFlows()
    {
        FlowLayer.Children.Clear();

        foreach (var f in _data.Flows)
        {
            if (!_views.TryGetValue(f.From, out var a) || !_views.TryGetValue(f.To, out var b)) continue;

            var s = OutPoint(a.Model);
            var e = InPoint(b.Model);
            var geo = BuildCurve(s, e);

            // 선택지 노드에서 갈라져 나가는 선은 강조색
            var colorKey = a.Model.Type == NodeType.Choice ? "FlowChoice" : "FlowNormal";

            var line = new WpfPath { Data = geo, StrokeThickness = 2.5, IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, colorKey);

            var head = new StreamGeometry();
            using (var ctx = head.Open())
            {
                ctx.BeginFigure(e, true, true);
                ctx.LineTo(new Point(e.X - 11, e.Y - 6), true, false);
                ctx.LineTo(new Point(e.X - 11, e.Y + 6), true, false);
            }
            head.Freeze();

            var arrow = new WpfPath { Data = head, IsHitTestVisible = false };
            arrow.SetResourceReference(Shape.FillProperty, colorKey);

            // 클릭하기 쉽도록 굵고 투명한 히트 영역을 따로 둔다
            var hit = new WpfPath { Data = geo, Stroke = Brushes.Transparent, StrokeThickness = 14, Tag = f };
            hit.MouseEnter += (_, _) =>
            {
                if (_mode != ToolMode.DeleteFlow) return;
                hit.Cursor = Cursors.Hand;
                line.SetResourceReference(Shape.StrokeProperty, "FlowDelete");
                arrow.SetResourceReference(Shape.FillProperty, "FlowDelete");
            };
            hit.MouseLeave += (_, _) =>
            {
                line.SetResourceReference(Shape.StrokeProperty, colorKey);
                arrow.SetResourceReference(Shape.FillProperty, colorKey);
            };
            hit.MouseLeftButtonDown += FlowHit_MouseLeftButtonDown;

            FlowLayer.Children.Add(line);
            FlowLayer.Children.Add(arrow);
            FlowLayer.Children.Add(hit);
        }
    }

    private void FlowHit_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != ToolMode.DeleteFlow) return;     // 그 외 모드에서는 배경처럼 패닝

        e.Handled = true;
        var flow = (FlowModel)((FrameworkElement)sender).Tag;
        _data.Flows.Remove(flow);
        RedrawFlows();
        MarkDirty();
        SetStatus("플로우를 삭제했습니다.");
    }

    // ════════════════════════ 내보내기 ════════════════════════

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProject()) return;
        SaveNow(true);      // 최신 플로우 데이터가 zip 에 들어가도록

        var folderName = IOPath.GetFileName(_projectFolder.TrimEnd('\\', '/'));
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "프로젝트 내보내기",
            Filter = "ZIP 파일 (*.zip)|*.zip",
            FileName = $"{folderName}_{DateTime.Now:yyyyMMdd_HHmm}.zip"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            ExportService.ExportZip(_projectFolder, dlg.FileName);
            SetStatus($"내보내기 완료: {dlg.FileName}");
            MessageBox.Show(this, "프로젝트를 zip 으로 저장했습니다.\n\n" + dlg.FileName, "프로젝트 내보내기",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "내보내기에 실패했습니다.\n\n" + ex.Message, "프로젝트 내보내기",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ════════════════════════ 키보드 ════════════════════════

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;     // 이름 편집 중에는 건드리지 않음

        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (InfoTab.IsChecked == true)
            {
                HomeTab.IsChecked = true;
                e.Handled = true;
                return;
            }
            if (_mode != ToolMode.None) SetMode(ToolMode.None);
            else ClearSelection();
            e.Handled = true;
        }
    }
}
