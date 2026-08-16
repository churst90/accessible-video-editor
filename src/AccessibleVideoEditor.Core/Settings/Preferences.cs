using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Settings;

/// <summary>
/// The behaviour behind the preferences window, kept out of the window.
///
/// Two questions live here, and neither of them is layout:
///
/// <list type="bullet">
/// <item><b>What did I just change?</b> A preferences dialog that answers "saved"
/// tells you the writing worked, which was never in doubt. What you cannot check
/// without sight is whether the thing you meant to change is the thing that
/// changed - so <see cref="Summarise"/> diffs the settings and names it.</item>
/// <item><b>Is this value usable?</b> A frame rate of zero is accepted by a spin
/// button and rejected by ffmpeg an hour later, at render time, with an error
/// about a filtergraph. <see cref="Clamp"/> catches those here and says what it
/// did, because a value silently corrected is a preference you will set twice.</item>
/// </list>
///
/// Both are pure functions over <see cref="AppSettings"/>, so the WPF head gets
/// them unchanged - see docs/CLIENTS.md.
/// </summary>
public static class Preferences
{
    /// <summary>
    /// Every setting the window can change, as the sentence that describes its
    /// current value.
    ///
    /// Phrases rather than values: two settings differ exactly when their
    /// phrases differ, so the diff and the speech come from one table and a new
    /// preference cannot be added to the window without becoming announceable.
    /// </summary>
    private static readonly (string Key, Func<AppSettings, string> Phrase)[] Watched =
    [
        ("name", s => s.DisplayName.Length == 0
            ? "your name is not set, so chat cannot tell when you are mentioned"
            : $"your name is {s.DisplayName}"),

        // ---- speech ------------------------------------------------------------
        ("verbosity", s => $"speech is {Lower(s.Behaviour.Verbosity)}"),
        ("earcons", s => s.Behaviour.Earcons ? "earcons are on" : "earcons are off"),
        ("follow", s => s.Behaviour.FollowPlayback
            ? "the view follows playback"
            : "the view stays put during playback"),

        // ---- saving ------------------------------------------------------------
        ("autosave", s => s.Behaviour.AutosaveMinutes <= 0
            ? "autosave is off"
            : $"autosave runs every {Minutes(s.Behaviour.AutosaveMinutes)}"),
        ("output", s => s.Behaviour.OutputDirectory.Length == 0
            ? "renders go to the project's own folder"
            : $"renders go to {s.Behaviour.OutputDirectory}"),

        // ---- new projects ------------------------------------------------------
        ("canvas", s => $"new projects are {s.Defaults.CanvasWidth} by {s.Defaults.CanvasHeight} "
                        + $"at {Rate(s.Defaults.Fps)}"),
        ("snap", s => s.Defaults.Snap ? "snapping starts on" : "snapping starts off"),
        ("ripple", s => $"ripple starts as {RippleWords(s.Defaults.RippleMode)}"),
        ("scrub", s => s.Defaults.AudioScrub
            ? $"audio scrub is on, {Seconds(s.Defaults.AudioScrubLength)} a step"
            : "audio scrub is off"),
        ("still", s => $"a still lasts {Seconds(s.Defaults.StillDuration)}"),
        ("kenburns", s => s.Defaults.KenBurnsByDefault
            ? "stills drift by default"
            : "stills are still by default"),
        ("loudness", s => $"renders are levelled to {Decibels(s.Defaults.TargetLoudnessLufs)} LUFS, "
                          + $"peaking at {Decibels(s.Defaults.TargetTruePeakDb)}"),

        // ---- devices -----------------------------------------------------------
        ("camera", s => Device("camera", s.Devices.Camera)),
        ("microphone", s => Device("microphone", s.Devices.Microphone)),
        ("monitor", s => s.Devices.MonitorOutput is { Length: > 0 } output
            ? $"monitoring goes to {output}"
            : "monitoring goes to the system default"),

        // ---- chat --------------------------------------------------------------
        ("chatburst", s => s.Behaviour.SpeakEveryChatMessage
            ? "every chat message is read"
            : $"chat stops being read past {s.Behaviour.ChatBurst} messages "
              + $"in {Seconds(s.Behaviour.ChatBurstWindow)}"),

        // ---- tools -------------------------------------------------------------
        ("ffmpeg", s => $"ffmpeg is {s.Tools.Ffmpeg}"),
        ("ffprobe", s => $"ffprobe is {s.Tools.Ffprobe}"),
        ("claude", s => $"the describe command is {s.Tools.Claude}"),
        ("whisper", s => s.Tools.WhisperPython.Length == 0
            ? "no Whisper environment is set, so nothing will be transcribed"
            : $"Whisper runs from {s.Tools.WhisperPython}"),
        ("cache", s => s.Tools.CacheDirectory.Length == 0
            ? "the cache lives beside each project"
            : $"the cache lives in {s.Tools.CacheDirectory}"),
    ];

