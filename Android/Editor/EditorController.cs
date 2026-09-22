using Nody.AndroidApp.Services;
using Nody.Models;
using IOPath = System.IO.Path;

namespace Nody.AndroidApp.Editor;

public enum ToolMode { None, DrawFlow, DeleteFlow }

public class EditorController
{
    public const float NodeW = 220;
    public const float NodeH = 132;

    public ProjectData Data { get; private set; } = new();
    public string ProjectFolder { get; private set; }
    public float Zoom = 1;
    public float OffX = 80;
    public float OffY = 80;
    public Guid? SelectedId;
    public Guid? FlowSourceId;
    public ToolMode Mode;
    public string Status = "";
    public bool Dark;
    public PointF? RubberWorld;
    public readonly Dictionary<Guid, List<string>> Previews = new();

    public float MmScale = 1, MmOx, MmOy, MmWorldL, MmWorldT;
    public RectF MinimapRect = new(0, 0, 160, 100);

    public event Action Changed;
    public void Notify() => Changed?.Invoke();

    private readonly Dictionary<Guid, DateTime> _previewWrite = new();
    private bool _dirty, _loading;

    public void LoadDefaultProject()
    {
        var settings = SettingsService.Load();
        Dark = settings.Theme == "Dark";
        var folder = string.IsNullOrEmpty(settings.LastProject) || !Directory.Exists(settings.LastProject)
            ? ProjectService.DefaultFolder
            : settings.LastProject;
        LoadProject(folder);
    }

    public void LoadProject(string folder)
    {
        SaveNow();
        _loading = true;
        ProjectFolder = folder;
        Directory.CreateDirectory(folder);
        Data = ProjectService.Load(folder);
        Zoom = Math.Clamp((float)(Data.View.Zoom <= 0 ? 1 : Data.View.Zoom), 0.3f, 2.5f);
        OffX = (float)Data.View.OffsetX;
        OffY = (float)Data.View.OffsetY;
        SelectedId = null;
        FlowSourceId = null;
        Mode = ToolMode.None;
        RubberWorld = null;
        Previews.Clear();
        _previewWrite.Clear();
        foreach (var n in Data.Nodes) RefreshPreview(n, true);
        _loading = false;
        _dirty = false;
        Status = $"프로젝트: {IOPath.GetFileName(folder.TrimEnd('\\', '/'))}";
        SettingsService.Save(new AppSettings { LastProject = folder, Theme = Dark ? "Dark" : "Light" });
        Changed?.Invoke();
    }

    public PointF ScreenToWorld(PointF p) => new((p.X - OffX) / Zoom, (p.Y - OffY) / Zoom);
    public PointF WorldToScreen(float x, float y) => new(x * Zoom + OffX, y * Zoom + OffY);

    public RectF NodeWorldRect(NodeModel n) => new((float)n.X, (float)n.Y, NodeW, NodeH);

    public NodeModel HitNode(PointF world)
    {
        for (int i = Data.Nodes.Count - 1; i >= 0; i--)
        {
            if (NodeWorldRect(Data.Nodes[i]).Contains(world)) return Data.Nodes[i];
        }
        return null;
    }

    public void SetMode(ToolMode mode)
    {
        Mode = Mode == mode ? ToolMode.None : mode;
        FlowSourceId = null;
        RubberWorld = null;
        Status = Mode switch
        {
            ToolMode.DrawFlow => "플로우 긋기: 시작 노드 → 도착 노드",
            ToolMode.DeleteFlow => "플로우 삭제: 선을 탭하세요",
            _ => ProjectFolder != null ? $"프로젝트: {IOPath.GetFileName(ProjectFolder)}" : ""
        };
        Changed?.Invoke();
    }

    public void ToggleTheme()
    {
        Dark = !Dark;
        SettingsService.Save(new AppSettings { LastProject = ProjectFolder, Theme = Dark ? "Dark" : "Light" });
        Changed?.Invoke();
    }

    public void Select(NodeModel node)
    {
        SelectedId = node?.Id;
        Changed?.Invoke();
    }

    public NodeModel AddNode(float canvasW, float canvasH)
    {
        var center = ScreenToWorld(new PointF(canvasW / 2, canvasH / 2));
        var m = new NodeModel
        {
            Name = UniqueName("새 노드"),
            Type = NodeType.Dialogue,
            X = Math.Round(center.X - NodeW / 2),
            Y = Math.Round(center.Y - NodeH / 2)
        };
        while (Data.Nodes.Any(n => Math.Abs(n.X - m.X) < 20 && Math.Abs(n.Y - m.Y) < 20))
        {
            m.X += 30;
            m.Y += 30;
        }

        ExcelService.CreateTemplate(FullPath(m), m.Type);
        Data.Nodes.Add(m);
        RefreshPreview(m, true);
        SelectedId = m.Id;
        MarkDirty();
        Status = $"'{m.Name}' 노드를 만들었습니다";
        Changed?.Invoke();
        return m;
    }

