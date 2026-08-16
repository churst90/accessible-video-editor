using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Samples;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.WpfSpike.Controls;

/// <summary>
/// A four-track scrubber, drawn rather than composed of widgets - the exact
/// shape of the control that killed the Avalonia attempt, so the same question
/// gets asked of WPF before anything is built on it.
///
/// The drawing is deliberately plain. Nothing here is trying to look good; the
/// spike answers one question and a pretty timeline that NVDA cannot read is
/// still a failure.
///
/// Every position string comes from <see cref="TrackProbe.Announce"/> and every
/// rectangle from <see cref="TimelineLayout.Build"/>, both untouched from Core.
/// That is a second, quieter finding: if the announcements had needed rewriting
/// for Windows, the claim that the interaction model is portable would have
/// been wrong.
/// </summary>
public sealed class TimelineCanvas : FrameworkElement
{
    private readonly EditSession _session;
    private readonly DocumentCursor _cursor = new();
    private readonly UiaAnnouncer _announcer;

    private TimelineView? _view;
    private double _pixelsPerSecond = 40;

    public TimelineCanvas(UiaAnnouncer announcer)
    {
        _announcer = announcer;

        _session = new EditSession(DemoProject.Create());
        _cursor.FocusedTrack = _session.Project.ProgrammeTrack.Id;

        Focusable = true;
        FocusVisualStyle = null;

        // Keyboard focus has to be real, not simulated: a control that paints a
        // focus ring without taking focus is invisible to a screen reader.
        Loaded += (_, _) => Keyboard.Focus(this);
    }

    /// <summary>Raised after anything that changes the status line.</summary>
    public event Action? Changed;

    public Project Project => _session.Project;

    public IReadOnlyList<Track> Tracks => [.. Project.InOrder];

    /// <summary>
    /// Named to avoid <see cref="FrameworkElement.Cursor"/>, which is the mouse
    /// pointer. Two things called "cursor" in one control is how a bug gets
    /// written that compiles.
    /// </summary>
    public DocumentCursor EditCursor => _cursor;

    public double ProgrammeTime => _cursor.ProgrammeTime;

    /// <summary>The announcement for one track at the cursor - the peers' labels come from here.</summary>
    public string DescribeTrack(Track track) =>
        TrackProbe.Announce(
            TrackProbe.At(Project, _session.Map, track.Id, _cursor.ProgrammeTime),
            _cursor.ProgrammeTime,
            Project.Settings.Verbosity);

    public string DescribeFocusedTrack() =>
        Project.TrackOf(_cursor.FocusedTrack ?? default) is { } track
            ? DescribeTrack(track)
            : Timecode.Speak(_cursor.ProgrammeTime);

    // ---- the peer ----------------------------------------------------------

    protected override AutomationPeer OnCreateAutomationPeer() => new TimelineCanvasPeer(this);

    /// <summary>
    /// Tells UIA that the rows changed, which is what makes a moving cursor
    /// observable to a screen reader rather than something it has to be told
    /// about separately.
    /// </summary>
    private void RefreshPeer()
    {
        if (UIElementAutomationPeer.FromElement(this) is TimelineCanvasPeer peer)
        {
            peer.Invalidate();
        }
    }

    // ---- keys --------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Core's own navigator, so a step means here exactly what it means on
        // Linux - word boundaries, segment edges, markers - rather than a
        // number of seconds this spike invented.
        var navigator = new TimelineNavigator(Project, _session.Map);

