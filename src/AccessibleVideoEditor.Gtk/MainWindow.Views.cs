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
/// Building the five views, and moving between them.
///
/// Every view is a native list or text view rather than something drawn,
/// so the accessibility tree is real: the rows Orca reads are the rows the
/// application has, not a description of them. The one drawn thing - the
/// timeline lanes - takes no focus and answers no keys, and the header list
/// beside it stays the thing you interact with.
/// </summary>
public sealed partial class MainWindow
{
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

    // ---- focus -------------------------------------------------------------

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

    // ---- keys --------------------------------------------------------------
}
