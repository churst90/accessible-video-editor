using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Review;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Knowing what is on screen without looking.
///
/// The lookup and the announcing rule are here; the ffmpeg scene detection and
/// the model call are in the Engine and are not exercised without media.
/// </summary>
public class ShotIndexTests
{
    private static readonly SourceId Camera = new("src-camera");
    private static readonly SourceId Other = new("src-other");

    private static ShotIndex ThreeShots()
    {
        var index = new ShotIndex();

        index.Set(Camera,
        [
            new Shot(0, 12, "you at the desk, wide", "A man sits at a desk facing the camera."),
            new Shot(12, 30, "the keyboard, close", "A close shot of hands on a mechanical keyboard."),
            new Shot(30, 45, "you at the desk, wide", "Back to the wide shot of the desk."),
        ]);

        return index;
    }

    [Fact]
    public void The_shot_under_a_moment_is_the_last_one_that_started()
    {
        var index = ThreeShots();

        Assert.Equal("you at the desk, wide", index.At(Camera, 0)?.Label);
        Assert.Equal("you at the desk, wide", index.At(Camera, 11.9)?.Label);
        Assert.Equal("the keyboard, close", index.At(Camera, 12)?.Label);
        Assert.Equal("the keyboard, close", index.At(Camera, 29.5)?.Label);
        Assert.Equal("you at the desk, wide", index.At(Camera, 44)?.Label);
    }

    [Fact]
    public void A_source_that_was_never_described_answers_nothing_rather_than_guessing()
    {
        Assert.Null(ThreeShots().At(Other, 5));
        Assert.False(ThreeShots().Has(Other));
    }

    [Fact]
    public void The_next_and_previous_shot_change_can_be_found()
    {
        var index = ThreeShots();

        // The navigation this feature exists to buy: a cut inside a take is
        // something you otherwise have no way to locate at all.
        Assert.Equal(12, index.Next(Camera, 0)?.At);
        Assert.Equal(30, index.Next(Camera, 12)?.At);
        Assert.Null(index.Next(Camera, 40));

        Assert.Equal(12, index.Previous(Camera, 30)?.At);
        Assert.Null(index.Previous(Camera, 0));
    }

    [Fact]
    public void Standing_exactly_on_a_boundary_does_not_count_as_the_next_one()
    {
        // Otherwise pressing "next shot" twice from a boundary would skip one.
        var index = ThreeShots();

        Assert.Equal(30, index.Next(Camera, 12)?.At);
    }

    // ---- what gets spoken --------------------------------------------------

    [Fact]
    public void A_move_inside_one_shot_is_silent()
    {
        var index = ThreeShots();
        var announcer = new ShotAnnouncer();

        Assert.Equal("you at the desk, wide", announcer.Moved(index, Camera, 0));

        // Four sentences on every arrow press would make the timeline unusable.
        Assert.Null(announcer.Moved(index, Camera, 3));
        Assert.Null(announcer.Moved(index, Camera, 7));
        Assert.Null(announcer.Moved(index, Camera, 11.5));
    }

    [Fact]
    public void Crossing_into_another_shot_announces_it()
    {
        var index = ThreeShots();
        var announcer = new ShotAnnouncer();

        announcer.Moved(index, Camera, 0);

        Assert.Equal("the keyboard, close", announcer.Moved(index, Camera, 13));
        Assert.Null(announcer.Moved(index, Camera, 20));
    }

    [Fact]
    public void Returning_to_a_shot_with_the_same_words_still_announces_it()
    {
        // Shots one and three are the same wide shot. Staying silent because
        // the label matches would mean a cut back to the wide read as no cut.
        var index = ThreeShots();
        var announcer = new ShotAnnouncer();

        announcer.Moved(index, Camera, 0);
        announcer.Moved(index, Camera, 13);

        Assert.Equal("you at the desk, wide", announcer.Moved(index, Camera, 35));
    }

    [Fact]
    public void Moving_off_the_end_and_back_announces_again()
    {
        var index = ThreeShots();
        var announcer = new ShotAnnouncer();

        announcer.Moved(index, Camera, 3);

        // Over a card, or past the end: there is no shot here.
        Assert.Null(announcer.Moved(index, null, 0));

        // You have been somewhere else, so where you are is worth saying.
        Assert.Equal("you at the desk, wide", announcer.Moved(index, Camera, 3));
    }

    [Fact]
    public void Moving_to_a_source_that_has_no_shots_is_silent_rather_than_stale()
    {
        var index = ThreeShots();
        var announcer = new ShotAnnouncer();

        announcer.Moved(index, Camera, 3);

        Assert.Null(announcer.Moved(index, Other, 3));
    }

    // ---- the cache ---------------------------------------------------------

    [Fact]
    public void An_index_survives_a_round_trip_to_disk()
    {
        var restored = ShotIndex.Deserialise(ThreeShots().Serialise());

        Assert.Equal(3, restored.Count(Camera));
        Assert.Equal("the keyboard, close", restored.At(Camera, 15)?.Label);
        Assert.Equal("A close shot of hands on a mechanical keyboard.", restored.At(Camera, 15)?.Detail);
    }

    [Fact]
    public void A_cache_that_cannot_be_read_is_empty_rather_than_fatal()
    {
        // It gets rebuilt. It is never the reason a project fails to open.
        Assert.Empty(ShotIndex.Deserialise("{ this is not json").BySource);
    }

    // ---- reading what the model sent back ----------------------------------

    [Fact]
    public void The_label_is_taken_off_the_front_of_the_reply()
    {
        var (label, detail) = Engine.ShotDescriber.Split(
            "LABEL: you at the desk, wide\nA man sits at a desk facing the camera. "
            + "A bookshelf is behind him.", 0);

        Assert.Equal("you at the desk, wide", label);
        Assert.StartsWith("A man sits at a desk", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reply_with_no_label_line_still_yields_one()
    {
        // Refusing the whole description because the format slipped would throw
        // away the part that was useful.
        var (label, detail) = Engine.ShotDescriber.Split(
            "A close shot of hands resting on a mechanical keyboard, lit from the left.", 0);

        Assert.Equal("A close shot of hands resting", label);
        Assert.Contains("mechanical keyboard", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shot_that_could_not_be_described_still_gets_a_label()
    {
        // A blank announcement at a boundary reads as a missed keypress.
        var (label, detail) = Engine.ShotDescriber.Split(string.Empty, 72.5);

        Assert.Contains("not described", label, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, label);
        Assert.Equal(string.Empty, detail);
    }

    [Fact]
    public void A_label_line_and_nothing_else_is_allowed()
    {
        var (label, detail) = Engine.ShotDescriber.Split("LABEL: black frame", 5);

        Assert.Equal("black frame", label);
        Assert.Equal(string.Empty, detail);
    }

    [Fact]
    public void Markdown_bullets_do_not_reach_the_speech()
    {
        var (_, detail) = Engine.ShotDescriber.Split(
            "LABEL: the whiteboard\n- A whiteboard covered in diagrams.\n- Text reads \"phase two\".", 0);

        Assert.DoesNotContain("- ", detail, StringComparison.Ordinal);
        Assert.Contains("phase two", detail, StringComparison.Ordinal);
    }
}
