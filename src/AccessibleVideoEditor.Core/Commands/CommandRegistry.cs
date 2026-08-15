namespace AccessibleVideoEditor.Core.Commands;

/// <summary>
/// Every action the application can perform, with its default binding and
/// <b>where that binding comes from</b>.
/// </summary>
public static class CommandRegistry
{
    public static IReadOnlyList<CommandDefinition> All { get; } =
    [
        // ---- file --------------------------------------------------------------
        new("file.recent", "Recent projects", "Ctrl+Shift+O", CommandGroup.File, KeyOrigin.Universal),
        new("file.new", "New project", "Ctrl+N", CommandGroup.File, KeyOrigin.Universal),
        new("file.open", "Open project", "Ctrl+O", CommandGroup.File, KeyOrigin.Universal),
        new("file.save", "Save", "Ctrl+S", CommandGroup.File, KeyOrigin.Universal,
            "Writes project.json and exports edit.md alongside it."),
        new("file.saveAs", "Save as", "Ctrl+Shift+S", CommandGroup.File, KeyOrigin.Universal),
        new("file.importMedia", "Import video or audio", "Ctrl+I", CommandGroup.File, KeyOrigin.Premiere,
            "Premiere uses Ctrl+I for Import."),
        new("file.reloadEdl", "Reload edit.md", "Ctrl+Shift+I", CommandGroup.File, KeyOrigin.Invented,
            "Reconciles a hand edit made in pluma, keeping element IDs intact."),
        new("file.exit", "Exit", "Ctrl+Q", CommandGroup.File, KeyOrigin.Universal),

        // ---- application -------------------------------------------------------
        new("view.byNumber", "Go to a view", "Ctrl+1 to Ctrl+6", CommandGroup.Application, KeyOrigin.CodeEditor,
            "Timeline, tracks, transcript, media bin, stream, images."),
        new("cursor.nextEdit", "Next edit point, any track", "Tab", CommandGroup.Navigation, KeyOrigin.Reaper,
            "Reaper's Tab moves to the next transient. The video equivalent is "
            + "the next thing that happens anywhere in the project.",
            Context: CommandContext.Timeline),
        new("cursor.previousEdit", "Previous edit point, any track", "Shift+Tab", CommandGroup.Navigation,
            KeyOrigin.Reaper, Context: CommandContext.Timeline),
        new("palette.open", "Command palette", "Ctrl+Shift+P", CommandGroup.Application, KeyOrigin.CodeEditor,
            "From VS Code, not from any NLE. Nothing in video editing has a palette; it should."),
        new("menu.context", "Context menu", "Applications", CommandGroup.Application, KeyOrigin.Windows,
            "Also Shift+F10. Contents depend on what is focused.", Alternate: "Shift+F10"),
        new("speech.verbosity", "Cycle verbosity", "Ctrl+Alt+V", CommandGroup.Application, KeyOrigin.Invented),

        // ---- function keys -----------------------------------------------------
        // One domain per key, stacked: plain does the common thing, Shift the
        // variant, Ctrl the setup. Guessable rather than memorised.
        new("help.context", "What can I do here", "F1", CommandGroup.Application, KeyOrigin.Universal,
            "The commands valid in this view."),
        new("help.keymap", "Read the whole keymap", "Shift+F1", CommandGroup.Application, KeyOrigin.Invented,
            "Everything, grouped. F1 lists only the view you are in."),
        new("help.about", "About this application", "Ctrl+F1", CommandGroup.Application, KeyOrigin.Windows,
            "Version, credits, and how to support the work."),

        new("render.master", "Render master", "F2", CommandGroup.Output, KeyOrigin.Invented,
            "1080p plus captions. Blocked while holes remain.", Alternate: "Ctrl+M"),
        new("render.draft", "Render draft", "Shift+F2", CommandGroup.Output, KeyOrigin.Invented,
            "540p, fast, for checking."),
        new("render.presets", "Export presets", "Ctrl+F2", CommandGroup.Output, KeyOrigin.Invented,
            "YouTube, vertical, square or audio only. Says what each one will crop before it runs."),

        new("find.next", "Find in transcript", "F3", CommandGroup.Navigation, KeyOrigin.Universal,
            "F3 is find-next everywhere."),
        new("find.previous", "Find previous", "Shift+F3", CommandGroup.Navigation, KeyOrigin.Universal),

        new("review.quality", "Picture and sound quality", "F4", CommandGroup.Review, KeyOrigin.Invented,
            "Exposure, white balance, sharpness, levels, clipping for this segment."),
        new("review.qualityAll", "Quality across the whole project", "Shift+F4", CommandGroup.Review,
            KeyOrigin.Invented,
            "Shot matching: which takes are darker, warmer or quieter than the rest."),

        new("track.arm", "Arm or disarm this track", "F5", CommandGroup.Tracks, KeyOrigin.Invented,
            "Binds the capture device and runs the signal check."),
        new("capture.record", "Start or stop recording", "Shift+F5", CommandGroup.Capture, KeyOrigin.Invented,
            "Records to the armed track.", Alternate: "R"),
        new("capture.output", "Choose the monitoring output", "Ctrl+Shift+F5", CommandGroup.Capture,
            KeyOrigin.Invented,
            "Where playback and scrub are heard. Separate from any input - you can "
            + "record from an interface while listening on headphones."),
        new("capture.channel", "Choose this track's input channel", "Ctrl+Alt+F5", CommandGroup.Capture,
            KeyOrigin.Invented,
            "Which input of a multi-input interface to record."),
        new("capture.device", "Choose this track's input", "Ctrl+F5", CommandGroup.Capture, KeyOrigin.Invented,
            "Cameras for a video track, microphones for an audio track. The input "
            + "is a property of the track, which is why there is no separate record view."),

        new("view.next", "Next view", "F6", CommandGroup.Application, KeyOrigin.Windows),
        new("view.previous", "Previous view", "Shift+F6", CommandGroup.Application, KeyOrigin.Windows),

        new("review.issues", "To-do list", "F7", CommandGroup.Review, KeyOrigin.Invented,
            "Holes, capture issues, and findings from the frame review."),

        new("review.describeFrame", "Describe this frame", "F8", CommandGroup.Review, KeyOrigin.Invented,
            "Renders the still under the cursor and reads back what is actually there."),
        new("review.describeEdit", "Read me the edit", "Shift+F8", CommandGroup.Review, KeyOrigin.Invented,
            "How long, what is on it, and what still needs doing. Ctrl+Alt+D does the "
            + "same thing from anywhere, because it is worth asking outside the timeline.",
            Context: CommandContext.Global, Alternate: "Ctrl+Alt+D"),

        new("capture.describeShot", "What is in shot", "F8 while the viewfinder is open",
            CommandGroup.Capture, KeyOrigin.Invented,
            "The talking viewfinder: what is actually in front of the camera, rather than "
            + "where you are in the frame. A different question, and the only one that needs eyes.",
            Context: CommandContext.Global),
        new("capture.viewfinder", "Accessible viewfinder", "F9", CommandGroup.Capture, KeyOrigin.Invented,
            "A mode, not a view: framing a shot is not editing, so you enter it and leave."),
        new("capture.monitor", "Monitor input levels", "Shift+F9", CommandGroup.Capture, KeyOrigin.Invented,
            "An audible VU meter: a tick whose pitch rises with the level, and the "
            + "zone name spoken as it changes. A mode, like the viewfinder."),

        new("cursor.where", "Where am I", "F12", CommandGroup.Navigation, KeyOrigin.Invented,
            "Full readout: view, track, segment, time, state.", Alternate: "Ctrl+Semicolon"),

        // ---- moving ------------------------------------------------------------
        new("cursor.left", "Move back one step", "Left", CommandGroup.Navigation, KeyOrigin.Universal,
            Context: CommandContext.Timeline),
        new("cursor.right", "Move forward one step", "Right", CommandGroup.Navigation, KeyOrigin.Universal,
            Context: CommandContext.Timeline),
        new("track.up", "Focus track above", "Up", CommandGroup.Navigation, KeyOrigin.DeviatesFromPremiere,
            "Premiere uses Up/Down for previous/next edit point. Tracks matter more "
            + "here: without a picture, moving between tracks at a fixed time is how "
            + "you read the vertical slice of the edit.", Context: CommandContext.Timeline),
        new("track.down", "Focus track below", "Down", CommandGroup.Navigation, KeyOrigin.DeviatesFromPremiere,
            Context: CommandContext.Timeline),
        new("cursor.previousSegment", "Previous segment", "Ctrl+Left", CommandGroup.Navigation, KeyOrigin.Invented,
            "Premiere's Up/Down, moved here because Up/Down changes track. Segment starts only."),
        new("cursor.nextSegment", "Next segment", "Ctrl+Right", CommandGroup.Navigation, KeyOrigin.Invented),
        new("cursor.segmentStart", "Jump to segment start", "Shift+Comma", CommandGroup.Navigation, KeyOrigin.Invented,
            "Start of the clip, card or image under the cursor on this track. "
            + "Press again to walk back through the track's segments."),
        new("cursor.segmentEnd", "Jump to segment end", "Shift+Period", CommandGroup.Navigation, KeyOrigin.Invented),
        new("granularity.coarser", "Zoom out", "Minus", CommandGroup.Navigation, KeyOrigin.Premiere,
            "Premiere's timeline zoom. Zoom and step size are the same control here: "
            + "zooming out makes each arrow press cover more time.", Alternate: "Ctrl+Up"),
        new("granularity.finer", "Zoom in", "Equals", CommandGroup.Navigation, KeyOrigin.Premiere,
            Alternate: "Ctrl+Down"),
        new("cursor.start", "Go to start", "Home", CommandGroup.Navigation, KeyOrigin.Universal),
        new("cursor.end", "Go to end", "End", CommandGroup.Navigation, KeyOrigin.Universal),

        // ---- playback ----------------------------------------------------------
        new("play.toggle", "Play or pause", "Space", CommandGroup.Playback, KeyOrigin.Universal),
        new("play.rewind", "Shuttle back", "J", CommandGroup.Playback, KeyOrigin.UniversalNle,
            "J K L is the shuttle on every editor and every tape deck before them.",
            Context: CommandContext.Editing),
        new("play.stop", "Stop", "K", CommandGroup.Playback, KeyOrigin.UniversalNle,
            Context: CommandContext.Editing),
        new("play.forward", "Shuttle forward", "L", CommandGroup.Playback, KeyOrigin.UniversalNle,
            Context: CommandContext.Editing),
        new("play.audition", "Audition around the cursor", "Ctrl+Space", CommandGroup.Playback, KeyOrigin.Invented,
            "Plays a second and a half either side. How you check a cut or a transition."),
        new("play.loopSelection", "Loop the selection", "Shift+Space", CommandGroup.Playback, KeyOrigin.Reaper),

        // ---- selection ---------------------------------------------------------
        new("select.in", "Mark in", "I", CommandGroup.Selection, KeyOrigin.UniversalNle,
            Alternate: "BracketLeft"),
        new("select.out", "Mark out", "O", CommandGroup.Selection, KeyOrigin.UniversalNle,
            Alternate: "BracketRight"),
        new("select.clear", "Clear selection", "Escape", CommandGroup.Selection, KeyOrigin.Universal),
        new("select.speak", "Speak the selection", "Ctrl+Shift+Semicolon", CommandGroup.Selection, KeyOrigin.Invented),
        new("select.segment", "Select the segment under the cursor", "Ctrl+A", CommandGroup.Selection, KeyOrigin.Universal),
        new("select.track", "Select everything on this track", "Ctrl+Shift+A", CommandGroup.Selection, KeyOrigin.Invented),

        // ---- assembling --------------------------------------------------------
        new("edit.insert", "Insert at cursor", "Comma", CommandGroup.Editing, KeyOrigin.Premiere,
            "Ripples the source selection in at the cursor. Premiere's Insert."),
        new("edit.overwrite", "Overwrite at cursor", "Period", CommandGroup.Editing, KeyOrigin.Premiere,
            "Replaces what is there without changing timing. Premiere's Overwrite."),

        // ---- editing -----------------------------------------------------------
        new("edit.split", "Split at cursor", "S", CommandGroup.Editing, KeyOrigin.Reaper,
            "Reaper's split. Premiere's Ctrl+K also works.",
            Alternate: "Ctrl+K", Context: CommandContext.Timeline),
        new("edit.splitAll", "Split every track at cursor", "Shift+S", CommandGroup.Editing, KeyOrigin.Reaper,
            Context: CommandContext.Timeline),
        new("edit.heal", "Heal a split", "Ctrl+J", CommandGroup.Editing, KeyOrigin.Reaper,
            "Reaper calls this healing; the key is ours - J for join, leaving Ctrl+H "
            + "as the mnemonic for hole. Rejoins two halves of one shot.",
            Context: CommandContext.Editing),
        new("edit.rippleDelete", "Ripple delete", "Delete", CommandGroup.Editing, KeyOrigin.DeviatesFromPremiere,
            Context: CommandContext.Timeline,
            Description:
            "Premiere has these the other way round: Delete lifts and Shift+Delete "
            + "ripples. Inverted here because a transcript-driven edit ripples by "
            + "default - deleting a sentence should close the gap."),
        new("edit.lift", "Lift", "Shift+Delete", CommandGroup.Editing, KeyOrigin.DeviatesFromPremiere,
            "Leaves silence of the same length so downstream timing survives.",
            Context: CommandContext.Timeline),
        new("edit.disable", "Enable or disable segment", "Shift+E", CommandGroup.Editing, KeyOrigin.Premiere,
            "Premiere's Enable Clip. Non-destructive - it stays in the document."),
        new("edit.mute", "Mute or unmute segment", "Ctrl+Shift+M", CommandGroup.Editing, KeyOrigin.Invented,
            "Silences the segment but keeps its picture."),
        new("edit.copy", "Copy", "Ctrl+C", CommandGroup.Editing, KeyOrigin.Universal),
        new("edit.cut", "Cut", "Ctrl+X", CommandGroup.Editing, KeyOrigin.Universal),
        new("edit.paste", "Paste at cursor", "Ctrl+V", CommandGroup.Editing, KeyOrigin.Universal,
            "Refused out loud if the clipboard does not match the track's medium."),
        new("edit.insertHole", "Insert a hole", "Ctrl+H", CommandGroup.Editing, KeyOrigin.Invented,
            "Reserved space with a note. Blocks the master render until filled.",
            Context: CommandContext.Editing),
        // Nudge and move-to-track are designed but unbuilt, so they are not
        // listed: EditOperations has no verb for either, and moving a spine
        // element to another track means turning it into an overlay item, which
        // is a decision rather than a keystroke. Alt+Left/Right and Alt+Up/Down
        // are held for them. See ROADMAP.md, "Ongoing".
        new("edit.trimHead", "Trim head to cursor", "Alt+BracketLeft", CommandGroup.Editing, KeyOrigin.Invented),
        new("edit.trimTail", "Trim tail to cursor", "Alt+BracketRight", CommandGroup.Editing, KeyOrigin.Invented),
        new("transcript.delete", "Delete this segment", "Ctrl+Shift+K", CommandGroup.Editing, KeyOrigin.CodeEditor,
            "VS Code's delete-line. Plain Delete has to stay character deletion here, "
            + "because the transcript is a real text field.",
            Context: CommandContext.Transcript),
        new("transcript.disable", "Cut or restore this segment", "Ctrl+Shift+E", CommandGroup.Editing,
            KeyOrigin.Invented,
            "The line stays, marked cut, and can be restored.",
            Context: CommandContext.Transcript),
        new("transcript.split", "Split at the caret", "Ctrl+Return", CommandGroup.Editing, KeyOrigin.Invented,
            "Splits the segment at the word the caret is on.",
            Context: CommandContext.Transcript),
        new("edit.reorderUp", "Move this line earlier", "Alt+Up", CommandGroup.Editing, KeyOrigin.CodeEditor,
            "Reordering a line reorders the video. The move-line binding from every "
            + "code editor.", Context: CommandContext.Transcript),
        new("edit.reorderDown", "Move this line later", "Alt+Down", CommandGroup.Editing, KeyOrigin.CodeEditor,
            Context: CommandContext.Transcript),
        new("edit.speed", "Set speed", "Ctrl+R", CommandGroup.Editing, KeyOrigin.Premiere,
            "Premiere's Speed/Duration dialog."),
        new("edit.marker", "Add a marker", "M", CommandGroup.Editing, KeyOrigin.UniversalNle,
            "M is the marker key in Premiere, Resolve and Reaper alike.",
            Context: CommandContext.Timeline),
        new("edit.undo", "Undo", "Ctrl+Z", CommandGroup.Editing, KeyOrigin.Universal),
        new("edit.redo", "Redo", "Ctrl+Shift+Z", CommandGroup.Editing, KeyOrigin.Universal, Alternate: "Ctrl+Y"),
        new("edit.rippleMode", "Cycle ripple mode", "Ctrl+Alt+R", CommandGroup.Editing, KeyOrigin.Invented,
            "Off, this track, all tracks. Always announced - a silent ripple mode destroys edits."),
        new("edit.snap", "Toggle snapping", "N", CommandGroup.Editing, KeyOrigin.Reaper,
            Context: CommandContext.Timeline),
        new("edit.captions", "Toggle caption on this element", "Ctrl+Shift+C", CommandGroup.Editing, KeyOrigin.Invented),

        // ---- groups, subclips, angles ------------------------------------------
        new("edit.group", "Group these segments", "Ctrl+Shift+G", CommandGroup.Editing, KeyOrigin.Invented,
            "A run of segments becomes one named thing you can move, cut and restore as one.",
            Context: CommandContext.Timeline),
        new("group.list", "Groups", "Ctrl+Alt+G", CommandGroup.Editing, KeyOrigin.Invented,
            "Collapse, expand, rename, ungroup or delete. Enter goes to one.",
            Context: CommandContext.Timeline),
        new("subclip.create", "Make a subclip", "U", CommandGroup.Media, KeyOrigin.Premiere,
            "Names the marked range of this source. Premiere uses Ctrl+U; plain letters are safe in the bin.",
            Context: CommandContext.MediaBin),
        new("subclip.list", "Subclips", "Shift+U", CommandGroup.Media, KeyOrigin.Invented,
            "Insert, overwrite, rename or remove. Enter inserts at the cursor.",
            Context: CommandContext.MediaBin),
        new("multicam.create", "Make a multicam group", "M", CommandGroup.Media, KeyOrigin.Invented,
            "Two or more cameras on the same thing.", Context: CommandContext.MediaBin),
        new("multicam.sync", "Sync the angles by sound", "Shift+M", CommandGroup.Media, KeyOrigin.Invented,
            "Lines the cameras up by their audio and says how well each one matched.",
            Context: CommandContext.MediaBin),
        new("multicam.switch", "Cut to an angle", "1 to 9", CommandGroup.Editing, KeyOrigin.UniversalNle,
            "A digit cuts to that camera at the cursor - the same idea as a digit cutting to a scene "
            + "while streaming.",
            Context: CommandContext.Timeline),

        // ---- sound -------------------------------------------------------------
        new("audio.effects", "Audio effects here", "Ctrl+Alt+E", CommandGroup.Editing, KeyOrigin.Invented,
            "Named treatments for this track or this segment, each read back with its setting.",
            Context: CommandContext.Timeline),
        new("audio.advise", "What is wrong with this sound", "Ctrl+F4", CommandGroup.Review,
            KeyOrigin.Invented,
            "Measures the recording and suggests the effects by name."),
        new("audio.automation", "Volume over time", "Ctrl+Alt+A", CommandGroup.Editing, KeyOrigin.Invented,
            "Named shapes - duck, ramp, ease - rather than a curve with points on it.",
            Context: CommandContext.Timeline),

        // ---- tracks (Tracks pane; plain letters are safe there) ----------------
        new("track.mute", "Mute or unmute", "M", CommandGroup.Tracks, KeyOrigin.UniversalNle,
            "M on a track header is mute in every DAW.", Context: CommandContext.Tracks),
        new("track.solo", "Solo or unsolo", "S", CommandGroup.Tracks, KeyOrigin.UniversalNle,
            Context: CommandContext.Tracks),
        new("track.lock", "Lock or unlock", "L", CommandGroup.Tracks, KeyOrigin.UniversalNle,
            Context: CommandContext.Tracks),
        new("track.type", "Change this track's type", "Ctrl+Shift+Y", CommandGroup.Tracks, KeyOrigin.Invented,
            "Video, audio or image. The type decides what the track can record from."),
        new("track.rename", "Rename this track", "N", CommandGroup.Tracks, KeyOrigin.Invented,
            "F2 would be the convention, but F2 is the render key here.",
            Context: CommandContext.Tracks),
        new("track.add", "Add a track", "Ctrl+T", CommandGroup.Tracks, KeyOrigin.Invented,
            Alternate: "Insert"),
        new("track.remove", "Delete this track", "Delete", CommandGroup.Tracks, KeyOrigin.Windows,
            "Delete means delete the focused thing. In the Tracks pane the focused "
            + "thing is a track, so there is no ambiguity with deleting content. "
            + "Confirms first.", Context: CommandContext.Tracks),

        // ---- overlays and cards ------------------------------------------------
        new("overlay.title", "Add a lower third", "Ctrl+Shift+L", CommandGroup.Overlays, KeyOrigin.Invented,
            "A card with a transparent background, over the video."),
        new("cleanup.fillers", "Remove filler words", "Ctrl+Alt+F", CommandGroup.Editing,
            KeyOrigin.Invented,
            "Cuts every um and uh. They are marked cut, not deleted."),
        new("cleanup.silences", "Remove long silences", "Ctrl+Alt+S", CommandGroup.Editing,
            KeyOrigin.Invented),
        new("review.pace", "Report speaking pace", "Ctrl+Alt+P", CommandGroup.Review,
            KeyOrigin.Invented,
            "Words per minute overall, and where it drifts."),
        new("edit.detachAudio", "Detach or reattach this segment's audio", "Ctrl+Shift+D",
            CommandGroup.Editing, KeyOrigin.Invented,
            "Moves the sound onto an audio track and leaves the picture where it is, "
            + "so the two can be cut independently. Reversible."),
        new("edit.duration", "Set how long this stays on screen", "Ctrl+Shift+U", CommandGroup.Editing,
            KeyOrigin.Invented,
            "For stills, cards, holes and pauses - anything whose length is not "
            + "given by its media."),
        new("edit.kenBurns", "Cycle movement on this still", "Ctrl+Shift+B", CommandGroup.Editing,
            KeyOrigin.Invented,
            "A still that does not move reads as a frozen video."),
        new("card.edit", "Edit this card", "Ctrl+E", CommandGroup.Overlays, KeyOrigin.Invented,
            "Layers, background and layout for the card under the cursor."),
        new("edit.fade", "Fades for this segment", "Ctrl+Shift+F", CommandGroup.Editing, KeyOrigin.Invented,
            "Fade in and out. A fade belongs to a segment; a transition belongs to "
            + "the boundary between two."),
        new("insert.segment", "Insert a segment", "Ctrl+Shift+N", CommandGroup.Overlays, KeyOrigin.Invented,
            "A card, a hole or a pause, at the cursor."),
        new("overlay.card", "Add a card", "Ctrl+Shift+T", CommandGroup.Overlays, KeyOrigin.Invented,
            "A full-screen composed screen on the programme track."),
        new("overlay.graphic", "Add an image", "Ctrl+G", CommandGroup.Overlays, KeyOrigin.Invented),
        new("overlay.broll", "Add b-roll", "Ctrl+B", CommandGroup.Overlays, KeyOrigin.Invented),
        new("place.cell", "Place at numpad cell", "Numpad1-9", CommandGroup.Overlays, KeyOrigin.Invented,
            "Three by three, matching the rule of thirds. Press again for a sub-cell."),
        new("place.nudge", "Nudge placement", "Alt+Arrows", CommandGroup.Overlays, KeyOrigin.Invented),
        // Moved off T, which is takes. Resolve puts transitions on Ctrl+T, but
        // that is new track here; X is free, next to the cut keys, and unused
        // anywhere else in the timeline.
        new("transition.set", "Set transition at boundary", "X", CommandGroup.Overlays,
            KeyOrigin.DeviatesFromPremiere,
            "Type, length, sound, audition and your own saved ones, all from one key.",
            Context: CommandContext.Timeline),
        new("transition.audition", "Audition the transition", "Shift+X", CommandGroup.Overlays,
            KeyOrigin.Invented, Context: CommandContext.Timeline),
        new("transition.sound", "Sound on this transition", "X, then sound", CommandGroup.Overlays,
            KeyOrigin.Invented,
            "It belongs to the boundary, so moving the cut moves the sound with it. The "
            + "programme is not ducked under it - the track faders are there for that.",
            Context: CommandContext.Timeline),
        new("transition.save", "Save this transition as my own", "X, then save", CommandGroup.Overlays,
            KeyOrigin.Invented,
            "An xfade name or an expression, kept by name and reusable.",
            Context: CommandContext.Timeline),
        new("track.volume", "Set this track's level", "Shift+G", CommandGroup.Tracks,
            KeyOrigin.UniversalNle,
            "There is no automatic ducking anywhere in this application, so this is how a "
            + "bed goes under a voice: by deciding it.",
            Context: CommandContext.Timeline | CommandContext.Tracks),

        // ---- review and capture ------------------------------------------------

        // ---- output ------------------------------------------------------------

        // ---- workflows ---------------------------------------------------------
        // Deliberately absent. Workflow and WorkflowStep exist and are tested,
        // but nothing records or runs one yet, and a registry entry is a promise
        // that a key does something: it feeds F1, the palette and the keymap, so
        // an entry with no handler is a key that lies. Ctrl+Alt+K and
        // Ctrl+Alt+Shift+K are held for it. See ROADMAP.md, "Ongoing".

        // ---- markers -------------------------------------------------------
        new("marker.remove", "Remove the marker here", "Shift+M", CommandGroup.Editing,
            KeyOrigin.Invented, Context: CommandContext.Editing),
        new("marker.list", "List the markers", "Ctrl+M", CommandGroup.Navigation, KeyOrigin.Invented,
            "Enter goes there.", Context: CommandContext.Editing),
        new("transcript.find", "Find in the transcript", "Ctrl+F", CommandGroup.Navigation,
            KeyOrigin.Universal, Context: CommandContext.Global),

        // ---- streaming ---------------------------------------------------------
        // Single letters, which nothing else in the application does. While you
        // are live you are also talking; a chord is a chord you will fumble on
        // air. They are safe because the only text entry in this view is the
        // chat reply box, and that is checked for before any key is read as a
        // command.
        new("stream.area", "Next area", "Ctrl+`", CommandGroup.Streaming, KeyOrigin.CodeEditor,
            "Scenes, sources, preview, then one chat per platform. Shift goes back.",
            Context: CommandContext.Stream, Alternate: "Ctrl+Shift+`"),
        new("stream.switch", "Cut to a scene", "1 to 9", CommandGroup.Streaming, KeyOrigin.Invented,
            "OBS puts scene switching on hotkeys; the number that selects a scene is "
            + "the number you would say. No confirmation - that is what a scene is for.",
            Context: CommandContext.Stream),
        new("stream.newScene", "New scene", "N", CommandGroup.Streaming, KeyOrigin.Invented,
            Context: CommandContext.Stream),
        new("stream.starter", "Starter setup", "Shift+N", CommandGroup.Streaming, KeyOrigin.Invented,
            "Face cam and screen share, built once rather than as fifteen steps.",
            Context: CommandContext.Stream),
        new("stream.renameScene", "Rename scene", "F2", CommandGroup.Streaming, KeyOrigin.Windows,
            Context: CommandContext.Stream),
        new("stream.addSource", "Add a source", "A", CommandGroup.Streaming, KeyOrigin.Invented,
            "Camera, screen, microphone, image, video or looping music.",
            Context: CommandContext.Stream),
        new("stream.visible", "Show or hide a source", "V", CommandGroup.Streaming, KeyOrigin.Invented,
            "Hiding keeps it in the scene, exactly as on a track.",
            Context: CommandContext.Stream),
        new("stream.mute", "Mute a source", "M", CommandGroup.Streaming, KeyOrigin.UniversalNle,
            Context: CommandContext.Stream),
        new("stream.reorder", "Move a source forward or back", "[ and ]", CommandGroup.Streaming,
            KeyOrigin.Invented,
            "Order decides who is in front, and there is no way to see that, so "
            + "every move says where it landed.",
            Context: CommandContext.Stream),
        new("stream.remove", "Remove", "Delete", CommandGroup.Streaming, KeyOrigin.Universal,
            "The scene or the source, depending on which area you are in.",
            Context: CommandContext.Stream),
        new("stream.connectChat", "Connect a chat", "C", CommandGroup.Streaming, KeyOrigin.Invented,
            "Twitch needs no account to read. YouTube and Facebook say what they are waiting for.",
            Context: CommandContext.Stream),
        new("stream.reply", "Reply in chat", "R", CommandGroup.Streaming, KeyOrigin.Invented,
            "Goes to the platform whose pane you are in, never to all of them.",
            Context: CommandContext.Stream),
        new("stream.live", "Back to live chat", "Ctrl+Home", CommandGroup.Streaming, KeyOrigin.Universal,
            Context: CommandContext.Stream),
        new("stream.key", "Set a stream key", "K", CommandGroup.Streaming, KeyOrigin.Invented,
            "Typed in and never read back. A stream key is a password.",
            Context: CommandContext.Stream),
        new("stream.preflight", "Am I ready to go live", "P", CommandGroup.Streaming, KeyOrigin.Invented,
            "Reads the list of things to fix. Costs one key and no risk.",
            Context: CommandContext.Stream),
        new("stream.golive", "Go live, or stop", "Ctrl+Shift+L", CommandGroup.Streaming, KeyOrigin.Invented,
            "The one thing here that is not a single letter, because an audience "
            + "feels it the instant it happens.",
            Context: CommandContext.Stream),

        // ---- chat, per platform ------------------------------------------------
        new("stream.youtube", "Connect YouTube chat", "Y", CommandGroup.Streaming, KeyOrigin.Invented,
            "Needs an API key and the live video's id. Reading is all an API key buys; "
            + "posting and moderating need signing in.",
            Context: CommandContext.Stream),
        new("stream.facebook", "Connect Facebook comments", "F", CommandGroup.Streaming, KeyOrigin.Invented,
            "Needs a page access token and the live video's id.",
            Context: CommandContext.Stream),
        new("stream.capabilities", "What can I do in this chat", "Shift+C", CommandGroup.Streaming,
            KeyOrigin.Invented,
            "The platforms differ, and a moderation key that appears to work and does "
            + "nothing is the worst outcome available.",
            Context: CommandContext.Stream),
        new("stream.secrets", "What keys are saved", "Shift+K", CommandGroup.Streaming, KeyOrigin.Invented,
            "Names only. No key or token is ever read back.",
            Context: CommandContext.Stream),

        // ---- moderation --------------------------------------------------------
        new("stream.deleteMessage", "Delete this message", "D", CommandGroup.Streaming, KeyOrigin.Invented,
            "Facebook hides rather than deletes, and says so.",
            Context: CommandContext.Stream),
        new("stream.timeout", "Time this person out", "T", CommandGroup.Streaming, KeyOrigin.Invented,
            "Ten minutes. Facebook has no timeout and says what it has instead.",
            Context: CommandContext.Stream),
        new("stream.ban", "Ban this person", "B", CommandGroup.Streaming, KeyOrigin.Invented,
            "Asks first, with No focused. It cannot be taken back.",
            Context: CommandContext.Stream),
        new("stream.pin", "Pin this message", "Shift+P", CommandGroup.Streaming, KeyOrigin.Invented,
            "No platform offers this from outside its own app; each says what it has instead.",
            Context: CommandContext.Stream),
        new("stream.announce", "Announce this in chat", "Ctrl+Shift+P", CommandGroup.Streaming,
            KeyOrigin.Invented, "Twitch only.", Context: CommandContext.Stream),

        // ---- music -------------------------------------------------------------
        new("stream.music", "Play the playlist", "Space", CommandGroup.Streaming, KeyOrigin.Universal,
            "Plays on this machine. It reaches the stream through a desktop-audio "
            + "source, and the application says so if there is not one.",
            Context: CommandContext.Stream),
        new("stream.musicStop", "Stop the music", "Shift+Space", CommandGroup.Streaming, KeyOrigin.Invented,
            Context: CommandContext.Stream),
        new("stream.musicNext", "Next track", "Shift+Right", CommandGroup.Streaming, KeyOrigin.UniversalNle,
            Context: CommandContext.Stream, Alternate: "Shift+Left for previous"),
        new("stream.musicShuffle", "Shuffle", "Shift+S", CommandGroup.Streaming, KeyOrigin.Universal,
            Context: CommandContext.Stream),
        new("stream.addMusic", "Add music to the playlist", "Shift+A", CommandGroup.Streaming,
            KeyOrigin.Invented, Context: CommandContext.Stream),

        // ---- how it is going ---------------------------------------------------
        new("stream.health", "How is the stream doing", "H", CommandGroup.Streaming, KeyOrigin.Invented,
            "Bitrate, frame rate, dropped frames. Dropping and recovering are also "
            + "earcons, so you do not have to ask.",
            Context: CommandContext.Stream),
        new("stream.meter", "Meter the live mix", "Shift+F9", CommandGroup.Streaming, KeyOrigin.Invented,
            "The same audible meter as the track editor, on the same key.",
            Context: CommandContext.Stream),

        // ---- pictures ----------------------------------------------------------
        // No pointer to emulate. Every operation names what it acts on and
        // reports what it did, which is the only way a picture can be edited
        // by someone who cannot see it.
        new("image.open", "Open a picture", "O", CommandGroup.Images, KeyOrigin.Universal,
            "Measures it first and says what it found - size, shape, how straight "
            + "it is, and how much of it is empty paper.",
            Context: CommandContext.Images),
        new("image.describe", "What does it look like", "F8", CommandGroup.Images, KeyOrigin.Invented,
            "The same key that describes a video frame. The one part that genuinely "
            + "needs eyes, done by something that has them.",
            Context: CommandContext.Images),
        new("image.resize", "Resize", "Arrow keys", CommandGroup.Images, KeyOrigin.Invented,
            "Every press says the new size. Control for a bigger step.",
            Context: CommandContext.Images),
        new("image.lock", "Lock or unlock the shape", "L", CommandGroup.Images, KeyOrigin.Universal,
            Context: CommandContext.Images),
        new("image.presets", "Size presets", "S", CommandGroup.Images, KeyOrigin.Invented,
            "Half, double, fit 1080, fit 4K. Named by what they are for.",
            Context: CommandContext.Images),
        new("image.cropContent", "Crop to the picture", "C", CommandGroup.Images, KeyOrigin.Invented,
            "Removes the paper around it. The most useful crop there is, and the one "
            + "nobody can do by eye without a mouse.",
            Context: CommandContext.Images),
        new("image.cropRatio", "Crop to a shape", "Shift+C", CommandGroup.Images, KeyOrigin.Invented,
            "Square, 16 by 9, 4 by 5 - anchored on a cell, so it is one instruction.",
            Context: CommandContext.Images),
        new("image.cropEdge", "Move one crop edge", "Shift+arrows", CommandGroup.Images, KeyOrigin.Invented,
            "Each press says the edge, how much is cut, and what is left.",
            Context: CommandContext.Images),
        new("image.resetCrop", "Back to the whole picture", "Shift+R", CommandGroup.Images,
            KeyOrigin.Invented, Context: CommandContext.Images),
        new("image.straighten", "Straighten it", "T", CommandGroup.Images, KeyOrigin.Invented,
            "By the angle the analysis measured.",
            Context: CommandContext.Images),
        new("image.rotate", "Turn a quarter", "[ and ]", CommandGroup.Images, KeyOrigin.Universal,
            "For a photograph that went on the scanner sideways.",
            Context: CommandContext.Images),
        new("image.fixScan", "Fix the scan", "Shift+F", CommandGroup.Images, KeyOrigin.Invented,
            "Straighten and crop in one, which is what anyone wants after scanning.",
            Context: CommandContext.Images),
        new("image.split", "Split into one file each", "Shift+S", CommandGroup.Images, KeyOrigin.Invented,
            "Several photographs on one scanner bed is the normal case, not the exotic one.",
            Context: CommandContext.Images),
        new("image.draw", "Draw something", "Shift+D", CommandGroup.Images, KeyOrigin.Invented,
            "Said rather than drawn: \"circle at centre, radius 20 percent, white\". "
            + "Exact, repeatable, and editable afterwards.",
            Context: CommandContext.Images),
        new("image.removeShape", "Remove a shape", "Delete", CommandGroup.Images, KeyOrigin.Universal,
            Context: CommandContext.Images),
        new("image.colours", "What colours are on it", "K", CommandGroup.Images, KeyOrigin.Invented,
            "\"Mostly navy, a fifth white\" answers most questions about a picture "
            + "without describing it.",
            Context: CommandContext.Images),
        new("image.sample", "What colour is that point", "P", CommandGroup.Images, KeyOrigin.UniversalNle,
            "By coordinates or by cell. Named before it is valued.",
            Context: CommandContext.Images),
        new("image.export", "Save it", "E", CommandGroup.Images, KeyOrigin.Invented,
            "Nothing has touched the original until now.",
            Context: CommandContext.Images),
        new("image.history", "What would undo do", "U", CommandGroup.Images, KeyOrigin.Invented,
            "Asked before pressing it, which is the only way to be sure the key is "
            + "about to do what you think.",
            Context: CommandContext.Images),

        // ---- the pointer you can hear ------------------------------------------
        new("image.sweep", "Sweep the picture", "G", CommandGroup.Images, KeyOrigin.Invented,
            "The arrows move a pointer instead of resizing. It is panned to where it "
            + "is and pitched to how far up - the viewfinder's vocabulary, already learnt. "
            + "Enter reads what is under it, a digit jumps to a cell, plus and minus "
            + "change the step, Escape leaves.",
            Context: CommandContext.Images),

        // ---- colour ------------------------------------------------------------
        new("image.correct", "Correct the colour", "V", CommandGroup.Images, KeyOrigin.Invented,
            "Brighter, warmer, punchier - the sentences people actually say about a "
            + "photograph. Each is a nudge, said back in stops and kelvin.",
            Context: CommandContext.Images),
        new("image.advise", "What is wrong with the colour", "Shift+V", CommandGroup.Images,
            KeyOrigin.Invented,
            "Measures it and suggests the correction, using the same words the "
            + "corrections are called.",
            Context: CommandContext.Images),

        // ---- shared with the video ---------------------------------------------
        new("image.card", "Put a card on it", "Shift+A", CommandGroup.Images, KeyOrigin.Invented,
            "The video editor's own card, edited by the same editor. A lower third over "
            + "a photograph is the same object as one over a clip.",
            Context: CommandContext.Images),
        new("image.levels", "Levels", ";", CommandGroup.Images, KeyOrigin.Invented,
            "The black point, the white point, and the three zones between them. Auto "
            + "sets the points from the picture's own histogram, which is the one "
            + "command that makes a curve worth having without a graph.",
            Context: CommandContext.Images),
        new("image.histogram", "Read the histogram", "'", CommandGroup.Images, KeyOrigin.Invented,
            "Five numbers rather than two hundred and fifty six. This is what the "
            + "curve was drawn on top of.",
            Context: CommandContext.Images),
        new("image.colourLevels", "Levels, per channel", ":", CommandGroup.Images,
            KeyOrigin.Invented,
            "The only thing that reaches a cast the temperature control cannot: "
            + "temperature moves the picture along one axis, and a yellowed page is off "
            + "in a direction that axis does not pass through.",
            Context: CommandContext.Images),
        new("image.balance", "Balance on the pointer", "W while sweeping", CommandGroup.Images,
            KeyOrigin.Invented,
            "The eyedropper, without pointing. Sweep to something that ought to be grey - "
            + "a wall, a shirt, the paper a photograph is printed on - and the correction "
            + "that makes it neutral is worked out from there.",
            Context: CommandContext.Images),
        new("image.cast", "Which way is the colour pulling", "\"", CommandGroup.Images,
            KeyOrigin.Invented,
            "A cast is invisible to a brightness histogram, so it is measured separately "
            + "and said as a direction rather than as three numbers.",
            Context: CommandContext.Images),
        new("image.batch", "Do this to a whole folder", "B", CommandGroup.Images, KeyOrigin.Invented,
            "The corrections travel; the geometry is measured per picture, because a "
            + "photograph lands somewhere different on the bed every time. Says what it "
            + "will do, then confirms.",
            Context: CommandContext.Images),
        new("image.toProject", "Send it to the project", "I", CommandGroup.Images, KeyOrigin.Invented,
            "Saves it and puts it in the media bin, so a photograph that has just been "
            + "straightened can go on the timeline without leaving the application.",
            Context: CommandContext.Images),
    ];

