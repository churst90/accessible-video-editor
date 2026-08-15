using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The parts of rendering that can be checked without running ffmpeg: what it
/// is asked to do, and what comes out of the timeline as captions.
/// </summary>
public class RenderTests
{
    /// <summary>The value ffmpeg was given for a flag, or null.</summary>
    private static string? ValueOf(IReadOnlyList<string> arguments, string flag)
    {
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == flag) return arguments[i + 1];
        }

        return null;
    }

    private static int PositionOf(IReadOnlyList<string> arguments, string flag)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == flag) return i;
        }

        return -1;
    }

    private static Project WithSegment(out PlacedElement placed, Action<SpanElement>? configure = null)
    {
        var project = Project.CreateDefault("render");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        var span = new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 10,
            SourceOut = 15,
            Text = "hello there",
        };

        configure?.Invoke(span);
        project.Spine.Add(span);

        placed = TimelineMap.Build(project).Elements[0];
        return project;
    }

    // ---- captions ---------------------------------------------------------

    [Fact]
    public void Captions_come_out_of_the_edit_already_timed()
    {
        // The whole reason for keeping the transcript beside the cut: the words
        // are already there, already timed, already corrected.
        var project = WithSegment(out _);
        var srt = Captions.Build(project, TimelineMap.Build(project));

        Assert.Contains("1\n", srt);
        Assert.Contains("00:00:00,000 --> 00:00:05,000", srt);
        Assert.Contains("hello there", srt);
    }

    [Fact]
    public void A_segment_with_captions_switched_off_produces_no_cue()
    {
        var project = WithSegment(out _, span => span.Captioned = false);

        Assert.Equal(string.Empty, Captions.Build(project, TimelineMap.Build(project)).Trim());
    }

    [Fact]
    public void An_overridden_caption_wins_over_the_transcript()
    {
        var project = WithSegment(out _, span => span.Caption = "Hello There, spelled properly");
        var srt = Captions.Build(project, TimelineMap.Build(project));

        Assert.Contains("Hello There, spelled properly", srt);
        Assert.DoesNotContain("hello there\n", srt);
    }

    [Fact]
    public void Segments_with_nothing_to_say_are_skipped_rather_than_left_empty()
    {
        // An empty cue shows as a flicker of nothing on screen.
        var project = WithSegment(out _);
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Titles"),
        });

        var srt = Captions.Build(project, TimelineMap.Build(project));

        // One cue only: the card has nothing to say.
        Assert.Equal(2, srt.Split("-->").Length);
    }

    [Theory]
    [InlineData(0, "00:00:00,000")]
    [InlineData(1.5, "00:00:01,500")]
    [InlineData(3725.25, "01:02:05,250")]
    public void Srt_timestamps_use_a_comma_before_the_milliseconds(double seconds, string expected)
    {
        Assert.Equal(expected, Captions.Stamp(seconds));
    }

    // ---- segment filters --------------------------------------------------

    [Fact]
    public void A_segment_seeks_before_the_input_so_only_what_is_needed_is_decoded()
    {
        var project = WithSegment(out var placed);
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "take1.mkv", "out.mkv");

        var seek = PositionOf(arguments, "-ss");
        var input = PositionOf(arguments, "-i");

        Assert.True(seek >= 0 && seek < input);
        Assert.Equal("10", arguments[seek + 1]);
    }

    [Fact]
    public void Every_segment_is_normalised_so_joining_them_cannot_fail()
    {
        // Segments that differ in size, frame rate or channel layout cannot be
        // concatenated; normalising on the way out is what makes the cache
        // usable at all.
        var project = WithSegment(out var placed);
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Master, "take1.mkv", "out.mkv");

        var video = ValueOf(arguments, "-vf")!;
        var audio = ValueOf(arguments, "-af")!;

        Assert.Contains("scale=1920:1080", video);
        Assert.Contains("setsar=1", video);
        Assert.Contains("aresample=48000", audio);
        Assert.Contains("channel_layouts=stereo", audio);
    }

    [Fact]
    public void A_draft_renders_smaller_and_faster_than_a_master()
    {
        var project = WithSegment(out var placed);

        var draft = SegmentFilters.Build(project, placed, RenderQuality.Draft, "a.mkv", "o.mkv");
        var master = SegmentFilters.Build(project, placed, RenderQuality.Master, "a.mkv", "o.mkv");

        Assert.Contains("veryfast", draft);
        Assert.Contains("medium", master);
        Assert.Contains("scale=960:540", ValueOf(draft, "-vf")!);
    }

    [Fact]
    public void Fades_reach_both_the_picture_and_the_sound()
    {
        var project = WithSegment(out var placed, span =>
        {
            span.FadeIn = 1;
            span.FadeOut = 2;
        });

        placed = TimelineMap.Build(project).Elements[0];
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "a.mkv", "o.mkv");

        var video = ValueOf(arguments, "-vf")!;
        var audio = ValueOf(arguments, "-af")!;

        Assert.Contains("fade=t=in:st=0:d=1", video);
        Assert.Contains("fade=t=out:st=3:d=2", video);
        Assert.Contains("afade=t=in", audio);
        Assert.Contains("afade=t=out", audio);
    }

    [Fact]
    public void A_fade_limited_to_the_picture_leaves_the_sound_alone()
    {
        var project = WithSegment(out var placed, span =>
        {
            span.FadeIn = 1;
            span.FadeTarget = FadeTarget.Video;
        });

        placed = TimelineMap.Build(project).Elements[0];
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "a.mkv", "o.mkv");

        Assert.Contains("fade=t=in", ValueOf(arguments, "-vf")!);
        Assert.DoesNotContain("afade", ValueOf(arguments, "-af")!);
    }

    [Fact]
    public void A_muted_segment_is_silenced_rather_than_left_out()
    {
        var project = WithSegment(out var placed, span => span.Muted = true);
        placed = TimelineMap.Build(project).Elements[0];

        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "a.mkv", "o.mkv");

        Assert.Contains("volume=0", ValueOf(arguments, "-af")!);
    }

    [Fact]
    public void An_extreme_retime_chains_atempo_rather_than_clamping_it()
    {
        // atempo only spans 0.5 to 2.0 per instance. Clamping instead of
        // chaining would silently desynchronise the sound from the picture.
        var project = WithSegment(out var placed, span => span.Speed = 4.0);
        placed = TimelineMap.Build(project).Elements[0];

        var audio = ValueOf(
            SegmentFilters.Build(project, placed, RenderQuality.Draft, "a.mkv", "o.mkv"), "-af")!;

        Assert.Equal(2, audio.Split("atempo=2").Length - 1);
    }

    // ---- card backgrounds -------------------------------------------------

    [Fact]
    public void A_gradient_card_becomes_a_gradients_source()
    {
        var card = new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = new CardComposition
            {
                Background = new CardBackground
                {
                    Kind = BackgroundKind.Gradient,
                    Colour = "#102030",
                    SecondColour = "#405060",
                    Direction = GradientDirection.Horizontal,
                },
            },
        };

        var source = SegmentFilters.BackgroundSource(card, 1920, 1080, 30, 4);

        Assert.StartsWith("gradients=", source);
        Assert.Contains("c0=0x102030", source);
        Assert.Contains("c1=0x405060", source);
        Assert.Contains("x1=1920:y1=0", source);
    }

    [Fact]
    public void A_solid_card_becomes_a_colour_source()
    {
        var card = new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = new CardComposition
            {
                Background = new CardBackground { Kind = BackgroundKind.Solid, Colour = "#FF8800" },
            },
        };

        Assert.Contains("color=c=0xFF8800", SegmentFilters.BackgroundSource(card, 1920, 1080, 30, 4));
    }

    [Theory]
    [InlineData("#FF8800", "0xFF8800")]
    [InlineData("ff8800", "0xFF8800")]
    [InlineData("not a colour", "black")]
    [InlineData("", "black")]
    public void Colours_are_converted_and_bad_ones_fall_back(string input, string expected)
    {
        Assert.Equal(expected, SegmentFilters.Colour(input));
    }

    [Fact]
    public void A_hole_renders_as_black_rather_than_failing()
    {
        var hole = new HoleElement { Id = Ids.NewElement(), Length = 5, Note = "to fill" };

        Assert.Contains("color=c=black", SegmentFilters.BackgroundSource(hole, 1920, 1080, 30, 5));
    }
}

