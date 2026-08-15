using AccessibleVideoEditor.Core.Commands;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The menu bar and the context menus.
///
/// A menu bar is not decoration here. It is the discoverability floor: every
/// command reachable by keyboard, labelled, with its accelerator spoken by the
/// screen reader when you arrow onto it. Someone who has never read the docs
/// can find "Heal split" by walking the Edit menu, and learn its key on the way
/// past.
///
/// Labels and accelerators come from <see cref="CommandRegistry"/> rather than
/// being typed here, so a menu item cannot drift out of step with the key that
/// actually fires it.
/// </summary>
public static class Menus
{
    /// <summary>File, Edit, View, Timeline, Help.</summary>
    public static Gio.Menu BuildMenuBar()
    {
        var bar = Gio.Menu.New();

        bar.AppendSubmenu("_File", Section(
            [("file.new", "win.new"), ("file.open", "win.open"), ("file.recent", "win.recent")],
            [("file.save", "win.save"), ("file.saveAs", "win.saveAs")],
            [("file.importMedia", "win.import"), ("file.reloadEdl", "win.reloadEdl")],
            [("render.draft", "win.renderDraft"), ("render.master", "win.export"),
             ("render.presets", "win.renderPresets")],
            [("file.exit", "win.quit")]));

        bar.AppendSubmenu("_Edit", Section(
            [("edit.undo", "win.undo"), ("edit.redo", "win.redo")],
            [("edit.cut", "win.cut"), ("edit.copy", "win.copy"), ("edit.paste", "win.paste")],
            [("edit.split", "win.split"), ("edit.heal", "win.heal")],
            [("edit.rippleDelete", "win.rippleDelete"), ("edit.lift", "win.lift"),
             ("edit.disable", "win.disable"), ("edit.mute", "win.mute")],
            [("edit.trimHead", "win.trimHead"), ("edit.trimTail", "win.trimTail"),
             ("edit.speed", "win.speed")],
            [("edit.insertHole", "win.insertHole"), ("edit.marker", "win.marker")],
            [("select.segment", "win.selectSegment"), ("select.track", "win.selectTrack"),
             ("select.clear", "win.selectClear")],
            [("edit.group", "win.group"), ("group.list", "win.groupList")],
            [("edit.snap", "win.snap"), ("edit.rippleMode", "win.rippleMode")]));

        bar.AppendSubmenu("_Media", Section(
            [("file.importMedia", "win.import")],
            [("subclip.create", "win.subclipCreate"), ("subclip.list", "win.subclipList")],
            [("multicam.create", "win.multicamCreate"), ("multicam.sync", "win.multicamSync"),
             ("multicam.switch", "win.multicamSwitch")],
            [("edit.insert", "win.insert"), ("edit.overwrite", "win.overwrite")]));

        bar.AppendSubmenu("_Sound", Section(
            [("audio.effects", "win.audioEffects"), ("audio.advise", "win.audioAdvise")],
            [("edit.automation", "win.audioAutomation")],
            [("track.volume", "win.trackVolume"), ("edit.detachAudio", "win.detachAudio")],
            [("edit.mute", "win.mute")]));

        bar.AppendSubmenu("_Clean_up", Section(
            [("cleanup.fillers", "win.removeFillers"), ("cleanup.silences", "win.removeSilences")],
            [("review.quality", "win.quality"), ("review.qualityAll", "win.qualityAll")],
            [("review.pace", "win.pace")]));

        bar.AppendSubmenu("_Mark", Section(
            [("edit.marker", "win.marker"), ("marker.remove", "win.removeMarker"),
             ("marker.list", "win.markerList")],
            [("review.issues", "win.issues"), ("review.describeEdit", "win.describeEdit")],
            [("transcript.find", "win.find")]));

        bar.AppendSubmenu("_Insert", Section(
            [("track.add", "win.addTrack"), ("insert.segment", "win.insertSegment")],
            [("overlay.card", "win.card"), ("overlay.title", "win.title"),
             ("overlay.graphic", "win.graphic"), ("overlay.broll", "win.broll")],
            [("edit.insertHole", "win.insertHole"), ("edit.marker", "win.marker")],
            [("file.importMedia", "win.import")]));

        bar.AppendSubmenu("_View", Section(
            [("view.next", "win.nextPane"), ("view.previous", "win.previousPane")],
            [("granularity.coarser", "win.zoomOut"), ("granularity.finer", "win.zoomIn")],
            [("cursor.where", "win.whereAmI"), ("speech.verbosity", "win.verbosity")],
            [("capture.output", "win.chooseOutput")],
            [("palette.open", "win.palette")]));

        bar.AppendSubmenu("_Timeline", Section(
            [("cursor.previousSegment", "win.previousSegment"), ("cursor.nextSegment", "win.nextSegment")],
            [("cursor.segmentStart", "win.segmentStart"), ("cursor.segmentEnd", "win.segmentEnd")],
            [("transition.set", "win.setTransition"), ("transition.audition", "win.auditionTransition"),
             ("transition.sound", "win.transitionSound"), ("transition.save", "win.saveTransition")],
            [("track.volume", "win.trackVolume")],
            [("track.add", "win.addTrack")],
            [("overlay.title", "win.title"), ("overlay.graphic", "win.graphic"),
             ("overlay.broll", "win.broll")]));

        bar.AppendSubmenu("_Stream", Section(
            [("stream.starter", "win.streamStarter"), ("stream.newScene", "win.streamScene")],
            [("stream.connectChat", "win.streamConnectTwitch"),
             ("stream.youtube", "win.streamConnectYouTube"),
             ("stream.facebook", "win.streamConnectFacebook"),
             ("stream.capabilities", "win.streamCapabilities")],
            [("stream.deleteMessage", "win.streamDelete"), ("stream.timeout", "win.streamTimeout"),
             ("stream.ban", "win.streamBan"), ("stream.announce", "win.streamAnnounce")],
            [("stream.music", "win.streamMusic"), ("stream.addMusic", "win.streamAddMusic"),
             ("stream.musicNext", "win.streamMusicNext"), ("stream.musicShuffle", "win.streamMusicShuffle"),
             ("stream.musicStop", "win.streamMusicStop")],
            [("stream.key", "win.keyTwitch"), ("stream.secrets", "win.streamSecrets")],
            [("stream.health", "win.streamHealth"), ("stream.meter", "win.streamMeter")],
            [("stream.preflight", "win.streamPreflight"), ("stream.golive", "win.streamLive")]));

        bar.AppendSubmenu("_Picture", Section(
            [("image.open", "win.imageOpen"), ("image.describe", "win.imageDescribe")],
            [("image.fixScan", "win.imageFixScan"), ("image.straighten", "win.imageStraighten"),
             ("image.split", "win.imageSplit")],
            [("image.cropContent", "win.imageCropContent"), ("image.resetCrop", "win.imageResetCrop"),
             ("image.lock", "win.imageAspectLock")],
            [("image.draw", "win.imageDraw"), ("image.card", "win.imageCard")],
            [("image.correct", "win.imageCorrect"), ("image.advise", "win.imageAdvise"),
             ("image.levels", "win.imageLevel"), ("image.histogram", "win.imageHistogram"),
             ("image.colourLevels", "win.imageColourLevels"), ("image.cast", "win.imageCast")],
            [("image.batch", "win.imageBatch")],
            [("image.sweep", "win.imageSweep"), ("image.colours", "win.imageColours"),
             ("image.sample", "win.imageSample")],
            [("image.toProject", "win.imageToProject")],
            [("image.history", "win.imageHistory")],
            [("image.export", "win.imageExport")]));

        bar.AppendSubmenu("_Help", Section(
            [("help.context", "win.contextHelp"), ("help.keymap", "win.keymap")],
            [("help.about", "win.about")]));

        return bar;
    }