    public static CommandDefinition? ById(string id) => All.FirstOrDefault(c => c.Id == id);

    public static IEnumerable<CommandDefinition> InGroup(CommandGroup group) =>
        All.Where(c => c.Group == group);

    /// <summary>What F1 reads out: everything valid where the focus currently is.</summary>
    public static IEnumerable<CommandDefinition> InContext(CommandContext pane) =>
        All.Where(c => c.Context.Includes(pane));

    /// <summary>
    /// Two commands claiming one key in a pane where both are live. Exposed
    /// rather than only tested, so a user-supplied keymap can be checked too.
    /// </summary>
    public static IEnumerable<string> Conflicts()
    {
        foreach (var pane in CommandContextExtensions.Panes())
        {
            var clashes = InContext(pane)
                .GroupBy(c => c.DefaultBinding, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var clash in clashes)
            {
                yield return $"{clash.Key} in {pane}: {string.Join(", ", clash.Select(c => c.Id))}";
            }
        }
    }

    /// <summary>
    /// Bindings with no precedent. Worth reviewing periodically - every one of
    /// these is something a user coming from another editor has to learn.
    /// </summary>
    public static IEnumerable<CommandDefinition> Invented =>
        All.Where(c => c.Origin == KeyOrigin.Invented);

    /// <summary>Places we knowingly disagree with Premiere. Each states why.</summary>
    public static IEnumerable<CommandDefinition> Deviations =>
        All.Where(c => c.Origin == KeyOrigin.DeviatesFromPremiere);

