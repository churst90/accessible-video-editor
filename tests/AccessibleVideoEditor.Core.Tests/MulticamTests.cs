using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Several cameras on the same thing, lined up by sound, with one key per angle.
///
/// The sync is tested against synthetic envelopes with a known shift, because
/// that is the only way to assert it actually finds the right answer rather than
/// merely producing one - which is the failure mode of audio sync.
/// </summary>
public class MulticamTests
{
    private static Project TwoCameras(out SourceId wide, out SourceId close)
    {
        var project = Project.CreateDefault("multicam");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var a = new Source { Id = Ids.NewSource(), Path = "wide.mkv", Duration = 120 };
        var b = new Source { Id = Ids.NewSource(), Path = "close.mkv", Duration = 120 };
        project.Sources.AddRange([a, b]);

        wide = a.Id;
        close = b.Id;

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = a.Id,
            SourceIn = 10,
            SourceOut = 30,
            Text = "the first half and the second half",
            Words =
            [
                new("the", 10, 11), new("first", 11, 12), new("half", 12, 13),
                new("and", 15, 16), new("the", 16, 17), new("second", 17, 18),
                new("half", 18, 19),
            ],
        });

        return project;
    }

    private static MulticamGroup Synced(Project project, double offset = 0)
    {
        var group = project.Multicams[0];

        foreach (var angle in group.Angles)
        {
            angle.SyncConfidence = 0.9;
            angle.Offset = angle.Source == group.Angles[0].Source ? 0 : offset;
        }

        return group;
    }

    // ---- sync by sound ---------------------------------------------------

    /// <summary>An envelope with a few distinct bursts, so a shift is findable.</summary>
    private static WaveformData Envelope(SourceId id, int length, int shift, double duration)
    {
        var peaks = new float[length];

        for (var i = 0; i < length; i++)
        {
            var at = i - shift;
            peaks[i] = at is >= 0 and < 400 && (at / 20) % 3 == 0 ? 0.9f : 0.05f;
        }

        return new WaveformData(id, duration, peaks);
    }

    [Fact]
    public void Two_recordings_of_the_same_sound_find_the_shift_between_them()
    {
        var reference = Envelope(Ids.NewSource(), 600, shift: 0, duration: 60);
        var candidate = Envelope(Ids.NewSource(), 600, shift: 50, duration: 60);

        var result = MulticamSync.Align(reference, candidate);

        // 600 peaks over 60 seconds is a tenth of a second each, so a 50-peak
        // shift is five seconds.
        Assert.True(result.Trustworthy, $"confidence was {result.Confidence:0.00}");
        Assert.Equal(-5, result.Offset, 1);
    }

    [Fact]
    public void An_identical_pair_reports_no_offset_and_high_confidence()
    {
        var a = Envelope(Ids.NewSource(), 600, 0, 60);
        var b = Envelope(Ids.NewSource(), 600, 0, 60);

        var result = MulticamSync.Align(a, b);

        Assert.Equal(0, result.Offset, 1);
        Assert.True(result.Confidence > 0.8);
        Assert.Contains("same moment", result.Announce);
    }

    [Fact]
    public void Two_unrelated_recordings_are_reported_as_not_matching()
    {
        // The important case. Correlation always produces a best shift, and
        // reporting that as a sync is how a whole edit ends up a second out.
        var a = Envelope(Ids.NewSource(), 600, 0, 60);

        var noise = new float[600];
        for (var i = 0; i < noise.Length; i++) noise[i] = (i * 37 % 11) / 100f;

        var result = MulticamSync.Align(a, new WaveformData(Ids.NewSource(), 60, noise));

        Assert.False(result.Trustworthy);
        Assert.Contains("did not match", result.Announce);
    }

    [Fact]
    public void A_recording_too_short_to_sync_says_so()
    {
        var tiny = new WaveformData(Ids.NewSource(), 1, [0.1f, 0.2f]);
        var full = Envelope(Ids.NewSource(), 600, 0, 60);

        Assert.Contains("too short", MulticamSync.Align(full, tiny).Announce);
    }

    [Fact]
    public void Confidence_is_described_in_words_not_only_as_a_number()
    {
        Assert.Contains("strong", MulticamSync.DescribeConfidence(0.9));
        Assert.Contains("weak", MulticamSync.DescribeConfidence(0.4));
        Assert.Contains("no real match", MulticamSync.DescribeConfidence(0.1));
    }

    // ---- the group -------------------------------------------------------

    [Fact]
    public void A_group_needs_at_least_two_angles()
    {
        var project = TwoCameras(out var wide, out _);

        var result = MulticamOperations.Create(project, "interview", [wide]);

        Assert.False(result.Changed);
        Assert.Contains("at least two", result.Description);
    }

    [Fact]
    public void Creating_a_group_says_that_syncing_is_the_next_step()
    {
        // An unsynced group cuts to the right camera at the wrong moment, and
        // nothing about that is audible.
        var project = TwoCameras(out var wide, out var close);

        var result = MulticamOperations.Create(project, "interview", [wide, close]);

        Assert.True(result.Changed);
        Assert.Contains("Sync", result.Description);
    }

    [Fact]
    public void Angles_are_named_after_their_files_and_can_be_renamed()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);

        Assert.Equal("wide", project.Multicams[0].Angles[0].Name);

        MulticamOperations.RenameAngle(project, project.Multicams[0].Id, 1, "over the shoulder");
        Assert.Equal("over the shoulder", project.Multicams[0].Angles[1].Name);
    }

    [Fact]
    public void Applying_a_sync_names_the_angles_that_did_not_match()
    {
        // Which camera is unreliable decides whether you can use it. A count
        // does not tell you that.
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);

        var result = MulticamOperations.ApplySync(project, project.Multicams[0].Id,
            new Dictionary<SourceId, SyncResult>
            {
                [wide] = new(0, 0.95, "reference"),
                [close] = new(1.2, 0.2, "weak"),
            });

        Assert.True(result.Changed);
        Assert.Contains("close", result.Description);
        Assert.Contains("check by ear", result.Description);
    }

    [Fact]
    public void A_sync_where_nothing_matched_is_refused_with_what_to_try()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);

        var result = MulticamOperations.ApplySync(project, project.Multicams[0].Id,
            new Dictionary<SourceId, SyncResult>
            {
                [wide] = new(0, 0.1, "no"),
                [close] = new(0, 0.05, "no"),
            });

        Assert.False(result.Changed);
        Assert.Contains("same take", result.Description);
    }

    // ---- switching -------------------------------------------------------

    [Fact]
    public void Switching_to_an_unsynced_angle_is_refused_rather_than_cut_wrongly()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);

        var result = MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.False(result.Changed);
        Assert.Contains("not synced", result.Description);
    }

    [Fact]
    public void Switching_splits_at_the_cursor_and_repoints_the_second_half()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project);

        var result = MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.True(result.Changed);
        Assert.Equal(2, project.Spine.Count);
        Assert.Equal(wide, ((SpanElement)project.Spine[0]).Source);
        Assert.Equal(close, ((SpanElement)project.Spine[1]).Source);
    }

    [Fact]
    public void A_switch_lands_at_the_same_moment_in_the_other_camera()
    {
        // The whole point of the offset. Cutting at 5 seconds into a segment
        // that starts 10 seconds into the wide shot means 15 seconds in - and
        // 12 in a camera that started 3 seconds later.
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project, offset: 3);

        MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.Equal(12, ((SpanElement)project.Spine[1]).SourceIn, 2);
    }

    [Fact]
    public void Switching_does_not_change_how_long_the_programme_is()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project);

        var before = TimelineMap.Build(project).Duration;
        MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.Equal(before, TimelineMap.Build(project).Duration, 2);
    }

    [Fact]
    public void The_words_stay_with_the_segment_when_the_camera_changes()
    {
        // The transcript belongs to the take, not to the camera, so switching
        // must leave exactly what a plain split would have left.
        var switched = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(switched, "interview", [wide, close]);
        Synced(switched);
        MulticamOperations.SwitchTo(switched, switched.Multicams[0].Id, 1, 5);

        var plain = TwoCameras(out _, out _);
        EditOperations.SplitAt(plain, 5);

        Assert.Equal(
            ((SpanElement)plain.Spine[1]).Text,
            ((SpanElement)switched.Spine[1]).Text);

        Assert.NotEmpty(((SpanElement)switched.Spine[1]).Text);
    }

    [Fact]
    public void Cutting_to_a_camera_that_was_not_running_then_is_refused()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project, offset: 100);

        var result = MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.False(result.Changed);
        Assert.Contains("had not started", result.Description);
    }

    [Fact]
    public void An_angle_that_does_not_exist_is_named_in_the_refusal()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project);

        var result = MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 5, 5);

        Assert.False(result.Changed);
        Assert.Contains("no angle 6", result.Description);
    }

    [Fact]
    public void Switching_announces_the_name_of_the_camera_not_its_number()
    {
        var project = TwoCameras(out var wide, out var close);
        MulticamOperations.Create(project, "interview", [wide, close]);
        Synced(project);

        var result = MulticamOperations.SwitchTo(project, project.Multicams[0].Id, 1, 5);

        Assert.Contains("close", result.Description);
    }
}
