using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class TrackProbeTests
{
    private static Project WithTitleOnSecondSpan(out TrackId graphics)
    {
        var project = Project.CreateDefault("probe");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 2; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source.Id,
                SourceIn = i * 10,
                SourceOut = i * 10 + 5,
                Text = $"sentence {i}",
            });
        }

        graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id;

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = graphics,
            Text = "Cody Hurst",
            Start = new TimeAnchor(project.Spine[1].Id),
            Length = 2,
        });

        return project;
    }

    [Fact]
    public void Programme_track_reports_video_where_there_is_a_span()
    {
        var project = WithTitleOnSecondSpan(out _);
        var map = TimelineMap.Build(project);

        var content = TrackProbe.At(project, map, project.ProgrammeTrack.Id, 2);

        Assert.True(content.HasContent);
        Assert.Equal(ContentKind.Video, content.Kind);
        Assert.Equal("video", content.Word);
    }

    [Fact]
    public void Graphics_track_is_blank_before_the_title_starts()
    {
        var project = WithTitleOnSecondSpan(out var graphics);
        var map = TimelineMap.Build(project);

        var content = TrackProbe.At(project, map, graphics, 2);

        Assert.False(content.HasContent);
        Assert.Equal("blank", content.Word);
    }

    [Fact]
    public void Graphics_track_reports_the_title_while_it_is_on_screen()
    {
        var project = WithTitleOnSecondSpan(out var graphics);
        var map = TimelineMap.Build(project);

        // The second span starts at 5; the title runs 5 to 7.
        var inside = TrackProbe.At(project, map, graphics, 6);
        var after = TrackProbe.At(project, map, graphics, 8);

        Assert.Equal(ContentKind.Title, inside.Kind);
        Assert.Equal("blank", after.Word);
    }

    [Fact]
    public void Terse_announcement_is_the_time_and_one_word()
    {
        var project = WithTitleOnSecondSpan(out var graphics);
        var map = TimelineMap.Build(project);
        var content = TrackProbe.At(project, map, graphics, 2);

        // Every syllable here is latency while an arrow key is held down.
        Assert.Equal("0:02.0, blank", TrackProbe.Announce(content, 2, Verbosity.Terse));
    }

    [Fact]
    public void Verbose_announcement_includes_what_is_left_of_the_item()
    {
        var project = WithTitleOnSecondSpan(out var graphics);
        var map = TimelineMap.Build(project);
        var content = TrackProbe.At(project, map, graphics, 6);

        var spoken = TrackProbe.Announce(content, 6, Verbosity.Verbose);

        Assert.Contains("title", spoken);
        Assert.Contains("Cody Hurst", spoken);
        Assert.Contains("remaining", spoken);
    }

    [Fact]
    public void Track_announces_its_name_medium_and_active_flags()
    {
        var project = WithTitleOnSecondSpan(out _);
        var track = project.Tracks.First(t => t.Kind == TrackKind.Overlay);

        Assert.Equal("B-roll, video track", track.Describe());

        track.Armed = true;
        track.Muted = true;

        Assert.Equal("B-roll, video track, armed, muted", track.Describe());
    }

    [Fact]
    public void Soloing_a_track_silences_the_others()
    {
        var project = WithTitleOnSecondSpan(out _);
        var music = project.Tracks.First(t => t.Kind == TrackKind.Audio);
        music.Soloed = true;

        Assert.True(music.IsAudible(anyTrackSoloed: true));
        Assert.False(project.ProgrammeTrack.IsAudible(anyTrackSoloed: true));
    }
}
