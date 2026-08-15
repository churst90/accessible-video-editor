using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Backgrounds and fades. Both are things you cannot check by looking, so both
/// have to read back accurately.
/// </summary>
public class CardEditingTests
{
    [Fact]
    public void A_solid_background_reads_back_its_colour()
    {
        var card = new CardComposition
        {
            Background = new CardBackground { Kind = BackgroundKind.Solid, Colour = "#101014" },
        };

        Assert.Contains("solid #101014", card.Background.Describe());
    }

    [Fact]
    public void A_gradient_reads_back_both_stops_and_its_direction()
    {
        // There is no way to check a gradient by looking, so the description is
        // the only feedback there is.
        var card = new CardComposition
        {
            Background = new CardBackground
            {
                Kind = BackgroundKind.Gradient,
                Colour = "#000000",
                SecondColour = "#3355AA",
                Direction = GradientDirection.Diagonal,
            },
        };

        var described = card.Background.Describe();

        Assert.Contains("diagonal gradient", described);
        Assert.Contains("#000000", described);
        Assert.Contains("#3355AA", described);
    }

    [Fact]
    public void A_transparent_background_says_it_is_over_the_video()
    {
        var background = new CardBackground { Kind = BackgroundKind.Transparent };

        Assert.Contains("over the video", background.Describe());
    }

    [Fact]
    public void Cloning_a_card_copies_its_gradient_rather_than_sharing_it()
    {
        var original = new CardComposition
        {
            Background = new CardBackground
            {
                Kind = BackgroundKind.Gradient,
                Colour = "#111111",
                SecondColour = "#222222",
            },
        };

        var copy = original.Clone();
        copy.Background.SecondColour = "#FFFFFF";

        Assert.Equal("#222222", original.Background.SecondColour);
    }

    [Fact]
    public void A_gradient_card_survives_a_project_round_trip()
    {
        var project = Project.CreateDefault("gradients");
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = new CardComposition
            {
                Background = new CardBackground
                {
                    Kind = BackgroundKind.Gradient,
                    Colour = "#0A0A0A",
                    SecondColour = "#204080",
                    Direction = GradientDirection.Horizontal,
                },
                Layers = [new TextLayer { Text = "Part two", Size = TextSize.Large }],
            },
        });

        var restored = ProjectJson.Deserialise(ProjectJson.Serialise(project));
        var card = Assert.IsType<CardElement>(restored.Spine[0]);

        Assert.Equal(BackgroundKind.Gradient, card.Composition.Background.Kind);
        Assert.Equal("#204080", card.Composition.Background.SecondColour);
        Assert.Equal(GradientDirection.Horizontal, card.Composition.Background.Direction);
    }

    // ---- fades ------------------------------------------------------------

    private static SpanElement Segment()
    {
        return new SpanElement
        {
            Id = Ids.NewElement(),
            Source = Ids.NewSource(),
            SourceIn = 0,
            SourceOut = 5,
            Text = "hello",
        };
    }

    [Fact]
    public void A_segment_with_no_fades_says_nothing_about_them()
    {
        Assert.Null(Segment().DescribeFades());
    }

    [Fact]
    public void Fades_touch_picture_and_sound_by_default()
    {
        // Fading the picture up from black while the sound arrives at full
        // volume is almost never wanted, and is not something you would notice
        // without watching.
        var segment = Segment();
        segment.FadeIn = 1;

        Assert.Equal(FadeTarget.Both, segment.FadeTarget);
        Assert.Contains("picture and sound", segment.DescribeFades());
    }

    [Fact]
    public void A_fade_can_be_limited_to_one_or_the_other()
    {
        var segment = Segment();
        segment.FadeOut = 0.5;
        segment.FadeTarget = FadeTarget.Audio;

        Assert.Contains("fade out 0.5 seconds", segment.DescribeFades());
        Assert.Contains("sound", segment.DescribeFades());
        Assert.DoesNotContain("picture and", segment.DescribeFades());
    }

    [Fact]
    public void Both_fades_are_reported_together()
    {
        var segment = Segment();
        segment.FadeIn = 1;
        segment.FadeOut = 2;

        var described = segment.DescribeFades()!;

        Assert.Contains("fade in 1 seconds", described);
        Assert.Contains("fade out 2 seconds", described);
    }

    [Fact]
    public void A_fade_does_not_change_how_long_a_segment_occupies()
    {
        // A fade happens within the segment; a transition overlaps two. If a
        // fade shortened the programme it would be a transition wearing the
        // wrong name.
        var project = Project.CreateDefault("fades");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 5, Text = "hello",
        });

        var before = TimelineMap.Build(project).Duration;

        project.Spine[0].FadeIn = 1;
        project.Spine[0].FadeOut = 1;

        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void Fades_survive_a_project_round_trip()
    {
        var project = Project.CreateDefault("fades");
        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 5,
            FadeIn = 0.75, FadeOut = 1.5, FadeTarget = FadeTarget.Video,
        });

        var restored = ProjectJson.Deserialise(ProjectJson.Serialise(project));

        Assert.Equal(0.75, restored.Spine[0].FadeIn, 3);
        Assert.Equal(1.5, restored.Spine[0].FadeOut, 3);
        Assert.Equal(FadeTarget.Video, restored.Spine[0].FadeTarget);
    }
}
