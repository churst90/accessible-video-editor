using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Samples;
using AccessibleVideoEditor.Core.Images;
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
///
/// Every pane is a native GtkListBox or GtkTextView, so Orca gets a real
/// accessibility tree rather than one the application had to construct. Within
/// a pane, navigation is the widget's own - Up and Down move between rows
/// because that is what a list does.
/// </summary>
public sealed class MainWindow
{
    private readonly Gtk_.ApplicationWindow _window;
    private readonly Workspace _workspace = new();
    private readonly EditSession _session;
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
    }

    private void AddView(Pane pane, Gtk_.Widget content) =>
        _views.AddNamed(content, pane.ToString());

    private Gtk_.Widget BuildImages()
    {
        _images = new ImageView(() => _announcer, () => _audio);

        return Framed(Pane.Images, _images.Build(), KeysFor(Pane.Images));
    }

    private Gtk_.Widget BuildStream()
    {
        _stream = new StreamView(() => _announcer, _settings, _secrets);

        return Framed(Pane.Stream, _stream.Build(), KeysFor(Pane.Stream));
    }

    /// <summary>
    /// A one-line text prompt. Shared by every command that needs a name typed,
    /// so they all behave the same: the entry has focus when it opens, Enter
    /// accepts, Escape leaves everything alone.
    /// </summary>
    private void Prompt(string title, string initial, string verb, System.Action<string> commit)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(400, 130);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var entry = Gtk_.Entry.New();
        entry.SetText(initial);
        box.Append(entry);

        var accept = Gtk_.Button.NewWithLabel(verb);
        accept.AddCssClass("suggested-action");
        box.Append(accept);

        void Commit()
        {
            var text = entry.GetText().Trim();
            dialog.Close();

            if (text.Length > 0) commit(text);
        }

        accept.OnClicked += (_, _) => Commit();
        entry.OnActivate += (_, _) => Commit();

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != Gdk.Constants.KEY_Escape) return false;

            dialog.Close();
            Announce($"{title} cancelled", urgent: true);

            return true;
        };

        dialog.AddController(keys);
        dialog.SetChild(box);
        dialog.Present();
        entry.GrabFocus();
    }


    /// <summary>
    /// A view that is not built yet. Top-aligned rather than centred: a label
    /// floating in the middle of an empty frame reads as a rendering fault.
    /// </summary>
    private Gtk_.Widget Placeholder(Pane pane, string text)
    {
        var label = Gtk_.Label.New(text);
        label.Wrap = true;
        label.Xalign = 0;
        label.Valign = Gtk_.Align.Start;
        label.Halign = Gtk_.Align.Start;
        Pad(label);

        return Framed(pane, label, null);
    }

    private static string TitleCase(string text) =>
        string.Join(' ', text.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    public void Present() => _window.Present();

    private Project Project => _session.Project;

    // ---- panes -----------------------------------------------------------

    private Gtk_.Widget BuildMediaBin()
    {
        _mediaList = Gtk_.ListBox.New();
        _mediaList.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var source in Project.Sources)
        {
            _mediaList.Append(Row($"{System.IO.Path.GetFileName(source.Path)}, " +
                                  $"{source.Kind.ToString().ToLowerInvariant()}, " +
                                  $"{Timecode.Speak(source.Duration)}"));
        }

        return Framed(Pane.MediaBin, _mediaList, KeysFor(Pane.MediaBin));
    }

    private Gtk_.Widget BuildTracks()
    {
        _trackList = Gtk_.ListBox.New();
        _trackList.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var track in Project.InOrder)
        {
            var label = Gtk_.Label.New(track.Describe());
            label.Xalign = 0;
            Pad(label);

            var row = Gtk_.ListBoxRow.New();
            row.SetChild(label);

            _trackList.Append(row);
            _trackRows.Add((row, label, track.Id));
        }

        _trackList.OnRowSelected += (_, args) => SelectTrackByIndex(args.Row?.GetIndex() ?? -1);

        return Framed(Pane.Tracks, _trackList, KeysFor(Pane.Tracks));
    }

    /// <summary>Lane geometry, shared by the drawing and by the CSS beside it.</summary>
    private const double LaneHeight = 60;
    private const double RulerHeight = 26;
    private const int HeaderWidth = 260;

    /// <summary>
    /// Track headers on the left, drawn lanes on the right - the layout every
    /// editor uses, and the one a sighted collaborator will already know.
    ///
    /// The headers are a real GtkListBox and they are still what you move
    /// through and what Orca reads. The drawing beside them is a picture of the
    /// same model, takes no focus, and can be ignored entirely.
    /// </summary>
    private Gtk_.Widget BuildTimeline()
    {
        _timelineList = Gtk_.ListBox.New();
        _timelineList.SelectionMode = Gtk_.SelectionMode.Single;
        _timelineList.AddCssClass("lane-headers");

        foreach (var track in Project.InOrder)
        {
            var (row, label) = LaneHeaderRow(track.Name);
            _timelineList.Append(row);
            _timelineRows.Add((row, label, track.Id));
        }

        _timelineList.OnRowSelected += (_, args) => SelectTrackByIndex(args.Row?.GetIndex() ?? -1);

        _canvas = new TimelineCanvas
        {
            Layout = BuildTimelineView,
            Waveforms = source => _waveforms?.Peek(source),
        };

        // The header column starts below the ruler so that a header and its
        // lane are on the same line to the pixel.
        var spacer = Gtk_.Box.New(Gtk_.Orientation.Vertical, 0);
        spacer.SetSizeRequest(-1, (int)RulerHeight);
        spacer.AddCssClass("ruler-gutter");

        var headers = Gtk_.Box.New(Gtk_.Orientation.Vertical, 0);
        headers.Append(spacer);
        headers.Append(_timelineList);
        headers.SetSizeRequest(HeaderWidth, -1);
        headers.AddCssClass("lane-header-column");

        var split = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 0);
        split.Append(headers);
        split.Append(_canvas.Widget);

        return Framed(Pane.Timeline, split, KeysFor(Pane.Timeline));
    }

    private static (Gtk_.ListBoxRow Row, Gtk_.Label Label) LaneHeaderRow(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        label.Ellipsize = Pango.EllipsizeMode.End;
        label.MarginStart = 12;
        label.MarginEnd = 12;

        var row = Gtk_.ListBoxRow.New();
        row.SetChild(label);
        row.AddCssClass("lane-header");

        return (row, label);
    }

    /// <summary>
    /// The picture, recomputed at draw time from the same model the speech
    /// comes from. Zoom is the step size, so what is on screen and what an
    /// arrow key moves by can never disagree.
    /// </summary>
    private TimelineView? BuildTimelineView(int width, int height)
    {
        var pixelsPerSecond = TimelineZoom.PixelsPerSecondFor(_cursor.Granularity);
        var viewDuration = pixelsPerSecond > 0 ? width / pixelsPerSecond : 0;

        _viewStart = TimelineLayout.Follow(_viewStart, _cursor.ProgrammeTime, viewDuration);

        return TimelineLayout.Build(
            Project,
            _session.Map,
            _cursor,
            new TimelineViewport(width, pixelsPerSecond, _viewStart, LaneHeight, 0, RulerHeight),
            LaneSlots());
    }

    /// <summary>
    /// Where each lane goes, asked of GTK rather than worked out from the CSS.
    ///
    /// A lane drawn even a pixel per row away from its own header would drift
    /// visibly by the bottom of the stack, and the picture would then be saying
    /// something the list beside it is not. Padding, font size, display scaling
    /// and the theme can all move a row, so the only reliable answer is where
    /// the row actually ended up.
    /// </summary>
    private IReadOnlyList<LaneSlot>? LaneSlots()
    {
        if (_timelineRows.Count == 0) return null;

        var tops = new List<double>(_timelineRows.Count);

        foreach (var (row, _, _) in _timelineRows)
        {
            if (row.GetHeight() <= 0) return null;
            if (!row.TranslateCoordinates(_canvas.Widget, 0, 0, out _, out var top)) return null;

            tops.Add(top);
        }

        // Each lane runs to the top of the next one rather than to its own
        // reported height: a row's allocation and the box it paints are not
        // quite the same thing, and the difference showed up as a two-pixel
        // seam between every lane and its header.
        var slots = new List<LaneSlot>(tops.Count);

        for (var i = 0; i < tops.Count; i++)
        {
            var height = i + 1 < tops.Count
                ? tops[i + 1] - tops[i]
                : _timelineRows[i].Row.GetHeight();

            slots.Add(new LaneSlot(tops[i], height));
        }

        return slots;
    }

    /// <summary>
    /// Waveforms are decoration for the sighted half of the room, so they are
    /// pulled in the background and the timeline redraws when they arrive. The
    /// editor never waits for one.
    /// </summary>
    private void LoadWaveforms()
    {
        _waveforms ??= new WaveformExtractor(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "video-waveforms"));

        foreach (var source in Project.Sources.ToList())
        {
            if (source.Kind == SourceKind.Image) continue;
            if (!_waveformsRequested.Add(source.Id)) continue;

            var extractor = _waveforms;
            var target = source;

            _ = Task.Run(async () =>
            {
                var data = await extractor.LoadAsync(target).ConfigureAwait(false);
                if (data is null) return;

                GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
                {
                    _canvas.Redraw();
                    return false;
                });
            });
        }
    }

    private Gtk_.Widget BuildTranscript()
    {
        _transcript = Gtk_.TextView.New();
        _transcript.WrapMode = Gtk_.WrapMode.Word;

        // A TextView swallows Tab as a literal character otherwise, leaving no
        // keyboard way out of the pane.
        _transcript.AcceptsTab = false;

        // Typing edits caption text only, never the cut. Committed when the
        // caret leaves the line rather than on every keystroke, so the buffer
        // is not rebuilt underneath you mid-word.
        _transcript.Editable = true;
        _transcript.GetBuffer().OnChanged += (_, _) => _transcriptDirty = true;

        // Moving between lines announces which line, when it starts and ends,
        // and how long it runs. The timecodes are spoken rather than written
        // into the text, so the transcript still reads as prose.
        var caretKeys = Gtk_.EventControllerKey.New();
        caretKeys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval is not (Gdk.Constants.KEY_Up or Gdk.Constants.KEY_Down
                or Gdk.Constants.KEY_Home or Gdk.Constants.KEY_End
                or Gdk.Constants.KEY_Page_Up or Gdk.Constants.KEY_Page_Down))
            {
                return false;
            }

            // Let the TextView move first, then report where it landed.
            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                AnnounceTranscriptLine();
                return false;
            });

            return false;
        };

        _transcript.AddController(caretKeys);

        var verbKeys = Gtk_.EventControllerKey.New();
        verbKeys.SetPropagationPhase(Gtk_.PropagationPhase.Capture);
        verbKeys.OnKeyPressed += (_, args) => OnTranscriptKey(args);
        _transcript.AddController(verbKeys);

        return Framed(Pane.Transcript, _transcript, null);
    }

    /// <summary>
    /// Wraps a pane in a titled frame. The heading gives Orca something to
    /// announce on entry and makes the pane a landmark rather than an
    /// anonymous list.
    /// </summary>
    private Gtk_.Widget Framed(Pane pane, Gtk_.Widget content, Gtk_.EventControllerKey? keys)
    {
        if (keys is not null) content.AddController(keys);

        _paneWidgets[pane] = content;

        var model = pane switch
        {
            Pane.Tracks => Menus.TrackContextMenu(),
            Pane.MediaBin => Menus.MediaContextMenu(),
            Pane.Timeline => Menus.ItemContextMenu(),
            _ => null,
        };

        if (model is not null)
        {
            // Parented to the window rather than to the list. A popover
            // parented inside a ScrolledWindow gets positioned against the
            // scrolled content, which can put it off-screen - where autohide
            // dismisses it immediately, so it looks like it never opened.
            var popover = Gtk_.PopoverMenu.NewFromModel(model);
            popover.SetParent(_window);
            popover.HasArrow = false;
            popover.Position = Gtk_.PositionType.Bottom;
            _contextMenus[pane] = popover;
        }

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 4);

        var heading = Gtk_.Label.New(TitleCase(Workspace.Name(pane)));
        heading.Xalign = 0;
        heading.AddCssClass("pane-heading");
        heading.MarginBottom = 4;
        box.Append(heading);

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(content);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");
        box.Append(scroller);

        return box;
    }

    private static Gtk_.ListBoxRow Row(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        Pad(label);

        var row = Gtk_.ListBoxRow.New();
        row.SetChild(label);
        return row;
    }

    private static void Pad(Gtk_.Widget widget)
    {
        widget.MarginTop = 8;
        widget.MarginBottom = 8;
        widget.MarginStart = 10;
        widget.MarginEnd = 10;
    }

    // ---- focus -----------------------------------------------------------

    private void FocusPane(Pane pane, bool silent = false)
    {
        // Leaving the transcript carries the caret position back to the
        // timeline; entering it carries the timeline cursor in.
        if (_workspace.Focused == Pane.Transcript && pane != Pane.Transcript)
        {
            CommitCaption();

            if (_transcriptDocument.LocationAt(_transcript.GetBuffer().CursorPosition) is
                { ProgrammeTime: { } time })
            {
                _cursor.MoveTo(time, CursorMoveCause.PaneSwitch);
                Refresh();
            }
        }

        _workspace.FocusOn(pane);
        _views.SetVisibleChildName(pane.ToString());

        if (pane == Pane.Transcript)
        {
            SyncTranscriptToCursor();
            _lastAnnouncedLine = -1;
        }

        if (_paneWidgets.TryGetValue(pane, out var widget))
        {
            if (widget is Gtk_.ListBox list && SelectedRowFor(pane) is { } row)
            {
                list.SelectRow(row);
                row.GrabFocus();
            }
            else
            {
                widget.GrabFocus();
            }
        }

        UpdateStatusLine();

        // By name, never by number: "view 3" says nothing about where you are.
        if (!silent) Announce(Workspace.Announce(pane, Project, _session.Map), urgent: true);
    }

    private Gtk_.ListBoxRow? SelectedRowFor(Pane pane) => pane switch
    {
        Pane.Tracks => _trackRows.FirstOrDefault(r => r.Track == _cursor.FocusedTrack).Row,
        Pane.Timeline => _timelineRows.FirstOrDefault(r => r.Track == _cursor.FocusedTrack).Row,
        Pane.MediaBin => _mediaList.GetRowAtIndex(0),
        _ => null,
    };

    private void SelectTrackByIndex(int index)
    {
        if (index < 0 || index >= _trackRows.Count) return;

        _cursor.FocusedTrack = _trackRows[index].Track;
    }

    // ---- keys ------------------------------------------------------------

    private bool OnWindowKeyPressed(Gtk_.EventControllerKey sender, Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);

        if (args.Keyval == Gdk.Constants.KEY_Escape && _viewfinder is { IsOpen: true })
        {
            _viewfinder.Close();
            return true;
        }

        if (args.Keyval == Gdk.Constants.KEY_F8 && _viewfinder is { IsOpen: true })
        {
            _viewfinder.DescribeShot();
            return true;
        }

        if (args.Keyval == Gdk.Constants.KEY_Menu || (args.Keyval == Gdk.Constants.KEY_F10 && shift))
        {
            ShowContextMenu();
            return true;
        }

        // Ctrl and a digit goes straight to a view. Direct beats cycling: you
        // always know where you are going.
        if (control && args.Keyval >= Gdk.Constants.KEY_1 && args.Keyval <= Gdk.Constants.KEY_6)
        {
            var pane = Workspace.ByNumber((int)(args.Keyval - Gdk.Constants.KEY_1) + 1);
            if (pane is null) return false;

            FocusPane(pane.Value);
            return true;
        }

        if (args.Keyval == Gdk.Constants.KEY_F6)
        {
            FocusPane(shift ? _workspace.Previous() : _workspace.Next());
            return true;
        }

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_F1 when control:
                ShowAbout();
                return true;

            case Gdk.Constants.KEY_F1 when shift:
                ReadKeymap();
                return true;

            case Gdk.Constants.KEY_F1:
                SpeakContextHelp();
                return true;

            case Gdk.Constants.KEY_F2:
                Run(shift ? "renderDraft" : "export");
                return true;

            case Gdk.Constants.KEY_F5 when control && shift:
                Run("chooseOutput");
                return true;

            case Gdk.Constants.KEY_F5 when control && args.State.HasFlag(Gdk.ModifierType.AltMask):
                Run("chooseChannel");
                return true;

            case Gdk.Constants.KEY_F5 when control:
                Run("chooseDevice");
                return true;

            case Gdk.Constants.KEY_F5 when !shift:
                Run("armTrack");
                return true;

            // R works where recording makes sense - the track editor and the
            // timeline - so you never leave the view you are editing in.
            case Gdk.Constants.KEY_F5 when shift:
            case Gdk.Constants.KEY_R or Gdk.Constants.KEY_r
                when _workspace.Focused is Pane.Tracks or Pane.Timeline:
                Run("record");
                return true;

            case Gdk.Constants.KEY_F3:
                Run("find");
                return true;

            case Gdk.Constants.KEY_F4:
                Run(shift ? "qualityAll" : "quality");
                return true;

            case Gdk.Constants.KEY_F7:
                Run("issues");
                return true;

            case Gdk.Constants.KEY_F8:
                Run(shift ? "describeEdit" : "describeFrame");
                return true;

            case Gdk.Constants.KEY_F9 when shift:
                Run("monitor");
                return true;

            case Gdk.Constants.KEY_F9:
                Run("viewfinder");
                return true;

            case Gdk.Constants.KEY_F12:
            case Gdk.Constants.KEY_semicolon when control:
                Announce(WhereAmI(), true);
                return true;

            case Gdk.Constants.KEY_T or Gdk.Constants.KEY_t when control:
                Run("addTrack");
                return true;
        }

        // Tab is the video equivalent of Reaper's move-to-next-transient: the
        // next thing that happens anywhere in the project, on any track.
        if (args.Keyval is Gdk.Constants.KEY_Tab or Gdk.Constants.KEY_ISO_Left_Tab
            && _workspace.Focused == Pane.Timeline)
        {
            JumpEditPoint(forward: args.Keyval == Gdk.Constants.KEY_Tab && !shift);
            return true;
        }

        // Tab is deliberately not handled: it moves between controls within a
        // view, as it does everywhere else.
        return false;
    }

    private Gtk_.EventControllerKey KeysFor(Pane pane)
    {
        var controller = Gtk_.EventControllerKey.New();

        controller.OnKeyPressed += (_, args) => pane switch
        {
            Pane.Timeline => OnTimelineKey(args),
            Pane.Tracks => OnTracksKey(args),
            Pane.MediaBin => OnMediaKey(args),
            Pane.Stream => OnStreamKey(args),
            Pane.Images => OnImageKey(args),
            _ => false,
        };

        return controller;
    }


    /// <summary>
    /// The streamer view.
    ///
    /// Single letters, because while you are live you are also talking and a
    /// three-key chord is a chord you will fumble. They are safe here: the only
    /// text entry in this view is the chat reply box, and typing in it is
    /// checked for first.
    /// </summary>

    /// <summary>
    /// The image editor.
    ///
    /// Arrow keys resize, shifted arrow keys move a crop edge, and everything
    /// else is a letter. There is no pointer to emulate: every operation names
    /// what it acts on and reports what it did, because a picture you cannot
    /// see can only be edited by measurement.
    /// </summary>
    private bool OnImageKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);

        if (_window.GetFocus() is Gtk_.Entry or Gtk_.Text) return false;

        // A bigger step with Control, so a 6000-pixel scan does not take a
        // thousand presses to trim.
        var step = control ? 100 : 10;

        // While the pointer is on it owns the arrow keys. Two things on one key
        // would be ambiguous; a mode that announces itself when it starts and
        // ends is not.
        if (_images.IsSweeping)
        {
            switch (args.Keyval)
            {
                case Gdk.Constants.KEY_Left: _images.Sweep(-1, 0); return true;
                case Gdk.Constants.KEY_Right: _images.Sweep(1, 0); return true;
                case Gdk.Constants.KEY_Up: _images.Sweep(0, -1); return true;
                case Gdk.Constants.KEY_Down: _images.Sweep(0, 1); return true;

                case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter:
                    _images.ReadUnderPointer();
                    return true;

                // The eyedropper, without pointing: sweep to something that
                // ought to be grey and balance the whole picture on it.
                case Gdk.Constants.KEY_w or Gdk.Constants.KEY_W:
                    _images.ColourLevel("balance on the pointer");
                    return true;

                case Gdk.Constants.KEY_Escape or Gdk.Constants.KEY_g or Gdk.Constants.KEY_G:
                    _images.ToggleSweep();
                    return true;

                case Gdk.Constants.KEY_plus or Gdk.Constants.KEY_equal:
                    _images.SweepStep(finer: true);
                    return true;

                case Gdk.Constants.KEY_minus:
                    _images.SweepStep(finer: false);
                    return true;

                case Gdk.Constants.KEY_F12:
                    _images.WhereIsThePointer();
                    return true;

                case >= Gdk.Constants.KEY_1 and <= Gdk.Constants.KEY_9:
                    _images.SweepTo(new Placement((int)(args.Keyval - Gdk.Constants.KEY_1) + 1));
                    return true;
            }
        }

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_o or Gdk.Constants.KEY_O:
                Prompt("Open a picture", string.Empty, "Open", path => _images.Open(path));
                return true;

            // ---- size ----------------------------------------------------

            case Gdk.Constants.KEY_Right when shift:
                _images.NudgeEdge(CropEdge.Right, -step);
                return true;

            case Gdk.Constants.KEY_Left when shift:
                _images.NudgeEdge(CropEdge.Left, step);
                return true;

            case Gdk.Constants.KEY_Down when shift:
                _images.NudgeEdge(CropEdge.Bottom, -step);
                return true;

            case Gdk.Constants.KEY_Up when shift:
                _images.NudgeEdge(CropEdge.Top, step);
                return true;

            case Gdk.Constants.KEY_Right:
                _images.Nudge(horizontal: true, step);
                return true;

            case Gdk.Constants.KEY_Left:
                _images.Nudge(horizontal: true, -step);
                return true;

            case Gdk.Constants.KEY_Up:
                _images.Nudge(horizontal: false, step);
                return true;

            case Gdk.Constants.KEY_Down:
                _images.Nudge(horizontal: false, -step);
                return true;

            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L:
                _images.Apply("the shape lock", ImageEdits.ToggleAspectLock);
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S when !shift:
                ChooseSizePreset();
                return true;

            // ---- crop ----------------------------------------------------

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C when shift:
                ChooseCropRatio();
                return true;

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C:
                _images.Apply("cropping to the picture", ImageEdits.CropToContent);
                return true;

            case Gdk.Constants.KEY_r or Gdk.Constants.KEY_R when shift:
                _images.Apply("resetting the crop", ImageEdits.ResetCrop);
                return true;

            // ---- straightening -------------------------------------------

            case Gdk.Constants.KEY_f or Gdk.Constants.KEY_F when shift:
                _images.Apply("fixing the scan", ImageEdits.FixScan);
                return true;

            case Gdk.Constants.KEY_t or Gdk.Constants.KEY_T:
                _images.Apply("straightening", ImageEdits.Straighten);
                return true;

            case Gdk.Constants.KEY_bracketright:
                _images.Apply("turning right", document => ImageEdits.Rotate(document, 1));
                return true;

            case Gdk.Constants.KEY_bracketleft:
                _images.Apply("turning left", document => ImageEdits.Rotate(document, -1));
                return true;

            // ---- drawing --------------------------------------------------

            case Gdk.Constants.KEY_d or Gdk.Constants.KEY_D when shift:
                Prompt("Draw", string.Empty, "Draw", sentence => _images.AddShape(sentence));
                return true;

            case Gdk.Constants.KEY_Delete or Gdk.Constants.KEY_BackSpace:
                _images.RemoveShape();
                return true;

            case Gdk.Constants.KEY_k or Gdk.Constants.KEY_K:
                _images.DescribeColours();
                return true;

            // ---- looking at it --------------------------------------------

            case Gdk.Constants.KEY_F8:
                _images.Describe();
                return true;

            case Gdk.Constants.KEY_u or Gdk.Constants.KEY_U:
                _images.DescribeHistory();
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G:
                _images.ToggleSweep();
                return true;

            // ---- colour ---------------------------------------------------

            case Gdk.Constants.KEY_v or Gdk.Constants.KEY_V when shift:
                _images.AdviseColour();
                return true;

            case Gdk.Constants.KEY_v or Gdk.Constants.KEY_V:
                ChooseCorrection();
                return true;

            // Shifting these keys produces a different keyval rather than the
            // same one with a modifier, so both are named. A colon is what the
            // keyboard actually sends for Shift and semicolon.
            case Gdk.Constants.KEY_colon:
            case Gdk.Constants.KEY_semicolon when shift:
                ChooseColourLevels();
                return true;

            case Gdk.Constants.KEY_semicolon:
                ChooseLevels();
                return true;

            case Gdk.Constants.KEY_quotedbl:
            case Gdk.Constants.KEY_apostrophe when shift:
                _images.ReadCast();
                return true;

            case Gdk.Constants.KEY_apostrophe:
                _images.ReadHistogram();
                return true;

            case Gdk.Constants.KEY_b or Gdk.Constants.KEY_B:
                RunBatch();
                return true;

            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when shift:
                EditImageCard();
                return true;

            case Gdk.Constants.KEY_i or Gdk.Constants.KEY_I:
                SendImageToProject();
                return true;

            case Gdk.Constants.KEY_p or Gdk.Constants.KEY_P:
                SampleAPoint();
                return true;

            // ---- out ------------------------------------------------------

            case Gdk.Constants.KEY_e or Gdk.Constants.KEY_E:
                Prompt("Save as", SuggestedImageName(), "Save", path => _images.Export(path));
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S when shift:
                Prompt("Split into folder", System.IO.Path.GetTempPath(), "Split",
                    directory => _images.Split(directory));
                return true;
        }

        return false;
    }

    private string SuggestedImageName() =>
        _images.Document is { } document
            ? System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(document.Path) ?? ".",
                System.IO.Path.GetFileNameWithoutExtension(document.Path) + "-edited.png")
            : string.Empty;

    /// <summary>
    /// Sizes by name rather than by number. "Fit 1080" is a decision; "1920 by
    /// 1080" is arithmetic you have to do first.
    /// </summary>
    private void ChooseSizePreset()
    {
        var menu = Gio.Menu.New();

        foreach (var (name, _, _) in ImageEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(name), $"win.imageSize::{name}"));
        }

        PopUp(menu, "size menu");
    }

    /// <summary>
    /// Corrections by name rather than by number: these are the sentences
    /// people say about a photograph, and each is a nudge so it can be applied
    /// twice when once was not enough.
    /// </summary>
    private void ChooseCorrection()
    {
        var menu = Gio.Menu.New();

        foreach (var preset in ColourEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageCorrect::{preset}"));
        }

        PopUp(menu, "colour menu. Shift+V measures the picture and suggests one");
    }

    /// <summary>
    /// Levels by name: the black point, the white point, and the three zones
    /// between them. Auto sets the points from the picture's own histogram,
    /// which is the one command that makes a curve worth having without a graph.
    /// </summary>
    private void ChooseLevels()
    {
        var menu = Gio.Menu.New();

        foreach (var preset in LevelEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageLevel::{preset}"));
        }

        PopUp(menu, "levels menu. Apostrophe reads the histogram");
    }

    /// <summary>
    /// Per-channel levels: the automatic answers first, then the nudges. This
    /// is the only thing that reaches a cast the temperature control cannot.
    /// </summary>
    private void ChooseColourLevels()
    {
        var menu = Gio.Menu.New();

        menu.AppendItem(Gio.MenuItem.New("Auto Colour Levels", "win.imageColourLevel::auto colour levels"));
        menu.AppendItem(Gio.MenuItem.New(
            "Balance On The Pointer", "win.imageColourLevel::balance on the pointer"));

        foreach (var preset in LevelEdits.ChannelPresets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageColourLevel::{preset}"));
        }

        PopUp(menu, "colour levels. Shift+apostrophe says which way the colour is pulling");
    }

    /// <summary>
    /// A folder of scans, treated like the one already on screen. It asks for
    /// both folders and says what it is about to do before it does it.
    /// </summary>
    private void RunBatch()
    {
        if (_images.Document is null)
        {
            Announce("open one picture and get it right first; the batch copies what you did to it",
                urgent: true);

            return;
        }

        Prompt("Folder of pictures", string.Empty, "Next", source =>
        {
            var preview = _images.PreviewBatch(source);

            if (preview.StartsWith("there are no", StringComparison.Ordinal))
            {
                Announce(preview, urgent: true);
                return;
            }

            Announce(preview, urgent: true);

            Prompt(
                "Where to write them",
                System.IO.Path.Combine(source, "edited"),
                "Run",
                target => ConfirmThen(
                    $"Do that to every picture in {System.IO.Path.GetFileName(source)}?",
                    () => _ = _images.RunBatch(source, target)));
        });
    }

    /// <summary>
    /// The card editor, on a picture. The same window that edits a card on the
    /// timeline - one editor, one vocabulary, and a lower third means the same
    /// thing in both places.
    /// </summary>
    private void EditImageCard()
    {
        if (_images.Document is null)
        {
            Announce("no picture is open", urgent: true);
            return;
        }

        var card = _images.EnsureCard();

        new CardEditor(_window, card, text => Announce(text, urgent: true), _images.CardChanged).Present();
    }

    /// <summary>
    /// Sends the edited picture into the project, so a photograph that has just
    /// been straightened and cropped can go straight onto the timeline without
    /// leaving the application or finding the file again.
    /// </summary>
    private void SendImageToProject()
    {
        if (_images.Document is not { } document)
        {
            Announce("no picture is open", urgent: true);
            return;
        }

        var target = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{System.IO.Path.GetFileNameWithoutExtension(document.Path)}-edited.png");

        _ = SendImageAsync(target);
    }

    private async Task SendImageAsync(string target)
    {
        Announce("saving and importing", urgent: true);

        var written = await _images.ExportTo(target).ConfigureAwait(true);

        if (!written.StartsWith("saved", StringComparison.Ordinal))
        {
            Announce(written, urgent: true);
            return;
        }

        await ImportAsync(target).ConfigureAwait(true);

        Announce("in the media bin. Insert it from there, or press I again after more changes",
            urgent: true);
    }

    private void ChooseCropRatio()
    {
        var menu = Gio.Menu.New();

        foreach (var (name, _) in CropRatios)
        {
            menu.AppendItem(Gio.MenuItem.New(name, $"win.imageCrop::{name}"));
        }

        PopUp(menu, "crop menu. It will ask where to anchor it");
    }

    private static readonly (string Name, double Ratio)[] CropRatios =
    [
        ("Square", 1),
        ("16 by 9", 16.0 / 9),
        ("4 by 3", 4.0 / 3),
        ("3 by 2", 3.0 / 2),
        ("4 by 5", 4.0 / 5),
        ("9 by 16", 9.0 / 16),
    ];

    private void PopUp(Gio.Menu menu, string announce)
    {
        var popover = Gtk_.PopoverMenu.NewFromModel(menu);
        popover.SetParent(_window);
        popover.HasArrow = false;
        popover.Popup();
        popover.GrabFocus();

        Announce(announce, urgent: true);
    }

    private void SampleAPoint()
    {
        if (_images.Document is not { } document) return;

        Prompt(
            "Point, as x and y or a cell number",
            $"{document.Width / 2} {document.Height / 2}",
            "Read",
            text =>
            {
                var parts = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 1 && int.TryParse(parts[0], out var cell) && cell is >= 1 and <= 9)
                {
                    var (nx, ny) = new Placement(cell).Resolve();

                    _images.SampleColour(nx * document.SourceWidth, ny * document.SourceHeight);
                    return;
                }

                if (parts.Length >= 2 && double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y))
                {
                    _images.SampleColour(x, y);
                    return;
                }

                Announce("say two numbers, or one cell number from 1 to 9", urgent: true);
            });
    }

    private bool OnStreamKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);

        // Anything typed into the reply box is a reply, not a command.
        if (_window.GetFocus() is Gtk_.Entry or Gtk_.Text) return false;

        if (args.Keyval is Gdk.Constants.KEY_grave or Gdk.Constants.KEY_asciitilde && control)
        {
            _stream.CycleArea(!shift);
            return true;
        }

        // A digit cuts to that scene. One key, no confirmation - that is what a
        // scene is for - and always announced with what is now live.
        if (args.Keyval >= Gdk.Constants.KEY_1 && args.Keyval <= Gdk.Constants.KEY_9 && !control)
        {
            _stream.SwitchToNumber((int)(args.Keyval - Gdk.Constants.KEY_1) + 1);
            return true;
        }

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_n when shift:
            case Gdk.Constants.KEY_N when shift:
                _stream.UseStarterSetup();
                return true;

            case Gdk.Constants.KEY_n or Gdk.Constants.KEY_N:
                Prompt("New scene", string.Empty, "Add", name => _stream.AddScene(name));
                return true;

            case Gdk.Constants.KEY_F2:
                Prompt("Rename scene", string.Empty, "Rename", name => _stream.RenameScene(name));
                return true;

            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when !shift:
                AddStreamSource();
                return true;

            case Gdk.Constants.KEY_v or Gdk.Constants.KEY_V:
                _stream.ToggleSourceVisible();
                return true;

            case Gdk.Constants.KEY_m or Gdk.Constants.KEY_M:
                _stream.ToggleSourceMuted();
                return true;

            case Gdk.Constants.KEY_bracketleft:
                _stream.ReorderSource(forward: false);
                return true;

            case Gdk.Constants.KEY_bracketright:
                _stream.ReorderSource(forward: true);
                return true;

            case Gdk.Constants.KEY_Delete or Gdk.Constants.KEY_BackSpace:
                DeleteInStream();
                return true;

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C when !shift:
                Prompt("Twitch channel", _settings.Streaming.TwitchChannel, "Connect",
                    channel => _stream.ConnectTwitch(channel));
                return true;

            case Gdk.Constants.KEY_k or Gdk.Constants.KEY_K when !shift:
                AddStreamKey();
                return true;

            case Gdk.Constants.KEY_p when control && shift:
            case Gdk.Constants.KEY_P when control && shift:
                _stream.Moderate(ChatAction.Announce);
                return true;

            case Gdk.Constants.KEY_p or Gdk.Constants.KEY_P when shift:
                _stream.Moderate(ChatAction.Pin);
                return true;

            case Gdk.Constants.KEY_p or Gdk.Constants.KEY_P:
                _stream.Preflight();
                return true;

            // ---- chat, and what may be done to it ------------------------

            case Gdk.Constants.KEY_y or Gdk.Constants.KEY_Y:
                Prompt("YouTube live video id", _settings.Streaming.YouTubeVideoId, "Connect",
                    id => _stream.ConnectYouTube(id));
                return true;

            case Gdk.Constants.KEY_f or Gdk.Constants.KEY_F:
                Prompt("Facebook live video id", _settings.Streaming.FacebookLiveVideoId, "Connect",
                    id => _stream.ConnectFacebook(id));
                return true;

            case Gdk.Constants.KEY_d or Gdk.Constants.KEY_D:
                _stream.Moderate(ChatAction.Delete);
                return true;

            case Gdk.Constants.KEY_t or Gdk.Constants.KEY_T:
                _stream.Moderate(ChatAction.Timeout, 600);
                return true;

            // Banning is not undoable and is not a single letter's worth of
            // certainty, so it asks first and the safe answer has the focus.
            case Gdk.Constants.KEY_b or Gdk.Constants.KEY_B:
                ConfirmThen("Ban this person from your chat?", () => _stream.Moderate(ChatAction.Ban));
                return true;

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C when shift:
                _stream.DescribeCapabilities();
                return true;

            case Gdk.Constants.KEY_k or Gdk.Constants.KEY_K when shift:
                _stream.DescribeSecrets();
                return true;

            // ---- music ---------------------------------------------------

            case Gdk.Constants.KEY_space when shift:
                _stream.StopMusic();
                return true;

            case Gdk.Constants.KEY_space:
                _stream.PlayMusic();
                return true;

            case Gdk.Constants.KEY_Right when shift:
                _stream.NextTrack();
                return true;

            case Gdk.Constants.KEY_Left when shift:
                _stream.PreviousTrack();
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S when shift:
                _stream.ShuffleMusic();
                return true;

            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when shift:
                Prompt("Music file", string.Empty, "Add", path => _stream.AddMusic(path));
                return true;

            // ---- how it is going -----------------------------------------

            case Gdk.Constants.KEY_h or Gdk.Constants.KEY_H:
                _stream.ReportHealth();
                return true;

            case Gdk.Constants.KEY_F9 when shift:
                _stream.ToggleMonitoring();
                return true;

            case Gdk.Constants.KEY_r or Gdk.Constants.KEY_R:
                _stream.FocusReply();
                return true;

            case Gdk.Constants.KEY_Home when control:
                _stream.ReturnToLive();
                return true;

            // Going live is the one thing in this view that an audience feels
            // the instant it happens, so it is the one thing that is not a
            // single letter.
            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L when control && shift:
                _stream.ToggleLive();
                return true;
        }

        return false;
    }

    private void DeleteInStream()
    {
        if (_stream.CurrentAreaName == "sources")
        {
            _stream.RemoveSource();
            return;
        }

        _stream.RemoveScene();
    }

    /// <summary>
    /// Adding a source asks what kind first, because the answer changes what
    /// else it needs - a camera needs a device, a song needs a file and a
    /// question about looping.
    /// </summary>
    private void AddStreamSource()
    {
        var menu = Gio.Menu.New();

        foreach (var (label, action) in new[]
                 {
                     ("Camera", "addCamera"),
                     ("Screen capture", "addScreen"),
                     ("Microphone", "addMicrophone"),
                     ("Image", "addImage"),
                     ("Video", "addVideo"),
                     ("Music, looping", "addMusic"),
                 })
        {
            menu.AppendItem(Gio.MenuItem.New(label, $"win.{action}"));
        }

        var popover = Gtk_.PopoverMenu.NewFromModel(menu);
        popover.SetParent(_window);
        popover.HasArrow = false;
        popover.Popup();
        popover.GrabFocus();

        Announce("add source menu", urgent: true);
    }

    private void AddStreamKey()
    {
        var menu = Gio.Menu.New();
        menu.AppendItem(Gio.MenuItem.New("Twitch stream key", "win.keyTwitch"));
        menu.AppendItem(Gio.MenuItem.New("YouTube stream key", "win.keyYouTube"));
        menu.AppendItem(Gio.MenuItem.New("Facebook stream key", "win.keyFacebook"));

        var popover = Gtk_.PopoverMenu.NewFromModel(menu);
        popover.SetParent(_window);
        popover.HasArrow = false;
        popover.Popup();
        popover.GrabFocus();

        Announce("stream key menu. Nothing typed here is ever read back aloud", urgent: true);
    }

    private bool OnTimelineKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);
        var alt = args.State.HasFlag(Gdk.ModifierType.AltMask);
        var navigator = new TimelineNavigator(Project, _session.Map);

        switch (args.Keyval)
        {
            // The boundary under the cursor: what it is, how long, and what it
            // sounds like. A transition is a navigable object here, so editing
            // one is a key rather than a dialog to go and find.
            case Gdk.Constants.KEY_x or Gdk.Constants.KEY_X when !control && !shift:
                ChooseTransition();
                return true;

            case Gdk.Constants.KEY_x or Gdk.Constants.KEY_X when shift:
                AuditionTransition();
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G when shift:
                SetTrackVolume();
                return true;

            case Gdk.Constants.KEY_Left or Gdk.Constants.KEY_Right when control:
                JumpItem(args.Keyval == Gdk.Constants.KEY_Right);
                ScrubHere();
                return true;

            case Gdk.Constants.KEY_Left or Gdk.Constants.KEY_Right:
                _cursor.MoveTo(navigator.Move(
                    _cursor.ProgrammeTime, _cursor.Granularity,
                    args.Keyval == Gdk.Constants.KEY_Right ? 1 : -1));
                Refresh();
                Announce(FocusedStatus(), urgent: false);
                ScrubHere();
                return true;

            case Gdk.Constants.KEY_less or Gdk.Constants.KEY_comma when shift:
                JumpEdge(forward: false);
                return true;

            case Gdk.Constants.KEY_greater or Gdk.Constants.KEY_period when shift:
                JumpEdge(forward: true);
                return true;

            case Gdk.Constants.KEY_minus:
                Step(coarser: true);
                return true;

            case Gdk.Constants.KEY_equal:
                Step(coarser: false);
                return true;

            case Gdk.Constants.KEY_Home:
                _cursor.MoveTo(0);
                Refresh();
                Announce(FocusedStatus(), urgent: true);
                return true;

            case Gdk.Constants.KEY_End:
                _cursor.MoveTo(_session.Map.Duration);
                Refresh();
                Announce(FocusedStatus(), urgent: true);
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S when !control && shift:
                SplitEveryTrack();
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S when !control:
                SplitFocusedTrack();
                return true;

            // Mark in and out. A selection is invisible state, so every change
            // to it is spoken.
            case Gdk.Constants.KEY_bracketleft when !alt:
            case Gdk.Constants.KEY_i or Gdk.Constants.KEY_I when !control:
                _cursor.SetSelectionStart(_cursor.ProgrammeTime);
                Announce(_cursor.Selection!.Value.DescribeMark(isStart: true), urgent: true);
                return true;

            case Gdk.Constants.KEY_bracketright when !alt:
            case Gdk.Constants.KEY_o or Gdk.Constants.KEY_O when !control:
                _cursor.SetSelectionEnd(_cursor.ProgrammeTime);
                Announce(_cursor.Selection!.Value.DescribeMark(isStart: false), urgent: true);
                return true;

            case Gdk.Constants.KEY_Escape:
                _cursor.ClearSelection();
                Announce("selection cleared", urgent: true);
                return true;

            case Gdk.Constants.KEY_semicolon when control && shift:
                Announce(_cursor.Selection?.Describe() ?? "no selection", urgent: true);
                return true;

            case Gdk.Constants.KEY_Delete when shift:
                LiftTarget();
                return true;

            case Gdk.Constants.KEY_Delete:
                DeleteTarget();
                return true;

            case Gdk.Constants.KEY_E when shift:
                Apply("disable", p => EditOperations.ToggleDisable(p, _cursor.ProgrammeTime));
                return true;

            case Gdk.Constants.KEY_j when control:
                Apply("heal", p => EditOperations.Heal(p, _cursor.ProgrammeTime));
                return true;

            case Gdk.Constants.KEY_e when control:
                Run("editCard");
                return true;

            case Gdk.Constants.KEY_f when control && shift:
                Run("fade");
                return true;

            case Gdk.Constants.KEY_d when control && shift:
                Run("detachAudio");
                return true;

            case Gdk.Constants.KEY_f when control && alt:
                Run("removeFillers");
                return true;

            case Gdk.Constants.KEY_s when control && alt:
                Run("removeSilences");
                return true;

            case Gdk.Constants.KEY_p when control && alt:
                Run("pace");
                return true;

            case Gdk.Constants.KEY_bracketleft when alt:
                Apply("trim head", p => EditOperations.TrimHead(p, _cursor.ProgrammeTime));
                return true;

            case Gdk.Constants.KEY_bracketright when alt:
                Apply("trim tail", p => EditOperations.TrimTail(p, _cursor.ProgrammeTime));
                return true;

            case Gdk.Constants.KEY_z when control:
                Announce(_session.Undo()?.Announce() ?? "nothing to undo", urgent: true);
                Refresh();
                return true;

            case Gdk.Constants.KEY_semicolon when control && shift:
                DescribeCard();
                return true;

            case Gdk.Constants.KEY_semicolon when control:
                Announce(WhereAmI(), urgent: true);
                return true;

            case Gdk.Constants.KEY_c when control:
            {
                if (_cursor.Selection is not { IsEmpty: false })
                {
                    Announce("mark a range first with bracket left and bracket right", urgent: true);
                    return true;
                }

                Announce(_clipboard.Copy(Project, _session.Map, _cursor.FocusedTrack!.Value,
                    _cursor.Selection.Value).Announce(), urgent: true);
                return true;
            }

            case Gdk.Constants.KEY_x when control:
                Apply("cut", _ => _clipboard.Cut(Project, _session.Map,
                    _cursor.FocusedTrack!.Value, Selection()));
                return true;

            case Gdk.Constants.KEY_v when control:
                Apply("paste", p => _clipboard.Paste(p, _cursor.FocusedTrack!.Value, _cursor.ProgrammeTime));
                return true;

            case Gdk.Constants.KEY_space when control:
                Audition();
                return true;

            case Gdk.Constants.KEY_space:
                TogglePlay();
                return true;

            case Gdk.Constants.KEY_t or Gdk.Constants.KEY_T when !control && !shift:
                CycleTake(1);
                return true;

            case Gdk.Constants.KEY_t or Gdk.Constants.KEY_T when shift && !control:
                CycleTake(-1);
                return true;

            case Gdk.Constants.KEY_j or Gdk.Constants.KEY_J:
                Shuttle(0.5);
                return true;

            case Gdk.Constants.KEY_k or Gdk.Constants.KEY_K:
                Shuttle(0);
                return true;

            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L:
                Shuttle(2);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Plain letters are safe here because nothing in the Tracks pane is a text
    /// field - which is exactly why track controls live in their own pane.
    /// </summary>
    /// <summary>
    /// Comma inserts and full stop overwrites, as in Premiere. Plain letters are
    /// safe here because nothing in the media bin is a text field.
    /// </summary>
    private bool OnMediaKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_comma:
                Run("insert");
                return true;

            case Gdk.Constants.KEY_period:
                Run("overwrite");
                return true;

            case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter:
                Run("insert");
                return true;

            case Gdk.Constants.KEY_i or Gdk.Constants.KEY_I:
                Run("import");
                return true;

            default:
                return false;
        }
    }

    private bool OnTracksKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var track = Project.TrackOf(_cursor.FocusedTrack ?? default);
        if (track is null) return false;

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_m or Gdk.Constants.KEY_M:
                Run("muteTrack");
                return true;

            case Gdk.Constants.KEY_s or Gdk.Constants.KEY_S:
                Run("soloTrack");
                return true;

            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L:
                Run("lockTrack");
                return true;

            case Gdk.Constants.KEY_n or Gdk.Constants.KEY_N:
                Run("renameTrack");
                return true;

            case Gdk.Constants.KEY_Delete:
                Run("removeTrack");
                return true;

            default:
                return false;
        }
    }

    // ---- actions ---------------------------------------------------------

    private void RegisterActions()
    {
        Action("split", () => Apply("split", p => EditOperations.SplitAt(p, _cursor.ProgrammeTime)));
        Action("heal", () => Apply("heal", p => EditOperations.Heal(p, _cursor.ProgrammeTime)));
        Action("rippleDelete", DeleteTarget);
        Action("lift", () => Apply("lift", p => EditOperations.Lift(p, Selection())));
        Action("disable", () => Apply("disable", p => EditOperations.ToggleDisable(p, _cursor.ProgrammeTime)));
        Action("mute", () => Apply("mute", p => EditOperations.ToggleMute(p, _cursor.ProgrammeTime)));
        Action("trimHead", () => Apply("trim head", p => EditOperations.TrimHead(p, _cursor.ProgrammeTime)));
        Action("trimTail", () => Apply("trim tail", p => EditOperations.TrimTail(p, _cursor.ProgrammeTime)));
        // Undo acts on whatever you are editing. One key, and it always means
        // "take back the last thing I did here" rather than "take back the last
        // thing I did to the video, wherever I happen to be standing".
        Action("undo", () =>
        {
            if (_workspace.Focused == Pane.Images)
            {
                _images.Undo();
                return;
            }

            Announce(_session.Undo()?.Announce() ?? "nothing to undo", true);
            Refresh();
        });

        Action("redo", () =>
        {
            if (_workspace.Focused == Pane.Images)
            {
                _images.Redo();
                return;
            }

            Announce(_session.Redo()?.Announce() ?? "nothing to redo", true);
            Refresh();
        });

        Action("renameTrack", RenameTrack);
        Action("addTrack", AddTrack);
        Action("armTrack", ToggleArm);
        Action("chooseDevice", ChooseDevice);
        Action("trackType", ChangeTrackType);
        Action("chooseOutput", ChooseOutput);
        Action("chooseChannel", ChooseChannel);
        Action("monitor", ToggleMonitoring);
        Action("editCard", EditCard);
        Action("duration", SetDuration);
        Action("kenBurns", () => Apply("movement",
            p => EditOperations.CycleKenBurns(p, _cursor.ProgrammeTime)));
        Action("describeFrame", DescribeFrame);
        Action("import", ImportMedia);
        Action("insert", () => AssembleFromBin(overwrite: false));
        Action("overwrite", () => AssembleFromBin(overwrite: true));
        Action("detachAudio", DetachAudio);
        Action("removeFillers", () => Apply("remove fillers", p => TranscriptCleanup.RemoveFillers(p)));
        Action("removeSilences", () => Apply("remove silences", p => TranscriptCleanup.RemoveSilences(p)));
        Action("pace", () => Announce(
            TranscriptCleanup.PaceReport(Project, _session.Map), urgent: true));
        Action("quality", () => AnalyseQuality(wholeProject: false));
        Action("qualityAll", () => AnalyseQuality(wholeProject: true));
        Action("export", () => Render(RenderQuality.Master));
        Action("renderDraft", () => Render(RenderQuality.Draft));
        Action("fade", EditFades);
        Action("insertSegment", InsertSegment);
        Action("record", ToggleRecording);

        // Menu items must reach the same implementations the keys do. These
        // existed as key handlers only, so choosing them from a menu silently
        // did nothing at all.
        Action("copy", () =>
        {
            if (_cursor.Selection is not { IsEmpty: false } selection)
            {
                Announce("mark a range first with bracket left and bracket right", urgent: true);
                return;
            }

            Announce(_clipboard.Copy(Project, _session.Map, _cursor.FocusedTrack!.Value, selection)
                .Announce(), urgent: true);
        });

        Action("cut", () => Apply("cut", _ => _clipboard.Cut(
            Project, _session.Map, _cursor.FocusedTrack!.Value, Selection())));

        Action("paste", () => Apply("paste", p => _clipboard.Paste(
            p, _cursor.FocusedTrack!.Value, _cursor.ProgrammeTime)));

        Action("splitAll", SplitEveryTrack);
        Action("split", SplitFocusedTrack);
        Action("lift", LiftTarget);
        Action("card", () => InsertCardAtCursor());
        Action("selectClear", () =>
        {
            _cursor.ClearSelection();
            Announce("selection cleared", urgent: true);
        });
        Action("viewfinder", EnterViewfinder);
        Action("describeShot", DescribeShot);
        Action("muteTrack", () => ToggleTrack(t => t.Muted = !t.Muted));
        Action("soloTrack", () => ToggleTrack(t => t.Soloed = !t.Soloed));
        Action("lockTrack", () => ToggleTrack(t => t.Locked = !t.Locked));

        Action("play", TogglePlay);
        Action("audition", Audition);

        Action("nextPane", () => FocusPane(_workspace.Next()));
        Action("previousPane", () => FocusPane(_workspace.Previous()));
        Action("whereAmI", () => Announce(WhereAmI(), true));
        Action("zoomOut", () => Step(coarser: true));
        Action("zoomIn", () => Step(coarser: false));
        Action("previousSegment", () => JumpItem(forward: false));
        Action("nextSegment", () => JumpItem(forward: true));
        Action("segmentStart", () => JumpEdge(forward: false));
        Action("segmentEnd", () => JumpEdge(forward: true));
        Action("contextHelp", SpeakContextHelp);
        Action("about", ShowAbout);
        Action("keymap", ReadKeymap);
        Action("quit", () => _window.Close());

        RegisterStreamActions();
        RegisterImageActions();
        RegisterTransitionActions();

        // Not built yet. Each says what it will do and roughly when, rather than
        // a bare "not wired" - so an unbuilt command is still informative.
        foreach (var (pending, note) in NotYetBuilt)
        {
            var name = pending;
            var message = note;
            Action(name, () => Announce(message, urgent: true));
        }
    }

    /// <summary>
    /// The streamer view's commands. Registered here with everything else so
    /// the menu, the popovers and the keys all go through one implementation -
    /// which is how a menu item ends up doing exactly what its key does.
    /// </summary>
    private void RegisterStreamActions()
    {
        Action("streamStarter", () => _stream.UseStarterSetup());
        Action("streamScene", () => Prompt("New scene", string.Empty, "Add", n => _stream.AddScene(n)));
        Action("streamPreflight", () => _stream.Preflight());
        Action("streamLive", () => _stream.ToggleLive());
        Action("streamConnectTwitch", () =>
            Prompt("Twitch channel", string.Empty, "Connect", c => _stream.ConnectTwitch(c)));

        Action("streamConnectYouTube", () =>
            Prompt("YouTube live video id", _settings.Streaming.YouTubeVideoId, "Connect",
                id => _stream.ConnectYouTube(id)));

        Action("streamConnectFacebook", () =>
            Prompt("Facebook live video id", _settings.Streaming.FacebookLiveVideoId, "Connect",
                id => _stream.ConnectFacebook(id)));

        Action("streamCapabilities", () => _stream.DescribeCapabilities());
        Action("streamHealth", () => _stream.ReportHealth());
        Action("streamMeter", () => _stream.ToggleMonitoring());
        Action("streamSecrets", () => _stream.DescribeSecrets());

        Action("streamMusic", () => _stream.PlayMusic());
        Action("streamMusicNext", () => _stream.NextTrack());
        Action("streamMusicPrevious", () => _stream.PreviousTrack());
        Action("streamMusicStop", () => _stream.StopMusic());
        Action("streamMusicShuffle", () => _stream.ShuffleMusic());
        Action("streamAddMusic", () =>
            Prompt("Music file", string.Empty, "Add", path => _stream.AddMusic(path)));

        Action("streamDelete", () => _stream.Moderate(ChatAction.Delete));
        Action("streamTimeout", () => _stream.Moderate(ChatAction.Timeout, 600));
        Action("streamBan", () => ConfirmThen(
            "Ban this person from your chat?",
            () => _stream.Moderate(ChatAction.Ban)));
        Action("streamAnnounce", () => _stream.Moderate(ChatAction.Announce));

        Action("twitchToken", () =>
            Prompt("Twitch token", string.Empty, "Save", t => _stream.SetSecret("twitch.token", t)));
        Action("twitchClientId", () =>
            Prompt("Twitch client id", string.Empty, "Save", t => _stream.SetSecret("twitch.clientId", t)));
        Action("youtubeApiKey", () =>
            Prompt("YouTube API key", string.Empty, "Save", t => _stream.SetSecret("youtube.apiKey", t)));
        Action("youtubeOAuth", () =>
            Prompt("YouTube sign-in token", string.Empty, "Save", t => _stream.SetSecret("youtube.oauth", t)));
        Action("facebookToken", () =>
            Prompt("Facebook page token", string.Empty, "Save", t => _stream.SetSecret("facebook.token", t)));

        Action("addCamera", () => AddDeviceSource("Camera", StreamSourceKind.Camera));
        Action("addScreen", () => AddDeviceSource("Screen", StreamSourceKind.Screen));
        Action("addMicrophone", () => AddDeviceSource("Microphone", StreamSourceKind.Microphone));
        Action("addImage", () => AddFileSource("Image", StreamSourceKind.Image, loop: false));
        Action("addVideo", () => AddFileSource("Video", StreamSourceKind.Video, loop: false));
        Action("addMusic", () => AddFileSource("Music", StreamSourceKind.Music, loop: true));

        Action("keyTwitch", () => AskForKey(StreamPlatform.Twitch));
        Action("keyYouTube", () => AskForKey(StreamPlatform.YouTube));
        Action("keyFacebook", () => AskForKey(StreamPlatform.Facebook));
    }

    /// <summary>
    /// Transitions, track levels, and the handful of commands that were in the
    /// core with no way to reach them.
    /// </summary>
    private void RegisterTransitionActions()
    {
        Action("setTransition", ChooseTransition);
        Action("auditionTransition", AuditionTransition);
        Action("transitionSound", ChooseTransitionSound);
        Action("saveTransition", SaveCustomTransition);
        Action("customTransition", ChooseCustomTransition);

        Action("trackVolume", SetTrackVolume);

        Action("speed", SetSpeed);
        Action("insertHole", InsertHole);
        Action("verbosity", CycleVerbosity);

        Action("removeTrack", () =>
        {
            if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track) return;

            ConfirmThen(
                $"Delete {track.Name} and everything on it?",
                () => Apply("delete track", p => EditOperations.RemoveTrack(p, track.Id)));
        });

        ParameterisedAction("pickTransition", name => ApplyTransition(name));
        ParameterisedAction("pickTransitionLength", name =>
        {
            if (double.TryParse(name, out var seconds)) SetTransitionLength(seconds);
        });
        ParameterisedAction("pickCustomTransition", name => UseCustomTransition(name));
    }

    /// <summary>
    /// The transition entering the segment under the cursor. Type first,
    /// because that is the decision; the length follows and has its own menu.
    /// </summary>
    private void ChooseTransition()
    {
        var menu = Gio.Menu.New();

        var common = Gio.Menu.New();
        foreach (var (name, _) in TransitionLibrary.Common)
        {
            common.Append(TitleCase(name), $"win.pickTransition::{name}");
        }

        menu.AppendSection(null, common);

        var more = Gio.Menu.New();
        foreach (var name in TransitionLibrary.More)
        {
            more.Append(name, $"win.pickTransition::{name}");
        }

        menu.AppendSubmenu("More", more);

        var lengths = Gio.Menu.New();
        foreach (var seconds in TransitionLibrary.Lengths)
        {
            lengths.Append(
                TransitionLibrary.DescribeLength(seconds),
                $"win.pickTransitionLength::{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        menu.AppendSubmenu("Length", lengths);

        var extras = Gio.Menu.New();
        extras.Append("Sound On This Transition", "win.transitionSound");
        extras.Append("Audition It", "win.auditionTransition");
        extras.Append("Save As My Own", "win.saveTransition");
        extras.Append("Use One Of Mine", "win.customTransition");
        menu.AppendSection(null, extras);

        PopUp(menu, $"transition menu. {DescribeTransitionHere()}");
    }

    private Transition? TransitionHere()
    {
        var placed = _session.Map.Locate(_cursor.ProgrammeTime);

        return placed?.Element.TransitionIn;
    }

    private string DescribeTransitionHere() =>
        TransitionHere() is { } transition
            ? $"currently {transition.Describe()}"
            : "currently the project default";

    private void ApplyTransition(string name)
    {
        if (name.Length == 0) return;

        var existing = TransitionHere();

        var match = TransitionLibrary.Common.FirstOrDefault(t => t.Name == name);

        var transition = existing?.Copy() ?? new Transition();

        if (match.Name is not null)
        {
            transition.Type = match.Type;
            transition.CustomType = null;
            transition.Expression = null;
        }
        else
        {
            transition.Type = TransitionType.Custom;
            transition.CustomType = name;
            transition.Expression = null;
        }

        if (transition.Type == TransitionType.Cut) transition.Duration = 0;
        else if (transition.Duration <= 0) transition.Duration = 0.4;

        Apply("set transition", p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));
    }

    private void SetTransitionLength(double seconds)
    {
        var transition = TransitionHere()?.Copy() ?? new Transition();

        transition.Duration = seconds;

        if (transition.Type == TransitionType.Cut && seconds > 0) transition.Type = TransitionType.Fade;

        Apply(
            "transition length",
            p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));

        Announce(TransitionLibrary.DescribeLength(seconds), urgent: true);
    }

    /// <summary>
    /// A sound under the cut. It belongs to the boundary, so moving the cut
    /// moves the sound with it.
    /// </summary>
    private void ChooseTransitionSound() =>
        Prompt("Sound file for this transition", string.Empty, "Use", path =>
        {
            var transition = TransitionHere()?.Copy() ?? new Transition();

            transition.SoundPath = path;

            Apply(
                "transition sound",
                p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));

            Announce(
                "the programme is not ducked under it; set the track levels yourself with Shift+G",
                urgent: true);
        });

    /// <summary>Plays across the boundary, so the transition can be heard rather than imagined.</summary>
    private void AuditionTransition()
    {
        if (TransitionHere() is not { } transition)
        {
            Announce("there is no transition here", urgent: true);
            return;
        }

        var length = Math.Max(0.4, transition.Duration);
        var from = Math.Max(0, _cursor.ProgrammeTime - length);

        if (!_player.IsAvailable || !EnsureLoaded()) return;

        Announce($"auditioning {transition.Describe()}", urgent: true);

        _ = _player.PlayRangeAsync(from, Math.Min(_session.Map.Duration, _cursor.ProgrammeTime + length));
    }

    private void SaveCustomTransition() =>
        Prompt("Name for this transition", string.Empty, "Save", name =>
        {
            var here = TransitionHere();

            Prompt(
                "xfade name, or an expression",
                here?.Expression ?? here?.FfmpegName ?? "fade",
                "Save",
                definition =>
                {
                    var custom = new CustomTransition
                    {
                        Name = name,
                        Definition = definition,
                        IsExpression = definition.Contains('(') || definition.Contains("PROGRESS"),
                        Duration = here?.Duration ?? 0.4,
                        SoundPath = here?.SoundPath,
                    };

                    Project.CustomTransitions.RemoveAll(t => t.Name == name);
                    Project.CustomTransitions.Add(custom);

                    Announce($"saved {custom.Describe()}", urgent: true);
                });
        });

    private void ChooseCustomTransition()
    {
        if (Project.CustomTransitions.Count == 0)
        {
            Announce("you have not saved any yet; set one up and choose save as my own", urgent: true);
            return;
        }

        var menu = Gio.Menu.New();

        foreach (var custom in Project.CustomTransitions)
        {
            menu.Append(custom.Name, $"win.pickCustomTransition::{custom.Name}");
        }

        PopUp(menu, $"{Project.CustomTransitions.Count} of your own");
    }

    private void UseCustomTransition(string name)
    {
        if (Project.CustomTransitions.FirstOrDefault(t => t.Name == name) is not { } custom) return;

        Apply(
            "custom transition",
            p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, custom.ToTransition()));
    }

    /// <summary>
    /// The level of one track. There is no automatic ducking anywhere in this
    /// application, so this is how a music bed is put under a voice - by
    /// deciding it.
    /// </summary>
    private void SetTrackVolume()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track) return;

        Prompt(
            $"{track.Name} level in dB",
            track.GainDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            "Set",
            text =>
            {
                if (!double.TryParse(text, out var db))
                {
                    Announce("say a number of decibels, like minus 12", urgent: true);
                    return;
                }

                track.GainDb = Math.Clamp(db, -60, 12);

                RebuildTrackRows();
                Announce($"{track.Name} at {track.GainDb:0.#} dB", urgent: true);
            });
    }

    private void SetSpeed() =>
        Prompt("Speed, where 1 is normal", "1", "Set", text =>
        {
            if (!double.TryParse(text, out var speed) || speed <= 0)
            {
                Announce("say a number, like 0.5 for half speed", urgent: true);
                return;
            }

            Apply("speed", p => EditOperations.SetSpeed(p, _cursor.ProgrammeTime, speed));
        });

    private void InsertHole() =>
        Prompt("What is missing here", string.Empty, "Insert", note =>
            Apply("insert hole", p => EditOperations.InsertHole(p, _cursor.ProgrammeTime, 2, note)));

    private void CycleVerbosity()
    {
        Project.Settings.Verbosity = Project.Settings.Verbosity switch
        {
            Verbosity.Terse => Verbosity.Normal,
            Verbosity.Normal => Verbosity.Verbose,
            _ => Verbosity.Terse,
        };

        _settings.Behaviour.Verbosity = Project.Settings.Verbosity;
        _settings.Save();

        Refresh();
        Announce($"{Project.Settings.Verbosity.ToString().ToLowerInvariant()} speech", urgent: true);
    }

    /// <summary>
    /// The image editor's commands, registered with everything else so the
    /// menu, the popovers and the keys are one implementation.
    /// </summary>
    private void RegisterImageActions()
    {
        Action("imageOpen", () => Prompt("Open a picture", string.Empty, "Open", p => _images.Open(p)));
        Action("imageDescribe", () => _images.Describe());
        Action("imageFixScan", () => _images.Apply("fixing the scan", ImageEdits.FixScan));
        Action("imageStraighten", () => _images.Apply("straightening", ImageEdits.Straighten));
        Action("imageCropContent", () => _images.Apply("cropping to the picture", ImageEdits.CropToContent));
        Action("imageResetCrop", () => _images.Apply("resetting the crop", ImageEdits.ResetCrop));
        Action("imageAspectLock", () => _images.Apply("the shape lock", ImageEdits.ToggleAspectLock));
        Action("imageColours", () => _images.DescribeColours());
        Action("imageSample", SampleAPoint);
        Action("imageHistory", () => _images.DescribeHistory());
        Action("imageSweep", () => _images.ToggleSweep());
        Action("imageAdvise", () => _images.AdviseColour());
        Action("imageCard", EditImageCard);
        Action("imageToProject", SendImageToProject);

        Action("imageHistogram", () => _images.ReadHistogram());
        Action("imageCast", () => _images.ReadCast());
        Action("imageColourLevels", ChooseColourLevels);
        Action("imageBatch", RunBatch);

        ParameterisedAction("imageCorrect", preset =>
        {
            if (preset.Length > 0) _images.Correct(preset);
        });

        ParameterisedAction("imageLevel", preset =>
        {
            if (preset.Length > 0) _images.Level(preset);
        });

        ParameterisedAction("imageColourLevel", preset =>
        {
            if (preset.Length > 0) _images.ColourLevel(preset);
        });
        Action("imageDraw", () => Prompt("Draw", string.Empty, "Draw", s => _images.AddShape(s)));
        Action("imageExport", () =>
            Prompt("Save as", SuggestedImageName(), "Save", path => _images.Export(path)));
        Action("imageSplit", () =>
            Prompt("Split into folder", System.IO.Path.GetTempPath(), "Split", d => _images.Split(d)));

        // The two menus that carry a value rather than being one command each.
        ParameterisedAction("imageSize", name =>
            _images.Apply("resizing", document => ImageEdits.ApplyPreset(document, name)));

        ParameterisedAction("imageCrop", name =>
        {
            var ratio = CropRatios.FirstOrDefault(r => r.Name == name).Ratio;

            if (ratio <= 0) return;

            Prompt("Anchor on which cell, 1 to 9", "5", "Crop", cell =>
            {
                var number = int.TryParse(cell, out var value) && value is >= 1 and <= 9 ? value : 5;

                _images.Apply("cropping to a shape", document =>
                    ImageEdits.CropToRatio(document, ratio, new Placement(number)));
            });
        });
    }

    /// <summary>
    /// An action that carries a string, for menus where every item is the same
    /// command with a different value. Without this each preset would need its
    /// own handler and they would drift apart.
    /// </summary>
    private void ParameterisedAction(string name, System.Action<string> handler)
    {
        _commands[name] = () => handler(string.Empty);

        var action = Gio.SimpleAction.New(name, GLib.VariantType.New("s"));
        // Named rather than discarded: inside this lambda a bare underscore is
        // the sender parameter, not a discard, and `out _` would bind to it.
        action.OnActivate += (sender, args) =>
        {
            if (args.Parameter is not { } parameter)
            {
                handler(string.Empty);
                return;
            }

            handler(parameter.GetString(out var length) ?? string.Empty);
        };

        _window.AddAction(action);
    }

    /// <summary>
    /// A yes-or-no dialog for the few things that cannot be taken back. Banning
    /// somebody and stopping a broadcast are both in that category; nothing
    /// else in this application is.
    /// </summary>
    private void ConfirmThen(string question, System.Action confirmed)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = question;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(420, 130);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var label = Gtk_.Label.New(question);
        label.Wrap = true;
        label.Xalign = 0;
        box.Append(label);

        var buttons = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 8);

        var yes = Gtk_.Button.NewWithLabel("Yes");
        yes.AddCssClass("suggested-action");
        var no = Gtk_.Button.NewWithLabel("No");

        yes.OnClicked += (_, _) =>
        {
            dialog.Close();
            confirmed();
        };

        no.OnClicked += (_, _) =>
        {
            dialog.Close();
            Announce("cancelled", urgent: true);
        };

        buttons.Append(yes);
        buttons.Append(no);
        box.Append(buttons);

        dialog.SetChild(box);
        dialog.Present();

        // Focus lands on No: the safe answer is the one you get by pressing
        // Enter without having listened.
        no.GrabFocus();
    }

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

    private static readonly (string Name, string Note)[] NotYetBuilt =
    [
        ("new", "new project is not built yet"),
        ("open", "open project is not built yet"),
        ("save", "save is not built yet"),
        ("saveAs", "save as is not built yet"),
        ("reloadEdl", "reloading edit dot m d is not built yet"),
        ("marker", "markers are not built yet"),
        ("palette", "the command palette is not built yet"),
        ("describeEdit", "read me the edit is not built yet, phase 9"),
        ("issues", "the to-do list is not built yet"),
        ("find", "find in transcript is not built yet"),
        ("title", "adding a lower third is not built yet; insert a card and edit it with Control E"),
        ("graphic", "adding an image is not built yet"),
        ("broll", "adding b-roll is not built yet"),
    ];


    private void Action(string name, System.Action handler)
    {
        _commands[name] = handler;

        var action = Gio.SimpleAction.New(name, null);
        action.OnActivate += (_, _) => Run(name);
        _window.AddAction(action);
    }

    /// <summary>Invokes a command by name. The single path for every entry point.</summary>
    private void Run(string name)
    {
        if (_commands.TryGetValue(name, out var handler)) handler();
        else Announce($"{name} is not wired yet", urgent: true);
    }

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
    /// <summary>
    /// Video, audio or image. The type is what decides which inputs the track
    /// can record from, so it is asked for when a track is made and changeable
    /// afterwards.
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

    /// <summary>A modal list. One shape for every "which one?" question.</summary>
    private void ChooseFromList(string title, IReadOnlyList<string> options, System.Action<int> chosen)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(460, 300);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var option in options) list.Append(Row(option));

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        box.Append(scroller);

        void Accept()
        {
            var index = list.GetSelectedRow()?.GetIndex() ?? 0;
            dialog.Close();
            chosen(Math.Clamp(index, 0, options.Count - 1));
        }

        var button = Gtk_.Button.NewWithLabel("Choose");
        button.OnClicked += (_, _) => Accept();
        box.Append(button);

        list.OnRowActivated += (_, _) => Accept();

        dialog.SetChild(box);
        dialog.Present();

        var first = list.GetRowAtIndex(0);
        if (first is not null)
        {
            list.SelectRow(first);
            first.GrabFocus();
        }
    }

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
    /// <summary>
    /// Opens the camera and guides framing by ear. Explicit, always - a camera
    /// never comes on as a side effect of anything else, and it says so before
    /// it does.
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
    /// <summary>
    /// Records <b>every armed track at once</b>, each to its own file. That is
    /// what makes a multi-camera shoot possible: arm two video tracks with
    /// different cameras, press record once, and get two angles that started
    /// together.
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

    /// <summary>
    /// The audible VU meter. A visual meter is glanceable; the equivalent by ear
    /// is a tick whose pitch rises with the level, with the zone name spoken
    /// only as it changes - a meter that talks constantly is one you turn off.
    ///
    /// A mode, like the viewfinder: it opens the microphone, so it runs only
    /// while you have asked for it.
    /// </summary>
    /// <summary>Opens the card under the cursor in its editor.</summary>
    /// <summary>
    /// Brings a file into the project and <b>reports what it got</b> -
    /// resolution, frame rate, length, and how many audio tracks and what they
    /// are. Importing silently is fine when a thumbnail appears; here the
    /// report is the only chance to notice you grabbed the wrong take.
    /// </summary>
    /// <summary>
    /// Puts the source selected in the media bin onto the timeline. Insert
    /// ripples everything after it; overwrite replaces what is there and leaves
    /// the timing alone.
    /// </summary>
    private void AssembleFromBin(bool overwrite)
    {
        var index = _mediaList.GetSelectedRow()?.GetIndex() ?? -1;

        if (index < 0 || index >= Project.Sources.Count)
        {
            Announce("select a source in the media bin first, Control 4", urgent: true);
            return;
        }

        var source = Project.Sources[index];

        var result = _session.Apply(overwrite ? "overwrite" : "insert", (project, _) => overwrite
            ? EditOperations.OverwriteSource(project, source.Id, _cursor.ProgrammeTime)
            : EditOperations.InsertSource(project, source.Id, _cursor.ProgrammeTime));

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    /// <summary>
    /// Moves a segment's sound onto an audio track, or puts it back. Needed
    /// whenever you want to keep someone's voice while cutting away from their
    /// picture.
    /// </summary>
    /// <summary>
    /// Renders, reporting progress as it goes and saying what came out. Runs off
    /// the UI thread so the editor stays usable, and only one render at a time -
    /// two ffmpeg runs over the same cache would fight.
    /// </summary>
    /// <summary>
    /// Measures the media under the cursor, or every source, and says what is
    /// wrong with it. This is the part that replaces looking.
    /// </summary>
    private void AnalyseQuality(bool wholeProject)
    {
        var sources = new List<(Source Source, double At)>();

        if (wholeProject)
        {
            sources.AddRange(Project.Sources
                .Where(s => s.Kind != SourceKind.Image)
                .Select(s => (s, Math.Min(1, s.Duration / 2))));
        }
        else if (_session.Map.ToSource(_cursor.ProgrammeTime) is { } point
                 && Project.SourceOf(point.Source) is { } source)
        {
            sources.Add((source, point.Time));
        }

        if (sources.Count == 0)
        {
            Announce(wholeProject ? "no media to measure" : "nothing measurable under the cursor",
                urgent: true);
            return;
        }

        Announce($"measuring {sources.Count} source{(sources.Count == 1 ? "" : "s")}", urgent: true);

        _ = Task.Run(async () =>
        {
            var analyser = new QualityAnalyser();
            var reports = new List<QualityReport>();

            foreach (var (source, at) in sources)
            {
                var path = System.IO.Path.IsPathRooted(source.Path) || Project.RootPath is null
                    ? source.Path
                    : System.IO.Path.Combine(Project.RootPath, source.Path);

                if (!System.IO.File.Exists(path)) continue;

                try
                {
                    reports.Add(await analyser.AnalyseAsync(path, at).ConfigureAwait(false));
                }
                catch (Exception)
                {
                    // A source that cannot be measured is reported as missing
                    // rather than taking the whole pass down.
                }
            }

            var message = reports.Count == 0
                ? "nothing could be measured; the files may be missing"
                : wholeProject && reports.Count > 1
                    ? QualityAnalyser.CompareShots(reports)
                    : reports[0].Announce();

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                Announce(message, urgent: true);
                return false;
            });
        });
    }

    private void Render(RenderQuality quality)
    {
        if (_rendering)
        {
            Announce("a render is already running", urgent: true);
            return;
        }

        if (Project.RootPath is null)
        {
            Announce("save the project first; a render needs somewhere to put its files", urgent: true);
            return;
        }

        _rendering = true;
        Announce(quality == RenderQuality.Draft ? "rendering draft" : "rendering master", urgent: true);

        var lastSpoken = -1;

        var progress = new Progress<RenderProgress>(report =>
        {
            // Every ten percent, not every tick: a render that talks constantly
            // is one you cannot work through.
            var decile = (int)(report.Fraction * 10);
            if (decile == lastSpoken || decile == 0) return;

            lastSpoken = decile;
            Announce($"{decile * 10} percent", urgent: false);
        });

        _ = Task.Run(async () =>
        {
            string message;

            try
            {
                var output = await new FfmpegRenderEngine()
                    .RenderAsync(Project, quality, progress).ConfigureAwait(false);

                message = $"rendered {Timecode.Speak(output.Duration)} to "
                          + $"{System.IO.Path.GetFileName(output.Path)}"
                          + (quality == RenderQuality.Master ? ", with captions" : string.Empty);
            }
            catch (Exception exception)
            {
                message = $"render failed. {exception.Message}";
            }

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                _rendering = false;
                Announce(message, urgent: true);
                return false;
            });
        });
    }

    private void DetachAudio()
    {
        if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element.Id is not { } id)
        {
            Announce("nothing under the cursor", urgent: true);
            return;
        }

        if (Project.Element(id) is { Muted: true })
        {
            var reattached = _session.Apply("reattach audio", (project, _) =>
                EditOperations.ReattachAudio(project, id));

            Refresh();
            Announce(reattached.Announce(), urgent: true);
            return;
        }

        var audioTracks = Project.InOrder.Where(t => t.Media == TrackMedia.Audio).ToList();

        if (audioTracks.Count == 0)
        {
            Announce("no audio track to detach onto. Control T makes one", urgent: true);
            return;
        }

        if (audioTracks.Count == 1)
        {
            Apply("detach audio", p => EditOperations.DetachAudio(p, id, audioTracks[0].Id));
            return;
        }

        ChooseFromList(
            "Detach onto which track",
            audioTracks.Select(t => t.Name).ToList(),
            choice => Apply("detach audio", p => EditOperations.DetachAudio(p, id, audioTracks[choice].Id)));
    }

    private void ImportMedia()
    {
        var dialog = Gtk_.FileChooserNative.New(
            "Import media", _window, Gtk_.FileChooserAction.Open, "Import", "Cancel");

        var filter = Gtk_.FileFilter.New();
        filter.Name = "Video, audio and images";

        foreach (var extension in MediaImporter.SupportedExtensions)
        {
            filter.AddPattern($"*{extension}");
            filter.AddPattern($"*{extension.ToUpperInvariant()}");
        }

        dialog.AddFilter(filter);

        dialog.OnResponse += (chooser, args) =>
        {
            if (args.ResponseId != (int)Gtk_.ResponseType.Accept) return;

            var path = dialog.GetFile()?.GetPath();
            if (path is null) return;

            ImportAsync(path).ConfigureAwait(true);
        };

        // Keep it alive until the response arrives; a native dialog collected
        // early simply never answers.
        _openDialog = dialog;
        dialog.Show();
    }

    private async Task ImportAsync(string path)
    {
        Announce($"importing {System.IO.Path.GetFileName(path)}", urgent: true);

        var result = await new MediaImporter().ImportAsync(Project, path).ConfigureAwait(true);

        RebuildMediaRows();
        Refresh();

        Announce(result.Succeeded
            ? $"imported. {result.Summary}"
            : $"could not import. {result.Summary}", urgent: true);
    }

    private void RebuildMediaRows()
    {
        while (_mediaList.GetRowAtIndex(0) is { } row) _mediaList.Remove(row);

        if (Project.Sources.Count == 0)
        {
            _mediaList.Append(Row("Media bin empty. Control I to import."));
            return;
        }

        foreach (var source in Project.Sources)
        {
            _mediaList.Append(Row(MediaImporter.Describe(source)));
        }
    }

    /// <summary>
    /// How long a still, card, hole or pause is held. A photograph has no
    /// duration of its own, so it can stay up for as long as you like.
    /// </summary>
    private void SetDuration()
    {
        ChooseFromList(
            "How long on screen",
            ["2 seconds", "3 seconds", "4 seconds", "6 seconds", "10 seconds",
             "One second longer", "One second shorter"],
            index =>
            {
                var result = _session.Apply("duration", (project, _) => index switch
                {
                    0 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 2),
                    1 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 3),
                    2 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 4),
                    3 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 6),
                    4 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 10),
                    5 => EditOperations.AdjustDuration(project, _cursor.ProgrammeTime, 1),
                    _ => EditOperations.AdjustDuration(project, _cursor.ProgrammeTime, -1),
                });

                Refresh();
                Announce(result.Announce(), urgent: true);
            });
    }

    /// <summary>
    /// Reads back what is actually in the frame under the cursor - the part of
    /// editing that genuinely needs eyes, done by something that has them.
    /// </summary>
    private void DescribeFrame()
    {
        if (_session.Map.ToSource(_cursor.ProgrammeTime) is not { } point
            || Project.SourceOf(point.Source) is not { } source)
        {
            Announce("nothing to describe under the cursor", urgent: true);
            return;
        }

        var describer = new FrameDescriber();

        if (!describer.IsAvailable)
        {
            Announce("the claude command is not installed, so frames cannot be described", urgent: true);
            return;
        }

        var path = System.IO.Path.IsPathRooted(source.Path) || Project.RootPath is null
            ? source.Path
            : System.IO.Path.Combine(Project.RootPath, source.Path);

        if (!System.IO.File.Exists(path))
        {
            Announce($"{System.IO.Path.GetFileName(source.Path)} is not on disk", urgent: true);
            return;
        }

        Announce("looking at the frame", urgent: true);

        var at = point.Time;

        _ = Task.Run(async () =>
        {
            var frame = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"videoedit-frame-{Guid.NewGuid():N}.jpg");

            string message;

            try
            {
                message = await describer.ExtractFrameAsync(path, at, frame).ConfigureAwait(false) is null
                    ? "could not take a frame from that point"
                    : await describer.DescribeAsync(frame).ConfigureAwait(false);
            }
            finally
            {
                if (System.IO.File.Exists(frame)) System.IO.File.Delete(frame);
            }

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                Announce(message, urgent: true);
                return false;
            });
        });
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

        // The signal probe would open the camera, so it is deferred to the
        // moment recording actually starts rather than happening because a key
        // was pressed.
        Announce(track.CaptureDeviceName is { Length: > 0 } device
            ? $"{track.Name} armed, input {device}. Recording is not wired yet"
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

    // ---- movement --------------------------------------------------------

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

    // ---- state -----------------------------------------------------------

    private TimeSelection Selection() =>
        _cursor.Selection ?? new TimeSelection(_cursor.ProgrammeTime, _cursor.ProgrammeTime + 1);

    private void Apply(string label, Func<Project, EditResult> operation)
    {
        var result = _session.Apply(label, (project, _) => operation(project));
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

    /// <summary>
    /// Transcript verbs. Every one takes a modifier: unmodified keys are
    /// typing, and plain Delete has to stay character deletion or the pane
    /// stops being a text editor.
    /// </summary>
    private bool OnTranscriptKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);
        var alt = args.State.HasFlag(Gdk.ModifierType.AltMask);

        var element = CaretSegment();

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_K or Gdk.Constants.KEY_k when control && shift:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("delete segment", p => EditOperations.DeleteSegment(p, element.Value));
                return true;

            case Gdk.Constants.KEY_E or Gdk.Constants.KEY_e when control && shift:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("cut", p => EditOperations.ToggleDisableSegment(p, element.Value));
                return true;

            case Gdk.Constants.KEY_Up when alt:
            case Gdk.Constants.KEY_Down when alt:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("move",
                    p => EditOperations.MoveSegment(p, element.Value,
                        args.Keyval == Gdk.Constants.KEY_Up ? -1 : 1));
                return true;

            case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter when control:
            {
                CommitCaption();
                var at = _transcriptDocument.LocationAt(_transcript.GetBuffer().CursorPosition).ProgrammeTime;

                if (at is null)
                {
                    Announce("cannot split here, this line is not in the programme", urgent: true);
                    return true;
                }

                ApplyToTranscript("split", p => EditOperations.SplitAt(p, at.Value));
                return true;
            }

            case Gdk.Constants.KEY_C or Gdk.Constants.KEY_c when control && shift:
                if (element is null) return true;
                ApplyToTranscript("caption", p => EditOperations.ToggleCaption(p, element.Value));
                return true;

            case Gdk.Constants.KEY_semicolon when control:
                Announce(_transcriptDocument.AnnounceLine(_transcript.GetBuffer().CursorPosition), urgent: true);
                return true;

            default:
                // The first ordinary keystroke in a line says what typing does,
                // because it surprises people who expect it to change the cut.
                if (!_captionRuleAnnounced && !control && !alt && args.Keyval > 0x20 && args.Keyval < 0xFF00)
                {
                    _captionRuleAnnounced = true;
                    Announce("editing caption text, not the cut", urgent: true);
                }

                return false;
        }
    }

    private ElementId? CaretSegment()
    {
        var index = _transcriptDocument.LineAt(_transcript.GetBuffer().CursorPosition);
        return index < 0 ? null : _transcriptDocument.Segments[index].Element;
    }

    /// <summary>
    /// Applies a structural edit and rebuilds the buffer, keeping the caret on
    /// a sensible line rather than dumping it at the top.
    /// </summary>
    private void ApplyToTranscript(string label, Func<Project, EditResult> operation)
    {
        var line = _transcriptDocument.LineAt(_transcript.GetBuffer().CursorPosition);

        var result = _session.Apply(label, (project, _) => operation(project));

        _suppressTranscriptCommit = true;
        Refresh();
        _suppressTranscriptCommit = false;

        PlaceCaretOnLine(Math.Min(line, _transcriptDocument.Segments.Count - 1));
        _lastAnnouncedLine = -1;

        Announce(result.Announce(), urgent: true);
    }

    private void PlaceCaretOnLine(int line)
    {
        if (line < 0 || line >= _transcriptDocument.Segments.Count) return;

        var buffer = _transcript.GetBuffer();
        buffer.GetIterAtOffset(out var iter, _transcriptDocument.Segments[line].CharStart);
        buffer.PlaceCursor(iter);
        _editingLine = line;
    }

    /// <summary>
    /// Writes the line the caret is leaving back as that segment's caption.
    /// Deferred rather than per-keystroke, so the buffer is never rebuilt
    /// underneath a half-typed word.
    /// </summary>
    private void CommitCaption()
    {
        if (!_transcriptDirty || _suppressTranscriptCommit || _editingLine < 0) return;

        _transcriptDirty = false;

        if (_editingLine >= _transcriptDocument.Segments.Count) return;

        var segment = _transcriptDocument.Segments[_editingLine];
        var text = CurrentLineText(_editingLine);

        // Bracketed lines are not speech; their text is generated, so an edit
        // to one is discarded rather than becoming a nonsense caption.
        if (segment.Span is null)
        {
            _suppressTranscriptCommit = true;
            Refresh();
            _suppressTranscriptCommit = false;
            Announce("that line is not editable text", urgent: true);
            return;
        }

        var result = _session.Apply("caption", (project, _) =>
            EditOperations.SetCaption(project, segment.Element, text));

        if (result.Changed) Announce(result.Announce(), urgent: false);
    }

    private string CurrentLineText(int line)
    {
        var buffer = _transcript.GetBuffer();
        var lines = (buffer.Text ?? string.Empty).Split('\n');
        return line >= 0 && line < lines.Length ? lines[line] : string.Empty;
    }

    private void AnnounceTranscriptLine()
    {
        var offset = _transcript.GetBuffer().CursorPosition;
        var line = _transcriptDocument.LineAt(offset);

        if (line == _lastAnnouncedLine) return;

        CommitCaption();

        _lastAnnouncedLine = line;
        _editingLine = line;
        Announce(_transcriptDocument.AnnounceLine(offset), urgent: false);
    }

    /// <summary>
    /// While playing, the cursor follows the player and segments announce
    /// themselves as they pass. Stopped, this does nothing at all.
    /// </summary>
    private bool OnPlaybackTick()
    {
        if (!_player.IsPlaying) return true;

        var position = _player.Position;

        if (position > 0)
        {
            _followingPlayback = true;
            _cursor.MoveTo(position, CursorMoveCause.Playback);
            _followingPlayback = false;

            UpdateStatusLine();
            RefreshLanes();

            if (_playbackAnnouncer.Tick(Project, _session.Map, position) is { } said)
            {
                Announce(said, urgent: false);
            }
        }

        // Stop at the end rather than sitting paused past it.
        if (_player.ReachedEnd || position >= _session.Map.Duration - 0.05)
        {
            _player.Pause();
            Announce("end of programme", urgent: true);
        }

        return true;
    }

    private void TogglePlay()
    {
        if (!_player.IsAvailable)
        {
            Announce("playback is unavailable: libmpv could not be loaded", urgent: true);
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
            Announce($"paused at {Timecode.FormatShort(_cursor.ProgrammeTime)}", urgent: true);
            return;
        }

        if (!EnsureLoaded()) return;

        _playbackAnnouncer.Reset();

        var started = _player.Play(_cursor.ProgrammeTime);

        if (started is null)
        {
            Announce("nothing to play from here", urgent: true);
            return;
        }

        // Cards, holes and pauses have nothing to play, so preview skips them.
        // Say so rather than appearing to start somewhere else for no reason.
        Announce(started.Value > _cursor.ProgrammeTime + 0.05
            ? $"skipping to {Timecode.FormatShort(started.Value)}, nothing to preview before it"
            : "playing", urgent: true);
    }

    /// <summary>
    /// Points the player at the current cut. Reports missing media rather than
    /// producing silence, which is indistinguishable from a broken player.
    /// </summary>
    private bool EnsureLoaded()
    {
        var missing = Project.Sources
            .Where(source => !System.IO.File.Exists(ResolvePath(source.Path)))
            .Select(source => System.IO.Path.GetFileName(source.Path))
            .ToList();

        if (missing.Count > 0)
        {
            Announce($"cannot play: {string.Join(", ", missing)} not found on disk", urgent: true);
            return false;
        }

        _player.SetOutput(Project.Settings.MonitorOutputId);
        _player.Load(MpvEdl.Build(Project, _session.Map));
        return true;
    }

    private string ResolvePath(string path) =>
        System.IO.Path.IsPathRooted(path) || Project.RootPath is null
            ? path
            : System.IO.Path.Combine(Project.RootPath, path);

    private void Shuttle(double rate)
    {
        if (!_player.IsAvailable || !EnsureLoaded()) return;

        if (rate == 0)
        {
            _player.Pause();
            Announce("stopped", urgent: true);
            return;
        }

        _playbackAnnouncer.Reset();
        _player.SetRate(rate);
        Announce(rate == 1 ? "playing" : $"{rate:0.##} times speed", urgent: true);
    }

    private void Audition()
    {
        if (!_player.IsAvailable || !EnsureLoaded()) return;

        var from = Math.Max(0, _cursor.ProgrammeTime - 1.5);
        var to = Math.Min(_session.Map.Duration, _cursor.ProgrammeTime + 1.5);

        Announce("auditioning", urgent: true);
        _ = _player.PlayRangeAsync(from, to);
    }

    /// <summary>
    /// A blip of the real audio wherever the cursor lands. This is what makes
    /// the timeline navigable by ear - a timestamp tells you where you are, the
    /// audio tells you what is there.
    /// </summary>
    private void ScrubHere()
    {
        // A click at every segment boundary, so holding an arrow key down lets
        // you hear the shape of the edit going past.
        if (Project.Settings.Earcons
            && _cursor.FocusedTrack is { } focused
            && TrackProbe.Segments(Project, _session.Map, focused)
                .Any(seg => Math.Abs(seg.Start - _cursor.ProgrammeTime) < 0.02))
        {
            _announcer.Earcon(Earcon.Boundary);
        }

        if (!Project.Settings.AudioScrub || _player.IsPlaying || _followingPlayback) return;
        if (!_player.IsAvailable) return;

        if (Project.Sources.Any(source => !System.IO.File.Exists(ResolvePath(source.Path)))) return;

        _player.Load(MpvEdl.Build(Project, _session.Map));
        _player.Scrub(_cursor.ProgrammeTime, Project.Settings.AudioScrubLength);
    }

    private void UpdateStatusLine() =>
        _readout.SetText(Workspace.StatusLine(
            _cursor.ProgrammeTime,
            _session.Map.Duration,
            _cursor.Granularity.Describe(),
            Project.TrackOf(_cursor.FocusedTrack ?? default)?.Name));

    private void SyncTranscriptToCursor()
    {
        var buffer = _transcript.GetBuffer();
        var offset = Math.Clamp(
            _transcriptDocument.OffsetAt(_cursor.ProgrammeTime),
            0,
            Math.Max(0, (buffer.Text?.Length ?? 1) - 1));

        buffer.GetIterAtOffset(out var iter, offset);
        buffer.PlaceCursor(iter);
    }

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

        if (track.Kind == TrackKind.Programme && here != _lastSpokenSegment)
        {
            _lastSpokenSegment = here;

            if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element is SpanElement span
                && span.Text.Length > 0)
            {
                return $"{spoken}. {span.Text}";
            }
        }

        return spoken;
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