/// <summary>
/// How the rendered segments are joined, and what is drawn on top. The
/// arithmetic of overlapping transitions is exactly the sort of thing that is
/// wrong by a frame and impossible to notice by ear.
/// </summary>
public class RenderPlanTests
{
    private static Project ThreeSegments()
    {
        var project = Project.CreateDefault("plan");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 3; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(), Source = source.Id,
                SourceIn = i * 10, SourceOut = i * 10 + 5, Text = $"line {i}",
            });
        }

        return project;
    }

    [Fact]
    public void With_no_transitions_the_whole_timeline_is_one_run()
    {
        // Which means a straight concatenation, with nothing re-encoded.
        var project = ThreeSegments();
        var runs = RenderPlan.Runs(project, TimelineMap.Build(project));

        Assert.Single(runs);
        Assert.Equal(3, runs[0].Segments.Count);
    }

    [Fact]
    public void A_transition_splits_the_timeline_into_runs_either_side_of_it()
    {
        var project = ThreeSegments();
        project.Spine[2].TransitionIn = new Transition { Type = TransitionType.WipeLeft, Duration = 1 };

        var runs = RenderPlan.Runs(project, TimelineMap.Build(project));

        Assert.Equal(2, runs.Count);
        Assert.Equal(2, runs[0].Segments.Count);
        Assert.Single(runs[1].Segments);
        Assert.Equal(TransitionType.WipeLeft, runs[1].LeadIn!.Type);
    }

    [Fact]
    public void An_explicit_cut_does_not_start_a_new_run()
    {
        var project = ThreeSegments();
        project.Spine[1].TransitionIn = Transition.Cut;

        Assert.Single(RenderPlan.Runs(project, TimelineMap.Build(project)));
    }

    [Fact]
    public void The_first_segment_never_has_a_transition_into_it()
    {
        // There is nothing before it to come from.
        var project = ThreeSegments();
        project.Settings.SceneTransitionDuration = 0.4;

        var map = TimelineMap.Build(project);

        Assert.Null(RenderPlan.TransitionFor(project, map.Elements[0], map));
    }

    [Fact]
    public void The_crossfade_offset_accounts_for_the_overlap()
    {
        // xfade overlaps its inputs, so a transition shortens what follows it.
        // Getting this wrong makes every later transition drift.
        Assert.Equal(9, RenderPlan.OffsetFor(10, 1), 3);
        Assert.Equal(0, RenderPlan.OffsetFor(0.5, 1), 3);
    }
}

