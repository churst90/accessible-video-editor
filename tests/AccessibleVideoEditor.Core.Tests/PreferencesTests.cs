using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Settings;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The preferences window's behaviour, which is all here rather than in the
/// window: what a save says it changed, and which values are corrected before
/// they can fail somewhere with no visible connection to the setting.
/// </summary>
public class PreferencesTests
{
    [Fact]
    public void A_save_that_changed_nothing_says_so()
    {
        var settings = new AppSettings();

        // Silence would be indistinguishable from a save that failed.
        Assert.Equal("settings saved, though nothing changed", Preferences.Summarise(settings, settings.Copy()));
    }

    [Fact]
    public void A_save_names_the_setting_that_changed()
    {
        var before = new AppSettings();
        var after = before.Copy();

        after.Behaviour.Verbosity = Verbosity.Verbose;

        var spoken = Preferences.Summarise(before, after);

        Assert.Contains("speech is verbose", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("earcons", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Several_changes_are_read_as_one_sentence()
    {
        var before = new AppSettings();
        var after = before.Copy();

        after.Behaviour.Earcons = false;
        after.Behaviour.AutosaveMinutes = 0;

        var spoken = Preferences.Summarise(before, after);

        Assert.Contains("earcons are off", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and autosave is off", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Turning_autosave_off_is_said_as_off_rather_than_as_zero()
    {
        var before = new AppSettings();
        var after = before.Copy();

        after.Behaviour.AutosaveMinutes = 0;

        // "autosave every 0 minutes" is a number you have to interpret.
        Assert.Contains("autosave is off", Preferences.Summarise(before, after), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0 minutes", Preferences.Summarise(before, after), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_device_says_what_will_happen_instead()
    {
        // "no camera" alone leaves you wondering whether recording is broken.
        Assert.Contains(
            Preferences.Describe(new AppSettings()),
            phrase => phrase.Contains("no camera is chosen", StringComparison.Ordinal)
                      && phrase.Contains("arm a track", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unset_whisper_environment_says_the_consequence()
    {
        Assert.Contains(
            Preferences.Describe(new AppSettings()),
            phrase => phrase.Contains("nothing will be transcribed", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_setting_the_window_offers_can_be_spoken()
    {
        // The diff and the speech come from one table, so a setting that cannot
        // be described is a setting whose change would be silent.
        Assert.All(Preferences.Describe(new AppSettings()), phrase => Assert.NotEqual(string.Empty, phrase));
    }

    // ---- clamping ----------------------------------------------------------

    [Fact]
    public void An_odd_canvas_is_made_even_and_says_why()
    {
        var settings = new AppSettings();
        settings.Defaults.CanvasWidth = 1921;

        var corrections = Preferences.Clamp(settings);

        Assert.Equal(1920, settings.Defaults.CanvasWidth);
        Assert.Contains(corrections, c => c.Contains("odd number of pixels", StringComparison.Ordinal));
    }

    [Fact]
    public void A_zero_frame_rate_is_refused_rather_than_failing_at_render_time()
    {
        var settings = new AppSettings();
        settings.Defaults.Fps = 0;

        var corrections = Preferences.Clamp(settings);

        Assert.Equal(30, settings.Defaults.Fps);
        Assert.Contains(corrections, c => c.Contains("frame rate", StringComparison.Ordinal));
    }

    [Fact]
    public void A_negative_autosave_interval_becomes_off_rather_than_an_enormous_timer()
    {
        // Timers take an unsigned interval, so a negative value is not "off".
        var settings = new AppSettings();
        settings.Behaviour.AutosaveMinutes = -5;

        var corrections = Preferences.Clamp(settings);

        Assert.Equal(0, settings.Behaviour.AutosaveMinutes);
        Assert.Contains(corrections, c => c.Contains("autosave", StringComparison.Ordinal));
    }

    [Fact]
    public void A_loudness_target_outside_the_levellers_range_is_pulled_back()
    {
        var settings = new AppSettings();
        settings.Defaults.TargetLoudnessLufs = -200;

        Preferences.Clamp(settings);

        Assert.Equal(-70, settings.Defaults.TargetLoudnessLufs);
    }

    [Fact]
    public void A_blank_tool_path_goes_back_to_the_one_on_the_path()
    {
        var settings = new AppSettings();
        settings.Tools.Ffmpeg = "   ";

        var corrections = Preferences.Clamp(settings);

        Assert.Equal("ffmpeg", settings.Tools.Ffmpeg);
        Assert.Contains(corrections, c => c.Contains("ffmpeg", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_that_are_already_sensible_are_corrected_silently_because_there_is_nothing_to_correct()
    {
        // Offering to fix something that is already right is how a warning
        // stops being listened to.
        Assert.Empty(Preferences.Clamp(new AppSettings()));
    }

    [Fact]
    public void A_copy_is_detached_from_the_original()
    {
        var settings = new AppSettings();
        var copy = settings.Copy();

        copy.Behaviour.ChatBurst = 99;
        copy.Defaults.CanvasWidth = 640;

        Assert.Equal(6, settings.Behaviour.ChatBurst);
        Assert.Equal(1920, settings.Defaults.CanvasWidth);
    }

    // ---- the defaults actually arriving somewhere --------------------------

    [Fact]
    public void A_new_project_starts_from_the_defaults()
    {
        // These were stored, saved, and read by nothing: the settings version
        // of a key that does nothing when pressed.
        var settings = new AppSettings();
        settings.Defaults.CanvasWidth = 3840;
        settings.Defaults.CanvasHeight = 2160;
        settings.Defaults.Fps = 60;
        settings.Defaults.Snap = false;
        settings.Defaults.StillDuration = 7;
        settings.Defaults.RippleMode = RippleMode.Off;

        var project = Project.CreateDefault("Test");
        Preferences.ApplyDefaults(settings, project);

        Assert.Equal(3840, project.Settings.CanvasWidth);
        Assert.Equal(2160, project.Settings.CanvasHeight);
        Assert.Equal(60, project.Settings.Fps);
        Assert.False(project.Settings.Snap);
        Assert.Equal(7, project.Settings.StillDuration);
        Assert.Equal(RippleMode.Off, project.Settings.RippleMode);
    }

    [Fact]
    public void Verbosity_comes_from_one_place_rather_than_two()
    {
        // Behaviour and Defaults both held a verbosity. Two copies of one
        // setting disagree eventually, and the one the announcer reads wins
        // silently.
        var settings = new AppSettings();
        settings.Behaviour.Verbosity = Verbosity.Verbose;
        settings.Defaults.Verbosity = Verbosity.Terse;

        var project = Project.CreateDefault("Test");
        Preferences.ApplyDefaults(settings, project);

        Assert.Equal(Verbosity.Verbose, project.Settings.Verbosity);
    }

    [Fact]
    public void A_monitor_default_travels_as_a_name_not_an_id()
    {
        // PipeWire ids are per-session; a project reopened tomorrow would point
        // at nothing.
        var settings = new AppSettings();
        settings.Devices.MonitorOutput = "Arctis Nova Pro Wireless";

        var project = Project.CreateDefault("Test");
        Preferences.ApplyDefaults(settings, project);

        Assert.Equal("Arctis Nova Pro Wireless", project.Settings.MonitorOutputName);
    }

    [Fact]
    public void A_track_with_no_input_falls_back_to_the_preferred_device()
    {
        var settings = new AppSettings();
        settings.Devices.Camera = "Logitech BRIO";
        settings.Devices.Microphone = "Arctis Nova Pro";

        Assert.Equal("Logitech BRIO", Preferences.DefaultInputFor(settings, TrackInput.Camera));
        Assert.Equal("Arctis Nova Pro", Preferences.DefaultInputFor(settings, TrackInput.Microphone));

        // An image track records nothing, so there is nothing to fall back to.
        Assert.Null(Preferences.DefaultInputFor(settings, TrackInput.None));
    }

    [Fact]
    public void No_preferred_device_stays_null_rather_than_becoming_an_empty_name()
    {
        // An empty string would arm the track and announce "input " with
        // nothing after it.
        Assert.Null(Preferences.DefaultInputFor(new AppSettings(), TrackInput.Camera));
    }

    [Fact]
    public void A_stream_size_is_clamped_the_same_way_the_canvas_is()
    {
        var settings = new AppSettings();
        settings.Streaming.Width = 1919;
        settings.Streaming.Fps = 0;

        Preferences.Clamp(settings);

        Assert.Equal(1918, settings.Streaming.Width);
        Assert.Equal(30, settings.Streaming.Fps);
    }
}
