using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Speech;
using AccessibleVideoEditor.Vision;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The viewfinder: knowing where you are in the frame, by ear.
///
/// A tone panned to where your face is, pitched to how far up it is, ticking
/// faster as you get too close - and <b>silence when you are framed</b>, which
/// is the part that matters. Silence is the target, so you stop moving when the
/// sound stops rather than having to interpret anything.
///
/// The words are held back unless the guidance changes. A viewfinder that
/// repeats "move left" four times a second is one you turn off, and then it is
/// not a viewfinder at all.
/// </summary>
public sealed class ViewfinderSession(Func<IAnnouncer> announcer, Func<SdlAudioOutput?> audio) : IDisposable
{
    private readonly ViewfinderCamera _camera = new();
    private readonly FaceTracker _tracker = new();

    private uint _tick;
    private double _seconds;
    private double _nextBeep;
    private string _lastSpoken = string.Empty;
    private double _lastSpokeAt = -10;
    private bool _wasLocked;
    private bool _lostAnnounced;

    public bool IsOpen => _camera.IsRunning;

    /// <summary>Seconds of the same guidance before it is repeated.</summary>
    public const double RepeatAfter = 6;

    public void Open(string device, string deviceName)
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        // Said before the camera comes on, every time. A camera that starts
        // without warning is a camera you cannot trust.
        Say($"opening the camera on {deviceName}", urgent: true);

        var result = _camera.Start(device);

        if (!_camera.IsRunning)
        {
            Say(result, urgent: true);
            return;
        }

        _tracker.Reset();
        _seconds = 0;
        _nextBeep = 0;
        _lastSpoken = string.Empty;
        _lastSpokeAt = -10;
        _wasLocked = false;
        _lostAnnounced = false;

        _camera.Failed += problem => GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
        {
            Say($"the camera stopped: {problem}", urgent: true);
            Close();

            return false;
        });

        // Polled rather than driven by the frame event: the camera arrives at
        // twelve frames a second and the ear wants a steadier rhythm than that.
        _tick = GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 100, OnTick);

        Say("viewfinder on. Silence means you are framed. Escape closes it, F8 says what is in shot",
            urgent: true);
    }

    private bool OnTick()
    {
        if (!_camera.IsRunning) return false;

        _seconds += 0.1;

        var frame = _camera.Latest;

        if (frame is null) return true;

        var found = FaceFinder.Find(frame, ViewfinderCamera.Width, ViewfinderCamera.Height);

        var error = _tracker.Track(
            found is null ? [] : [found],
            _seconds);

        var state = ViewfinderSonifier.Evaluate(error);

        if (!error.FaceVisible)
        {
            // Said once, not every tenth of a second - and the searching tone
            // keeps going, so you can sweep the camera and hear it catch.
            if (!_lostAnnounced)
            {
                Say("no face in shot", urgent: true);
                _lostAnnounced = true;
            }
        }
        else
        {
            _lostAnnounced = false;
        }

        if (state.Locked)
        {
            // The moment of arriving is worth marking; staying there is not.
            if (!_wasLocked)
            {
                _wasLocked = true;
                audio()?.Play(880, 0.09, 0.5, 0);
                Say("framed", urgent: true);
            }

            return true;
        }

        _wasLocked = false;

        if (_seconds >= _nextBeep)
        {
            audio()?.Play(state.PitchHz, 0.045, 0.4, state.Pan);
            _nextBeep = _seconds + 1.0 / Math.Max(0.5, state.BeepsPerSecond);
        }

        if (state.Guidance != _lastSpoken || _seconds - _lastSpokeAt > RepeatAfter)
        {
            _lastSpoken = state.Guidance;
            _lastSpokeAt = _seconds;

            Say(state.Guidance);
        }

        return true;
    }

    /// <summary>
    /// The talking viewfinder: what is actually in shot, rather than where you
    /// are in it. A different question, and the only one that needs eyes.
    /// </summary>
    public async void DescribeShot()
    {
        if (_camera.Latest is not { } frame)
        {
            Say("the viewfinder is not open", urgent: true);
            return;
        }

        Say("looking");

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "viewfinder-shot.png");

        var canvas = new AccessibleVideoEditor.Core.Images.Canvas(
            ViewfinderCamera.Width, ViewfinderCamera.Height);

        for (var y = 0; y < ViewfinderCamera.Height; y++)
        {
            for (var x = 0; x < ViewfinderCamera.Width; x++)
            {
                var at = (y * ViewfinderCamera.Width + x) * 3;

                canvas.Set(x, y, (frame[at], frame[at + 1], frame[at + 2]));
            }
        }

        canvas.WritePng(path);

        var describer = new FrameDescriber();

        if (!describer.IsAvailable)
        {
            Say("the claude command is not installed, so the shot cannot be described", urgent: true);
            return;
        }

        Say(await describer.DescribeAsync(path), urgent: true);
    }

    public void Close()
    {
        if (_tick != 0)
        {
            GLib.Functions.SourceRemove(_tick);
            _tick = 0;
        }

        var said = _camera.Stop();

        Say(said, urgent: true);
    }

    private void Say(string text, bool urgent = false) =>
        announcer().Say(text, urgent ? AnnouncePriority.Urgent : AnnouncePriority.Normal);

    public void Dispose()
    {
        if (_tick != 0) GLib.Functions.SourceRemove(_tick);

        _camera.Dispose();
    }
}
