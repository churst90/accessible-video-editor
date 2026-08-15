using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The timeline, drawn.
///
/// <b>It is a picture of the model and nothing more.</b> It holds no state of
/// its own, answers no keys, and takes no focus - the accessible list beside it
/// is still the thing you interact with. Everything here is computed by
/// <see cref="TimelineLayout"/> in Core, so the drawing cannot start deciding
/// what the timeline contains.
///
/// Its whole purpose is that a sighted collaborator can look over your shoulder
/// and see the same edit you are hearing.
/// </summary>
public sealed class TimelineCanvas
{
    public Gtk_.DrawingArea Widget { get; }

    /// <summary>
    /// Asked for a layout at draw time rather than handed one, because only
    /// then is the real width known - and the width is what decides how much
    /// time fits on screen.
    /// </summary>
    public Func<int, int, TimelineView?>? Layout { get; set; }

    /// <summary>
    /// Peaks for a source, or null while they are still being extracted. Null
    /// is normal and must never be an error: a block simply draws solid until
    /// its waveform turns up.
    /// </summary>
    public Func<SourceId, WaveformData?>? Waveforms { get; set; }

    public TimelineCanvas()
    {
        Widget = Gtk_.DrawingArea.New();
        Widget.Hexpand = true;
        Widget.Vexpand = true;
        Widget.CanFocus = false;
        Widget.Focusable = false;

        // Nothing here is worth stopping on with a screen reader; the lane list
        // next to it carries every one of these facts as text.
        Widget.AccessibleRole = Gtk_.AccessibleRole.Presentation;

        Widget.SetDrawFunc((_, cr, width, height) => Draw(cr, width, height));
    }

    /// <summary>Called whenever the model changes; the draw itself is GTK's business.</summary>
    public void Redraw() => Widget.QueueDraw();