    /// <summary>
    /// What changed, as something worth hearing.
    ///
    /// Silence would be the obvious implementation and the wrong one: pressing
    /// Save and hearing nothing is indistinguishable from pressing Save and
    /// having it fail, so an unchanged save says so explicitly.
    /// </summary>
    public static string Summarise(AppSettings before, AppSettings after)
    {
        var changes = Watched
            .Where(setting => setting.Phrase(before) != setting.Phrase(after))
            .Select(setting => setting.Phrase(after))
            .ToList();

        return changes.Count == 0
            ? "settings saved, though nothing changed"
            : $"settings saved. {Sentence(changes)}";
    }

    /// <summary>Every setting as it stands, for reading the whole lot back.</summary>
    public static IReadOnlyList<string> Describe(AppSettings settings) =>
        [.. Watched.Select(setting => setting.Phrase(settings))];

    /// <summary>
    /// Stamps the application's defaults onto a project that has just been
    /// created.
    ///
    /// <see cref="AppSettings.Defaults"/> and <see cref="AppSettings.Devices"/>
    /// were both stored and never read by anything, which is the settings
    /// version of a key that does nothing: you change it, it is written to
    /// disk, and no behaviour follows. A preference the window offers has to
    /// arrive somewhere, and this is where.
    ///
    /// Verbosity and earcons come from <see cref="AppSettings.Behaviour"/>
    /// rather than from <see cref="AppSettings.Defaults"/>, because both places
    /// held a copy and two copies of one setting eventually disagree.
    /// </summary>
    public static void ApplyDefaults(AppSettings settings, Project project)
    {
        var defaults = settings.Defaults;

        project.Settings.CanvasWidth = defaults.CanvasWidth;
        project.Settings.CanvasHeight = defaults.CanvasHeight;
        project.Settings.Fps = defaults.Fps;
        project.Settings.SpanPadIn = defaults.SpanPadIn;
        project.Settings.SpanPadOut = defaults.SpanPadOut;
        project.Settings.JumpCutDuration = defaults.JumpCutDuration;
        project.Settings.SceneTransitionDuration = defaults.SceneTransitionDuration;
        project.Settings.RippleMode = defaults.RippleMode;
        project.Settings.AudioScrub = defaults.AudioScrub;
        project.Settings.AudioScrubLength = defaults.AudioScrubLength;
        project.Settings.StillDuration = defaults.StillDuration;
        project.Settings.KenBurnsByDefault = defaults.KenBurnsByDefault;
        project.Settings.Snap = defaults.Snap;
        project.Settings.TargetLoudnessLufs = defaults.TargetLoudnessLufs;
        project.Settings.TargetTruePeakDb = defaults.TargetTruePeakDb;

        project.Settings.Verbosity = settings.Behaviour.Verbosity;
        project.Settings.Earcons = settings.Behaviour.Earcons;

        // The monitor is a property of this machine, so it travels as a name
        // rather than as an id: ids are per-session on PipeWire and a project
        // opened tomorrow would point at nothing.
        if (settings.Devices.MonitorOutput is { Length: > 0 } monitor)
        {
            project.Settings.MonitorOutputName = monitor;
        }
    }

