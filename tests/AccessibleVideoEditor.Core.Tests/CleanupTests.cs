using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The cleanup passes that are only cheap because the edit is text. Removing
/// every "um" from an hour of footage is a day's work with a waveform and
/// seconds with word timings.
/// </summary>
public class CleanupTests
{
    private static Project WithWords(params (string Text, double Start, double End)[] words)
    {
        var project = Project.CreateDefault("cleanup");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = words.Min(w => w.Start),
            SourceOut = words.Max(w => w.End),
            Text = string.Join(' ', words.Select(w => w.Text)),
            Words = words.Select(w => new Word(w.Text, w.Start, w.End)).ToList(),
        });

        return project;
    }

    [Fact]
    public void Filler_words_are_cut_and_the_video_gets_shorter()
    {
        var project = WithWords(
            ("hello", 0, 0.5), ("um", 0.6, 1.0), ("there", 1.1, 1.6));

        var before = TimelineMap.Build(project).Duration;
        var result = TranscriptCleanup.RemoveFillers(project);

        Assert.True(result.Changed);
        Assert.True(TimelineMap.Build(project).Duration < before);
        Assert.Contains("1 filler word", result.Description);
    }

    [Fact]
    public void Fillers_are_marked_cut_rather_than_deleted()
    {
        // A pass that removed something it should not have is then one
        // keystroke from being put back.
        var project = WithWords(("hello", 0, 0.5), ("uh", 0.6, 1.0), ("there", 1.1, 1.6));

        TranscriptCleanup.RemoveFillers(project);

        Assert.Contains(project.Spine, e => !e.Enabled);
        Assert.Contains(project.Spine, e => e.Enabled);
    }

    [Fact]
    public void A_take_with_no_fillers_is_left_alone_and_says_so()
    {
        var project = WithWords(("hello", 0, 0.5), ("there", 0.6, 1.0));
        var before = project.Spine.Count;

        var result = TranscriptCleanup.RemoveFillers(project);

        Assert.False(result.Changed);
        Assert.Equal(before, project.Spine.Count);
        Assert.Contains("no filler words", result.Description);
    }

    [Fact]
    public void Punctuation_around_a_filler_does_not_hide_it()
    {
        var project = WithWords(("hello", 0, 0.5), ("um,", 0.6, 1.0), ("there", 1.1, 1.6));

        Assert.True(TranscriptCleanup.RemoveFillers(project).Changed);
    }

    [Fact]
    public void Real_words_that_merely_contain_a_filler_are_not_touched()
    {
        // "umbrella" starts with "um"; a pass that ate it would be worse than
        // no pass at all.
        var project = WithWords(("the", 0, 0.4), ("umbrella", 0.5, 1.2));

        Assert.False(TranscriptCleanup.RemoveFillers(project).Changed);
    }

    [Fact]
    public void A_custom_filler_list_is_honoured()
    {
        var project = WithWords(("so", 0, 0.4), ("basically", 0.5, 1.2), ("yes", 1.3, 1.6));

        Assert.True(TranscriptCleanup.RemoveFillers(project, ["basically"]).Changed);
    }

    // ---- silences ---------------------------------------------------------

    [Fact]
    public void Long_gaps_between_words_are_cut()
    {
        var project = WithWords(("hello", 0, 0.5), ("there", 3.0, 3.5));

        var result = TranscriptCleanup.RemoveSilences(project, longerThan: 1.0);

        Assert.True(result.Changed);
        Assert.Contains("1 gap", result.Description);
    }

    [Fact]
    public void The_rhythm_of_ordinary_speech_is_left_alone()
    {
        // Short pauses are the shape of a sentence; removing them makes
        // delivery sound frantic.
        var project = WithWords(("hello", 0, 0.5), ("there", 0.8, 1.3), ("everyone", 1.5, 2.2));

        Assert.False(TranscriptCleanup.RemoveSilences(project, longerThan: 1.0).Changed);
    }

    [Fact]
    public void The_threshold_is_adjustable()
    {
        var project = WithWords(("hello", 0, 0.5), ("there", 1.2, 1.7));

        Assert.False(TranscriptCleanup.RemoveSilences(project, longerThan: 1.0).Changed);
        Assert.True(TranscriptCleanup.RemoveSilences(project, longerThan: 0.5).Changed);
    }

    // ---- pace -------------------------------------------------------------

    [Fact]
    public void Pace_is_reported_in_words_per_minute()
    {
        // Two words in one second is 120 a minute.
        var project = WithWords(("hello", 0, 0.4), ("there", 0.5, 1.0));

        Assert.Contains("120 words per minute", TranscriptCleanup.PaceReport(project, TimelineMap.Build(project)));
    }

    [Fact]
    public void Pace_drift_between_segments_is_pointed_out()
    {
        // You cannot hear your own tempo change across twenty minutes, and it
        // is what makes a video feel rushed at the end.
        var project = WithWords(("one", 0, 1.0), ("two", 1.0, 2.0));

        var source = project.Sources[0].Id;
        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source, SourceIn = 10, SourceOut = 12,
            Text = "much faster now indeed truly",
            Words =
            [
                new Word("much", 10, 10.3), new Word("faster", 10.4, 10.7),
                new Word("now", 10.8, 11.0), new Word("indeed", 11.1, 11.4),
                new Word("truly", 11.5, 11.9),
            ],
        });

        var report = TranscriptCleanup.PaceReport(project, TimelineMap.Build(project));

        Assert.Contains("Fastest", report);
        Assert.Contains("slowest", report);
    }

    [Fact]
    public void A_project_with_nothing_transcribed_says_so()
    {
        var project = Project.CreateDefault("bare");

        Assert.Contains("nothing transcribed", TranscriptCleanup.PaceReport(project, TimelineMap.Build(project)));
    }
}

