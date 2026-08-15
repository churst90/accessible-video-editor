using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

public class ClipboardTests
{
    private static Project ThreeSpans()
    {
        var project = Project.CreateDefault("clipboard");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
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

        return project;
    }

    [Fact]
    public void Copying_the_programme_track_captures_the_elements_in_range()
    {
        var project = ThreeSpans();
        var clipboard = new EditClipboard();

        var result = clipboard.Copy(
            project, TimelineMap.Build(project), project.ProgrammeTrack.Id, new TimeSelection(0, 5));

        Assert.True(result.Changed);
        Assert.Single(clipboard.Contents!.Elements);
    }

    [Fact]
    public void Pasting_mints_fresh_ids_so_two_pastes_are_independent()
    {
        var project = ThreeSpans();
        var clipboard = new EditClipboard();
        var programme = project.ProgrammeTrack.Id;

        clipboard.Copy(project, TimelineMap.Build(project), programme, new TimeSelection(0, 5));
        clipboard.Paste(project, programme, 15);
        clipboard.Paste(project, programme, 15);

        Assert.Equal(5, project.Spine.Count);
        Assert.Equal(project.Spine.Count, project.Spine.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Pasting_video_onto_an_audio_track_is_refused_out_loud()
    {
        // A wrong paste is obvious the moment you see it on a visual timeline.
        // Here the refusal has to be spoken, or it is a silent surprise.
        var project = ThreeSpans();
        var clipboard = new EditClipboard();
        var broll = project.Tracks.First(t => t.Kind == TrackKind.Overlay);
        var music = project.Tracks.First(t => t.Kind == TrackKind.Audio);

        project.Overlays.Add(new BrollItem
        {
            Id = Ids.NewItem(),
            Track = broll.Id,
            Source = project.Sources[0].Id,
            Start = new TimeAnchor(project.Spine[0].Id),
            Length = 3,
        });

        clipboard.Copy(project, TimelineMap.Build(project), broll.Id, new TimeSelection(0, 5));
        var result = clipboard.Paste(project, music.Id, 0);

        Assert.False(result.Changed);
        Assert.Contains("cannot paste video", result.Description);
        Assert.Contains("Music", result.Description);
    }

    [Fact]
    public void Pasting_onto_a_locked_track_is_refused()
    {
        var project = ThreeSpans();
        var clipboard = new EditClipboard();
        var programme = project.ProgrammeTrack;

        clipboard.Copy(project, TimelineMap.Build(project), programme.Id, new TimeSelection(0, 5));
        programme.Locked = true;

        var result = clipboard.Paste(project, programme.Id, 10);

        Assert.False(result.Changed);
        Assert.Contains("locked", result.Description);
    }

    [Fact]
    public void Cut_removes_the_range_and_keeps_it_on_the_clipboard()
    {
        var project = ThreeSpans();
        var clipboard = new EditClipboard();

        var result = clipboard.Cut(
            project, TimelineMap.Build(project), project.ProgrammeTrack.Id, new TimeSelection(0, 5));

        Assert.True(result.Changed);
        Assert.Equal(10, TimelineMap.Build(project).Duration, 3);
        Assert.False(clipboard.IsEmpty);
    }

    [Fact]
    public void Pasting_an_empty_clipboard_changes_nothing()
    {
        var project = ThreeSpans();
        var result = new EditClipboard().Paste(project, project.ProgrammeTrack.Id, 0);

        Assert.False(result.Changed);
        Assert.Equal("clipboard is empty", result.Description);
    }
}