    /// <summary>
    /// The default input for a track that has none, or null when there is no
    /// default for that kind of track.
    ///
    /// Arming a track with nothing chosen used to end at "no input chosen,
    /// Control F5 to choose one" every single time. A remembered default is
    /// exactly the setting that stops that being a step you repeat.
    /// </summary>
    public static string? DefaultInputFor(AppSettings settings, TrackInput input) => input switch
    {
        TrackInput.Camera => Blank(settings.Devices.Camera),
        TrackInput.Microphone => Blank(settings.Devices.Microphone),
        _ => null,
    };

    private static string? Blank(string? value) => value is { Length: > 0 } ? value : null;

    /// <summary>
    /// Corrects values that would fail somewhere far away from here, and returns
    /// one sentence per correction.
    ///
    /// The rule for what belongs in this method: it is not validation of taste.
    /// Nothing here has an opinion about a good frame rate. Everything here is a
    /// value that some later stage - ffmpeg, the encoder, a timer - cannot act
    /// on at all, where the failure would arrive without any connection to the
    /// preference that caused it.
    /// </summary>
    public static IReadOnlyList<string> Clamp(AppSettings settings)
    {
        var corrections = new List<string>();

        // Timers take an unsigned interval. A negative one is not "off", it is
        // an enormous positive number of milliseconds.
        if (settings.Behaviour.AutosaveMinutes < 0)
        {
            settings.Behaviour.AutosaveMinutes = 0;
            corrections.Add("autosave cannot be negative, so it is off");
        }

        if (settings.Behaviour.ChatBurst < 1)
        {
            settings.Behaviour.ChatBurst = 1;
            corrections.Add("the chat burst has to be at least one message");
        }

        if (settings.Behaviour.ChatBurstWindow <= 0)
        {
            settings.Behaviour.ChatBurstWindow = 4;
            corrections.Add("the chat burst window has to be longer than nothing, so it is back to 4 seconds");
        }

        corrections.AddRange(ClampCanvas(settings.Defaults));
        corrections.AddRange(ClampStream(settings.Streaming));

        if (settings.Defaults.StillDuration <= 0)
        {
            settings.Defaults.StillDuration = 4;
            corrections.Add("a still with no duration would never appear, so it is back to 4 seconds");
        }

        // Short enough to be a click, long enough to be a word.
        if (settings.Defaults.AudioScrubLength is < 0.02 or > 2)
        {
            settings.Defaults.AudioScrubLength = Math.Clamp(settings.Defaults.AudioScrubLength, 0.02, 2);
            corrections.Add($"the scrub blip is now {Seconds(settings.Defaults.AudioScrubLength)}");
        }

        if (settings.Defaults.SpanPadIn < 0 || settings.Defaults.SpanPadOut < 0)
        {
            settings.Defaults.SpanPadIn = Math.Max(0, settings.Defaults.SpanPadIn);
            settings.Defaults.SpanPadOut = Math.Max(0, settings.Defaults.SpanPadOut);
            corrections.Add("padding cannot be negative; a negative pad would trim the words it exists to protect");
        }

        // loudnorm's own range. Outside it the filter refuses and the render
        // stops, which is a long way from the preference that caused it.
        if (settings.Defaults.TargetLoudnessLufs is < -70 or > -5)
        {
            settings.Defaults.TargetLoudnessLufs = Math.Clamp(settings.Defaults.TargetLoudnessLufs, -70, -5);
            corrections.Add(
                $"the loudness target is now {Decibels(settings.Defaults.TargetLoudnessLufs)} LUFS, "
                + "which is as far as the leveller goes");
        }

        if (settings.Defaults.TargetTruePeakDb is < -9 or > 0)
        {
            settings.Defaults.TargetTruePeakDb = Math.Clamp(settings.Defaults.TargetTruePeakDb, -9, 0);
            corrections.Add($"the peak ceiling is now {Decibels(settings.Defaults.TargetTruePeakDb)}");
        }

        if (settings.Tools.Ffmpeg.Trim().Length == 0)
        {
            settings.Tools.Ffmpeg = "ffmpeg";
            corrections.Add("ffmpeg is back to the one on your path");
        }

        if (settings.Tools.Ffprobe.Trim().Length == 0)
        {
            settings.Tools.Ffprobe = "ffprobe";
            corrections.Add("ffprobe is back to the one on your path");
        }

        if (settings.Tools.Claude.Trim().Length == 0)
        {
            settings.Tools.Claude = "claude";
            corrections.Add("the describe command is back to the one on your path");
        }

        return corrections;
    }

