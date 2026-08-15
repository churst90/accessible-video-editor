using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Saving and opening, on a project that has actually been edited.
///
/// This is the one failure no amount of announcing makes up for: everything
/// else in the application can be done again, and an afternoon's edit cannot.
/// So the round trip is checked on the things a real project accumulates rather
/// than on an empty one.
/// </summary>
public class ProjectRoundTripTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ave-project-" + Guid.NewGuid().ToString("n")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static Project Edited()
    {
        var project = Project.CreateDefault("Round trip");

        var source = new Source { Id = Ids.NewSource(), Path = "/tmp/take.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 3; i++)
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

        project.Spine.Add(new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Chapter one"),
        });

        project.Spine[1].Muted = true;
        project.Spine[2].Enabled = false;
        project.Spine[1].TransitionIn = new Transition
        {
            Type = TransitionType.WipeLeft,
            Duration = 0.6,
            SoundPath = "/tmp/whoosh.wav",
            SoundGainDb = -8,
        };

        project.Tracks.First(t => t.Kind == TrackKind.Audio).GainDb = -14;

        project.CustomTransitions.Add(new CustomTransition
        {
            Name = "my wipe",
            Definition = "wiperight",
            Duration = 0.8,
        });

        return project;
    }

    [Fact]
    public async Task Everything_that_was_edited_survives_being_saved_and_opened()
    {
        var before = Edited();
        var duration = TimelineMap.Build(before).Duration;

        await ProjectJson.SaveAsync(before, _directory);

        var after = await ProjectJson.LoadAsync(_directory);

        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Spine.Count, after.Spine.Count);
        Assert.Equal(duration, TimelineMap.Build(after).Duration, 3);

        // The three states are three different things, and all three have to
        // come back as themselves.
        Assert.True(after.Spine[1].Muted);
        Assert.False(after.Spine[2].Enabled);
        Assert.True(after.Spine[0].Enabled);
    }

    [Fact]
    public async Task A_transition_comes_back_with_its_sound_and_its_length()
    {
        await ProjectJson.SaveAsync(Edited(), _directory);

        var transition = (await ProjectJson.LoadAsync(_directory)).Spine[1].TransitionIn;

        Assert.NotNull(transition);
        Assert.Equal(TransitionType.WipeLeft, transition!.Type);
        Assert.Equal(0.6, transition.Duration, 3);
        Assert.Equal("/tmp/whoosh.wav", transition.SoundPath);
        Assert.Equal(-8, transition.SoundGainDb, 3);
    }

    [Fact]
    public async Task Track_levels_survive_because_there_is_no_automatic_ducking_to_recreate_them()
    {
        await ProjectJson.SaveAsync(Edited(), _directory);

        var track = (await ProjectJson.LoadAsync(_directory)).Tracks.First(t => t.Kind == TrackKind.Audio);

        Assert.Equal(-14, track.GainDb, 3);
    }

    [Fact]
    public async Task Transitions_you_made_yourself_are_kept_with_the_project()
    {
        await ProjectJson.SaveAsync(Edited(), _directory);

        var custom = Assert.Single((await ProjectJson.LoadAsync(_directory)).CustomTransitions);

        Assert.Equal("my wipe", custom.Name);
        Assert.Equal(0.8, custom.Duration, 3);
    }

    [Fact]
    public async Task A_card_keeps_its_words()
    {
        await ProjectJson.SaveAsync(Edited(), _directory);

        var card = Assert.IsType<CardElement>((await ProjectJson.LoadAsync(_directory)).Spine[^1]);

        Assert.Contains("Chapter one", card.Composition.PlainText());
    }

    [Fact]
    public async Task Element_ids_are_stable_across_a_save_so_anchors_still_point_somewhere()
    {
        // Overlays are anchored to element ids rather than to times. If the ids
        // changed on load, every title would move.
        var before = Edited();

        before.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = before.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Text = "hello",
            Start = new TimeAnchor(before.Spine[1].Id, 0.5),
            Length = 1,
        });

        var at = TimelineMap.Build(before).ResolveAnchor(before.Overlays[0].Start);

        await ProjectJson.SaveAsync(before, _directory);

        var after = await ProjectJson.LoadAsync(_directory);

        Assert.Equal(before.Spine[1].Id, after.Spine[1].Id);
        Assert.Equal(at!.Value, TimelineMap.Build(after).ResolveAnchor(after.Overlays[0].Start)!.Value, 3);
    }

    [Fact]
    public async Task Saving_twice_leaves_no_temporary_file_behind()
    {
        // The save writes to a temp file and moves it, so a crash mid-save
        // cannot leave a half-written project. The temp must not survive.
        await ProjectJson.SaveAsync(Edited(), _directory);
        await ProjectJson.SaveAsync(Edited(), _directory);

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task The_project_remembers_where_it_lives()
    {
        var project = Edited();

        await ProjectJson.SaveAsync(project, _directory);

        Assert.Equal(_directory, project.RootPath);
        Assert.Equal(_directory, (await ProjectJson.LoadAsync(_directory)).RootPath);
    }

    [Fact]
    public async Task Opening_a_folder_with_no_project_in_it_fails_rather_than_inventing_one()
    {
        Directory.CreateDirectory(_directory);

        await Assert.ThrowsAnyAsync<Exception>(() => ProjectJson.LoadAsync(_directory));
    }
}
