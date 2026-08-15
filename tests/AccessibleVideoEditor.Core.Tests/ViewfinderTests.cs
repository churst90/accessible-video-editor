using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Vision;

namespace AccessibleVideoEditor.Core.Tests;

public class ViewfinderTests
{
    private static FramingError Framed => new(
        FaceVisible: true,
        CentreX: 0.5,
        EyelineY: ViewfinderSonifier.TargetEyelineY,
        FaceWidth: ViewfinderSonifier.TargetFaceWidth);

    [Fact]
    public void On_target_the_tone_stops()
    {
        // A tone playing through the whole take is unusable. Silence is the
        // signal that you can start talking.
        var state = ViewfinderSonifier.Evaluate(Framed);

        Assert.True(state.Locked);
        Assert.True(state.Silent);
        Assert.Equal("framed", state.Guidance);
    }

    [Theory]
    [InlineData(0.75, "move left")]
    [InlineData(0.25, "move right")]
    public void Horizontal_error_pans_towards_the_face_and_says_which_way(double centreX, string expected)
    {
        var state = ViewfinderSonifier.Evaluate(Framed with { CentreX = centreX });

        Assert.False(state.Silent);
        Assert.Equal(Math.Sign(centreX - 0.5), Math.Sign(state.Pan));
        Assert.Contains(expected, state.Guidance);
    }

    [Fact]
    public void Vertical_target_is_the_upper_third_not_the_centre()
    {
        // Centring a face vertically is bad framing; a tool that trains you
        // into it is worse than no tool.
        var centred = ViewfinderSonifier.Evaluate(Framed with { EyelineY = 0.5 });

        Assert.False(centred.Locked);
        Assert.Contains("camera", centred.Guidance);
    }

    [Fact]
    public void Being_too_close_beeps_faster_than_being_too_far()
    {
        var close = ViewfinderSonifier.Evaluate(Framed with { FaceWidth = 0.5 });
        var far = ViewfinderSonifier.Evaluate(Framed with { FaceWidth = 0.12 });

        Assert.True(close.BeepsPerSecond > far.BeepsPerSecond);
        Assert.Contains("move back", close.Guidance);
        Assert.Contains("move closer", far.Guidance);
    }

    [Fact]
    public void Cropping_is_reported_before_anything_else()
    {
        var state = ViewfinderSonifier.Evaluate(Framed with { CentreX = 0.8, CroppedTop = true });

        Assert.Equal("you are cropped at the top", state.Guidance);
    }

    [Fact]
    public void No_face_is_its_own_state()
    {
        var state = ViewfinderSonifier.Evaluate(FramingError.NoFace);

        Assert.False(state.Locked);
        Assert.Equal("no face detected", state.Guidance);
    }

    // ---- tracking --------------------------------------------------------

    private static FaceObservation Face(double x, double y, double size = 0.28, double confidence = 0.9) =>
        new(x - size / 2, y, size, size, y + size * 0.35, y + size * 0.35, confidence);

    [Fact]
    public void Tracker_smooths_jitter_instead_of_following_it()
    {
        // A few pixels of detector jitter is invisible on screen and audible as
        // a warbling tone.
        var tracker = new FaceTracker { Smoothing = 0.35 };

        tracker.Track([Face(0.5, 0.2)], 0);
        var jittered = tracker.Track([Face(0.9, 0.2)], 0.033);

        Assert.InRange(jittered.CentreX, 0.5, 0.75);
    }

    [Fact]
    public void Tracker_snaps_on_first_acquisition()
    {
        var tracker = new FaceTracker();
        var first = tracker.Track([Face(0.8, 0.2)], 0);

        Assert.True(first.FaceVisible);
        Assert.Equal(0.8, first.CentreX, 3);
    }

    [Fact]
    public void A_dropped_frame_does_not_announce_a_lost_face()
    {
        var tracker = new FaceTracker { LostGrace = 0.5 };
        tracker.Track([Face(0.5, 0.2)], 0);

        var blip = tracker.Track([], 0.1);
        var gone = tracker.Track([], 1.0);

        Assert.True(blip.FaceVisible);
        Assert.False(gone.FaceVisible);
    }

    [Fact]
    public void Tracker_follows_the_nearest_face_not_the_most_confident()
    {
        // Someone walking past in the background is not the subject.
        var tracker = new FaceTracker();
        var subject = Face(0.5, 0.2, size: 0.30, confidence: 0.8);
        var passerby = Face(0.9, 0.5, size: 0.08, confidence: 0.99);

        var tracked = tracker.Track([passerby, subject], 0);

        Assert.Equal(0.5, tracked.CentreX, 2);
    }

    [Fact]
    public void Low_confidence_detections_are_ignored()
    {
        var tracker = new FaceTracker();
        var tracked = tracker.Track([Face(0.5, 0.2, confidence: 0.2)], 0);

        Assert.False(tracked.FaceVisible);
    }
}
