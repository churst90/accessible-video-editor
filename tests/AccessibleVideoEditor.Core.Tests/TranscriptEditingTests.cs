using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Editing from the transcript. Segments are addressed by ID rather than by
/// time here, because a cut line has no programme time and must still be
/// deletable, restorable and movable.
/// </summary>
public class TranscriptEditingTests
{
    private static Project ThreeSentences()
    {
        var project = Project.CreateDefault("transcript editing");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        string[] lines = ["first sentence", "second sentence", "third sentence"];

        for (var i = 0; i < lines.Length; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source.Id,
                SourceIn = i * 10,
                SourceOut = i * 10 + 5,
                Text = lines[i],
            });
        }

        return project;
    }

    // ---- deleting --------------------------------------------------------

    [Fact]
    public void Deleting_a_line_removes_that_segment_and_closes_the_gap()
    {
        var project = ThreeSentences();
        var target = project.Spine[1].Id;

        var result = EditOperations.DeleteSegment(project, target);

        Assert.True(result.Changed);
        Assert.Equal(2, project.Spine.Count);
        Assert.Null(project.Element(target));
        Assert.Equal(10, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void A_cut_line_can_still_be_deleted_even_though_it_has_no_time()
    {
        // The case that forced identity-based operations: a disabled segment
        // is absent from the timeline map entirely.
        var project = ThreeSentences();
        project.Spine[1].Enabled = false;
        var target = project.Spine[1].Id;

        var before = TimelineMap.Build(project).Duration;
        var result = EditOperations.DeleteSegment(project, target);

        Assert.True(result.Changed);
        Assert.Equal(2, project.Spine.Count);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Deleting_a_segment_an_overlay_was_anchored_to_reports_the_orphan()
    {
        // On a visual timeline you would see the title vanish. Here it has to
        // be said.
        var project = ThreeSentences();
        project.Spine[1].Enabled = false;

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "orphan me",
            Start = new TimeAnchor(project.Spine[1].Id),
            Length = 2,
        });

        var result = EditOperations.DeleteSegment(project, project.Spine[1].Id);

        Assert.Contains(result.Warnings, w => w.Contains("lost its anchor"));
    }

    // ---- disabling -------------------------------------------------------

    [Fact]
    public void Disabling_and_restoring_a_line_round_trips()
    {
        var project = ThreeSentences();
        var target = project.Spine[1].Id;
        var before = TimelineMap.Build(project).Duration;

        var cut = EditOperations.ToggleDisableSegment(project, target);
        Assert.Contains("cut", cut.Description);
        Assert.Equal(10, TimelineMap.Build(project).Duration, 3);

        var restored = EditOperations.ToggleDisableSegment(project, target);
        Assert.Contains("restored", restored.Description);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);

        // The line never left the document, which is what makes it findable.
        Assert.Equal(3, project.Spine.Count);
    }

    // ---- reordering ------------------------------------------------------

    [Fact]
    public void Moving_a_line_earlier_reorders_the_video()
    {
        var project = ThreeSentences();
        var target = project.Spine[2].Id;

        var result = EditOperations.MoveSegment(project, target, -1);

        Assert.True(result.Changed);
        Assert.Equal(target, project.Spine[1].Id);
        Assert.Equal("third sentence", ((SpanElement)project.Spine[1]).Text);
    }

    [Fact]
    public void Moving_a_line_later_reorders_the_video()
    {
        var project = ThreeSentences();
        var target = project.Spine[0].Id;

        EditOperations.MoveSegment(project, target, 1);

        Assert.Equal(target, project.Spine[1].Id);
        Assert.Equal("second sentence", ((SpanElement)project.Spine[0]).Text);
    }

    [Fact]
    public void Moving_past_either_end_is_refused_rather_than_silently_ignored()
    {
        var project = ThreeSentences();

        Assert.Equal("already first", EditOperations.MoveSegment(project, project.Spine[0].Id, -1).Description);
        Assert.Equal("already last", EditOperations.MoveSegment(project, project.Spine[2].Id, 1).Description);
    }

    [Fact]
    public void Overlays_ride_along_when_their_segment_is_reordered()
    {
        // Nothing needs re-anchoring: overlays name the segment they ride on,
        // not a time. This test exists to keep it that way.
        var project = ThreeSentences();
        var third = project.Spine[2];

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "rides along",
            Start = new TimeAnchor(third.Id),
            Length = 2,
        });

        Assert.Equal(10, TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start)!.Value, 3);

        EditOperations.MoveSegment(project, third.Id, -2);

        Assert.Equal(0, TimelineMap.Build(project).ResolveAnchor(project.Overlays[0].Start)!.Value, 3);
    }

    [Fact]
    public void Reordering_changes_the_transcript_order_too()
    {
        var project = ThreeSentences();
        EditOperations.MoveSegment(project, project.Spine[2].Id, -2);

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        Assert.StartsWith("third sentence", document.Text);
    }

    // ---- captions --------------------------------------------------------

    [Fact]
    public void Editing_the_words_changes_the_caption_and_never_the_cut()
    {
        var project = ThreeSentences();
        var target = project.Spine[1];
        var before = TimelineMap.Build(project).Duration;

        EditOperations.SetCaption(project, target.Id, "Second sentence, spelled properly");

        Assert.Equal("Second sentence, spelled properly", target.EffectiveCaption);
        Assert.Equal("second sentence", ((SpanElement)target).Text);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void A_caption_matching_the_transcript_clears_the_override()
    {
        // Otherwise every line you visit and leave unchanged would acquire a
        // redundant override that then stops tracking the transcript.
        var project = ThreeSentences();
        var target = project.Spine[1];

        EditOperations.SetCaption(project, target.Id, "changed");
        Assert.NotNull(target.Caption);

        EditOperations.SetCaption(project, target.Id, "second sentence");
        Assert.Null(target.Caption);
        Assert.Equal("second sentence", target.EffectiveCaption);
    }

    [Fact]
    public void Captions_can_be_turned_off_per_segment()
    {
        var project = ThreeSentences();
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Part two"),
        });

        var card = project.Spine[^1];

        Assert.Contains("no caption", EditOperations.ToggleCaption(project, card.Id).Description);
        Assert.Null(card.EffectiveCaption);
    }

    [Fact]
    public void A_non_speech_segment_has_no_caption_unless_one_is_given()
    {
        var project = ThreeSentences();
        project.Spine.Add(new ClipElement
        {
            Id = Ids.NewElement(),
            Source = project.Sources[0].Id,
            SourceIn = 20,
            SourceOut = 25,
        });

        var clip = project.Spine[^1];
        Assert.Null(clip.EffectiveCaption);

        EditOperations.SetCaption(project, clip.Id, "[keyboard clacking]");
        Assert.Equal("[keyboard clacking]", clip.EffectiveCaption);
    }

    // ---- splitting from the transcript -----------------------------------

    [Fact]
    public void Splitting_at_a_word_splits_the_segment_there()
    {
        var project = Project.CreateDefault("split at word");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 0,
            SourceOut = 6,
            Text = "hello there everybody",
            Words = [new Word("hello", 0, 1), new Word("there", 2, 3), new Word("everybody", 4, 5)],
        });

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));
        var caret = document.Text.IndexOf("everybody", StringComparison.Ordinal);
        var at = document.LocationAt(caret).ProgrammeTime!.Value;

        var result = EditOperations.SplitAt(project, at);

        Assert.True(result.Changed);
        Assert.Equal(2, project.Spine.Count);
        Assert.Equal("hello there", ((SpanElement)project.Spine[0]).Text);
        Assert.Equal("everybody", ((SpanElement)project.Spine[1]).Text);
    }
}
