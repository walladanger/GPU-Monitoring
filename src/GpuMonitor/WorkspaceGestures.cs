using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace GpuMonitor;

internal sealed partial class MonitoringWorkspace
{
    private string? movingId, dropId;
    private bool dropAfter;
    private double moveX, moveY;

    private void WireGestures(WorkspaceChartCard card)
    {
        string id = card.Specification.Id;
        Point moveStart = new(), resizeStart = new();
        card.TitleHandle.DragStarted += (_, _) => { movingId = id; dropId = null; moveX = moveY = 0; moveStart = PointerPosition(); };
        card.TitleHandle.DragDelta += (_, e) =>
        {
            if (movingId != id) return;
            var point = PointerPosition(); moveX = point.X - moveStart.X; moveY = point.Y - moveStart.Y;
            PreviewDropAt(id, point);
        };
        card.TitleHandle.DragCompleted += (_, e) =>
        {
            if (movingId != id) return;
            string? target = dropId; bool after = dropAfter;
            bool moved = Math.Abs(moveX) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moveY) >= SystemParameters.MinimumVerticalDragDistance;
            ClearDrop();
            if (!e.Canceled && moved && target != null && cards.ContainsKey(id) && cards.ContainsKey(target))
                Change(() => WorkspaceActions.MoveTo(Layout, id, target, after));
        };
        WorkspacePanel? original = null, resized = null;
        double slotWidth = 0; int columns = 1;
        card.ResizeGrip.DragStarted += (_, _) =>
        {
            original = Layout.Panels.FirstOrDefault(p => p.Id == id); resized = null; resizeStart = PointerPosition();
            columns = Math.Max(1, (int)Math.Floor(panels.ActualWidth / 500)); slotWidth = Math.Max(1, panels.ActualWidth / columns);
        };
        card.ResizeGrip.DragDelta += (_, e) =>
        {
            if (original == null) return;
            // The thumb moves as its card resizes. Measure in stable workspace coordinates,
            // not relative to that moving thumb, to prevent snap feedback and size jumps.
            var point = PointerPosition();
            resized = WorkspaceActions.Resize(original, point.X - resizeStart.X, point.Y - resizeStart.Y, slotWidth, columns);
            card.PreviewSize(resized); Reflow();
        };
        card.ResizeGrip.DragCompleted += (_, e) =>
        {
            var candidate = resized; var before = original; original = resized = null;
            card.PreviewSize(null); Reflow();
            if (!e.Canceled && candidate != null && before != null &&
                (candidate.WidthSlots != before.WidthSlots || candidate.HeightRows != before.HeightRows) &&
                cards.TryGetValue(id, out var current) && current == card) PutPanel(candidate);
        };
        foreach (var handle in new[] { card.TitleHandle, card.ResizeGrip })
            handle.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && handle.IsDragging) { handle.CancelDrag(); e.Handled = true; } };
    }

    // Pointer positions and hit rectangles are in workspace DIPs, including under Windows scaling.
    internal void PreviewDropAt(string sourceId, Point point)
    {
        foreach (var card in cards.Values) card.BorderBrush = OverviewTheme.Border;
        dropId = null;
        if (movingId != sourceId || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        foreach (var panel in Layout.Panels)
        {
            if (panel.Id == sourceId) continue;
            var card = cards[panel.Id]; var bounds = new Rect(card.TranslatePoint(new Point(), panels), card.RenderSize);
            bounds.Inflate(7, 7);
            if (!bounds.Contains(point)) continue;
            dropId = panel.Id; dropAfter = point.Y > bounds.Top + bounds.Height / 2;
            card.BorderBrush = OverviewTheme.Blue; break;
        }
    }

    private void ClearDrop()
    {
        movingId = dropId = null;
        foreach (var card in cards.Values) card.BorderBrush = OverviewTheme.Border;
    }
    private void Change(Func<WorkspaceLayout> action)
    {
        try { var next = action(); if (!ReferenceEquals(next, Layout)) ApplyLayout(next); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { System.Windows.MessageBox.Show(Window.GetWindow(this), error.Message, "Panel change could not be applied"); }
    }
    internal WorkspaceChartCard FindCard(string id) => cards[id];
    internal Point PanelPoint(string id, double verticalFraction = .25)
    {
        var card = cards[id]; var origin = card.TranslatePoint(new Point(), panels);
        return new(origin.X + card.ActualWidth / 2, origin.Y + card.ActualHeight * verticalFraction);
    }
}
