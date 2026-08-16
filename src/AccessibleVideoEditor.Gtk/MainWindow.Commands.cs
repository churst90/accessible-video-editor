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
/// Where every command is given its implementation.
///
/// One registration per command, reached by the key, the menu item and the
/// palette alike - which is what stops a menu entry doing something subtly
/// different from the key that shares its name. A registry entry with no
/// handler here is a key that lies, and a test reads these sources as text
/// to make sure there are none.
/// </summary>
public sealed partial class MainWindow
{
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
        Action("selectSegment", () => Select(Selections.Segment(Project, _session.Map, _cursor)));
        Action("selectTrack", () => Select(Selections.Track(Project, _session.Map, _cursor)));

        // Modes rather than edits, so they change the settings directly and
        // stay off the undo stack - see EditModes.
        Action("snap", () =>
        {
            Announce(EditModes.ToggleSnap(Project.Settings), urgent: true);
            _dirty = true;
        });

        Action("rippleMode", () =>
        {
            Announce(EditModes.CycleRipple(Project.Settings), urgent: true);
            _dirty = true;
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

        Action("preferences", ShowPreferences);

        RegisterStreamActions();
        RegisterImageActions();
        RegisterShotActions();
        RegisterTransitionActions();
        RegisterFileActions();
        RegisterReviewActions();
        RegisterLibraryActions();
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
}
