using Nody.Models;

namespace Nody.AndroidApp.Editor;

public class EditorDrawable : IDrawable
{
    public EditorController Editor { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var e = Editor;
        if (e == null) return;

        var dark = e.Dark;
        var bg = dark ? Color.FromArgb("#1A1D23") : Color.FromArgb("#F8F9FB");
        var grid = dark ? Color.FromArgb("#30353F") : Color.FromArgb("#C6CBD6");
        var nodeBg = dark ? Color.FromArgb("#262B34") : Colors.White;
        var nodeBd = dark ? Color.FromArgb("#3A4050") : Color.FromArgb("#D5D9E2");
        var fg = dark ? Color.FromArgb("#E6E8EE") : Color.FromArgb("#1F2430");
        var muted = dark ? Color.FromArgb("#9AA3B2") : Color.FromArgb("#6B7280");
        var sel = dark ? Color.FromArgb("#60A5FA") : Color.FromArgb("#3B82F6");
        var dialogue = Color.FromArgb(dark ? "#5B9BFF" : "#4C8BF5");
        var choice = Color.FromArgb(dark ? "#FBBF24" : "#F59E0B");
        var flowN = Color.FromArgb(dark ? "#7B8598" : "#8A94A6");
        var flowC = choice;
        var flowS = Color.FromArgb("#10B981");
        var mmBg = dark ? Color.FromArgb("#2B2A29") : Color.FromArgb("#F3F2F1");

        canvas.FillColor = bg;
        canvas.FillRectangle(dirtyRect);

        DrawGrid(canvas, grid, e);

        canvas.SaveState();
        canvas.Translate(e.OffX, e.OffY);
        canvas.Scale(e.Zoom, e.Zoom);

        foreach (var f in e.Data.Flows)
        {
            var a = e.Data.Nodes.FirstOrDefault(n => n.Id == f.From);
            var b = e.Data.Nodes.FirstOrDefault(n => n.Id == f.To);
            if (a == null || b == null) continue;
            var s = EditorController.OutPoint(a);
            var t = EditorController.InPoint(b);
            EditorController.Bezier(s, t, out var c1, out var c2);
            canvas.StrokeColor = a.Type == NodeType.Choice ? flowC : flowN;
            canvas.StrokeSize = 2.5f / e.Zoom;
            canvas.DrawPath(BezierPath(s, c1, c2, t));
            DrawArrow(canvas, t, a.Type == NodeType.Choice ? flowC : flowN, e.Zoom);
        }

        if (e.FlowSourceId != null && e.RubberWorld is PointF rub)
        {
            var src = e.Data.Nodes.FirstOrDefault(n => n.Id == e.FlowSourceId);
            if (src != null)
            {
                var s = EditorController.OutPoint(src);
                EditorController.Bezier(s, rub, out var c1, out var c2);
                canvas.StrokeColor = flowS;
                canvas.StrokeSize = 2f / e.Zoom;
                canvas.StrokeDashPattern = new float[] { 6, 4 };
                canvas.DrawPath(BezierPath(s, c1, c2, rub));
                canvas.StrokeDashPattern = null;
            }
        }

        foreach (var n in e.Data.Nodes)
            DrawNode(canvas, e, n, nodeBg, nodeBd, fg, muted, sel, dialogue, choice, flowS);

        canvas.RestoreState();

        e.UpdateMinimap(Width, Height);
        DrawMinimap(canvas, e, mmBg, nodeBd, sel, dialogue, choice);
    }

    private static void DrawGrid(ICanvas canvas, Color color, EditorController e)
    {
        canvas.FillColor = color;
        const float step = 40;
        var left = -e.OffX / e.Zoom;
        var top = -e.OffY / e.Zoom;
        var right = left + Width / Math.Max(e.Zoom, 0.01f);
        var bottom = top + Height / Math.Max(e.Zoom, 0.01f);
        var x0 = MathF.Floor(left / step) * step;
        var y0 = MathF.Floor(top / step) * step;
        for (var x = x0; x <= right + step; x += step)
        {
            for (var y = y0; y <= bottom + step; y += step)
                canvas.FillCircle(x * e.Zoom + e.OffX, y * e.Zoom + e.OffY, 1.4f);
        }
    }