    private static IEnumerable<string> ClampCanvas(ProjectSettings defaults)
    {
        var (width, height, note) = EvenFrame(defaults.CanvasWidth, defaults.CanvasHeight, "the canvas");

        defaults.CanvasWidth = width;
        defaults.CanvasHeight = height;

        if (note is not null) yield return note;

        if (defaults.Fps is <= 0 or > 240)
        {
            defaults.Fps = defaults.Fps <= 0 ? 30 : 240;
            yield return $"the frame rate is now {Rate(defaults.Fps)}";
        }
    }

    private static IEnumerable<string> ClampStream(StreamSettings streaming)
    {
        var (width, height, note) = EvenFrame(streaming.Width, streaming.Height, "the stream");

        streaming.Width = width;
        streaming.Height = height;

        if (note is not null) yield return note;

        if (streaming.Fps is <= 0 or > 120)
        {
            streaming.Fps = streaming.Fps <= 0 ? 30 : 120;
            yield return $"the stream is now {Rate(streaming.Fps)}";
        }
    }

    /// <summary>
    /// H.264 cannot encode an odd dimension, and the failure surfaces as
    /// "width not divisible by 2" from a filter three stages downstream of the
    /// setting that caused it.
    /// </summary>
    private static (int Width, int Height, string? Note) EvenFrame(int width, int height, string what)
    {
        var fixedWidth = Math.Max(16, width - (width % 2));
        var fixedHeight = Math.Max(16, height - (height % 2));

        return (fixedWidth, fixedHeight,
            fixedWidth == width && fixedHeight == height
                ? null
                : $"{what} is now {fixedWidth} by {fixedHeight}, because video cannot be an odd number of pixels");
    }

    // ---- saying numbers ----------------------------------------------------

    private static string Sentence(IReadOnlyList<string> parts) =>
        parts.Count == 1
            ? Capitalise(parts[0]) + "."
            : Capitalise(string.Join(", ", parts.Take(parts.Count - 1))) + $", and {parts[^1]}.";

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Lower(object value) => value.ToString()!.ToLowerInvariant();

    private static string Minutes(int minutes) =>
        minutes == 1 ? "minute" : $"{minutes} minutes";

    private static string Seconds(double seconds) =>
        seconds >= 1
            ? $"{seconds:0.##} seconds"
            : $"{seconds * 1000:0} milliseconds";

    private static string Rate(double fps) => $"{fps:0.###} frames per second";

    private static string Decibels(double value) => $"{value:0.#} decibels";

    private static string Device(string what, string? name) =>
        name is { Length: > 0 }
            ? $"the {what} is {name}"
            : $"no {what} is chosen, so one is picked when you arm a track";

    /// <summary>
    /// Ripple mode said as what it does rather than as its name. "All tracks"
    /// is the name of the setting; "everything after a cut moves" is the thing
    /// you are actually deciding.
    /// </summary>
    private static string RippleWords(RippleMode mode) => mode switch
    {
        RippleMode.Off => "off, so a delete leaves a gap",
        RippleMode.FocusedTrack => "this track only",
        _ => "all tracks, so everything after a cut moves together",
    };
}