    public bool DeleteSelected()
    {
        var n = Selected();
        if (n == null)
        {
            Status = "삭제할 노드를 먼저 선택하세요";
            Changed?.Invoke();
            return false;
        }

        var path = FullPath(n);
        if (File.Exists(path))
        {
            var trash = IOPath.Combine(ProjectFolder, ProjectService.TrashFolder);
            Directory.CreateDirectory(trash);
            File.Move(path, IOPath.Combine(trash, $"{n.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"));
        }

        Data.Flows.RemoveAll(f => f.From == n.Id || f.To == n.Id);
        Data.Nodes.Remove(n);
        Previews.Remove(n.Id);
        if (FlowSourceId == n.Id) FlowSourceId = null;
        SelectedId = null;
        MarkDirty();
        Status = $"'{n.Name}' 노드를 삭제했습니다";
        Changed?.Invoke();
        return true;
    }

    public bool Rename(NodeModel n, string newName)
    {
        newName = (newName ?? "").Trim().TrimEnd('.');
        if (newName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) newName = newName[..^5];
        if (newName.Length == 0) { Status = "이름을 입력해 주세요"; Changed?.Invoke(); return false; }
        if (newName.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0)
        {
            Status = "파일 이름에 쓸 수 없는 문자가 있습니다";
            Changed?.Invoke();
            return false;
        }
        if (Data.Nodes.Any(o => o != n && string.Equals(o.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "같은 이름의 노드가 이미 있습니다";
            Changed?.Invoke();
            return false;
        }

        var oldPath = FullPath(n);
        var newPath = IOPath.Combine(ProjectFolder, newName + ".xlsx");
        if (File.Exists(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            Status = "같은 이름의 파일이 이미 있습니다";
            Changed?.Invoke();
            return false;
        }
        if (File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.Ordinal))
            File.Move(oldPath, newPath);

        n.Name = newName;
        RefreshPreview(n, true);
        MarkDirty();
        Changed?.Invoke();
        return true;
    }

    public void ChangeType(NodeModel n, NodeType type)
    {
        if (n.Type == type) return;
        n.Type = type;
        var path = FullPath(n);
        try
        {
            if (File.Exists(path) && ExcelService.IsUntouched(path))
                ExcelService.ReplaceWithTemplate(path, type);
        }
        catch { }
        RefreshPreview(n, true);
        MarkDirty();
        Changed?.Invoke();
    }

    public void HandleNodeTap(NodeModel n)
    {
        if (Mode == ToolMode.DrawFlow)
        {
            if (FlowSourceId == null) { FlowSourceId = n.Id; Status = $"시작: {n.Name}"; }
            else if (FlowSourceId == n.Id) { FlowSourceId = null; RubberWorld = null; }
            else
            {
                AddFlow(Data.Nodes.First(x => x.Id == FlowSourceId.Value), n);
                FlowSourceId = null;
                RubberWorld = null;
            }
            Changed?.Invoke();
            return;
        }

        Select(n);
    }

    public void AddFlow(NodeModel from, NodeModel to)
    {
        if (Data.Flows.Any(f => f.From == from.Id && f.To == to.Id))
        {
            Status = "이미 연결된 노드입니다";
            return;
        }
        Data.Flows.Add(new FlowModel { From = from.Id, To = to.Id });
        MarkDirty();
        Status = $"{from.Name} → {to.Name}";
    }

    public bool TryDeleteFlowAt(PointF world)
    {
        if (Mode != ToolMode.DeleteFlow) return false;
        foreach (var f in Data.Flows.ToList())
        {
            var a = Data.Nodes.FirstOrDefault(n => n.Id == f.From);
            var b = Data.Nodes.FirstOrDefault(n => n.Id == f.To);
            if (a == null || b == null) continue;
            if (DistanceToBezier(OutPoint(a), InPoint(b), world) < 14 / Zoom)
            {
                Data.Flows.Remove(f);
                MarkDirty();
                Status = "플로우를 삭제했습니다";
                Changed?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void MoveNode(NodeModel n, float dx, float dy)
    {
        n.X += dx;
        n.Y += dy;
        Changed?.Invoke();
    }

    public void Pan(float dx, float dy)
    {
        OffX += dx;
        OffY += dy;
        Changed?.Invoke();
    }

    public void ZoomAt(PointF screen, float factor)
    {
        var next = Math.Clamp(Zoom * factor, 0.3f, 2.5f);
        factor = next / Zoom;
        OffX = screen.X - (screen.X - OffX) * factor;
        OffY = screen.Y - (screen.Y - OffY) * factor;
        Zoom = next;
        Changed?.Invoke();
    }

    public void CenterOn(PointF world, float canvasW, float canvasH)
    {
        OffX = canvasW / 2 - world.X * Zoom;
        OffY = canvasH / 2 - world.Y * Zoom;
        Changed?.Invoke();
    }

    public void EndGesture()
    {
        MarkDirty();
        SaveNow();
        Changed?.Invoke();
    }

    public NodeModel Selected() => Data.Nodes.FirstOrDefault(n => n.Id == SelectedId);

    public string FullPath(NodeModel m) => IOPath.Combine(ProjectFolder, m.FileName);

    public void RefreshPreview(NodeModel n, bool force = false)
    {
        var path = FullPath(n);
        if (!File.Exists(path))
        {
            Previews[n.Id] = new List<string> { "(파일 없음)" };
            return;
        }
        try
        {
            var write = File.GetLastWriteTimeUtc(path);
            if (!force && _previewWrite.TryGetValue(n.Id, out var last) && last == write) return;
            Previews[n.Id] = ExcelService.ReadPreview(path);
            _previewWrite[n.Id] = write;
        }
        catch { }
    }

    public void MarkDirty()
    {
        if (_loading || ProjectFolder == null) return;
        _dirty = true;
    }

    public void SaveNow()
    {
        if (_loading || ProjectFolder == null || !_dirty) return;
        Data.View.Zoom = Zoom;
        Data.View.OffsetX = OffX;
        Data.View.OffsetY = OffY;
        ProjectService.Save(ProjectFolder, Data);
        _dirty = false;
    }

    public static PointF OutPoint(NodeModel m) => new((float)m.X + NodeW, (float)m.Y + NodeH / 2);
    public static PointF InPoint(NodeModel m) => new((float)m.X, (float)m.Y + NodeH / 2);

    public static void Bezier(PointF s, PointF e, out PointF c1, out PointF c2)
    {
        var dx = Math.Max(60, Math.Abs(e.X - s.X) * 0.5f);
        c1 = new PointF(s.X + dx, s.Y);
        c2 = new PointF(e.X - dx, e.Y);
    }

    public void UpdateMinimap(float canvasW, float canvasH)
    {
        var ratio = Math.Max(canvasW, 1) / Math.Max(canvasH, 1);
        const float maxW = 160;
        var w = maxW;
        var h = w / ratio;
        if (h > canvasH * 0.28f)
        {
            h = canvasH * 0.28f;
            w = h * ratio;
        }
        MinimapRect = new RectF(canvasW - w - 14, 14, w, h);

        var camL = -OffX / Zoom;
        var camT = -OffY / Zoom;
        var camR = camL + canvasW / Zoom;
        var camB = camT + canvasH / Zoom;
        float l = camL, t = camT, r = camR, b = camB;
        foreach (var n in Data.Nodes)
        {
            l = Math.Min(l, (float)n.X);
            t = Math.Min(t, (float)n.Y);
            r = Math.Max(r, (float)n.X + NodeW);
            b = Math.Max(b, (float)n.Y + NodeH);
        }
        var pad = Math.Max(24, Math.Max(r - l, b - t) * 0.08f);
        l -= pad; t -= pad; r += pad; b += pad;
        var bw = Math.Max(r - l, 1);
        var bh = Math.Max(b - t, 1);
        MmScale = Math.Min(w / bw, h / bh);
        MmOx = MinimapRect.X + (w - bw * MmScale) / 2;
        MmOy = MinimapRect.Y + (h - bh * MmScale) / 2;
        MmWorldL = l;
        MmWorldT = t;
    }

    public PointF WorldToMini(float x, float y) =>
        new((x - MmWorldL) * MmScale + MmOx, (y - MmWorldT) * MmScale + MmOy);

    public PointF MiniToWorld(PointF p) =>
        new((p.X - MmOx) / MmScale + MmWorldL, (p.Y - MmOy) / MmScale + MmWorldT);

    public bool HitMinimap(PointF screen) => MinimapRect.Contains(screen);

    public string ExportZipPath()
    {
        SaveNow();
        _dirty = true;
        SaveNow();
        var name = IOPath.GetFileName(ProjectFolder.TrimEnd('\\', '/'));
        var zip = IOPath.Combine(FileSystem.CacheDirectory, $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.zip");
        ExportService.ExportZip(ProjectFolder, zip);
        return zip;
    }

    private string UniqueName(string baseName)
    {
        var used = new HashSet<string>(Data.Nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var name = $"{baseName} {i}";
            if (!used.Contains(name) && !File.Exists(IOPath.Combine(ProjectFolder, name + ".xlsx"))) return name;
        }
    }

    private static float DistanceToBezier(PointF s, PointF e, PointF p)
    {
        Bezier(s, e, out var c1, out var c2);
        var best = float.MaxValue;
        for (int i = 1; i <= 20; i++)
        {
            var t = i / 20f;
            var u = 1 - t;
            var pt = new PointF(
                u * u * u * s.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * e.X,
                u * u * u * s.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * e.Y);
            var dx = p.X - pt.X;
            var dy = p.Y - pt.Y;
            best = Math.Min(best, MathF.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }
}