    /// <summary>
    /// The Applications key on a track header. These are the things you would
    /// otherwise have to remember a letter for.
    /// </summary>
    public static Gio.Menu TrackContextMenu() => Section(
        [("track.rename", "win.renameTrack"), ("track.type", "win.trackType")],
        [("track.arm", "win.armTrack"), ("capture.device", "win.chooseDevice"),
         ("capture.channel", "win.chooseChannel"),
         ("capture.record", "win.record"), ("capture.viewfinder", "win.viewfinder"),
         ("capture.monitor", "win.monitor")],
        [("track.mute", "win.muteTrack"), ("track.solo", "win.soloTrack"),
         ("track.lock", "win.lockTrack")],
        [("track.add", "win.addTrack"), ("track.remove", "win.removeTrack")]);

    /// <summary>The Applications key on a clip, title or image in the timeline.</summary>
    public static Gio.Menu ItemContextMenu() => Section(
        [("edit.split", "win.split"), ("edit.heal", "win.heal")],
        [("edit.rippleDelete", "win.rippleDelete"), ("edit.lift", "win.lift"),
         ("edit.disable", "win.disable"), ("edit.mute", "win.mute")],
        [("edit.trimHead", "win.trimHead"), ("edit.trimTail", "win.trimTail"),
         ("edit.speed", "win.speed")],
        [("edit.cut", "win.cut"), ("edit.copy", "win.copy"), ("edit.paste", "win.paste")],
        [("transition.set", "win.setTransition"), ("edit.fade", "win.fade"),
         ("edit.duration", "win.duration"), ("edit.kenBurns", "win.kenBurns"),
         ("card.edit", "win.editCard"), ("edit.marker", "win.marker")],
        [("review.describeFrame", "win.describeFrame"), ("review.quality", "win.quality")]);

