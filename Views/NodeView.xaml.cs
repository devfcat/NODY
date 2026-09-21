using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Nody.Models;

namespace Nody.Views;

public partial class NodeView : UserControl
{
    public const double NodeWidth = 220;
    public const double NodeHeight = 132;

    public NodeModel Model { get; }

    /// <summary>미리보기를 마지막으로 읽은 시점의 파일 수정 시각</summary>
    public DateTime LastWrite { get; set; }

    /// <summary>마우스 좌클릭 (클릭 횟수 전달). 드래그 시작은 받는 쪽에서 BeginDrag() 호출.</summary>
    public event Action<NodeView, int> Pressed;
    /// <summary>우클릭/드롭다운 등으로 노드와 상호작용 → 선택 처리용</summary>
    public event Action<NodeView> Activated;
    public event Action<NodeView> Moved;
    public event Action<NodeView> MoveCompleted;
    public event Action<NodeView, NodeType> TypeChanged;
    /// <summary>이름 변경 요청. true 를 반환하면 수락.</summary>
    public event Func<NodeView, string, bool> RenameRequested;

    private bool _selected, _flowSource, _renaming, _dragging;
    private Point _dragStart;
    private double _startX, _startY;
    private DateTime _popupClosedAt = DateTime.MinValue;

    public NodeView(NodeModel model)
    {
        InitializeComponent();
        Model = model;
        Width = NodeWidth;
        Height = NodeHeight;
        RefreshName();
        RefreshType();
        RefreshBorder();
    }

    // ───────────── 상태 표시 ─────────────

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; RefreshBorder(); }
    }

    /// <summary>플로우 긋기에서 시작 노드로 지정된 상태</summary>
    public bool IsFlowSource
    {
        get => _flowSource;
        set { _flowSource = value; RefreshBorder(); }
    }

    private void RefreshBorder()
    {
        var key = _flowSource ? "FlowSource" : _selected ? "NodeSelected" : "NodeBorder";
        Root.SetResourceReference(Border.BorderBrushProperty, key);
    }

    public void RefreshName() => NameText.Text = Model.Name;

    public void RefreshType()
    {
        var isDialogue = Model.Type == NodeType.Dialogue;
        TypeText.Text = isDialogue ? "대사 파일" : "선택지 파일";
        var key = isDialogue ? "DialogueAccent" : "ChoiceAccent";
        AccentBar.SetResourceReference(Border.BackgroundProperty, key);
        TypeDot.SetResourceReference(Shape.FillProperty, key);
    }

    public void SetPreview(IEnumerable<string> lines, bool placeholder)
    {
        PreviewPanel.Children.Clear();
        foreach (var line in lines)
        {
            var tb = new TextBlock
            {
                Text = line,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontStyle = placeholder ? FontStyles.Italic : FontStyles.Normal,
                Margin = new Thickness(0, 0, 0, 1)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "FgMuted");
            PreviewPanel.Children.Add(tb);
        }
    }

    // ───────────── 마우스 ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;              // 캔버스 패닝으로 전달되지 않게
        if (_renaming) return;
        Pressed?.Invoke(this, e.ClickCount);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_renaming || TypeButton.IsMouseOver) return;
        e.Handled = true;
        Activated?.Invoke(this);
        BeginRename();
    }

    public void BeginDrag()
    {
        _dragStart = Mouse.GetPosition((IInputElement)Parent);
        _startX = Model.X;
        _startY = Model.Y;
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var p = Mouse.GetPosition((IInputElement)Parent);   // 캔버스(월드) 좌표
        Model.X = _startX + (p.X - _dragStart.X);
        Model.Y = _startY + (p.Y - _dragStart.Y);
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
        Moved?.Invoke(this);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
        MoveCompleted?.Invoke(this);
    }

    // ───────────── 이름 수정 ─────────────

    public void BeginRename()
    {
        if (_renaming) return;
        _renaming = true;
        NameBox.Text = Model.Name;
        NameText.Visibility = Visibility.Collapsed;
        NameBox.Visibility = Visibility.Visible;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void EndRenameUi()
    {
        _renaming = false;
        NameBox.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
    }

    private void CommitRename()
    {
        if (!_renaming) return;
        var newName = NameBox.Text.Trim();
        EndRenameUi();

        if (newName != Model.Name)
            RenameRequested?.Invoke(this, newName);

        RefreshName();
    }

    private void CancelRename()
    {
        if (!_renaming) return;
        EndRenameUi();
        RefreshName();
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitRename();

    // ───────────── 타입 드롭다운 ─────────────

    private void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        // 열려 있는 팝업을 이 버튼으로 닫을 때 곧바로 다시 열리는 현상 방지
        if ((DateTime.Now - _popupClosedAt).TotalMilliseconds < 200) return;
        Activated?.Invoke(this);
        TypePopup.IsOpen = true;
    }

    private void TypePopup_Closed(object sender, EventArgs e) => _popupClosedAt = DateTime.Now;

    private void TypeItem_Click(object sender, RoutedEventArgs e)
    {
        TypePopup.IsOpen = false;
        var type = Enum.Parse<NodeType>((string)((Button)sender).Tag);
        if (type == Model.Type) return;

        Model.Type = type;
        RefreshType();
        TypeChanged?.Invoke(this, type);
    }
}