    private static void DrawNode(ICanvas canvas, EditorController e, NodeModel n,
        Color nodeBg, Color nodeBd, Color fg, Color muted, Color sel, Color dialogue, Color choice, Color flowS)
    {
        var r = e.NodeWorldRect(n);
        var accent = n.Type == NodeType.Dialogue ? dialogue : choice;
        var border = n.Id == e.FlowSourceId ? flowS : n.Id == e.SelectedId ? sel : nodeBd;

        canvas.FillColor = nodeBg;
        canvas.FillRoundedRectangle(r, 8);
        canvas.StrokeColor = border;
        canvas.StrokeSize = (n.Id == e.SelectedId ? 3 : 2) / e.Zoom;
        canvas.DrawRoundedRectangle(r, 8);

        canvas.FillColor = accent;
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, 6, 6);

        canvas.FontColor = fg;
        canvas.FontSize = 14 / e.Zoom;
        canvas.DrawString(n.Name, r.X + 10, r.Y + 14, r.Width - 20, 22, HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.FillColor = Color.FromArgb("#22" + (e.Dark ? "1B1F26" : "F3F4F8").TrimStart('#'));
        canvas.FillColor = e.Dark ? Color.FromArgb("#1B1F26") : Color.FromArgb("#F3F4F8");
        canvas.FillRoundedRectangle(r.X + 10, r.Y + 40, 110, 22, 5);
        canvas.FillColor = accent;
        canvas.FillCircle(r.X + 20, r.Y + 51, 4);
        canvas.FontColor = fg;
        canvas.FontSize = 11 / e.Zoom;
        canvas.DrawString(n.Type == NodeType.Dialogue ? "대사 파일" : "선택지 파일",
            r.X + 28, r.Y + 40, 90, 22, HorizontalAlignment.Left, VerticalAlignment.Center);

        e.Previews.TryGetValue(n.Id, out var lines);
        canvas.FontColor = muted;
        canvas.FontSize = 11 / e.Zoom;
        var y = r.Y + 70;
        foreach (var line in (lines ?? new List<string> { "(비어 있음)" }).Take(3))
        {
            canvas.DrawString(line, r.X + 10, y, r.Width - 20, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
            y += 16;
        }
    }

    private static void DrawMinimap(ICanvas canvas, EditorController e, Color mmBg, Color border, Color sel, Color dialogue, Color choice)
    {
        var mm = e.MinimapRect;
        canvas.FillColor = mmBg;
        canvas.FillRoundedRectangle(mm, 4);
        canvas.StrokeColor = border;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(mm, 4);

        foreach (var n in e.Data.Nodes)
        {
            var p = e.WorldToMini((float)n.X, (float)n.Y);
            canvas.FillColor = n.Type == NodeType.Dialogue ? dialogue : choice;
            canvas.FillRoundedRectangle(p.X, p.Y, Math.Max(3, EditorController.NodeW * e.MmScale), Math.Max(2, EditorController.NodeH * e.MmScale), 1);
        }

        var camL = -e.OffX / e.Zoom;
        var camT = -e.OffY / e.Zoom;
        var cam = e.WorldToMini(camL, camT);
        canvas.StrokeColor = sel;
        canvas.FillColor = Color.FromArgb("#332563EB");
        canvas.StrokeSize = 1.4f;
        canvas.FillRectangle(cam.X, cam.Y, Math.Max(4, e.Width / e.Zoom * e.MmScale), Math.Max(4, e.Height / e.Zoom * e.MmScale));
        canvas.DrawRectangle(cam.X, cam.Y, Math.Max(4, e.Width / e.Zoom * e.MmScale), Math.Max(4, e.Height / e.Zoom * e.MmScale));
    }

    private static PathF BezierPath(PointF s, PointF c1, PointF c2, PointF e)
    {
        var p = new PathF();
        p.MoveTo(s);
        p.CurveTo(c1, c2, e);
        return p;
    }

    private static void DrawArrow(ICanvas canvas, PointF e, Color color, float zoom)
    {
        canvas.FillColor = color;
        var p = new PathF();
        p.MoveTo(e);
        p.LineTo(e.X - 11, e.Y - 6);
        p.LineTo(e.X - 11, e.Y + 6);
        p.Close();
        canvas.FillPath(p);
    }
}
