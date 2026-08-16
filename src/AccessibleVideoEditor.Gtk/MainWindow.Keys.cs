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
/// Every key, in one place.
///
/// The window handler runs first and owns the keys that mean the same
/// thing everywhere - the function keys, the view numbers, the context
/// menu. Everything else is dispatched to the focused view, which is why
/// a plain letter can be a track control in one place and typing in
/// another without either having to know about the other.
///
/// The view-specific choosers live here too, beside the key that opens
/// them, rather than in the file that builds the view they belong to.
/// </summary>
public sealed partial class MainWindow
{
    private bool OnWindowKeyPressed(Gtk_.EventControllerKey sender, Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var alt = args.State.HasFlag(Gdk.ModifierType.AltMask);

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

            case Gdk.Constants.KEY_F2 when control:
                Run("renderPresets");
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
            //
            // Bare R only. This ran on any modified R too, which meant Ctrl+Alt+R
            // and friends silently started a take: it opens the camera and begins
            // recording, and there is nothing to see that it happened.
            case Gdk.Constants.KEY_F5 when shift:
            case Gdk.Constants.KEY_R or Gdk.Constants.KEY_r
                when !control && !alt && _workspace.Focused is Pane.Tracks or Pane.Timeline:
                Run("record");
                return true;

            case Gdk.Constants.KEY_F3:
                Run("find");
                return true;

            case Gdk.Constants.KEY_F4 when control:
                Run("audioAdvise");
                return true;

            case Gdk.Constants.KEY_F4:
                Run(shift ? "qualityAll" : "quality");
                return true;

            case Gdk.Constants.KEY_F7:
                Run("issues");
                return true;

            case Gdk.Constants.KEY_F8 when control && alt:
                Run("shotDetail");
                return true;

            case Gdk.Constants.KEY_F8 when control:
                Run("describeShots");
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

            // ---- size --------------------------------------------------------------

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

            // ---- crop --------------------------------------------------------------

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C when shift:
                ChooseCropRatio();
                return true;

            case Gdk.Constants.KEY_c or Gdk.Constants.KEY_C:
                _images.Apply("cropping to the picture", ImageEdits.CropToContent);
                return true;

            case Gdk.Constants.KEY_r or Gdk.Constants.KEY_R when shift:
                _images.Apply("resetting the crop", ImageEdits.ResetCrop);
                return true;

            // ---- straightening -----------------------------------------------------

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

            // ---- drawing -----------------------------------------------------------

            case Gdk.Constants.KEY_d or Gdk.Constants.KEY_D when shift:
                Prompt("Draw", string.Empty, "Draw", sentence => _images.AddShape(sentence));
                return true;

            case Gdk.Constants.KEY_Delete or Gdk.Constants.KEY_BackSpace:
                _images.RemoveShape();
                return true;

            case Gdk.Constants.KEY_k or Gdk.Constants.KEY_K:
                _images.DescribeColours();
                return true;

            // ---- looking at it -----------------------------------------------------

            case Gdk.Constants.KEY_F8:
                _images.Describe();
                return true;

            case Gdk.Constants.KEY_u or Gdk.Constants.KEY_U:
                _images.DescribeHistory();
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G:
                _images.ToggleSweep();
                return true;

            // ---- colour ------------------------------------------------------------

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

            // ---- out ---------------------------------------------------------------

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

            // ---- chat, and what may be done to it ----------------------------------

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

            // ---- music -------------------------------------------------------------

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

            // ---- how it is going ---------------------------------------------------

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
            case Gdk.Constants.KEY_m or Gdk.Constants.KEY_M when control:
                Run("markerList");
                return true;

            case Gdk.Constants.KEY_m or Gdk.Constants.KEY_M when shift:
                Run("removeMarker");
                return true;

            case Gdk.Constants.KEY_m or Gdk.Constants.KEY_M when !control && !shift:
                Run("marker");
                return true;

            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L when control && shift:
                Run("title");
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G when control && !shift:
                Run("graphic");
                return true;

            case Gdk.Constants.KEY_b or Gdk.Constants.KEY_B when control:
                Run("broll");
                return true;

            case Gdk.Constants.KEY_d or Gdk.Constants.KEY_D when control && alt:
                Run("describeEdit");
                return true;

            case Gdk.Constants.KEY_f or Gdk.Constants.KEY_F when control:
                Run("find");
                return true;

            case Gdk.Constants.KEY_x or Gdk.Constants.KEY_X when !control && !shift:
                ChooseTransition();
                return true;

            case Gdk.Constants.KEY_x or Gdk.Constants.KEY_X when shift:
                AuditionTransition();
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G when shift:
                SetTrackVolume();
                return true;

            // Before the plain Control case, which does not look at Shift and
            // would otherwise swallow this.
            case Gdk.Constants.KEY_Left or Gdk.Constants.KEY_Right when control && shift:
                JumpShot(forward: args.Keyval == Gdk.Constants.KEY_Right);
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

            // Select by naming the range rather than marking both ends of it.
            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when control && shift:
                Run("selectTrack");
                return true;

            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when control:
                Run("selectSegment");
                return true;

            case Gdk.Constants.KEY_n when !control && !alt && !shift:
                Run("snap");
                return true;

            case Gdk.Constants.KEY_r or Gdk.Constants.KEY_R when control && alt:
                Run("rippleMode");
                return true;

            // Groups, effects and volume shapes.
            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G when control && shift:
                Run("group");
                return true;

            case Gdk.Constants.KEY_g or Gdk.Constants.KEY_G when control && alt:
                Run("groupList");
                return true;

            case Gdk.Constants.KEY_e or Gdk.Constants.KEY_E when control && alt:
                Run("audioEffects");
                return true;

            case Gdk.Constants.KEY_a or Gdk.Constants.KEY_A when control && alt:
                Run("audioAutomation");
                return true;

            // A digit cuts to that camera angle - the same gesture as a digit
            // cutting to a scene while streaming, so it is learnt once.
            case >= Gdk.Constants.KEY_1 and <= Gdk.Constants.KEY_9 when !control && !alt:
                SwitchAngle((int)(args.Keyval - Gdk.Constants.KEY_1) + 1);
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

            // Plain letters are safe here: nothing in the bin is a text field.
            case Gdk.Constants.KEY_U:
                Run("subclipList");
                return true;

            case Gdk.Constants.KEY_u:
                Run("subclipCreate");
                return true;

            case Gdk.Constants.KEY_M:
                Run("multicamSync");
                return true;

            case Gdk.Constants.KEY_m:
                Run("multicamCreate");
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

    // ---- actions -----------------------------------------------------------
}
