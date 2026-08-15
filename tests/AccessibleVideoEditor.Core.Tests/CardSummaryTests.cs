using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// A card is the one segment whose contents are entirely invisible without
/// being told. "Card" says nothing about whether the logo made it on, or
/// whether the subtitle ended up somewhere useless.
/// </summary>
public class CardSummaryTests
{
    [Fact]
    public void A_summary_names_the_background_the_layout_and_every_layer()
    {
        var card = CardTemplates.TitleCard("Accessible Trade Terminal", "version 1.4");
        var summary = card.Summarise();

        Assert.Contains("solid", summary);
        Assert.Contains("2 layers", summary);
        Assert.Contains("stacked", summary);
        Assert.Contains("Accessible Trade Terminal", summary);
        Assert.Contains("version 1.4", summary);
    }

    [Fact]
    public void Each_layer_line_says_where_that_layer_actually_lands()
    {
        var card = CardTemplates.TitleCard("Heading", "Subheading");
        var lines = card.LayerLines();

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Matches(@"(upper|middle|lower|centre)", line));
    }

    [Fact]
    public void A_lower_third_reports_itself_as_low_and_over_the_video()
    {
        var card = CardTemplates.LowerThird("Cody Hurst", "host");
        var summary = card.Summarise();

        Assert.Contains("over the video", summary);
        Assert.Contains("lower", summary);
    }

    [Fact]
    public void An_image_layer_is_reported_with_its_position()
    {
        // The logo-in-the-corner case: you have to be able to confirm it is
        // actually in the corner.
        var card = new CardComposition
        {
            Layout = CardLayout.Grid,
            Layers =
            [
                new ImageLayer { Source = Ids.NewSource(), Scale = 0.15, Placement = new Placement(3) },
            ],
        };

        var summary = card.Summarise();

        Assert.Contains("image", summary);
        Assert.Contains("lower right", summary);
    }

    [Fact]
    public void An_empty_card_says_it_is_empty_rather_than_listing_nothing()
    {
        Assert.Contains("empty card", new CardComposition().Summarise());
    }

    [Theory]
    [InlineData(0.1, 0.1, "upper left")]
    [InlineData(0.5, 0.5, "centre")]
    [InlineData(0.9, 0.9, "lower right")]
    [InlineData(0.5, 0.9, "lower centre")]
    public void Positions_are_described_in_thirds_not_percentages(double x, double y, string expected)
    {
        Assert.Equal(expected, CardComposition.DescribePosition(x, y));
    }

    // ---- transcript line announcements -----------------------------------

    [Fact]
    public void Moving_onto_a_transcript_line_announces_its_position_and_times()
    {
        var project = Project.CreateDefault("lines");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id,
            SourceIn = 0, SourceOut = 4, Text = "hello there",
        });

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id,
            SourceIn = 10, SourceOut = 16, Text = "welcome back",
        });

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));
        var offset = document.Text.IndexOf("welcome", StringComparison.Ordinal);

        var announced = document.AnnounceLine(offset);

        Assert.Contains("line 2 of 2", announced);
        Assert.Contains("0:04.0", announced);
        Assert.Contains("0:10.0", announced);
        Assert.Contains("6 seconds", announced);
        Assert.Contains("welcome back", announced);
    }

    [Fact]
    public void A_cut_line_says_it_is_not_in_the_programme_instead_of_giving_times()
    {
        var project = Project.CreateDefault("lines");
        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id,
            SourceIn = 0, SourceOut = 4, Text = "flubbed take", Enabled = false,
        });

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        Assert.Contains("cut, not in the programme", document.AnnounceLine(2));
    }

    [Fact]
    public void Non_speech_segments_still_get_a_line_so_they_can_be_reached()
    {
        var project = Project.CreateDefault("lines");
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Opening"),
        });

        var document = TranscriptDocument.Build(project, TimelineMap.Build(project));

        Assert.Contains("[card: Opening]", document.Text);
        Assert.Equal(0, document.LineAt(2));
    }
}
