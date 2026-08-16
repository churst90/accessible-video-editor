using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Edl;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Samples;
using AccessibleVideoEditor.Core.Images;
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
/// Four panes, in the direction work flows: media comes in, becomes tracks,
/// becomes a timeline, reads out as a transcript. Tab moves rightwards through
/// that pipeline, Shift+Tab back.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Gtk_.ApplicationWindow _window;
    private readonly Workspace _workspace = new();
    private EditSession _session;

    /// <summary>Set by every edit, cleared by every save. What makes closing safe.</summary>
    private bool _dirty;
    private readonly DocumentCursor _cursor = new();
    private readonly EditClipboard _clipboard = new();

    private readonly Dictionary<Pane, Gtk_.Widget> _paneWidgets = [];
    private readonly List<(Gtk_.ListBoxRow Row, Gtk_.Label Label, TrackId Track)> _trackRows = [];
    private readonly List<(Gtk_.ListBoxRow Row, Gtk_.Label Label, TrackId Track)> _timelineRows = [];

    private Gtk_.ListBox _mediaList = null!;
    private Gtk_.ListBox _trackList = null!;
    private Gtk_.ListBox _timelineList = null!;
    private Gtk_.TextView _transcript = null!;
    private Gtk_.Label _readout = null!;
    private Gtk_.Stack _views = null!;
    private TimelineCanvas _canvas = null!;
    private StreamView _stream = null!;
    private ImageView _images = null!;
    private ViewfinderSession? _viewfinder;

    /// <summary>
    /// You and this machine, rather than this project. Loaded once at startup;
    /// saved whenever something in it changes, so nothing has to be set up
    /// twice.
    /// </summary>
    private readonly AppSettings _settings = AppSettings.Load();

    /// <summary>Counts up to <c>AutosaveMinutes</c>, which is read on every tick.</summary>
    private int _minutesSinceAutosave;

    /// <summary>So a failing autosave is said once rather than every few minutes.</summary>
    private bool _autosaveFailed;

    private readonly SecretStore _secrets = SecretStore.Load();

    /// <summary>
    /// Where the drawn timeline starts, in programme seconds. It exists only
    /// for the picture - the cursor is the model's, and this follows it.
    /// </summary>
    private double _viewStart;

    private WaveformExtractor? _waveforms;
    private readonly HashSet<SourceId> _waveformsRequested = [];

    private IAnnouncer _announcer = new NullAnnouncer();
    private SdlAudioOutput? _audio;
    private Gtk_.FileChooserNative? _openDialog;
    private bool _rendering;
    private TranscriptDocument _transcriptDocument = null!;
    private int _lastAnnouncedLine = -1;
    private int _editingLine = -1;
    private bool _transcriptDirty;
    private bool _captionRuleAnnounced;
    private bool _suppressTranscriptCommit;
    private ElementId _lastSpokenSegment;
    private readonly Dictionary<Pane, Gtk_.PopoverMenu> _contextMenus = [];

    /// <summary>
    /// Every command, once. Menu items, context menus and key handlers all
    /// invoke through here rather than each carrying their own copy - which is
    /// how Rename ended up announcing "not wired" from the menu while the key
    /// opened the dialog.
    /// </summary>
    private readonly Dictionary<string, System.Action> _commands = [];

    private readonly PreviewPlayer _player = new();
    private readonly PlaybackAnnouncer _playbackAnnouncer = new();
    private bool _followingPlayback;

    private readonly Recorder _recorder = new();
    private readonly LevelReader _levels = new();
    private readonly LevelMonitor _meter = new();
    private uint _meterTick;
    private double? _lastLevelDb;
    private double _meterSeconds;
    private readonly List<(RecordingSession Session, TrackId Track)> _recordings = [];
    private double _recordingFrom;

    public MainWindow(Gtk_.Application application)
    {
        _session = new EditSession(DemoProject.Create());
        _cursor.FocusedTrack = _session.Project.ProgrammeTrack.Id;

        _window = Gtk_.ApplicationWindow.New(application);
        _window.Title = $"{_session.Project.Name} - Accessible Video Editor";
        _window.SetDefaultSize(1440, 860);
        _window.AddCssClass("videoeditor");

        Theme.Install(_window.GetDisplay());

        var root = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);

        RegisterActions();
        Menus.InstallAccelerators(application);
        root.Append(Gtk_.PopoverMenuBar.NewFromModel(Menus.BuildMenuBar()));

        var body = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        body.MarginTop = 8;
        body.MarginBottom = 12;
        body.MarginStart = 12;
        body.MarginEnd = 12;

        // The status line is outside the stack on purpose: it is the one thing
        // that must never be a view away.
        _readout = Gtk_.Label.New(string.Empty);
        _readout.Xalign = 0;
        _readout.Wrap = true;
        _readout.AddCssClass("readout");
        body.Append(_readout);

        _views = Gtk_.Stack.New();
        _views.Vexpand = true;

        AddView(Pane.Timeline, BuildTimeline());
        AddView(Pane.Tracks, BuildTracks());
        AddView(Pane.Transcript, BuildTranscript());
        AddView(Pane.MediaBin, BuildMediaBin());
        AddView(Pane.Stream, BuildStream());
        AddView(Pane.Images, BuildImages());

        body.Append(_views);

        var footer = Gtk_.Label.New(
            "Ctrl+1-6 view  ·  F1 help  ·  F2 render  ·  F5 arm  ·  F6 next view  ·  F12 where am I  "
            + "·  Up/Down track  ·  Left/Right move  ·  Tab next edit point  ·  S split");
        footer.Xalign = 0;
        footer.Wrap = true;
        footer.AddCssClass("footer");
        body.Append(footer);

        root.Append(body);
        _window.SetChild(root);

        _audio = SdlAudioOutput.TryOpen();
        _announcer = new GtkAnnouncer(_window, _audio);

        var paneKeys = Gtk_.EventControllerKey.New();
        paneKeys.SetPropagationPhase(Gtk_.PropagationPhase.Capture);
        paneKeys.OnKeyPressed += OnWindowKeyPressed;
        _window.AddController(paneKeys);

        // Closing the window has to take the stream and the chat connection
        // with it. An encoder left running after the application is gone is
        // still broadcasting, and there is nothing left on screen to say so.
        _window.OnCloseRequest += (_, _) =>
        {
            if (_dirty)
            {
                // Said rather than blocked: a modal on the way out that a
                // screen reader has not caught up with is a trap.
                Announce(
                    $"closing with unsaved changes to {Project.Name}",
                    urgent: true);
            }

            _viewfinder?.Dispose();
            _stream.Shutdown();
            _player.Dispose();

            return false;
        };

        Refresh();
        FocusPane(Pane.Timeline, silent: true);

        // 100 ms is fine: announcements are on boundary crossings, not on the
        // tick, so the poll only has to be finer than a segment.
        GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 100, OnPlaybackTick);

        // The playlist advances on the same tick rather than a second timer:
        // one thing to reason about, and the end of a track is not urgent to
        // within a tenth of a second.
        GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, 1000, () =>
        {
            _stream.Tick();
            return true;
        });

        // Chats that are already configured connect themselves, so opening the
        // view is enough and nothing has to be set up twice.
        _stream.ConnectConfiguredChats();

        // One minute tick that counts, rather than a timer built from the
        // interval. The interval is a preference, and a timer created once at
        // startup would keep the old one until the application was restarted -
        // so changing it would appear to do nothing, which is the worst
        // available outcome for a setting you cannot see.
        GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, 60_000, () =>
        {
            var minutes = _settings.Behaviour.AutosaveMinutes;

            if (minutes <= 0)
            {
                _minutesSinceAutosave = 0;
                return true;
            }

            if (++_minutesSinceAutosave < minutes) return true;

            _minutesSinceAutosave = 0;
            _ = Autosave();

            return true;
        });
    }

    private static string TitleCase(string text) =>
        string.Join(' ', text.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    public void Present() => _window.Present();

    private Project Project => _session.Project;

    // ---- panes -------------------------------------------------------------

    private void AddDeviceSource(string name, StreamSourceKind kind) =>
        Prompt($"{name} name", name, "Add", text => _stream.AddSource(new StreamSource
        {
            Id = StreamIds.NewSource(),
            Name = text,
            Kind = kind,
        }));

    /// <summary>
    /// A song over a static picture is the case this exists for, so music is
    /// added looping by default: a bed that stops when the track ends is not
    /// what anyone meant.
    /// </summary>
    private void AddFileSource(string name, StreamSourceKind kind, bool loop) =>
        Prompt($"{name} file", string.Empty, "Add", path => _stream.AddSource(new StreamSource
        {
            Id = StreamIds.NewSource(),
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Kind = kind,
            Path = path,
            Loop = loop,
        }));

    /// <summary>
    /// The one thing in this application that is typed in and never read back.
    /// A stream key lets anyone broadcast as you, and speech is often on a
    /// speaker in a room with other people in it.
    /// </summary>
    private void AskForKey(StreamPlatform platform) =>
        Prompt($"{platform} stream key", string.Empty, "Set", key => _stream.AddTarget(platform, key));

    /// <summary>Every segment edge on every track, so Tab finds the next event anywhere.</summary>
    private void JumpEditPoint(bool forward)
    {
        var map = _session.Map;

        var points = Project.Tracks
            .SelectMany(t => TrackProbe.Segments(Project, map, t.Id))
            .SelectMany(seg => new[] { seg.Start, seg.End })
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var next = forward
            ? points.Where(t => t > _cursor.ProgrammeTime + 1e-3).Select(t => (double?)t).FirstOrDefault()
            : points.Where(t => t < _cursor.ProgrammeTime - 1e-3).Select(t => (double?)t).LastOrDefault();

        if (next is null)
        {
            Announce(forward ? "no edit point after this" : "no edit point before this", urgent: true);
            return;
        }

        _cursor.MoveTo(next.Value);
        Refresh();
        Announce(FocusedStatus(), urgent: true);
    }

    /// <summary>
    /// T cycles takes of the segment under the cursor. The segment keeps its
    /// place and everything anchored to it; only the media changes.
    /// </summary>
    private void CycleTake(int direction)
    {
        if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element.Id is not { } id)
        {
            Announce("nothing under the cursor", urgent: true);
            return;
        }

        var result = _session.Apply("take", (project, _) =>
            EditOperations.CycleTake(project, id, direction));

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    /// <summary>
    /// Picks the input for the focused track. Which devices are offered comes
    /// from the track's medium: a video track offers cameras, an audio track
    /// microphones, an image track nothing. That is what removed the need for a
    /// separate record view - the input is a property of the track.
    /// </summary>
    private void ChangeTrackType()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        ChooseFromList(
            $"Type for {track.Name}",
            ["Video - records from cameras", "Audio - records from microphones",
             "Image - stills, records nothing", "Mixed - picture and sound together"],
            index =>
            {
                track.Media = index switch
                {
                    0 => TrackMedia.Video,
                    1 => TrackMedia.Audio,
                    2 => TrackMedia.Image,
                    _ => TrackMedia.Mixed,
                };

                if (track.AcceptsInput == TrackInput.None) track.Armed = false;

                RebuildTrackRows();
                Announce(track.Describe(), urgent: true);
            });
    }

    private void InsertSegment()
    {
        ChooseFromList(
            "Insert a segment",
            ["Card - a composed screen", "Hole - reserved space with a note",
             "Pause - a beat of black and silence"],
            index =>
            {
                var result = _session.Apply("insert segment", (project, _) => index switch
                {
                    0 => InsertCard(project),
                    1 => EditOperations.InsertHole(project, _cursor.ProgrammeTime, 5, "to be filled"),
                    _ => InsertPause(project),
                });

                Refresh();
                Announce(result.Announce(), urgent: true);
            });
    }

    private void InsertCardAtCursor()
    {
        var result = _session.Apply("insert card", (project, _) => InsertCard(project));

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    private EditResult InsertCard(Project project)
    {
        EditOperations.SplitAt(project, _cursor.ProgrammeTime);

        var map = TimelineMap.Build(project);
        var next = map.Elements.FirstOrDefault(p => p.ProgrammeStart >= _cursor.ProgrammeTime - 1e-4);
        var index = next is null ? project.Spine.Count : project.Spine.IndexOf(next.Element);

        project.Spine.Insert(index, new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = CardTemplates.TitleCard("New card"),
            TransitionIn = Transition.Cut,
        });

        return EditResult.Ok("card inserted, 4 seconds");
    }

    private EditResult InsertPause(Project project)
    {
        EditOperations.SplitAt(project, _cursor.ProgrammeTime);

        var map = TimelineMap.Build(project);
        var next = map.Elements.FirstOrDefault(p => p.ProgrammeStart >= _cursor.ProgrammeTime - 1e-4);
        var index = next is null ? project.Spine.Count : project.Spine.IndexOf(next.Element);

        project.Spine.Insert(index, new PauseElement
        {
            Id = Ids.NewElement(),
            Length = 1,
            TransitionIn = Transition.Cut,
        });

        return EditResult.Ok("pause inserted, 1 second");
    }

    private void EditCard()
    {
        if (CardUnderCursor() is not { } composition)
        {
            Announce("no card under the cursor. Insert one from the Insert menu", urgent: true);
            return;
        }

        var editor = new CardEditor(
            _window,
            composition,
            text => Announce(text, urgent: true),
            () =>
            {
                _session.Invalidate();
                Refresh();
            });

        editor.Present();
    }

    /// <summary>
    /// Fades for the segment under the cursor. A fade belongs to a segment; a
    /// transition belongs to the boundary between two. Both are needed and they
    /// are not the same thing.
    /// </summary>
    private void EditFades()
    {
        if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element is not { } element)
        {
            Announce("nothing under the cursor", urgent: true);
            return;
        }

        ChooseFromList(
            "Fades for this segment",
            [
                "Fade in half a second",
                "Fade in one second",
                "Fade out half a second",
                "Fade out one second",
                "Fade in and out, one second each",
                "Picture only",
                "Sound only",
                "Picture and sound",
                "Clear all fades",
            ],
            index =>
            {
                switch (index)
                {
                    case 0: element.FadeIn = 0.5; break;
                    case 1: element.FadeIn = 1.0; break;
                    case 2: element.FadeOut = 0.5; break;
                    case 3: element.FadeOut = 1.0; break;
                    case 4: element.FadeIn = element.FadeOut = 1.0; break;
                    case 5: element.FadeTarget = FadeTarget.Video; break;
                    case 6: element.FadeTarget = FadeTarget.Audio; break;
                    case 7: element.FadeTarget = FadeTarget.Both; break;
                    default:
                        element.FadeIn = element.FadeOut = 0;
                        break;
                }

                Refresh();
                Announce(element.DescribeFades() ?? "no fades on this segment", urgent: true);
            });
    }

    private void ToggleTrack(System.Action<Track> change)
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        change(track);
        Refresh();
        Announce(track.Describe(), urgent: true);
    }

    private void ToggleArm()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        track.Armed = !track.Armed;
        Refresh();

        if (!track.Armed)
        {
            Announce($"{track.Name} disarmed", urgent: true);
            return;
        }

        if (track.AcceptsInput == TrackInput.None)
        {
            track.Armed = false;
            Announce($"{track.Name} is an image track and cannot be armed", urgent: true);
            return;
        }

        // A track with no input of its own falls back to the preferred device
        // for its kind, and says that is where it came from - a default that
        // applied silently would be indistinguishable from having chosen it.
        var fromPreferences = false;

        if (track.CaptureDeviceName is not { Length: > 0 }
            && Preferences.DefaultInputFor(_settings, track.AcceptsInput) is { } preferred)
        {
            track.CaptureDeviceName = preferred;
            fromPreferences = true;
        }

        // The signal probe would open the camera, so it is deferred to the
        // moment recording actually starts rather than happening because a key
        // was pressed.
        Announce(track.CaptureDeviceName is { Length: > 0 } device
            ? $"{track.Name} armed, input {device}{(fromPreferences ? ", from preferences" : string.Empty)}. "
              + "Recording is not wired yet"
            : $"{track.Name} armed, but no input chosen. Control F5 to choose one",
            urgent: true);
    }

    /// <summary>
    /// Asks for the type first: it decides what the track can record from and
    /// what can be pasted onto it, so guessing it would be guessing the most
    /// consequential thing about the track.
    /// </summary>
    private void AddTrack()
    {
        ChooseFromList(
            "New track",
            ["Video - b-roll and cutaways", "Audio - music and voice",
             "Image - stills and graphics"],
            index =>
            {
                var media = index switch
                {
                    0 => TrackMedia.Video,
                    1 => TrackMedia.Audio,
                    _ => TrackMedia.Image,
                };

                var kind = media switch
                {
                    TrackMedia.Audio => TrackKind.Audio,
                    TrackMedia.Image => TrackKind.Graphics,
                    _ => TrackKind.Overlay,
                };

                var result = _session.Apply("add track", (project, _) =>
                {
                    var track = new Track
                    {
                        Id = Ids.NewTrack(),
                        Name = $"{media} {project.Tracks.Count(t => t.Media == media) + 1}",
                        Kind = kind,
                        Media = media,
                        Order = project.Tracks.Count,
                    };

                    project.Tracks.Add(track);
                    return EditResult.Ok($"added {track.Describe()}");
                });

                // Track rows are built once, so a new track needs them rebuilt
                // before it can be focused.
                RebuildTrackRows();
                Announce(result.Announce(), urgent: true);
            });
    }

    private void RebuildTrackRows()
    {
        foreach (var (row, _, _) in _trackRows) _trackList.Remove(row);
        foreach (var (row, _, _) in _timelineRows) _timelineList.Remove(row);

        _trackRows.Clear();
        _timelineRows.Clear();

        foreach (var track in Project.InOrder)
        {
            var trackLabel = Gtk_.Label.New(track.Describe());
            trackLabel.Xalign = 0;
            Pad(trackLabel);
            var trackRow = Gtk_.ListBoxRow.New();
            trackRow.SetChild(trackLabel);
            _trackList.Append(trackRow);
            _trackRows.Add((trackRow, trackLabel, track.Id));

            var (laneRow, laneLabel) = LaneHeaderRow(track.Name);
            _timelineList.Append(laneRow);
            _timelineRows.Add((laneRow, laneLabel, track.Id));
        }

        Refresh();
    }

    /// <summary>Rename the focused track. A modal entry, because it is text input.</summary>
    private void RenameTrack()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track) return;

        var dialog = Gtk_.Window.New();
        dialog.Title = $"Rename {track.Name}";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(360, 120);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var entry = Gtk_.Entry.New();
        entry.SetText(track.Name);
        box.Append(entry);

        var accept = Gtk_.Button.NewWithLabel("Rename");
        box.Append(accept);

        void Commit()
        {
            var name = entry.GetText().Trim();
            if (name.Length > 0)
            {
                track.Name = name;
                RebuildTrackRows();
                Announce($"renamed to {name}", urgent: true);
            }

            dialog.Close();
        }

        accept.OnClicked += (_, _) => Commit();
        entry.OnActivate += (_, _) => Commit();

        dialog.SetChild(box);
        dialog.Present();
        entry.GrabFocus();
    }

    /// <summary>
    /// Version, credits and how to support the work. Selectable text rather
    /// than a label, so an address can be copied rather than transcribed by
    /// ear - which is the one thing you must not have to do with a wallet
    /// address.
    /// </summary>
    private void ShowAbout()
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = $"About {AboutInfo.Name}";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(560, 420);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 10);
        box.MarginTop = 16; box.MarginBottom = 16; box.MarginStart = 16; box.MarginEnd = 16;

        var heading = Gtk_.Label.New($"{AboutInfo.Name} {AboutInfo.Version}");
        heading.Xalign = 0;
        heading.AddCssClass("pane-heading");
        box.Append(heading);

        var lines = new List<string>
        {
            AboutInfo.Tagline,
            string.Empty,
            $"Made by {AboutInfo.Author}.",
            string.Empty,
            "Donations",
            $"  Cash App: {AboutInfo.CashTag}",
        };

        var crypto = AboutInfo.KnownCrypto.ToList();

        lines.AddRange(crypto.Count == 0
            ? ["  Crypto addresses are not set yet."]
            : crypto.Select(c => $"  {c.Coin}: {c.Address}"));

        var text = Gtk_.TextView.New();
        text.Editable = false;
        text.AcceptsTab = false;
        text.WrapMode = Gtk_.WrapMode.Word;
        text.GetBuffer().SetText(string.Join('\n', lines), -1);

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(text);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");
        box.Append(scroller);

        var close = Gtk_.Button.NewWithLabel("Close");
        close.AddCssClass("suggested-action");
        close.OnClicked += (_, _) => dialog.Close();
        box.Append(close);

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != Gdk.Constants.KEY_Escape) return false;

            dialog.Close();
            return true;
        };

        dialog.AddController(keys);
        dialog.SetChild(box);
        dialog.Present();

        text.GrabFocus();
        Announce(AboutInfo.Speak(), urgent: true);
    }

    /// <summary>
    /// The whole keymap, grouped, for reading through rather than searching.
    /// F1 lists the view you are in; this is everything.
    /// </summary>
    private void ReadKeymap()
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = "Keyboard";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(720, 620);

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var group in AccessibleVideoEditor.Core.Commands.CommandRegistry.All
                     .GroupBy(c => c.Group)
                     .OrderBy(g => g.Key.ToString()))
        {
            list.Append(Row(group.Key.ToString().ToUpperInvariant()));

            foreach (var command in group.OrderBy(c => c.Title))
            {
                list.Append(Row($"{command.Title}: {command.Keys}"
                                + (command.Alternate is { Length: > 0 } alternate ? $", or {alternate}" : string.Empty)));
            }
        }

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != Gdk.Constants.KEY_Escape) return false;

            dialog.Close();
            return true;
        };

        dialog.AddController(keys);
        dialog.SetChild(scroller);
        dialog.Present();

        list.GrabFocus();
        Announce($"{AccessibleVideoEditor.Core.Commands.CommandRegistry.All.Count} commands. Escape closes", urgent: true);
    }

    private void SpeakContextHelp()
    {
        var context = Workspace.ContextOf(_workspace.Focused);
        var commands = AccessibleVideoEditor.Core.Commands.CommandRegistry.InContext(context)
            .Where(c => c.Context != AccessibleVideoEditor.Core.Commands.CommandContext.Global)
            .Take(12)
            .Select(c => $"{c.Title}, {c.Keys}");

        Announce($"{Workspace.Name(_workspace.Focused)}. {string.Join(". ", commands)}", urgent: true);
    }

    /// <summary>
    /// Delete resolves its target explicitly and says what it did - three
    /// possible meanings on one key is a trap when there is nothing to look at.
    /// </summary>
    private void DeleteTarget()
    {
        var target = EditTarget.Resolve(Project, _session.Map, _cursor);

        if (!target.IsActionable)
        {
            Announce(target.Describe, urgent: true);
            return;
        }

        var result = _session.Apply("ripple delete", (project, _) =>
            EditOperations.RippleDelete(project, target.Range));

        AfterDestructiveEdit(target, result);
    }

    private void LiftTarget()
    {
        var target = EditTarget.Resolve(Project, _session.Map, _cursor);

        if (!target.IsActionable)
        {
            Announce(target.Describe, urgent: true);
            return;
        }

        var result = _session.Apply("lift", (project, _) =>
            EditOperations.Lift(project, target.Range));

        AfterDestructiveEdit(target, result);
    }

    /// <summary>
    /// A consumed selection is cleared, so the next Delete acts on the segment
    /// under the cursor rather than silently repeating the range that is no
    /// longer there.
    /// </summary>
    private void AfterDestructiveEdit(EditTargetInfo target, EditResult result)
    {
        if (target.Kind == EditTargetKind.Selection) _cursor.ClearSelection();

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    /// <summary>
    /// Each pane owns its popover, built and parented once. An earlier version
    /// created one on demand and re-parented it, which silently did nothing -
    /// a GTK popover has to be parented before it is realised.
    /// </summary>
    private void ShowContextMenu()
    {
        if (!_contextMenus.TryGetValue(_workspace.Focused, out var menu))
        {
            Announce("no context menu here", urgent: true);
            return;
        }

        // Point at the focused pane so the menu lands somewhere on screen, then
        // take the keyboard. Without the explicit focus grab the arrow keys
        // stay with the list underneath and the menu appears inert.
        if (_paneWidgets.TryGetValue(_workspace.Focused, out var anchor)
            && anchor.TranslateCoordinates(_window, 0, 0, out var x, out var y))
        {
            menu.SetPointingTo(new Gdk.Rectangle
            {
                X = (int)x,
                Y = (int)y,
                Width = Math.Max(1, anchor.GetWidth()),
                Height = 1,
            });
        }

        Announce($"{Workspace.Name(_workspace.Focused)} menu", urgent: true);

        menu.Popup();
        menu.GrabFocus();
    }

    // ---- movement ----------------------------------------------------------

    private void Step(bool coarser)
    {
        _cursor.Granularity = coarser ? _cursor.Granularity.Coarser() : _cursor.Granularity.Finer();
        Announce($"stepping by {_cursor.Granularity.Describe()}", urgent: true);
        Refresh();
    }

    /// <summary>Ctrl+left and Ctrl+right: the start of each segment on this track.</summary>
    /// <summary>
    /// Splits whatever is under the cursor <b>on the focused track</b>. Which
    /// track was cut is announced, because splitting the wrong one silently is
    /// exactly the failure this replaces.
    /// </summary>
    private void SplitFocusedTrack()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            Announce("no track focused", urgent: true);
            return;
        }

        if (track.Locked)
        {
            Announce($"{track.Name} is locked", urgent: true);
            return;
        }

        var result = _session.Apply("split", (project, _) => track.Kind == TrackKind.Programme
            ? EditOperations.SplitAt(project, _cursor.ProgrammeTime)
            : EditOperations.SplitItemAt(project, track.Id, _cursor.ProgrammeTime));

        Refresh();
        Announce($"{track.Name}: {result.Announce()}", urgent: true);
    }

    private void SplitEveryTrack()
    {
        var result = _session.Apply("split all tracks", (project, _) =>
        {
            var cut = 0;

            if (EditOperations.SplitAt(project, _cursor.ProgrammeTime).Changed) cut++;

            foreach (var track in project.InOrder.Where(t => t.Kind != TrackKind.Programme && !t.Locked))
            {
                if (EditOperations.SplitItemAt(project, track.Id, _cursor.ProgrammeTime).Changed) cut++;
            }

            return cut == 0
                ? EditResult.NoChange("nothing to split here")
                : EditResult.Ok($"split {cut} track{(cut == 1 ? "" : "s")}");
        });

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    private void JumpItem(bool forward)
    {
        if (_cursor.FocusedTrack is not { } track) return;

        if (TrackProbe.Segments(Project, _session.Map, track).Count == 0)
        {
            Announce("no segments on this track", urgent: true);
            return;
        }

        var start = TrackProbe.AdjacentSegmentStart(
            Project, _session.Map, track, _cursor.ProgrammeTime, forward);

        if (start is null)
        {
            Announce(forward ? "last segment on this track" : "first segment on this track", urgent: true);
            return;
        }

        _cursor.Intend(EditIntent.Segment);
        _cursor.MoveTo(start.Value);
        Refresh();
        Announce(FocusedStatus(), urgent: true);
    }

    /// <summary>Shift+comma and Shift+period: the edges of the current segment.</summary>
    private void JumpEdge(bool forward)
    {
        if (_cursor.FocusedTrack is not { } track) return;

        if (TrackProbe.Segments(Project, _session.Map, track).Count == 0)
        {
            Announce("no segments on this track", urgent: true);
            return;
        }

        var edge = forward
            ? TrackProbe.SegmentEnd(Project, _session.Map, track, _cursor.ProgrammeTime)
            : TrackProbe.SegmentStart(Project, _session.Map, track, _cursor.ProgrammeTime);

        if (edge is null)
        {
            Announce(forward ? "no segment end after this" : "no segment start before this", urgent: true);
            return;
        }

        _cursor.MoveTo(edge.Value);
        Refresh();
        Announce(FocusedStatus(), urgent: true);
        ScrubHere();
    }

    // ---- state -------------------------------------------------------------

    private TimeSelection Selection() =>
        _cursor.Selection ?? new TimeSelection(_cursor.ProgrammeTime, _cursor.ProgrammeTime + 1);

    /// <summary>
    /// Applies a selection built by naming what you wanted. A refusal leaves any
    /// existing selection alone: clearing it as a side effect of a key that
    /// failed would silently change what the next Delete acts on.
    /// </summary>
    private void Select(SelectionResult result)
    {
        if (result.Range is { } range) _cursor.SelectRange(range.From, range.To);

        Refresh();
        Announce(result.Announce, urgent: true);
    }

    private void Apply(string label, Func<Project, EditResult> operation)
    {
        var result = _session.Apply(label, (project, _) => operation(project));

        if (result.Changed) _dirty = true;
        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    private void RefreshLanes()
    {
        var map = _session.Map;

        foreach (var (_, label, trackId) in _timelineRows)
        {
            if (Project.TrackOf(trackId) is not { } track) continue;

            var content = TrackProbe.At(Project, map, trackId, _cursor.ProgrammeTime);
            label.SetText($"{track.Name} - " +
                          TrackProbe.Announce(content, _cursor.ProgrammeTime, Project.Settings.Verbosity));
        }

        _canvas.Redraw();
    }

    private void Refresh()
    {
        var map = _session.Map;

        foreach (var (_, label, trackId) in _trackRows)
        {
            if (Project.TrackOf(trackId) is { } track) label.SetText(track.Describe());
        }

        foreach (var (_, label, trackId) in _timelineRows)
        {
            if (Project.TrackOf(trackId) is not { } track) continue;

            var content = TrackProbe.At(Project, map, trackId, _cursor.ProgrammeTime);
            label.SetText($"{track.Name} - " +
                          TrackProbe.Announce(content, _cursor.ProgrammeTime, Project.Settings.Verbosity));
        }

        UpdateStatusLine();

        _canvas.Widget.ContentHeight = (int)(RulerHeight + Project.InOrder.Count() * LaneHeight);
        _canvas.Redraw();
        LoadWaveforms();

        _transcriptDocument = TranscriptDocument.Build(Project, map);

        // Never rewrite the buffer while it holds uncommitted typing.
        var buffer = _transcript.GetBuffer();
        if (!_transcriptDirty && buffer.Text != _transcriptDocument.Text)
        {
            buffer.SetText(_transcriptDocument.Text, -1);
        }
    }

    private void UpdateStatusLine() =>
        _readout.SetText(Workspace.StatusLine(
            _cursor.ProgrammeTime,
            _session.Map.Duration,
            _cursor.Granularity.Describe(),
            Project.TrackOf(_cursor.FocusedTrack ?? default)?.Name));

    /// <summary>
    /// The cursor readout, plus the sentence itself whenever the cursor crosses
    /// into a different segment. Moving within a sentence stays terse; arriving
    /// at a new one tells you what it says, which is what you actually need to
    /// know when moving through an edit.
    /// </summary>
    private string FocusedStatus()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track)
        {
            return Timecode.FormatShort(_cursor.ProgrammeTime);
        }

        var content = TrackProbe.At(Project, _session.Map, track.Id, _cursor.ProgrammeTime);
        var spoken = TrackProbe.Announce(content, _cursor.ProgrammeTime, Project.Settings.Verbosity);

        var here = _session.Map.Locate(_cursor.ProgrammeTime)?.Element.Id ?? default;

        // What is on screen, added only when the picture actually changes. Same
        // rule as the sentence below and as the playback announcer: terse while
        // you move, and the thing you could not otherwise know at the boundary.
        var shot = ShotLabelHere();

        if (track.Kind == TrackKind.Programme && here != _lastSpokenSegment)
        {
            _lastSpokenSegment = here;

            if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element is SpanElement span
                && span.Text.Length > 0)
            {
                return shot is null ? $"{spoken}. {span.Text}" : $"{spoken}, {shot}. {span.Text}";
            }
        }

        return shot is null ? spoken : $"{spoken}, {shot}";
    }

    /// <summary>
    /// Ctrl+shift+semicolon: the full contents of the card under the cursor -
    /// background, layout, and every layer with where it lands. A card is the
    /// one segment whose contents are otherwise entirely invisible.
    /// </summary>
    private void DescribeCard()
    {
        var composition = CardUnderCursor();

        if (composition is null)
        {
            Announce("no card under the cursor", urgent: true);
            return;
        }

        Announce(composition.Summarise(), urgent: true);
    }

    private CardComposition? CardUnderCursor()
    {
        if (_cursor.FocusedTrack is not { } trackId) return null;

        var track = Project.TrackOf(trackId);

        if (track?.Kind == TrackKind.Programme)
        {
            return _session.Map.Locate(_cursor.ProgrammeTime)?.Element is CardElement card
                ? card.Composition
                : null;
        }

        foreach (var item in Project.ItemsOn(trackId).OfType<CardItem>().Where(i => i.Enabled))
        {
            var start = _session.Map.ResolveAnchor(item.Start);
            if (start is null) continue;

            var end = item.End is { } anchor
                ? _session.Map.ResolveAnchor(anchor)
                : start + (item.Length ?? 0);

            if (end is not null && _cursor.ProgrammeTime >= start && _cursor.ProgrammeTime < end)
            {
                return item.Composition;
            }
        }

        return null;
    }

    private string WhereAmI()
    {
        var track = Project.TrackOf(_cursor.FocusedTrack ?? default);
        var content = track is null
            ? TrackContent.Blank
            : TrackProbe.At(Project, _session.Map, track.Id, _cursor.ProgrammeTime);

        return $"{Workspace.Name(_workspace.Focused)}. {track?.Describe() ?? "no track"}. " +
               TrackProbe.Announce(content, _cursor.ProgrammeTime, Verbosity.Verbose) +
               $". stepping by {_cursor.Granularity.Describe()}. " +
               $"{_cursor.Selection?.Describe() ?? "no selection"}.";
    }

    private void Announce(string text, bool urgent) =>
        _announcer.Say(text, urgent ? AnnouncePriority.Urgent : AnnouncePriority.Progress);
}
