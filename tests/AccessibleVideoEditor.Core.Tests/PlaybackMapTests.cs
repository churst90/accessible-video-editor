using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Playback;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// An EDL can only contain real media, but a programme also contains cards,
/// holes and pauses - segments that occupy time and have nothing to play.
/// Without a translation the two clocks drift apart by the length of every such
/// segment, and every seek lands somewhere other than where the cursor said.
/// </summary>
public class PlaybackMapTests
{
    /// <summary>A three second card, then two five second spans.</summary>
    private static Project CardThenSpans()
    {
        var project = Project.CreateDefault("playback map");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "/tmp/take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Opening"),
        });

        for (var i = 0; i < 2; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source.Id,
                SourceIn = 20 + i * 10,
                SourceOut = 25 + i * 10,
                Text = $"sentence {i}",
            });
        }

        return project;
    }

    [Fact]
    public void Programme_time_and_playback_time_are_not_the_same_clock()
    {
        // The card occupies three seconds of programme and none of playback.
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.Equal(13, map.ProgrammeDuration, 3);
        Assert.Equal(0, map.ToPlayback(3)!.Value, 3);
        Assert.Equal(2, map.ToPlayback(5)!.Value, 3);
    }

    [Fact]
    public void A_moment_inside_a_card_has_no_playback_time_at_all()
    {
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.Null(map.ToPlayback(0));
        Assert.Null(map.ToPlayback(2.9));
    }

    [Fact]
    public void Playback_time_maps_back_to_the_right_programme_time()
    {
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        // Two seconds into playback is five seconds into the programme.
        Assert.Equal(5, map.ToProgramme(2), 3);
        Assert.Equal(9, map.ToProgramme(6), 3);
    }

    [Fact]
    public void A_round_trip_through_both_clocks_returns_the_same_moment()
    {
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        foreach (var programmeTime in new[] { 3.0, 5.5, 8.0, 11.25 })
        {
            var playback = map.ToPlayback(programmeTime);

            Assert.NotNull(playback);
            Assert.Equal(programmeTime, map.ToProgramme(playback!.Value), 3);
        }
    }

    [Fact]
    public void Starting_inside_a_card_skips_to_the_next_real_media()
    {
        // Preview cannot render a card, so it starts where it can and the UI
        // says so, rather than silently playing from somewhere else.
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.Equal(3, map.NextPlayable(0)!.Value, 3);
        Assert.Equal(5.5, map.NextPlayable(5.5)!.Value, 3);
    }

    [Fact]
    public void There_is_nothing_playable_past_the_end()
    {
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.Null(map.NextPlayable(13.5));
    }

    [Fact]
    public void A_project_with_no_media_at_all_reports_it_rather_than_building_a_broken_uri()
    {
        var project = Project.CreateDefault("cards only");
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 4,
            Composition = CardTemplates.TitleCard("Nothing to play"),
        });

        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.False(map.HasPlayableMedia);
        Assert.Null(map.NextPlayable(0));
    }

    [Fact]
    public void The_uri_is_an_edl_listing_only_the_playable_segments()
    {
        var project = CardThenSpans();
        var map = MpvEdl.Build(project, TimelineMap.Build(project));

        Assert.StartsWith("edl://", map.Uri);
        Assert.Equal(2, map.Uri.Split(';').Length);
        Assert.Contains("take1.mkv", map.Uri);
    }
}
