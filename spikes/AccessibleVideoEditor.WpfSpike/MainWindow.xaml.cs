using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.WpfSpike.Controls;

namespace AccessibleVideoEditor.WpfSpike;

public partial class MainWindow : Window
{
    private readonly TimelineCanvas _canvas;
    private readonly UiaAnnouncer _announcer;

    public MainWindow()
    {
        InitializeComponent();

        // The announcer is hosted on the window rather than on the canvas: a
        // notification raised from an element that has just lost focus can be
        // dropped, and the window is the one element always present.
        _announcer = new UiaAnnouncer(this);

        _canvas = new TimelineCanvas(_announcer);
        _canvas.Changed += Tick;

        CanvasHost.Content = _canvas;
        AutomationProperties.SetName(_canvas, "Timeline");

        Loaded += (_, _) =>
        {
            Keyboard.Focus(_canvas);
            Tick();

            // Said once, on arrival. If this is the only thing NVDA ever reads,
            // the announcement channel works and the peer tree does not - a
            // different failure from total silence, and worth telling apart.
            _announcer.Say(
                "Windows scrubber spike. Four tracks. Arrows to move.",
                UiaAnnouncer.Priority.Normal);
        };
    }

    /// <summary>
    /// The status line, which is also a polite live region - so a reader that
    /// ignores notifications entirely still has a route to the position.
    /// </summary>
    private void Tick()
    {
        var text = $"{Timecode.FormatShort(_canvas.ProgrammeTime)}  ·  "
                   + $"{_canvas.Project.TrackOf(_canvas.EditCursor.FocusedTrack ?? default)?.Name ?? "-"}  ·  "
                   + $"step {_canvas.EditCursor.Granularity.Describe()}";

        if (Status.Text != text) Status.Text = text;
    }
}
