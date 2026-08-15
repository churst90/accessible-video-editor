using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Putting media on the timeline, and separating a segment's sound from its
/// picture.
/// </summary>
public class AssemblyTests
{
    private static Project TwoSegments(out SourceId spare)
    {
        var project = Project.CreateDefault("assembly");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var take = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(take);

        for (var i = 0; i < 2; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = take.Id,
                SourceIn = i * 10,
                SourceOut = i * 10 + 5,
                Text = $"sentence {i}",
            });
        }

        var broll = new Source { Id = Ids.NewSource(), Path = "broll.mkv", Duration = 4 };
        project.Sources.Add(broll);
        spare = broll.Id;

        return project;
    }

    // ---- insert and overwrite --------------------------------------------

    [Fact]
    public void Inserting_a_source_ripples_everything_after_it()
    {
        var project = TwoSegments(out var broll);
        Assert.Equal(10, TimelineMap.Build(project).Duration, 3);

        var result = EditOperations.InsertSource(project, broll, 5);

        Assert.True(result.Changed);
        Assert.Equal(14, TimelineMap.Build(project).Duration, 3);
        Assert.Contains("broll.mkv", result.Description);
    }

    [Fact]
    public void Overwriting_replaces_without_changing_the_total_length()
    {
        // That is the whole distinction: insert moves everything downstream,
        // overwrite leaves the timing exactly as it was.
        var project = TwoSegments(out var broll);
        var before = TimelineMap.Build(project).Duration;

        var result = EditOperations.OverwriteSource(project, broll, 3);

        Assert.True(result.Changed);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
    }

    [Fact]
    public void A_source_with_no_duration_is_refused_rather_than_inserted_empty()
    {
        var project = TwoSegments(out _);
        var empty = new Source { Id = Ids.NewSource(), Path = "nothing.mkv", Duration = 0 };
        project.Sources.Add(empty);

        var result = EditOperations.InsertSource(project, empty.Id, 5);

        Assert.False(result.Changed);
        Assert.Contains("no duration", result.Description);
    }

    [Fact]
    public void Inserting_something_that_is_not_in_the_project_is_refused()
    {
        var project = TwoSegments(out _);

        Assert.False(EditOperations.InsertSource(project, Ids.NewSource(), 0).Changed);
    }

    // ---- detach audio -----------------------------------------------------

    private static TrackId AudioTrack(Project project) =>
        project.Tracks.First(t => t.Media == TrackMedia.Audio).Id;

    [Fact]
    public void Detaching_audio_leaves_the_picture_where_it_is()
    {
        var project = TwoSegments(out _);
        var before = TimelineMap.Build(project).Duration;

        var result = EditOperations.DetachAudio(project, project.Spine[0].Id, AudioTrack(project));

        Assert.True(result.Changed);
        Assert.Equal(before, TimelineMap.Build(project).Duration, 3);
        Assert.Equal(2, project.Spine.Count);
    }

    [Fact]
    public void The_detached_audio_lands_on_the_audio_track_over_the_same_stretch()
    {
        var project = TwoSegments(out _);
        EditOperations.DetachAudio(project, project.Spine[0].Id, AudioTrack(project));

        var item = Assert.IsType<AudioItem>(project.Overlays.Single());
        var map = TimelineMap.Build(project);

        Assert.Equal(AudioTrack(project), item.Track);
        Assert.Equal(0, map.ResolveAnchor(item.Start)!.Value, 3);
        Assert.Equal(5, item.Length!.Value, 3);
    }

    [Fact]
    public void The_original_is_muted_rather_than_stripped_so_it_can_be_undone()
    {
        var project = TwoSegments(out _);
        EditOperations.DetachAudio(project, project.Spine[0].Id, AudioTrack(project));

        Assert.True(project.Spine[0].Muted);

        var reattached = EditOperations.ReattachAudio(project, project.Spine[0].Id);

        Assert.True(reattached.Changed);
        Assert.False(project.Spine[0].Muted);
        Assert.Empty(project.Overlays);
    }

    [Fact]
    public void Detached_audio_remembers_what_it_came_from()
    {
        // Without the link the two would silently drift apart, which is
        // unrecoverable by ear.
        var project = TwoSegments(out _);
        EditOperations.DetachAudio(project, project.Spine[0].Id, AudioTrack(project));

        var item = Assert.IsType<AudioItem>(project.Overlays.Single());

        Assert.Equal(project.Spine[0].Id, item.LinkedTo);
    }

    [Fact]
    public void Detaching_onto_a_video_track_is_refused()
    {
        var project = TwoSegments(out _);
        var video = project.Tracks.First(t => t.Media == TrackMedia.Video).Id;

        var result = EditOperations.DetachAudio(project, project.Spine[0].Id, video);

        Assert.False(result.Changed);
        Assert.Contains("audio track", result.Description);
    }

    [Fact]
    public void Detaching_twice_is_refused()
    {
        var project = TwoSegments(out _);
        var track = AudioTrack(project);

        EditOperations.DetachAudio(project, project.Spine[0].Id, track);

        Assert.False(EditOperations.DetachAudio(project, project.Spine[0].Id, track).Changed);
    }

    [Fact]
    public void Detached_audio_rides_along_when_its_segment_moves()
    {
        // It is anchored to the segment, not to a time, so reordering keeps
        // the sound with the picture it came from.
        var project = TwoSegments(out _);
        EditOperations.DetachAudio(project, project.Spine[1].Id, AudioTrack(project));

        var item = project.Overlays.OfType<AudioItem>().Single();
        Assert.Equal(5, TimelineMap.Build(project).ResolveAnchor(item.Start)!.Value, 3);

        EditOperations.MoveSegment(project, project.Spine[1].Id, -1);

        Assert.Equal(0, TimelineMap.Build(project).ResolveAnchor(item.Start)!.Value, 3);
    }

    [Fact]
    public void A_card_has_no_sound_to_detach()
    {
        var project = TwoSegments(out _);
        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Nothing to hear"),
        });

        var result = EditOperations.DetachAudio(project, project.Spine[^1].Id, AudioTrack(project));

        Assert.False(result.Changed);
        Assert.Contains("no sound", result.Description);
    }
}
