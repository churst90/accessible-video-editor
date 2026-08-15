using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class TranscriptSyncTests
{
    private static Project TwoSpans()
    {
        var project = Project.CreateDefault("transcript");
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
            SourceOut = 4,
            Text = "hello there everybody",
            Words = [new Word("hello", 0, 1), new Word("there", 1.5, 2.2), new Word("everybody", 2.5, 3.8)],
        });

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 10,
            SourceOut = 14,
            Text = "welcome back",
            Words = [new Word("welcome", 10, 11), new Word("back", 11.5, 12.5)],
        });

        return project;
    }

    [Fact]
    public void The_transcript_is_one_line_per_element()
    {
        var project = TwoSpans();
        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        Assert.Equal(2, document.Segments.Count);
        Assert.Contains("hello there everybody", document.Text);
        Assert.Contains("welcome back", document.Text);
    }

    [Fact]
    public void Timeline_cursor_lands_on_the_matching_word_in_the_transcript()
    {
        // This is the whole of "the two panes work together" - the cursor does
        // not move between panes, only the lens changes.
        var project = TwoSpans();
        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        // 2.6s into the programme is inside "everybody".
        var offset = document.OffsetAt(2.6);

        Assert.Equal("everybody", document.Text[offset..].Split(' ', '\n')[0]);
    }

    [Fact]
    public void Caret_position_maps_back_to_the_moment_that_word_is_spoken()
    {
        var project = TwoSpans();
        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        var offset = document.Text.IndexOf("welcome", StringComparison.Ordinal);
        var location = document.LocationAt(offset);

        Assert.True(location.InProgramme);
        Assert.Equal(4, location.ProgrammeTime!.Value, 1);
        Assert.Equal("welcome", location.Describe);
    }

    [Fact]
    public void A_round_trip_through_the_transcript_returns_to_the_same_word()
    {
        var project = TwoSpans();
        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        var location = document.LocationAt(document.OffsetAt(1.7));

        Assert.True(location.InProgramme);
        Assert.Equal("there", location.Describe);
    }

    [Fact]
    public void Cut_lines_stay_visible_but_report_that_they_are_not_in_the_programme()
    {
        // You have to be able to find a cut line to restore it, but it has no
        // programme time and the UI must not pretend otherwise.
        var project = TwoSpans();
        project.Spine[0].Enabled = false;

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));
        var offset = document.Text.IndexOf("hello", StringComparison.Ordinal);
        var location = document.LocationAt(offset);

        Assert.Contains(TranscriptDocument.CutMarker, document.Text);
        Assert.False(location.InProgramme);
        Assert.Null(location.ProgrammeTime);
        Assert.Contains("cut", location.Describe);
    }

    [Fact]
    public void Holes_and_pauses_appear_as_their_own_lines()
    {
        var project = TwoSpans();
        project.Spine.Add(new HoleElement
        {
            Id = Ids.NewElement(),
            Length = 5,
            Note = "explain the order panel",
        });

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        Assert.Contains("[hole: explain the order panel]", document.Text);
    }
}