    /// <summary>Fuzzy match for the palette.</summary>
    public static IEnumerable<CommandDefinition> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;

        var needle = query.Trim();

        return All
            .Select(c => (Command: c, Score: Score(c, needle)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Command);
    }

    private static int Score(CommandDefinition command, string needle)
    {
        if (command.Title.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return 4;
        if (command.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 3;
        if (command.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 2;
        if (command.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true) return 1;
        return 0;
    }
}

public sealed record CommandDefinition(
    string Id,
    string Title,
    string DefaultBinding,
    CommandGroup Group,
    KeyOrigin Origin,
    string? Description = null,
    string? Alternate = null,
    CommandContext Context = CommandContext.Global)
{
    public string Keys => Alternate is null ? DefaultBinding : $"{DefaultBinding} or {Alternate}";

    public string Announce() =>
        Description is null ? $"{Title}, {Keys}" : $"{Title}, {Keys}. {Description}";
}

/// <summary>Where a default binding came from.</summary>
public enum KeyOrigin
{
    /// <summary>Every application on every desktop. Ctrl+S, Ctrl+Z, Space.</summary>
    Universal,

    /// <summary>Every video editor. J K L, I and O, M for marker.</summary>
    UniversalNle,

    Premiere,
    Reaper,
    Resolve,

    /// <summary>Windows and desktop UI conventions. F2, Applications key, Tab between panes.</summary>
    Windows,

    /// <summary>Text and code editors. Ctrl+Shift+P, Alt+Up to move a line.</summary>
    CodeEditor,

    /// <summary>No precedent anywhere. Ours, and open to change.</summary>
    Invented,

    /// <summary>Premiere does this differently and we chose otherwise. The reason is stated.</summary>
    DeviatesFromPremiere,
}

/// <summary>
/// Which panes a command is legal in. Flags, because most commands work in
/// more than one place and a single-context model forces false choices - Delete
/// has to mean ripple-delete in both the timeline and the transcript, while
/// meaning delete-this-track in the Tracks pane.
/// </summary>
[Flags]
public enum CommandContext
{
    None = 0,
    MediaBin = 1,
    Tracks = 2,
    Timeline = 4,
    Transcript = 8,
    Viewfinder = 16,
    Stream = 32,
    Images = 64,

    /// <summary>The two panes where you edit the cut.</summary>
    Editing = Timeline | Transcript,

    Global = MediaBin | Tracks | Timeline | Transcript | Viewfinder | Stream | Images,
}

public static class CommandContextExtensions
{
    public static bool Includes(this CommandContext context, CommandContext pane) =>
        (context & pane) != 0;

    public static IEnumerable<CommandContext> Panes()
    {
        yield return CommandContext.MediaBin;
        yield return CommandContext.Tracks;
        yield return CommandContext.Timeline;
        yield return CommandContext.Transcript;
        yield return CommandContext.Viewfinder;
    }
}

public enum CommandGroup
{
    File,
    Application,
    Navigation,
    Playback,
    Selection,
    Editing,
    Tracks,

    /// <summary>The bin: sources, subclips and camera angles.</summary>
    Media,

    Overlays,
    Review,
    Capture,
    Output,
    Workflows,
    Streaming,
    Images,
}