    /// <summary>The Applications key on a source in the media bin.</summary>
    public static Gio.Menu MediaContextMenu() => Section(
        [("edit.insert", "win.insert"), ("edit.overwrite", "win.overwrite")],
        [("file.importMedia", "win.import")],
        [("review.quality", "win.quality")]);

    /// <summary>
    /// Each array is one visually separated group. Separators matter to a
    /// screen reader too - Orca announces the boundary, which chunks a long
    /// menu into something memorable.
    /// </summary>
    private static Gio.Menu Section(params (string CommandId, string Action)[][] groups)
    {
        var menu = Gio.Menu.New();

        foreach (var group in groups)
        {
            var section = Gio.Menu.New();

            foreach (var (commandId, action) in group)
            {
                var command = CommandRegistry.ById(commandId);
                if (command is null) continue;

                section.Append(command.Title, action);
            }

            menu.AppendSection(null, section);
        }

        return menu;
    }

    /// <summary>
    /// Registers the accelerators the menu advertises, translated from the
    /// registry's notation into GTK's. A menu item that shows a key it does not
    /// actually trigger is worse than showing none.
    /// </summary>
    public static void InstallAccelerators(Gtk_.Application application)
    {
        foreach (var (commandId, action) in AcceleratedCommands)
        {
            var command = CommandRegistry.ById(commandId);
            if (command is null) continue;

            var accelerator = ToGtkAccelerator(command.DefaultBinding);
            if (accelerator is null) continue;

            application.SetAccelsForAction(action, [accelerator]);
        }
    }

    private static readonly (string CommandId, string Action)[] AcceleratedCommands =
    [
        ("file.new", "win.new"),
        ("file.open", "win.open"),
        ("file.save", "win.save"),
        ("file.saveAs", "win.saveAs"),
        ("file.importMedia", "win.import"),
        ("file.exit", "win.quit"),
        ("edit.undo", "win.undo"),
        ("edit.copy", "win.copy"),
        ("edit.cut", "win.cut"),
        ("edit.paste", "win.paste"),
        ("render.master", "win.export"),
        ("palette.open", "win.palette"),
    ];

    /// <summary>
    /// "Ctrl+Shift+S" becomes "&lt;Control&gt;&lt;Shift&gt;s". Returns null for
    /// bindings GTK cannot express as an accelerator, such as bare letters.
    /// </summary>
    public static string? ToGtkAccelerator(string binding)
    {
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var modifiers = string.Empty;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers += parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => "<Control>",
                "shift" => "<Shift>",
                "alt" => "<Alt>",
                _ => string.Empty,
            };
        }

        if (modifiers.Length == 0) return null;

        var key = parts[^1] switch
        {
            "Comma" => "comma",
            "Period" => "period",
            "Minus" => "minus",
            "Equals" => "equal",
            "Semicolon" => "semicolon",
            "BracketLeft" => "bracketleft",
            "BracketRight" => "bracketright",
            var other when other.Length == 1 => other.ToLowerInvariant(),
            var other => other,
        };

        return modifiers + key;
    }
}