    private void Draw(Cairo.Context cr, int width, int height)
    {
        Fill(cr, Theme.Window, 0, 0, width, height);

        if (Layout?.Invoke(width, height) is not { } view) return;

        DrawLanes(cr, view, width);
        DrawSelection(cr, view, height);
        DrawBlocks(cr, view);
        DrawRuler(cr, view, width);
        DrawPlayhead(cr, view, height);

        if (view.EmptyMessage is { } message)
        {
            cr.SetSourceRgb(Theme.Faint.R, Theme.Faint.G, Theme.Faint.B);
            cr.SelectFontFace("Cantarell", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
            cr.SetFontSize(13);
            cr.MoveTo(16, view.RulerHeight + 30);
            cr.ShowText(message);
        }
    }

    // ---- lanes -------------------------------------------------------------

    private static void DrawLanes(Cairo.Context cr, TimelineView view, int width)
    {
        foreach (var lane in view.Lanes)
        {
            var background = lane.IsFocused
                ? Theme.LaneFocused
                : lane.Index % 2 == 0 ? Theme.LaneEven : Theme.LaneOdd;

            Fill(cr, background, 0, lane.Top, width, lane.Height);

            // The focused lane gets a bar down its left edge as well as a tint:
            // a tint alone is too subtle at a glance, and colour alone should
            // never be the only carrier of a state.
            if (lane.IsFocused)
            {
                Fill(cr, Theme.Accent, 0, lane.Top, 3, lane.Height);
            }

            if (lane.Armed)
            {
                Fill(cr, Theme.Armed, width - 4, lane.Top, 4, lane.Height);
            }

            Fill(cr, Theme.Line, 0, lane.Top + lane.Height, width, 1);
        }
    }

    private static void DrawSelection(Cairo.Context cr, TimelineView view, int height)
    {
        if (view.Selection is not { } band || band.Width <= 0) return;

        var top = view.RulerHeight;

        cr.SetSourceRgba(Theme.Selection.R, Theme.Selection.G, Theme.Selection.B, 0.20);
        cr.Rectangle(band.X, top, band.Width, height - top);
        cr.Fill();

        // Edges drawn solid so a very short selection is still visible when the
        // wash across it is only a pixel or two wide.
        cr.SetSourceRgba(Theme.Selection.R, Theme.Selection.G, Theme.Selection.B, 0.95);
        cr.Rectangle(band.X, top, 1.5, height - top);
        cr.Rectangle(band.X + band.Width - 1.5, top, 1.5, height - top);
        cr.Fill();
    }

    // ---- blocks ------------------------------------------------------------

    private void DrawBlocks(Cairo.Context cr, TimelineView view)
    {
        foreach (var lane in view.Lanes)
        {
            foreach (var block in lane.Blocks)
            {
                DrawBlock(cr, lane, block);
            }
        }
    }

    private void DrawBlock(Cairo.Context cr, TimelineLane lane, TimelineBlock block)
    {
        const double inset = 4;

        var x = block.X;
        var y = lane.Top + inset;
        var w = Math.Max(TimelineLayout.MinimumBlockWidth, block.Width);
        var h = lane.Height - inset * 2;

        var colour = Theme.ColourFor(block.Kind);

        // Disabled material stays on screen - it is restorable, not gone - so it
        // is drawn as a ghost rather than removed.
        if (block.Disabled) colour = colour.Darken(0.55);
        if (block.Hidden) colour = colour.Darken(0.3);

        RoundedRectangle(cr, x, y, w, h, 5);
        cr.SetSourceRgba(colour.R, colour.G, colour.B, block.Disabled ? 0.45 : 1.0);
        cr.Fill();

        // A lighter band along the top gives the block some depth without a
        // gradient, which would cost a pattern object per block.
        RoundedRectangle(cr, x, y, w, Math.Min(3, h), 5);
        cr.SetSourceRgba(1, 1, 1, 0.13);
        cr.Fill();

        if (lane.ShowsWaveform && block.Source is { } source && w > 8)
        {
            DrawWaveform(cr, block, source, x, y, w, h);
        }

        DrawFades(cr, block, x, y, w, h);

        if (block.HasTransitionIn && block.TransitionWidth > 0)
        {
            // A transition overlaps the outgoing segment, so it is drawn as a
            // hatched band across the join rather than as part of either side.
            DrawTransition(cr, x, y, Math.Min(block.TransitionWidth, w), h);
        }

        // The border carries the cursor: brighter and thicker on the segment
        // the cursor is inside, so it can be found without reading a label.
        RoundedRectangle(cr, x + 0.5, y + 0.5, Math.Max(1, w - 1), Math.Max(1, h - 1), 5);
        if (block.UnderCursor)
        {
            cr.SetSourceRgb(1, 1, 1);
            cr.LineWidth = 2;
        }
        else
        {
            cr.SetSourceRgba(0, 0, 0, 0.5);
            cr.LineWidth = 1;
        }

        cr.Stroke();

        if (block.Muted) DrawMuteStripe(cr, x, y, w, h);

        DrawLabel(cr, block, x, y, w, h);
    }

    private void DrawWaveform(
        Cairo.Context cr,
        TimelineBlock block,
        SourceId source,
        double x,
        double y,
        double w,
        double h)
    {
        if (Waveforms?.Invoke(source) is not { } data || data.Peaks.Count == 0) return;

        var from = block.SourceOut > block.SourceIn ? block.SourceIn : 0;
        var to = block.SourceOut > block.SourceIn ? block.SourceOut : data.Duration;

        var columns = Math.Max(1, (int)w);
        var peaks = data.Slice(from, to, columns);
        if (peaks.Length == 0) return;

        var middle = y + h / 2;
        var half = h / 2 - 3;

        cr.Save();
        RoundedRectangle(cr, x, y, w, h, 5);
        cr.Clip();

        cr.SetSourceRgba(1, 1, 1, 0.42);

        for (var i = 0; i < peaks.Length; i++)
        {
            var amplitude = Math.Max(0.6, peaks[i] * half);
            cr.Rectangle(x + i, middle - amplitude, 1, amplitude * 2);
        }

        cr.Fill();
        cr.Restore();
    }

    /// <summary>
    /// A fade is drawn as the wedge it is - the part of the block where the
    /// picture is on its way in or out - rather than as a marker at the edge.
    /// </summary>
    private static void DrawFades(Cairo.Context cr, TimelineBlock block, double x, double y, double w, double h)
    {
        cr.SetSourceRgba(0, 0, 0, 0.55);

        if (block.FadeInWidth > 0.5)
        {
            var width = Math.Min(block.FadeInWidth, w);
            cr.MoveTo(x, y);
            cr.LineTo(x + width, y);
            cr.LineTo(x, y + h);
            cr.ClosePath();
            cr.Fill();
        }

        if (block.FadeOutWidth > 0.5)
        {
            var width = Math.Min(block.FadeOutWidth, w);
            cr.MoveTo(x + w, y);
            cr.LineTo(x + w, y + h);
            cr.LineTo(x + w - width, y + h);
            cr.ClosePath();
            cr.Fill();
        }
    }

    private static void DrawTransition(Cairo.Context cr, double x, double y, double w, double h)
    {
        cr.Save();
        cr.Rectangle(x, y, w, h);
        cr.Clip();

        cr.SetSourceRgba(1, 1, 1, 0.30);
        cr.LineWidth = 1;

        for (var offset = -h; offset < w; offset += 6)
        {
            cr.MoveTo(x + offset, y + h);
            cr.LineTo(x + offset + h, y);
        }

        cr.Stroke();
        cr.Restore();
    }

    private static void DrawMuteStripe(Cairo.Context cr, double x, double y, double w, double h)
    {
        cr.Save();
        RoundedRectangle(cr, x, y, w, h, 5);
        cr.Clip();

        cr.SetSourceRgba(0, 0, 0, 0.45);
        cr.Rectangle(x, y, w, h);
        cr.Fill();

        cr.SetSourceRgba(1, 1, 1, 0.22);
        cr.LineWidth = 2;

        for (var offset = -h; offset < w; offset += 10)
        {
            cr.MoveTo(x + offset, y + h);
            cr.LineTo(x + offset + h, y);
        }

        cr.Stroke();
        cr.Restore();
    }

    private static void DrawLabel(Cairo.Context cr, TimelineBlock block, double x, double y, double w, double h)
    {
        if (w < 24 || block.Label.Length == 0) return;

        cr.Save();
        cr.Rectangle(x + 6, y, w - 10, h);
        cr.Clip();

        cr.SelectFontFace("Cantarell", Cairo.FontSlant.Normal, Cairo.FontWeight.Bold);
        cr.SetFontSize(11);

        // Drawn twice: a dark pass underneath so a pale block and a bright block
        // both keep the label readable.
        cr.SetSourceRgba(0, 0, 0, 0.55);
        cr.MoveTo(x + 7, y + h / 2 + 4.5);
        cr.ShowText(Ellipsise(block.Label, w));

        cr.SetSourceRgb(1, 1, 1);
        cr.MoveTo(x + 6, y + h / 2 + 4);
        cr.ShowText(Ellipsise(block.Label, w));

        cr.Restore();
    }

    /// <summary>
    /// Cut to fit by estimate rather than by measuring. The label is clipped to
    /// the block anyway, so an approximate cut only decides where the ellipsis
    /// lands, and estimating keeps this off the text-extents path per block.
    /// </summary>
    private static string Ellipsise(string text, double width)
    {
        var characters = (int)Math.Max(0, (width - 12) / 6.2);

        if (text.Length <= characters) return text;
        if (characters <= 1) return string.Empty;

        return text[..(characters - 1)] + "…";
    }

    // ---- ruler and playhead ------------------------------------------------

    private static void DrawRuler(Cairo.Context cr, TimelineView view, int width)
    {
        Fill(cr, Theme.Panel, 0, 0, width, view.RulerHeight);
        Fill(cr, Theme.Line, 0, view.RulerHeight - 1, width, 1);

        cr.SelectFontFace("Cantarell", Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
        cr.SetFontSize(10);

        foreach (var tick in view.Ticks)
        {
            if (tick.X < -40 || tick.X > width + 40) continue;

            var height = tick.Labelled ? 9.0 : 4.0;

            cr.SetSourceRgba(
                Theme.Faint.R, Theme.Faint.G, Theme.Faint.B,
                tick.Labelled ? 1.0 : 0.55);

            cr.Rectangle(tick.X, view.RulerHeight - height - 1, 1, height);
            cr.Fill();

            if (tick.Label is not { } label) continue;

            cr.SetSourceRgb(Theme.Muted.R, Theme.Muted.G, Theme.Muted.B);
            cr.MoveTo(tick.X + 4, 12);
            cr.ShowText(label);
        }
    }

    private static void DrawPlayhead(Cairo.Context cr, TimelineView view, int height)
    {
        if (view.PlayheadX is not { } x) return;

        cr.SetSourceRgb(Theme.Playhead.R, Theme.Playhead.G, Theme.Playhead.B);
        cr.Rectangle(x - 0.5, 0, 1.6, height);
        cr.Fill();

        // A head on top so the line can be found when it is sitting over a
        // block of a similar colour.
        cr.MoveTo(x - 5.5, 0);
        cr.LineTo(x + 6.5, 0);
        cr.LineTo(x + 0.5, 9);
        cr.ClosePath();
        cr.Fill();
    }

    // ---- primitives --------------------------------------------------------

    private static void Fill(Cairo.Context cr, Rgb colour, double x, double y, double w, double h)
    {
        cr.SetSourceRgb(colour.R, colour.G, colour.B);
        cr.Rectangle(x, y, w, h);
        cr.Fill();
    }

    private static void RoundedRectangle(Cairo.Context cr, double x, double y, double w, double h, double radius)
    {
        radius = Math.Min(radius, Math.Min(w, h) / 2);

        cr.NewPath();

        if (radius <= 0.5)
        {
            cr.Rectangle(x, y, w, h);
            return;
        }

        const double Quarter = Math.PI / 2;

        cr.Arc(x + w - radius, y + radius, radius, -Quarter, 0);
        cr.Arc(x + w - radius, y + h - radius, radius, 0, Quarter);
        cr.Arc(x + radius, y + h - radius, radius, Quarter, Math.PI);
        cr.Arc(x + radius, y + radius, radius, Math.PI, 3 * Quarter);
        cr.ClosePath();
    }
}
