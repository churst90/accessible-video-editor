using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The parts of recording that can be tested without opening a camera: what
/// ffmpeg is asked to do, and what its measurements are taken to mean. That is
/// also where the mistakes which cost you a take actually live.
/// </summary>
public class RecorderTests
{
    [Fact]
    public void A_camera_recording_captures_the_microphone_too()
    {
        // A talking-head take with no sound is a wasted take.
        var arguments = Recorder.CameraArguments("/dev/video0", "alsa_input.usb", "/tmp/out.mkv");

        Assert.Contains("v4l2", arguments);
        Assert.Contains("/dev/video0", arguments);
        Assert.Contains("pulse", arguments);
        Assert.Contains("alsa_input.usb", arguments);
        Assert.Contains("aac", arguments);
        Assert.Equal("/tmp/out.mkv", arguments[^1]);
    }

    [Fact]
    public void A_camera_with_no_microphone_records_silent_rather_than_failing()
    {
        var arguments = Recorder.CameraArguments("/dev/video0", null, "/tmp/out.mkv");

        Assert.DoesNotContain("pulse", arguments);
        Assert.DoesNotContain("aac", arguments);
        Assert.Contains("libx264", arguments);
    }

    [Fact]
    public void An_audio_only_recording_never_touches_the_camera()
    {
        var arguments = Recorder.MicrophoneArguments("alsa_input.usb", "/tmp/out.m4a");

        Assert.DoesNotContain("v4l2", arguments);
        Assert.DoesNotContain("libx264", arguments);
        Assert.Contains("pulse", arguments);
    }

    [Fact]
    public void The_probes_are_bounded_to_one_second()
    {
        // The check opens the device, so it must be over quickly.
        Assert.Contains("1", Recorder.MicrophoneProbeArguments("src"));
        Assert.Contains("-t", Recorder.MicrophoneProbeArguments("src"));
        Assert.Contains("-t", Recorder.CameraProbeArguments("/dev/video0"));
    }

    // ---- interpreting what came back --------------------------------------

    private const string Silent = "[Parsed_volumedetect_0 @ 0x1] mean_volume: -91.0 dB\n"
                                  + "[Parsed_volumedetect_0 @ 0x1] max_volume: -90.3 dB";

    private const string Speech = "[Parsed_volumedetect_0 @ 0x1] mean_volume: -24.6 dB\n"
                                  + "[Parsed_volumedetect_0 @ 0x1] max_volume: -6.2 dB";

    private const string Clipping = "[Parsed_volumedetect_0 @ 0x1] mean_volume: -8.0 dB\n"
                                    + "[Parsed_volumedetect_0 @ 0x1] max_volume: 0.0 dB";

    [Fact]
    public void A_silent_microphone_fails_the_check_and_says_why()
    {
        // This is the whole point of the check: an hour of footage from a muted
        // microphone is unrecoverable, and it is entirely preventable.
        var result = Recorder.InterpretMicrophone("Arctis Nova Pro", Silent);

        Assert.False(result.Ok);
        Assert.Contains("silent", result.Message);
        Assert.Contains("muted", result.Message);
    }

    [Fact]
    public void Ordinary_speech_passes_and_reports_its_level()
    {
        var result = Recorder.InterpretMicrophone("Arctis Nova Pro", Speech);

        Assert.True(result.Ok);
        Assert.False(result.IsWarning);
        Assert.Equal(-24.6, result.LevelDb!.Value, 1);
    }

    [Fact]
    public void Clipping_warns_but_does_not_refuse_to_record()
    {
        // Too loud is recoverable and the take may still be the one you want;
        // silence is not. So one warns and the other refuses.
        var result = Recorder.InterpretMicrophone("Arctis Nova Pro", Clipping);

        Assert.True(result.Ok);
        Assert.True(result.IsWarning);
        Assert.Contains("clipping", result.Message);
    }

    [Fact]
    public void A_microphone_that_reported_nothing_measurable_fails()
    {
        var result = Recorder.InterpretMicrophone("Nothing", "no measurements here");

        Assert.False(result.Ok);
        Assert.Contains("no measurable audio", result.Message);
    }

    [Fact]
    public void A_black_picture_fails_the_check()
    {
        var result = Recorder.InterpretCamera(
            "Laptop Webcam",
            "[blackdetect @ 0x1] black_start:0 black_end:1.0 black_duration:1.0");

        Assert.False(result.Ok);
        Assert.Contains("black", result.Message);
        Assert.Contains("lens", result.Message);
    }

    [Fact]
    public void A_camera_producing_picture_passes()
    {
        var result = Recorder.InterpretCamera("Laptop Webcam", "frame= 30 fps=30 q=-1.0");

        Assert.True(result.Ok);
        Assert.Contains("producing picture", result.Message);
    }

    [Theory]
    [InlineData("mean_volume: -24.6 dB", -24.6)]
    [InlineData("mean_volume: 0.0 dB", 0.0)]
    [InlineData("mean_volume: -inf dB", null)]
    [InlineData("nothing at all", null)]
    public void Mean_volume_parsing_handles_what_ffmpeg_actually_prints(string output, double? expected)
    {
        Assert.Equal(expected, Recorder.ParseMeanVolume(output));
    }
}

/// <summary>
/// Multi-input interfaces and monitoring. A two-input interface presents as one
/// stereo source, so recording it whole puts the microphone on one side and
/// silence on the other - which sounds like a broken take and has no meter to
/// notice it on.
/// </summary>
public class InputChannelTests
{
    [Fact]
    public void Recording_a_single_input_of_an_interface_pans_it_to_mono()
    {
        Assert.Equal("pan=mono|c0=c0", Recorder.PanFilter(AccessibleVideoEditor.Core.Model.InputChannel.Left));
        Assert.Equal("pan=mono|c0=c1", Recorder.PanFilter(AccessibleVideoEditor.Core.Model.InputChannel.Right));
    }

    [Fact]
    public void Taking_both_channels_adds_no_filter_at_all()
    {
        Assert.Null(Recorder.PanFilter(AccessibleVideoEditor.Core.Model.InputChannel.All));
    }

    [Fact]
    public void The_channel_choice_reaches_the_ffmpeg_command()
    {
        var arguments = Recorder.MicrophoneArguments(
            "alsa_input.usb-Focusrite", "/tmp/out.m4a", AccessibleVideoEditor.Core.Model.InputChannel.Left);

        Assert.Contains("-af", arguments);
        Assert.Contains("pan=mono|c0=c0", arguments);
    }

    [Fact]
    public void A_camera_take_can_also_pick_one_input_of_its_microphone()
    {
        var arguments = Recorder.CameraArguments(
            "/dev/video0", "alsa_input.usb-Focusrite", "/tmp/out.mkv",
            AccessibleVideoEditor.Core.Model.InputChannel.Right);

        Assert.Contains("pan=mono|c0=c1", arguments);
        Assert.Contains("v4l2", arguments);
    }

    [Fact]
    public void With_both_channels_the_command_stays_as_simple_as_it_was()
    {
        var arguments = Recorder.MicrophoneArguments("src", "/tmp/out.m4a");

        Assert.DoesNotContain("-af", arguments);
    }
}
