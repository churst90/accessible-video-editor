using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// What gets said while the video plays. The rule is boundary crossings only:
/// continuous announcements would bury the audio you are listening to, and
/// silence would leave you unable to tell whether an edit took.
/// </summary>
public class PlaybackAnnouncerTests
{
    private static Project ThreeSentences(out TimelineMap map)
    {
        var project = Project.CreateDefault("playback");
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

        map = TimelineMap.Build(project);
        return project;
    }

    [Fact]
    public void The_first_segment_is_announced_rather_than_assumed()
    {
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        Assert.Equal("first sentence", announcer.Tick(project, map, 0.1));
    }

    [Fact]
    public void Nothing_is_said_while_a_segment_keeps_playing()
    {
        // The whole point: silence between boundaries, so you can hear the
        // audio you are checking.
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 0.1);

        Assert.Null(announcer.Tick(project, map, 1.0));
        Assert.Null(announcer.Tick(project, map, 2.0));
        Assert.Null(announcer.Tick(project, map, 4.9));
    }

    [Fact]
    public void Crossing_into_the_next_segment_announces_it()
    {
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 0.1);

        Assert.Equal("second sentence", announcer.Tick(project, map, 5.1));
    }

    [Fact]
    public void A_transition_is_announced_as_it_begins()
    {
        // This is how you confirm the wipe you inserted is actually there,
        // without stopping to inspect the boundary.
        var project = ThreeSentences(out _);
        project.Spine[1].TransitionIn = new Transition
        {
            Type = TransitionType.WipeLeft,
            Duration = 1,
        };

        var map = TimelineMap.Build(project);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 0.1);
        var spoken = announcer.Tick(project, map, 4.2);

        Assert.NotNull(spoken);
        Assert.Contains("transition", spoken);
        Assert.Contains("wipeleft", spoken);
    }

    [Fact]
    public void A_transition_is_announced_once_not_on_every_tick()
    {
        var project = ThreeSentences(out _);
        project.Spine[1].TransitionIn = new Transition { Type = TransitionType.Fade, Duration = 1 };

        var map = TimelineMap.Build(project);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 0.1);
        var first = announcer.Tick(project, map, 4.2);
        var second = announcer.Tick(project, map, 4.5);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void B_roll_starting_is_announced_because_you_cannot_hear_it()
    {
        var project = ThreeSentences(out _);

        project.Overlays.Add(new BrollItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Overlay).Id,
            Source = project.Sources[0].Id,
            Start = new TimeAnchor(project.Spine[1].Id),
            Length = 3,
        });

        var map = TimelineMap.Build(project);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 0.1);
        var spoken = announcer.Tick(project, map, 5.5);

        Assert.NotNull(spoken);
        Assert.Contains("b-roll", spoken);
    }

    [Fact]
    public void A_card_announces_its_text()
    {
        var project = ThreeSentences(out _);
        project.Spine.Insert(0, new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Opening titles"),
        });

        var map = TimelineMap.Build(project);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        var spoken = announcer.Tick(project, map, 0.5);

        Assert.NotNull(spoken);
        Assert.Contains("Opening titles", spoken);
    }

    [Fact]
    public void Timecodes_are_never_spoken_during_playback()
    {
        // You can hear time passing. What you cannot hear is what is on screen.
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        foreach (var t in new[] { 0.1, 2.0, 5.1, 7.5, 10.1 })
        {
            var spoken = announcer.Tick(project, map, t);
            if (spoken is null) continue;

            Assert.DoesNotContain(":", spoken);
            Assert.DoesNotMatch(@"\d+\.\d+ seconds", spoken);
        }
    }

    [Fact]
    public void Verbosity_off_says_nothing_at_all()
    {
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer { Verbosity = PlaybackVerbosity.Off };
        announcer.Reset();

        Assert.Null(announcer.Tick(project, map, 0.1));
        Assert.Null(announcer.Tick(project, map, 5.1));
    }

    [Fact]
    public void Resetting_makes_the_next_tick_announce_again()
    {
        // Starting playback from the middle should say where you are, not
        // assume you remember from last time.
        var project = ThreeSentences(out var map);
        var announcer = new PlaybackAnnouncer();
        announcer.Reset();

        announcer.Tick(project, map, 5.1);
        Assert.Null(announcer.Tick(project, map, 5.5));

        announcer.Reset();
        Assert.Equal("second sentence", announcer.Tick(project, map, 5.5));
    }
}