public class OverlayFilterTests
{
    private static Project WithLowerThird(out TimelineMap map)
    {
        var project = Project.CreateDefault("overlays");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 10, Text = "hello",
        });

        project.Overlays.Add(new CardItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Composition = CardTemplates.LowerThird("Cody Hurst", "host"),
            Start = new TimeAnchor(project.Spine[0].Id, 1),
            Length = 4,
        });

        map = TimelineMap.Build(project);
        return project;
    }

    [Fact]
    public void A_lower_third_becomes_drawtext_switched_on_for_its_stretch_only()
    {
        var project = WithLowerThird(out var map);
        var filter = OverlayFilters.Video(project, map, 1920, 1080, "/font.ttf")!;

        Assert.Contains("drawtext", filter);
        Assert.Contains("Cody Hurst", filter);
        Assert.Contains("between(t,1,5)", filter);
    }

    [Fact]
    public void Text_is_given_an_outline_so_it_stays_legible_over_anything()
    {
        // Nobody is going to look at the result and notice white text on a
        // white wall.
        var project = WithLowerThird(out var map);
        var filter = OverlayFilters.Video(project, map, 1920, 1080, "/font.ttf")!;

        Assert.Contains("borderw=", filter);
        Assert.Contains("shadowx=", filter);
    }

    [Fact]
    public void A_project_with_no_overlays_produces_no_filter_at_all()
    {
        // So the simple case stays a simple command.
        var project = Project.CreateDefault("bare");
        Assert.Null(OverlayFilters.Video(project, TimelineMap.Build(project), 1920, 1080, "/font.ttf"));
    }

    [Fact]
    public void A_hidden_overlay_is_not_drawn()
    {
        var project = WithLowerThird(out var map);
        project.Overlays[0].Hidden = true;

        Assert.Null(OverlayFilters.Video(project, map, 1920, 1080, "/font.ttf"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("time: 10:30", @"time\: 10\:30")]
    [InlineData("100%", @"100\%")]
    [InlineData("it's", @"it\'s")]
    public void Text_that_would_break_the_filter_graph_is_escaped(string input, string expected)
    {
        // drawtext parses its text twice, so a stray colon takes the whole
        // graph down rather than just looking wrong.
        Assert.Equal(expected, OverlayFilters.Escape(input));
    }

    [Fact]
    public void A_music_bed_is_mixed_and_ducked_under_the_programme()
    {
        var project = WithLowerThird(out var map);
        var music = new Source { Id = Ids.NewSource(), Path = "bed.mp3", Duration = 120 };
        project.Sources.Add(music);

        project.Overlays.Add(new MusicItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Audio).Id,
            Source = music.Id,
            GainDb = -20,
            DuckDb = 9,
            Start = new TimeAnchor(project.Spine[0].Id),
            Length = 10,
        });

        var mix = OverlayFilters.Music(project, map)!;

        Assert.Equal("bed.mp3", mix.Path);
        Assert.Contains("volume=-20dB", mix.Filter);
        Assert.Contains("sidechaincompress", mix.Filter);
        Assert.Contains("[aout]", mix.Filter);
    }

    [Fact]
    public void With_no_music_there_is_no_mix_to_apply()
    {
        var project = WithLowerThird(out var map);

        Assert.Null(OverlayFilters.Music(project, map));
    }
}

