using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Playback;
using AccessibleVideoEditor.Vision;
using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// Choosing an input, arming, recording, takes and monitoring.
///
/// The rule this file exists to keep: <b>listing a device never opens
/// it</b>. Enumeration reads sysfs and asks pactl; only arming and
/// recording touch hardware, and only when asked. A camera light that
/// comes on because a menu was opened is a bug you cannot see.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Where playback and scrub are heard. Deliberately separate from any
    /// input: recording from an interface while monitoring on headphones is
    /// normal, and assuming they are the same is how people end up listening to
    /// the wrong thing.
    /// </summary>
    private void ChooseOutput()
    {
        var outputs = new LinuxCaptureDevices()
            .EnumerateAsync(CaptureDeviceKind.Output).GetAwaiter().GetResult();

        if (outputs.Count == 0)
        {
            Announce("no outputs found", urgent: true);
            return;
        }

        var options = new List<string> { "System default" };
        options.AddRange(outputs.Select(o => o.Name));

        ChooseFromList("Monitoring output", options, index =>
        {
            if (index == 0)
            {
                Project.Settings.MonitorOutputId = null;
                Project.Settings.MonitorOutputName = null;
                _player.SetOutput(null);
                Announce("monitoring on the system default", urgent: true);
                return;
            }

            var chosen = outputs[index - 1];

            Project.Settings.MonitorOutputId = chosen.Id;
            Project.Settings.MonitorOutputName = chosen.Name;
            _player.SetOutput(chosen.Id);

            Announce($"monitoring on {chosen.Name}", urgent: true);
        });
    }

    /// <summary>
    /// Which input of a multi-input interface this track records. A two-input
    /// interface presents as one stereo source, so recording it whole puts the
    /// microphone on one side and silence on the other - which sounds like a
    /// broken take and has no meter to notice it on.
    /// </summary>
    private void ChooseChannel()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        ChooseFromList(
            $"Input channel for {track.Name}",
            ["Both channels as they come", "Left only - input 1", "Right only - input 2"],
            index =>
            {
                track.Channel = index switch
                {
                    1 => InputChannel.Left,
                    2 => InputChannel.Right,
                    _ => InputChannel.All,
                };

                Announce($"{track.Name} records {track.Channel switch
                {
                    InputChannel.Left => "the left channel only",
                    InputChannel.Right => "the right channel only",
                    _ => "both channels",
                }}", urgent: true);
            });
    }

    private void ChooseDevice()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        var kind = track.AcceptsInput switch
        {
            TrackInput.Camera => CaptureDeviceKind.Camera,
            TrackInput.Microphone => CaptureDeviceKind.Microphone,
            _ => (CaptureDeviceKind?)null,
        };

        if (kind is null)
        {
            Announce($"{track.Name} is an image track and records nothing", urgent: true);
            return;
        }

        var devices = new LinuxCaptureDevices().EnumerateAsync(kind.Value).GetAwaiter().GetResult();

        if (devices.Count == 0)
        {
            Announce($"no {kind.Value.ToString().ToLowerInvariant()} found", urgent: true);
            return;
        }

        ShowDeviceChooser(track, kind.Value, devices);
    }

    private void ShowDeviceChooser(Track track, CaptureDeviceKind kind, IReadOnlyList<CaptureDevice> devices)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = $"Input for {track.Name}";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(480, 320);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var heading = Gtk_.Label.New(
            $"{kind} for {track.Name}. Listing does not open a device.");
        heading.Xalign = 0;
        heading.Wrap = true;
        box.Append(heading);

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var device in devices)
        {
            list.Append(Row(device.Describe()));
        }

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        box.Append(scroller);

        void Choose()
        {
            var index = list.GetSelectedRow()?.GetIndex() ?? 0;
            if (index < 0 || index >= devices.Count) index = 0;

            track.CaptureDeviceId = devices[index].Id;
            track.CaptureDeviceName = devices[index].Name;

            Refresh();
            Announce($"{track.Name} input set to {devices[index].Name}", urgent: true);
            dialog.Close();
        }

        var accept = Gtk_.Button.NewWithLabel("Use this input");
        accept.OnClicked += (_, _) => Choose();
        box.Append(accept);

        list.OnRowActivated += (_, _) => Choose();

        dialog.SetChild(box);
        dialog.Present();

        var firstRow = list.GetRowAtIndex(0);
        if (firstRow is not null)
        {
            list.SelectRow(firstRow);
            firstRow.GrabFocus();
        }
    }

    /// <summary>
    /// The viewfinder is a mode, not a view. Framing a shot is not editing -
    /// you are pointing a camera at yourself and the tones need the whole audio
    /// channel - so it is something you enter and leave rather than a place you
    /// can Tab into and get stuck.
    /// </summary>
    private void EnterViewfinder()
    {
        if (_viewfinder is { IsOpen: true })
        {
            _viewfinder.Close();
            return;
        }

        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track
            || track.AcceptsInput != TrackInput.Camera)
        {
            Announce("focus a video track first; the viewfinder needs a camera", urgent: true);
            return;
        }

        if (track.CaptureDeviceId is not { Length: > 0 } device)
        {
            Announce("choose a camera first with Control F5", urgent: true);
            return;
        }

        _viewfinder ??= new ViewfinderSession(() => _announcer, () => _audio);
        _viewfinder.Open(device, track.CaptureDeviceName ?? device);
    }

    private void DescribeShot()
    {
        if (_viewfinder is not { IsOpen: true })
        {
            Announce("the viewfinder is not open; F9 opens it", urgent: true);
            return;
        }

        _viewfinder.DescribeShot();
    }

    /// <summary>
    /// Record, or stop recording. The signal check runs here rather than at arm
    /// time, because it opens the device - and a camera should never come on
    /// because a key was pressed for some other reason.
    /// </summary>
    private void ToggleRecording()
    {
        if (_recordings.Count > 0)
        {
            StopRecording();
            return;
        }

        var armed = Project.InOrder
            .Where(t => t.Armed && t.AcceptsInput != TrackInput.None)
            .ToList();

        if (armed.Count == 0)
        {
            Announce("no armed tracks. F5 arms the focused one", urgent: true);
            return;
        }

        var missing = armed.Where(t => t.CaptureDeviceId is not { Length: > 0 }).ToList();

        if (missing.Count > 0)
        {
            Announce(
                $"{string.Join(" and ", missing.Select(t => t.Name))} " +
                $"{(missing.Count == 1 ? "has" : "have")} no input chosen. Control F5 to choose one",
                urgent: true);
            return;
        }

        _ = StartRecordingAsync(armed);
    }

    private async Task StartRecordingAsync(List<Track> armed)
    {
        Announce($"checking {armed.Count} input{(armed.Count == 1 ? "" : "s")}", urgent: true);

        var devices = new List<(Track Track, CaptureDevice Device)>();

        // Every device is checked before any recording starts. Discovering the
        // second camera is dead after the first has been rolling for a minute
        // would waste the take.
        foreach (var track in armed)
        {
            var device = new CaptureDevice(
                track.CaptureDeviceId!,
                track.CaptureDeviceName ?? track.CaptureDeviceId!,
                track.AcceptsInput == TrackInput.Camera
                    ? CaptureDeviceKind.Camera
                    : CaptureDeviceKind.Microphone);

            var check = await _recorder.CheckSignalAsync(device).ConfigureAwait(true);

            if (!check.Ok)
            {
                Announce($"cannot record. {track.Name}: {check.Message}", urgent: true);
                return;
            }

            if (check.IsWarning) Announce($"{track.Name}: {check.Message}", urgent: true);

            devices.Add((track, device));
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "videoedit", "recordings");

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // A spoken countdown, because starting to talk the instant a key is
        // pressed gives you a take that begins mid-breath.
        foreach (var count in new[] { "three", "two", "one" })
        {
            Announce(count, urgent: true);
            await Task.Delay(700).ConfigureAwait(true);
        }

        _recordingFrom = _cursor.ProgrammeTime;

        foreach (var (track, device) in devices)
        {
            var extension = device.Kind == CaptureDeviceKind.Camera ? "mkv" : "m4a";
            var path = Path.Combine(
                directory, $"{track.Name.Replace(' ', '-')}-{stamp}.{extension}");

            try
            {
                var session = _recorder.Start(
                    device,
                    path,
                    device.Kind == CaptureDeviceKind.Camera ? MicrophoneForRecording() : null,
                    track.Channel);

                _recordings.Add((session, track.Id));
            }
            catch (Exception exception)
            {
                Announce($"{track.Name} failed to start: {exception.Message}", urgent: true);
            }
        }

        Announce(_recordings.Count == 0
            ? "nothing started recording"
            : $"recording {_recordings.Count} track{(_recordings.Count == 1 ? "" : "s")}. "
              + "Press R again to stop",
            urgent: true);
    }

    /// <summary>The first microphone, so a camera take carries sound as well as picture.</summary>
    private string? MicrophoneForRecording()
    {
        var microphones = new LinuxCaptureDevices()
            .EnumerateAsync(CaptureDeviceKind.Microphone).GetAwaiter().GetResult();

        return microphones.Count > 0 ? microphones[0].Id : null;
    }

    private void StopRecording()
    {
        var sessions = _recordings.ToList();
        _recordings.Clear();

        if (sessions.Count == 0) return;

        Announce("stopping", urgent: true);

        _ = Task.Run(async () =>
        {
            var results = new List<(string? Path, TrackId Track)>();

            foreach (var (session, track) in sessions)
            {
                results.Add((await session.StopAsync().ConfigureAwait(false), track));
            }

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                OnRecordingFinished(results);
                return false;
            });
        });
    }

    /// <summary>
    /// A finished recording becomes a take on the segment the cursor was on, so
    /// the structure of the video does not change while you are still getting
    /// the words right.
    /// </summary>
    private void OnRecordingFinished(List<(string? Path, TrackId Track)> results)
    {
        var written = results.Where(r => r.Path is not null).ToList();

        if (written.Count == 0)
        {
            Announce("recording produced no files", urgent: true);
            return;
        }

        // Every file goes into the media bin. Only the first becomes a take -
        // a second camera angle is a separate piece of footage, not another
        // attempt at the same line, and it belongs on its own track.
        Source? first = null;
        var length = 0.0;

        foreach (var (path, _) in written)
        {
            var duration = ProbeDuration(path!);

            var media = new Source
            {
                Id = Ids.NewSource(),
                Path = path!,
                Kind = path!.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
                    ? SourceKind.Audio
                    : SourceKind.Video,
                Duration = duration,
            };

            Project.Sources.Add(media);

            first ??= media;
            length = Math.Max(length, duration);
        }

        if (written.Count > 1)
        {
            Announce(
                $"recorded {written.Count} angles, {Timecode.Speak(length)}, all in the media bin",
                urgent: true);
            Refresh();
            return;
        }

        AttachAsTake(first!, length);
    }

    private void AttachAsTake(Source source, double length)
    {

        var target = _session.Map.Locate(_recordingFrom)?.Element.Id;

        if (target is null)
        {
            Announce($"recorded {Timecode.Speak(length)} into the media bin. " +
                     "There was no segment at the cursor to attach it to", urgent: true);
            Refresh();
            return;
        }

        var result = _session.Apply("record", (project, _) => EditOperations.AddTake(
            project,
            target.Value,
            new Take
            {
                Id = Ids.NewTake(),
                Source = source.Id,
                SourceIn = 0,
                SourceOut = length,
                Label = $"recorded {DateTime.Now:HH:mm}",
            }));

        Refresh();
        Announce($"recorded {Timecode.Speak(length)}. {result.Announce()}", urgent: true);
    }

    private static double ProbeDuration(string path)
    {
        try
        {
            return new FfmpegProbe().ProbeAsync(path).GetAwaiter().GetResult().Duration;
        }
        catch (Exception)
        {
            return 0;
        }
    }
    private void ToggleMonitoring()
    {
        if (_levels.IsRunning)
        {
            StopMonitoring();
            return;
        }

        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        var sourceId = track.CaptureDeviceId;

        // Monitoring a video track means monitoring the microphone that would
        // be recorded alongside it.
        if (track.AcceptsInput == TrackInput.Camera) sourceId = MicrophoneForRecording();

        if (sourceId is not { Length: > 0 })
        {
            Announce("no input to monitor. Control F5 to choose one", urgent: true);
            return;
        }

        _meter.Reset();
        _lastLevelDb = null;
        _meterSeconds = 0;

        _levels.Start(
            sourceId,
            level => _lastLevelDb = level,
            error => GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                Announce($"monitoring failed: {error}", urgent: true);
                return false;
            }));

        if (!_levels.IsRunning)
        {
            Announce("monitoring could not start", urgent: true);
            return;
        }

        // The tick is driven from the UI thread so it cannot outlive the mode.
        _meterTick = GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 120, OnMeterTick);

        Announce($"monitoring {track.Name}. Shift F9 to stop", urgent: true);
    }

    private bool OnMeterTick()
    {
        if (!_levels.IsRunning) return false;

        // The pitch is the reading and is played continuously; the zone name is
        // spoken only when it changes.
        // Nothing is announced until a real sample has arrived. Reporting the
        // starting value would say "silent" before the microphone has been read
        // once, which is a state that was never measured.
        if (_lastLevelDb is not { } db) return true;

        _meterSeconds += 0.12;

        // The tick is the reading: pitch rises with the level, played
        // continuously so the shape of your delivery is audible without a word
        // being spoken.
        _audio?.Play(
            LevelSonifier.PitchFor(db),
            seconds: 0.03,
            amplitude: LevelSonifier.ZoneOf(db) == LevelZone.Clipping ? 1.0 : 0.6);

        if (_meter.Observe(db, _meterSeconds) is { } zone) Announce(zone, urgent: false);

        return true;
    }

    private void StopMonitoring()
    {
        if (_meterTick != 0)
        {
            GLib.Functions.SourceRemove(_meterTick);
            _meterTick = 0;
        }

        _levels.Stop();
        Announce($"monitoring off. {_meter.Summarise()}", urgent: true);
    }
}
