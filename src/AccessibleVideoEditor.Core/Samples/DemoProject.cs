using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Samples;

/// <summary>
/// A synthetic project so a UI can be run with no media on disk. Shared by the
/// accessibility probes so they are exercising identical data.
/// </summary>
public static class DemoProject
{
    public static Project Create()
    {
        var project = Project.CreateDefault("Accessible Video Editor demo timeline");

        // A generated file with a tone that steps every eight seconds, so
        // scrubbing sounds different in different places. Falls back to a bare
        // name when it has not been generated, and playback then says the media
        // is missing rather than failing silently.
        var generated = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "videoedit", "demo", "take1.mkv");

        var take = new Source
        {
            Id = Ids.NewSource(),
            Path = System.IO.File.Exists(generated) ? generated : "take1.mkv",
            Duration = 56,
        };

        project.Sources.Add(take);

        string[] lines =
        [
            "Hey everybody, welcome back to the channel.",
            "Today I want to walk through version one point four.",
            "The order panel got a complete rewrite.",
            "It is fully keyboard driven now.",
            "That is the whole flow, start to finish.",
        ];

        for (var i = 0; i < lines.Length; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = take.Id,
                SourceIn = i * 8,
                SourceOut = i * 8 + 5,
                Text = lines[i],
                Words = lines[i]
                    .Split(' ')
                    .Select((word, index) => new Word(
                        word,
                        i * 8 + index * 0.4,
                        i * 8 + index * 0.4 + 0.35))
                    .ToList(),
            });
        }

        // A title card opens the video: a full screen on the programme track.
        project.Spine.Insert(0, new CardElement
        {
            Id = Ids.NewElement(),
            Length = 3,
            Composition = CardTemplates.TitleCard("Accessible Trade Terminal", "version 1.4"),
        });

        // The same composition type, transparent, riding over the video.
        project.Overlays.Add(new CardItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id,
            Composition = CardTemplates.LowerThird("Cody Hurst", "Accessible Trade Terminal"),
            Start = new TimeAnchor(project.Spine[1].Id, 0.5),
            Length = 4,
        });

        project.Overlays.Add(new BrollItem
        {
            Id = Ids.NewItem(),
            Track = project.Tracks.First(t => t.Kind == TrackKind.Overlay).Id,
            Source = take.Id,
            SourceIn = 40,
            Start = new TimeAnchor(project.Spine[2].Id),
            Length = 6,
        });

        return project;
    }
}