/// <summary>
/// Judging what ffmpeg measured. This is the part that replaces looking:
/// exposure, cast and levels are things a sighted editor takes in at a glance.
/// </summary>
public class QualityAnalyserTests
{
    private static string Stats(
        double luma = 125, double low = 40, double high = 200,
        double u = 128, double v = 128, double saturation = 40,
        double loudness = -18, double peak = -6, double noise = -60) =>
        $"lavfi.signalstats.YAVG={luma}\nlavfi.signalstats.YLOW={low}\nlavfi.signalstats.YHIGH={high}\n"
        + $"lavfi.signalstats.UAVG={u}\nlavfi.signalstats.VAVG={v}\nlavfi.signalstats.SATAVG={saturation}\n"
        + $"I:         {loudness} LUFS\nPeak_level: {peak}\nNoise_floor: {noise}\n";

    [Fact]
    public void Well_exposed_footage_produces_no_findings()
    {
        var report = QualityAnalyser.Interpret("take1.mkv", Stats());

        Assert.Empty(report.Findings);
        Assert.Contains("looks and sounds fine", report.Announce());
    }

    [Fact]
    public void Under_and_over_exposure_are_both_reported()
    {
        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(luma: 40)).Findings,
            f => f.Contains("under-exposed"));

        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(luma: 200)).Findings,
            f => f.Contains("over-exposed"));
    }

    [Fact]
    public void Clipped_highlights_and_crushed_blacks_are_reported()
    {
        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(high: 254)).Findings,
            f => f.Contains("highlights are clipped"));

        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(low: 2)).Findings,
            f => f.Contains("blacks are crushed"));
    }

    [Fact]
    public void A_colour_cast_is_named_by_direction()
    {
        // Which way it is wrong is the whole point; "the colour is off" is not
        // actionable.
        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(v: 150)).Findings,
            f => f.Contains("warm colour cast"));

        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(u: 150)).Findings,
            f => f.Contains("cool colour cast"));
    }

    [Fact]
    public void Audio_that_is_too_quiet_or_clipping_is_reported()
    {
        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(loudness: -36)).Findings,
            f => f.Contains("very quiet"));

        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(peak: -0.1)).Findings,
            f => f.Contains("clipping"));
    }

    [Fact]
    public void A_noisy_floor_is_reported()
    {
        Assert.Contains(
            QualityAnalyser.Interpret("a", Stats(noise: -30)).Findings,
            f => f.Contains("noisy"));
    }

    // ---- shot matching ----------------------------------------------------

    [Fact]
    public void Matching_takes_are_reported_as_matching()
    {
        var reports = new[]
        {
            QualityAnalyser.Interpret("take1", Stats()),
            QualityAnalyser.Interpret("take2", Stats(luma: 128)),
        };

        Assert.Contains("match each other", QualityAnalyser.CompareShots(reports));
    }

    [Fact]
    public void A_take_that_is_darker_than_the_rest_is_named()
    {
        // Not whether each take is acceptable alone, but whether they match -
        // which is the usual reason amateur footage looks amateur.
        var reports = new[]
        {
            QualityAnalyser.Interpret("take1", Stats(luma: 130)),
            QualityAnalyser.Interpret("take2", Stats(luma: 130)),
            QualityAnalyser.Interpret("take3", Stats(luma: 70)),
        };

        var comparison = QualityAnalyser.CompareShots(reports);

        Assert.Contains("take3", comparison);
        Assert.Contains("darker", comparison);
    }

    [Fact]
    public void A_take_recorded_quieter_than_the_others_is_named()
    {
        var reports = new[]
        {
            QualityAnalyser.Interpret("take1", Stats(loudness: -16)),
            QualityAnalyser.Interpret("take2", Stats(loudness: -16)),
            QualityAnalyser.Interpret("take3", Stats(loudness: -28)),
        };

        var comparison = QualityAnalyser.CompareShots(reports);

        Assert.Contains("take3", comparison);
        Assert.Contains("quieter", comparison);
    }

    [Fact]
    public void One_take_cannot_be_compared_with_anything()
    {
        Assert.Contains(
            "at least two",
            QualityAnalyser.CompareShots([QualityAnalyser.Interpret("only", Stats())]));
    }
}
