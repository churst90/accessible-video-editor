using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Vision;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The preferences window.
///
/// This is the one place in the application built out of ordinary form
/// controls rather than lists and spoken verbs, and that is deliberate. Every
/// other view replaces a visual control with something describable because no
/// accessible equivalent exists; a checkbox has no such problem. GTK's own
/// widgets already carry their role, their state and their label into the
/// accessibility tree, so a check button announces "earcons, checked" without
/// this file saying a word - and inventing a spoken list here would mean
/// re-implementing, worse, something the toolkit does properly.
///
/// So the rule for this file is that it holds <b>layout only</b>. What a save
/// says it changed, and which values are corrected before they can fail
/// somewhere else, both live in <see cref="Preferences"/> where they are tested
/// without a window and where the WPF head will find them.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] VerbosityChoices =
        ["Terse - position only", "Normal - position and what is there", "Verbose - everything"];

    private static readonly string[] RippleChoices =
        ["Off - a delete leaves a gap", "This track only", "All tracks - everything after a cut moves"];

    /// <summary>
    /// Reading the devices costs a `pactl` call and a walk of sysfs, and
    /// neither opens anything - listing a camera cannot switch its light on.
    /// Done once when the window opens rather than per dropdown.
    /// </summary>
    private sealed record DeviceLists(
        IReadOnlyList<CaptureDevice> Cameras,
        IReadOnlyList<CaptureDevice> Microphones,
        IReadOnlyList<CaptureDevice> Outputs);

    private void ShowPreferences()
    {
        var before = _settings.Copy();

        var dialog = Gtk_.Window.New();
        dialog.Title = "Preferences";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(620, 620);

        var page = Gtk_.Box.New(Gtk_.Orientation.Vertical, 12);
        page.MarginTop = 12; page.MarginBottom = 12; page.MarginStart = 12; page.MarginEnd = 12;

        var devices = ReadDevices();

        // ---- speech ------------------------------------------------------------
        var verbosity = Dropdown(VerbosityChoices, (int)_settings.Behaviour.Verbosity);
        var earcons = Gtk_.CheckButton.NewWithLabel("Play earcons");
        earcons.Active = _settings.Behaviour.Earcons;
        var follow = Gtk_.CheckButton.NewWithLabel("Follow the playhead while playing");
        follow.Active = _settings.Behaviour.FollowPlayback;
        var displayName = Entry(_settings.DisplayName);

        page.Append(Group("Speech", [
            Field("_How much is said", verbosity),
            earcons,
            follow,
            Field("Your _name, for spotting mentions in chat", displayName),
        ]));

        // ---- saving ------------------------------------------------------------
        var autosave = Spin(0, 60, 1, _settings.Behaviour.AutosaveMinutes);
        var output = Entry(_settings.Behaviour.OutputDirectory);

        page.Append(Group("Saving and output", [
            Field("_Autosave every, in minutes - zero turns it off", autosave),
            Field("Render into this f_older - blank means the project's own", output),
        ]));

        // ---- new projects ------------------------------------------------------
        var width = Spin(16, 7680, 2, _settings.Defaults.CanvasWidth);
        var height = Spin(16, 4320, 2, _settings.Defaults.CanvasHeight);
        var fps = Spin(1, 240, 1, _settings.Defaults.Fps);
        var snap = Gtk_.CheckButton.NewWithLabel("Snap to boundaries, word starts and markers");
        snap.Active = _settings.Defaults.Snap;
        var ripple = Dropdown(RippleChoices, (int)_settings.Defaults.RippleMode);
        var scrub = Gtk_.CheckButton.NewWithLabel("Play a blip of audio as the cursor moves");
        scrub.Active = _settings.Defaults.AudioScrub;
        var scrubLength = Spin(0.02, 2, 0.02, _settings.Defaults.AudioScrubLength, digits: 2);
        var still = Spin(0.1, 60, 0.5, _settings.Defaults.StillDuration, digits: 1);
        var kenBurns = Gtk_.CheckButton.NewWithLabel("Give stills a slow drift by default");
        kenBurns.Active = _settings.Defaults.KenBurnsByDefault;
        var loudness = Spin(-70, -5, 0.5, _settings.Defaults.TargetLoudnessLufs, digits: 1);
        var peak = Spin(-9, 0, 0.1, _settings.Defaults.TargetTruePeakDb, digits: 1);

        page.Append(Group("What a new project starts from", [
            Field("Canvas _width", width),
            Field("Canvas _height", height),
            Field("_Frames per second", fps),
            snap,
            Field("_Ripple", ripple),
            scrub,
            Field("Scrub _blip length, in seconds", scrubLength),
            Field("How long a _still lasts, in seconds", still),
            kenBurns,
            Field("_Loudness target, LUFS", loudness),
            Field("_Peak ceiling, decibels", peak),
        ]));

        // ---- devices -----------------------------------------------------------
        var camera = DeviceDropdown(devices.Cameras, _settings.Devices.Camera, "camera");
        var microphone = DeviceDropdown(devices.Microphones, _settings.Devices.Microphone, "microphone");
        var monitor = DeviceDropdown(devices.Outputs, _settings.Devices.MonitorOutput, "output");

        page.Append(Group("Devices - used when a track has none of its own", [
            Field("Default _camera", camera.Widget),
            Field("Default _microphone", microphone.Widget),
            Field("Where monitoring is hear_d", monitor.Widget),
        ]));

        // ---- chat --------------------------------------------------------------
        var everyMessage = Gtk_.CheckButton.NewWithLabel("Read every chat message, however busy it gets");
        everyMessage.Active = _settings.Behaviour.SpeakEveryChatMessage;
        var burst = Spin(1, 60, 1, _settings.Behaviour.ChatBurst);
        var burstWindow = Spin(0.5, 30, 0.5, _settings.Behaviour.ChatBurstWindow, digits: 1);

        page.Append(Group("Chat", [
            everyMessage,
            Field("Stop reading past this many _messages", burst),
            Field("...within this many _seconds", burstWindow),
        ]));

        // ---- tools -------------------------------------------------------------
        var ffmpeg = Entry(_settings.Tools.Ffmpeg);
        var ffprobe = Entry(_settings.Tools.Ffprobe);
        var claude = Entry(_settings.Tools.Claude);
        var whisper = Entry(_settings.Tools.WhisperPython);
        var cache = Entry(_settings.Tools.CacheDirectory);

        page.Append(Group("Where the tools are", [
            Field("ff_mpeg", ffmpeg),
            Field("ffpro_be", ffprobe),
            Field("The describe command", claude),
            Field("_Whisper environment", whisper),
            Field("Cache folder - blank keeps it beside each project", cache),
        ]));

        // Stream keys are deliberately absent. They are the one thing here that
        // is a password, they live in a separate file with tighter permissions,
        // and a settings window that could read one back would undo that.
        page.Append(Note(
            "Stream keys are not here. They are set in the streamer view and are never read back."));

        void Save()
        {
            _settings.DisplayName = displayName.GetText().Trim();

            _settings.Behaviour.Verbosity = (Verbosity)verbosity.Selected;
            _settings.Behaviour.Earcons = earcons.Active;
            _settings.Behaviour.FollowPlayback = follow.Active;
            _settings.Behaviour.AutosaveMinutes = (int)autosave.Value;
            _settings.Behaviour.OutputDirectory = output.GetText().Trim();
            _settings.Behaviour.SpeakEveryChatMessage = everyMessage.Active;
            _settings.Behaviour.ChatBurst = (int)burst.Value;
            _settings.Behaviour.ChatBurstWindow = burstWindow.Value;

            _settings.Defaults.CanvasWidth = (int)width.Value;
            _settings.Defaults.CanvasHeight = (int)height.Value;
            _settings.Defaults.Fps = fps.Value;
            _settings.Defaults.Snap = snap.Active;
            _settings.Defaults.RippleMode = (RippleMode)ripple.Selected;
            _settings.Defaults.AudioScrub = scrub.Active;
            _settings.Defaults.AudioScrubLength = scrubLength.Value;
            _settings.Defaults.StillDuration = still.Value;
            _settings.Defaults.KenBurnsByDefault = kenBurns.Active;
            _settings.Defaults.TargetLoudnessLufs = loudness.Value;
            _settings.Defaults.TargetTruePeakDb = peak.Value;

            _settings.Devices.Camera = camera.Chosen();
            _settings.Devices.Microphone = microphone.Chosen();
            _settings.Devices.MonitorOutput = monitor.Chosen();

            var corrections = Preferences.Clamp(_settings);
            var summary = Preferences.Summarise(before, _settings);
            var written = _settings.Save();

            dialog.Close();

            ApplyPreferencesToThisSession();

            // A failed write is the one outcome that must not be buried at the
            // end of a list of what changed: nothing changed, on disk.
            Announce(
                written.StartsWith("could not", StringComparison.Ordinal)
                    ? written
                    : string.Join(" ", new[] { summary }.Concat(corrections)),
                urgent: true);
        }

        var buttons = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 8);

        var save = Gtk_.Button.NewWithLabel("Save");
        save.AddCssClass("suggested-action");
        save.OnClicked += (_, _) => Save();

        var cancel = Gtk_.Button.NewWithLabel("Cancel");
        cancel.OnClicked += (_, _) =>
        {
            dialog.Close();
            Announce("preferences closed, nothing changed", urgent: true);
        };

        buttons.Append(save);
        buttons.Append(cancel);

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(page);
        scroller.Vexpand = true;

        var root = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        root.MarginTop = 8; root.MarginBottom = 12; root.MarginStart = 12; root.MarginEnd = 12;
        root.Append(scroller);
        root.Append(buttons);

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != Gdk.Constants.KEY_Escape) return false;

            dialog.Close();
            Announce("preferences closed, nothing changed", urgent: true);

            return true;
        };

        dialog.AddController(keys);
        dialog.SetChild(root);
        dialog.Present();

        verbosity.GrabFocus();
    }

    /// <summary>
    /// The settings that describe how this session behaves, rather than what
    /// the next project starts from, take effect on save instead of on restart.
    ///
    /// Verbosity is the one that proves the point: changing it and then having
    /// to reopen the application to hear the difference would make it
    /// impossible to tell whether the setting had worked at all.
    /// </summary>
    private void ApplyPreferencesToThisSession()
    {
        Project.Settings.Verbosity = _settings.Behaviour.Verbosity;
        Project.Settings.Earcons = _settings.Behaviour.Earcons;

        Refresh();
    }

    private DeviceLists ReadDevices()
    {
        var devices = new LinuxCaptureDevices();

        IReadOnlyList<CaptureDevice> Read(CaptureDeviceKind kind)
        {
            try
            {
                return devices.EnumerateAsync(kind).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // No pactl, or no sysfs. An empty list becomes "leave it
                // alone", which is the right outcome rather than an error
                // dialog in front of every other preference.
                return [];
            }
        }

        return new DeviceLists(
            Read(CaptureDeviceKind.Camera),
            Read(CaptureDeviceKind.Microphone),
            Read(CaptureDeviceKind.Output));
    }

    /// <summary>
    /// A device list with "no default" first, so the unset state is a choice
    /// you can make rather than something you can only reach by never touching
    /// the control.
    /// </summary>
    private static (Gtk_.Widget Widget, Func<string?> Chosen) DeviceDropdown(
        IReadOnlyList<CaptureDevice> devices, string? current, string what)
    {
        if (devices.Count == 0)
        {
            // Not an empty dropdown: an empty list of choices reads as a
            // control that has failed rather than as a machine with no cameras.
            var label = Gtk_.Label.New($"no {what} found on this machine");
            label.Xalign = 0;

            return (label, () => current);
        }

        var names = new List<string> { $"No default - choose per track" };
        names.AddRange(devices.Select(device => device.Name));

        var index = current is { Length: > 0 }
            ? names.FindIndex(name => string.Equals(name, current, StringComparison.Ordinal))
            : 0;

        var dropdown = Dropdown([.. names], index < 0 ? 0 : index);

        return (dropdown, () => dropdown.Selected == 0 ? null : names[(int)dropdown.Selected]);
    }

    // ---- the small pieces --------------------------------------------------

    private static Gtk_.DropDown Dropdown(string[] choices, int selected)
    {
        var dropdown = Gtk_.DropDown.NewFromStrings(choices);
        dropdown.Selected = (uint)Math.Clamp(selected, 0, choices.Length - 1);
        dropdown.Hexpand = true;

        return dropdown;
    }

    private static Gtk_.Entry Entry(string text)
    {
        var entry = Gtk_.Entry.New();
        entry.SetText(text);
        entry.Hexpand = true;

        return entry;
    }

    private static Gtk_.SpinButton Spin(double min, double max, double step, double value, int digits = 0)
    {
        var spin = Gtk_.SpinButton.NewWithRange(min, max, step);
        spin.Digits = (uint)digits;
        spin.Value = value;

        return spin;
    }

    /// <summary>
    /// A label beside its control, joined by <c>SetMnemonicWidget</c> - which
    /// does two jobs at once. It gives the field an Alt key, and it publishes
    /// the label-for relation, so focusing the control announces the label
    /// without the application having to speak it.
    /// </summary>
    private static Gtk_.Widget Field(string mnemonic, Gtk_.Widget control)
    {
        var row = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 12);

        var label = Gtk_.Label.NewWithMnemonic(mnemonic);
        label.Xalign = 0;
        label.Wrap = true;
        label.SetSizeRequest(300, -1);
        label.SetMnemonicWidget(control);

        row.Append(label);
        row.Append(control);

        return row;
    }

    /// <summary>
    /// A titled frame per section. Orca announces the frame's label on entry,
    /// which chunks a long window of controls into groups you can remember -
    /// the same job the separators do in the menus.
    /// </summary>
    private static Gtk_.Widget Group(string title, IReadOnlyList<Gtk_.Widget> children)
    {
        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 8; box.MarginBottom = 8; box.MarginStart = 8; box.MarginEnd = 8;

        foreach (var child in children) box.Append(child);

        var frame = Gtk_.Frame.New(title);
        frame.SetChild(box);

        return frame;
    }

    private static Gtk_.Widget Note(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;

        return label;
    }
}