/// <summary>
/// Stills. A photograph has no duration of its own, so it can be held for as
/// long as you like - which is the whole difference between a still and a clip.
/// </summary>
public class StillTests
{
    private static Project WithStill(out SourceId image)
    {
        var project = Project.CreateDefault("stills");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var photo = new Source
        {
            Id = Ids.NewSource(), Path = "photo.png", Kind = SourceKind.Image, Duration = 0,
        };

        project.Sources.Add(photo);
        image = photo.Id;

        return project;
    }

    [Fact]
    public void An_image_with_no_duration_can_still_be_inserted()
    {
        // It has no length of its own, so the project supplies one - refusing
        // it for having zero duration would make stills unusable.
        var project = WithStill(out var image);

        var result = AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        Assert.True(result.Changed);
        Assert.Contains("as a still", result.Description);
        Assert.Equal(project.Settings.StillDuration, TimelineMap.Build(project).Duration, 2);
    }

    [Fact]
    public void A_still_can_be_stretched_to_any_length()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        AccessibleVideoEditor.Core.Editing.EditOperations.SetDuration(project, 1, 12);

        Assert.Equal(12, TimelineMap.Build(project).Duration, 2);
    }

    [Fact]
    public void Its_length_can_be_nudged_up_and_down()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        AccessibleVideoEditor.Core.Editing.EditOperations.AdjustDuration(project, 1, 2);
        Assert.Equal(6, TimelineMap.Build(project).Duration, 2);

        AccessibleVideoEditor.Core.Editing.EditOperations.AdjustDuration(project, 1, -4);
        Assert.Equal(2, TimelineMap.Build(project).Duration, 2);
    }

    [Fact]
    public void A_still_never_shrinks_to_nothing()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        AccessibleVideoEditor.Core.Editing.EditOperations.AdjustDuration(project, 1, -100);

        Assert.True(TimelineMap.Build(project).Duration > 0);
    }

    [Fact]
    public void Footage_keeps_its_own_length_and_says_to_trim_instead()
    {
        // A clip's length comes from its media, so setting it arbitrarily would
        // silently mean something different from what it does for a still.
        var project = WithStill(out _);
        var video = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(video);

        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, video.Id, 0);

        var result = AccessibleVideoEditor.Core.Editing.EditOperations.SetDuration(project, 1, 5);

        Assert.False(result.Changed);
        Assert.Contains("trim it instead", result.Description);
    }

    [Fact]
    public void An_inserted_still_does_not_move_unless_you_ask_it_to()
    {
        // A slideshow, or a two-second flash of a photograph, does not want a
        // slow drift over it - and a still that moves when you did not ask
        // reads as a mistake.
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        Assert.Equal(KenBurns.None, project.Spine[0].KenBurns);
    }

    [Fact]
    public void Movement_can_be_cycled_and_turned_off()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        var seen = new List<KenBurns>();

        for (var i = 0; i < 5; i++)
        {
            AccessibleVideoEditor.Core.Editing.EditOperations.CycleKenBurns(project, 1);
            seen.Add(project.Spine[0].KenBurns);
        }

        Assert.Contains(KenBurns.None, seen);
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public void Moving_footage_has_nothing_to_drift()
    {
        var project = WithStill(out _);
        var video = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(video);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, video.Id, 0);

        Assert.False(AccessibleVideoEditor.Core.Editing.EditOperations.CycleKenBurns(project, 1).Changed);
    }

    [Fact]
    public void A_still_is_looped_rather_than_seeked_into_when_rendered()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        var placed = TimelineMap.Build(project).Elements[0];
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "photo.png", "o.mkv");

        Assert.Contains("-loop", arguments);
        Assert.DoesNotContain("-ss", arguments);
    }

    [Fact]
    public void The_drift_reaches_the_render()
    {
        var project = WithStill(out var image);
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);
        AccessibleVideoEditor.Core.Editing.EditOperations.CycleKenBurns(project, 0);

        var placed = TimelineMap.Build(project).Elements[0];
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "photo.png", "o.mkv");

        Assert.Contains("zoompan", string.Join(" ", arguments));
    }

    [Fact]
    public void A_still_with_no_movement_is_not_needlessly_oversampled()
    {
        var project = WithStill(out var image);
        project.Settings.KenBurnsByDefault = false;
        AccessibleVideoEditor.Core.Editing.EditOperations.InsertSource(project, image, 0);

        var placed = TimelineMap.Build(project).Elements[0];
        var arguments = SegmentFilters.Build(project, placed, RenderQuality.Draft, "photo.png", "o.mkv");

        Assert.DoesNotContain("zoompan", string.Join(" ", arguments));
    }

    [Theory]
    [InlineData(KenBurns.ZoomIn)]
    [InlineData(KenBurns.ZoomOut)]
    [InlineData(KenBurns.PanLeft)]
    [InlineData(KenBurns.PanRight)]
    public void Every_movement_produces_a_usable_filter(KenBurns motion)
    {
        var filter = SegmentFilters.KenBurnsFilter(motion, 4, 30, 960, 540);

        Assert.StartsWith("zoompan=", filter);
        Assert.Contains("s=960x540", filter);
        Assert.Contains("d=120", filter);
    }
}

public class FrameDescriberTests
{
    [Fact]
    public void The_brief_asks_only_for_what_cannot_be_checked_otherwise()
    {
        // An unprompted description wanders into scene-setting prose; what is
        // needed is the handful of things a sighted editor catches in a glance.
        Assert.Contains("how they are framed", FrameDescriber.Prompt);
        Assert.Contains("background", FrameDescriber.Prompt);
        Assert.Contains("legible", FrameDescriber.Prompt);
        Assert.Contains("If something is fine, do not mention it", FrameDescriber.Prompt);
    }

    [Theory]
    [InlineData("  hello  ", "hello")]
    [InlineData("one\n\ntwo", "one two")]
    [InlineData("- bullet\n- another", "bullet another")]
    [InlineData("", "")]
    public void Replies_are_tidied_into_something_worth_speaking(string input, string expected)
    {
        Assert.Equal(expected, FrameDescriber.Tidy(input));
    }
}