        switch (e.Key)
        {
            case Key.Right:
                Move(navigator.Move(_cursor.ProgrammeTime, _cursor.Granularity, 1));
                break;

            case Key.Left:
                Move(navigator.Move(_cursor.ProgrammeTime, _cursor.Granularity, -1));
                break;

            case Key.Down when !control:
                FocusTrack(1);
                break;

            case Key.Up when !control:
                FocusTrack(-1);
                break;

            case Key.Home:
                Move(0);
                break;

            case Key.End:
                Move(_session.Map.Duration);
                break;

            case Key.OemMinus:
                Zoom(coarser: true);
                break;

            case Key.OemPlus:
                Zoom(coarser: false);
                break;

            case Key.F12:
                // Urgent: it must arrive over a stream of position updates.
                _announcer.Say(WhereAmI(), UiaAnnouncer.Priority.Urgent);
                break;

            case Key.E:
                // A deliberate failure, to hear an urgent message interrupt the
                // progress stream. Holding Right and pressing E is the test.
                _announcer.Say("nothing to do here", UiaAnnouncer.Priority.Urgent);
                break;

            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    private void Move(double time)
    {
        _cursor.MoveTo(Math.Clamp(time, 0, _session.Map.Duration));

        InvalidateVisual();
        RefreshPeer();
        Changed?.Invoke();

        // Progress, because at navigation speed only the newest position is
        // worth hearing. This is the priority MAUI cannot express.
        _announcer.Say(DescribeFocusedTrack(), UiaAnnouncer.Priority.Progress);
    }

    private void FocusTrack(int direction)
    {
        var tracks = Tracks;
        var index = tracks.ToList().FindIndex(t => t.Id == _cursor.FocusedTrack);
        var next = Math.Clamp(index + direction, 0, tracks.Count - 1);

        if (next == index)
        {
            _announcer.Say(
                direction < 0 ? "first track" : "last track", UiaAnnouncer.Priority.Urgent);
            return;
        }

        _cursor.FocusedTrack = tracks[next].Id;

        InvalidateVisual();
        RefreshPeer();
        Changed?.Invoke();

        _announcer.Say($"{tracks[next].Name}. {DescribeTrack(tracks[next])}", UiaAnnouncer.Priority.Normal);
    }

    private void Zoom(bool coarser)
    {
        _cursor.Granularity = coarser ? _cursor.Granularity.Coarser() : _cursor.Granularity.Finer();
        _pixelsPerSecond = Math.Clamp(coarser ? _pixelsPerSecond / 2 : _pixelsPerSecond * 2, 2, 400);

        InvalidateVisual();
        Changed?.Invoke();

        _announcer.Say($"step {_cursor.Granularity.Describe()}", UiaAnnouncer.Priority.Normal);
    }

    private string WhereAmI() =>
        $"{Timecode.Speak(_cursor.ProgrammeTime)} of {Timecode.Speak(_session.Map.Duration)}. "
        + $"{Project.TrackOf(_cursor.FocusedTrack ?? default)?.Name ?? "no track"}. "
        + DescribeFocusedTrack();

    // ---- drawing -----------------------------------------------------------

    protected override void OnRender(DrawingContext context)
    {
        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);

        context.DrawRectangle(Background(0x14, 0x16, 0x1a), null, new Rect(0, 0, width, height));

        var viewport = new TimelineViewport(width, _pixelsPerSecond, ViewStart(width), LaneHeight: 56);

        _view = TimelineLayout.Build(Project, _session.Map, _cursor, viewport);

        var typeface = new Typeface("Segoe UI");

        foreach (var tick in _view.Ticks)
        {
            context.DrawLine(
                new Pen(Background(0x3a, 0x40, 0x4a), 1),
                new Point(tick.X, 0), new Point(tick.X, _view.RulerHeight));

            if (tick.Labelled && tick.Label is { } label)
            {
                context.DrawText(Text(label, typeface, 11, 0xb8, 0xc0, 0xcc), new Point(tick.X + 3, 4));
            }
        }

        foreach (var lane in _view.Lanes)
        {
            context.DrawRectangle(
                Background(
                    lane.IsFocused ? (byte)0x1f : (byte)0x1a,
                    lane.IsFocused ? (byte)0x26 : (byte)0x1e,
                    0x2e),
                null,
                new Rect(0, lane.Top, width, lane.Height));

            foreach (var block in lane.Blocks)
            {
                var rect = new Rect(block.X, lane.Top + 6, Math.Max(2, block.Width), lane.Height - 12);

                context.DrawRectangle(
                    Background(
                        block.Disabled ? (byte)0x33 : (byte)0x2c,
                        block.Disabled ? (byte)0x33 : (byte)0x5a,
                        0x8a),
                    block.UnderCursor ? new Pen(Background(0xff, 0xd7, 0x4a), 2) : null,
                    rect);

                if (block.Width > 40 && block.Label.Length > 0)
                {
                    context.PushClip(new RectangleGeometry(rect));
                    context.DrawText(
                        Text(block.Label, typeface, 12, 0xf0, 0xf4, 0xf8),
                        new Point(rect.X + 5, rect.Y + 4));
                    context.Pop();
                }
            }
        }

        if (_view.PlayheadX is { } playhead)
        {
            context.DrawLine(
                new Pen(Background(0xff, 0x5c, 0x5c), 2),
                new Point(playhead, 0), new Point(playhead, height));
        }
    }

    /// <summary>Keeps the playhead on screen, using Core's own follow rule.</summary>
    private double ViewStart(double width)
    {
        var duration = _pixelsPerSecond > 0 ? width / _pixelsPerSecond : 0;

        _viewStart = TimelineLayout.Follow(_viewStart, _cursor.ProgrammeTime, duration);

        return _viewStart;
    }

    private double _viewStart;

    private static SolidColorBrush Background(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    private static FormattedText Text(string text, Typeface typeface, double size, byte r, byte g, byte b) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size,
            Background(r, g, b), 1.0);
}
