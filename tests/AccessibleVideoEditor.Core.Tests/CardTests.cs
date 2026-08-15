using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class CardTests
{
    [Fact]
    public void A_stacked_card_spreads_its_layers_vertically_without_overlapping()
    {
        var card = CardTemplates.TitleCard("Accessible Trade Terminal", "version 1.4");
        var resolved = card.Resolve();

        Assert.Equal(2, resolved.Count);
        Assert.True(resolved[0].Y < resolved[1].Y);
        Assert.All(resolved, layer => Assert.InRange(layer.Y, 0, 1));
    }

    [Fact]
    public void A_single_stacked_layer_sits_centred()
    {
        var card = CardTemplates.SectionBreak("Part two");
        var resolved = card.Resolve();

        Assert.Single(resolved);
        Assert.Equal(0.5, resolved[0].Y, 1);
    }

    [Fact]
    public void Grid_layout_puts_each_layer_where_its_numpad_cell_says()
    {
        var card = new CardComposition
        {
            Layout = CardLayout.Grid,
            Layers =
            [
                new TextLayer { Text = "top left", Placement = new Placement(7) },
                new TextLayer { Text = "bottom right", Placement = new Placement(3) },
            ],
        };

        var resolved = card.Resolve();

        Assert.True(resolved[0].X < 0.5 && resolved[0].Y < 0.5);
        Assert.True(resolved[1].X > 0.5 && resolved[1].Y > 0.5);
    }

    [Fact]
    public void A_bottom_anchored_stack_sits_low_and_stays_on_canvas()
    {
        // The lower third case: it must clear the bottom edge, not hang off it.
        var card = CardTemplates.LowerThird("Cody Hurst", "Accessible Trade Terminal");
        var resolved = card.Resolve();

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, layer => Assert.InRange(layer.Y, 0.5, 1.0));
        Assert.All(resolved, layer => Assert.InRange(layer.Y, 0, 1));
    }

    [Fact]
    public void A_lower_third_is_transparent_so_it_composites_over_the_video()
    {
        // This is the one property that makes the same composition an overlay
        // rather than a full screen.
        var lowerThird = CardTemplates.LowerThird("Cody Hurst");
        var titleCard = CardTemplates.TitleCard("Hello");

        Assert.Equal(BackgroundKind.Transparent, lowerThird.Background.Kind);
        Assert.Equal(BackgroundKind.Solid, titleCard.Background.Kind);
    }

    [Fact]
    public void A_card_with_many_layers_still_fits_on_the_canvas()
    {
        var card = new CardComposition
        {
            Layers = Enumerable.Range(0, 6)
                .Select(i => (CardLayer)new TextLayer { Text = $"line {i}", Size = TextSize.Medium })
                .ToList(),
        };

        Assert.All(card.Resolve(), layer => Assert.InRange(layer.Y, 0, 1));
    }

    [Fact]
    public void An_empty_card_describes_itself_rather_than_going_silent()
    {
        var card = new CardComposition();

        Assert.Contains("empty card", card.Describe());
    }

    [Fact]
    public void A_card_describes_its_text_and_what_it_contains()
    {
        var card = CardTemplates.TitleCard("Version 1.4", "walkthrough");
        var described = card.Describe();

        Assert.Contains("Version 1.4", described);
        Assert.Contains("2 text", described);
        Assert.Contains("stacked", described);
    }

    [Fact]
    public void Every_template_builds_and_produces_layers()
    {
        Assert.All(CardTemplates.All, template =>
        {
            var card = CardTemplates.Build(template.Id, "Heading", "Subheading");

            Assert.NotEmpty(card.Layers);
            Assert.All(card.Resolve(), layer => Assert.InRange(layer.Y, 0, 1));
        });
    }

    [Fact]
    public void Cloning_a_card_does_not_share_its_layers()
    {
        var original = CardTemplates.TitleCard("Original");
        var copy = original.Clone();

        ((TextLayer)copy.Layers[0]).Text = "Changed";

        Assert.Equal("Original", ((TextLayer)original.Layers[0]).Text);
    }

    [Fact]
    public void A_card_on_the_programme_track_occupies_its_own_time()
    {
        var project = Project.CreateDefault("cards");
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Hello"),
        });

        var map = TimelineMap.Build(project);

        Assert.Equal(3, map.Duration, 3);
        Assert.Equal(ContentKind.Card,
            TrackProbe.At(project, map, project.ProgrammeTrack.Id, 1).Kind);
    }

    [Fact]
    public void The_same_composition_works_as_an_overlay_on_the_graphics_track()
    {
        var project = Project.CreateDefault("cards");
        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 0,
            SourceOut = 10,
            Text = "hello",
        });

        var graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id;

        project.Overlays.Add(new CardItem
        {
            Id = Ids.NewItem(),
            Track = graphics,
            Composition = CardTemplates.LowerThird("Cody Hurst", "host"),
            Start = new TimeAnchor(project.Spine[0].Id, 1),
            Length = 4,
        });

        var map = TimelineMap.Build(project);

        Assert.Equal(ContentKind.Card, TrackProbe.At(project, map, graphics, 2).Kind);
        Assert.False(TrackProbe.At(project, map, graphics, 6).HasContent);
    }

    [Fact]
    public void Cards_survive_a_project_json_round_trip_with_their_layers()
    {
        var project = Project.CreateDefault("cards");
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = CardTemplates.Quote("Keyboard driven all the way down", "Cody"),
        });

        var restored = ProjectJson.Deserialise(ProjectJson.Serialise(project));
        var card = Assert.IsType<CardElement>(restored.Spine[0]);

        Assert.Equal(2, card.Composition.Layers.Count);
        Assert.All(card.Composition.Layers, layer => Assert.IsType<TextLayer>(layer));
        Assert.Contains("Keyboard driven", ((TextLayer)card.Composition.Layers[0]).Text);
    }
}
