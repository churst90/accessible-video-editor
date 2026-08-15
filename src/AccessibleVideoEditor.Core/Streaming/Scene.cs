using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// A named arrangement of live inputs.
///
/// The same idea as a card, one layer up: a card composes text and pictures
/// over a clip, a scene composes cameras, screens and media over nothing. That
/// is deliberate - placement, layers and the way they are summarised aloud all
/// carry over unchanged, so there is one vocabulary for "where things are on
/// screen" whether you are editing or live.
///
/// Sources are held by reference, not by copy. One lower third used in five
/// scenes is one object; renaming it renames it everywhere, which is the whole
/// reason OBS-style scenes are worth having.
/// </summary>
public sealed class Scene
{
    public required SceneId Id { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// Bottom of the stack first, so index order is the order you hear them
    /// listed and the order they are drawn.
    /// </summary>
    public List<SourceRef> Sources { get; set; } = [];

    /// <summary>
    /// Spoken when the scene is switched to. Says what is live, not what
    /// exists: a hidden source in a scene you have just cut to is not on air.
    /// </summary>
    public string Describe(StreamSetup setup)
    {
        var live = Sources.Where(s => s.Visible).ToList();

        if (live.Count == 0) return $"{Name}, empty scene";

        var names = live
            .Select(s => setup.SourceOf(s.Source)?.Name ?? "missing source")
            .ToList();

        return $"{Name}, {names.Count} {(names.Count == 1 ? "source" : "sources")}: {string.Join(", ", names)}";
    }
}

/// <summary>
/// One source as it appears in one scene: which source, where it sits, whether
/// it is showing and how loud it is here.
/// </summary>
public sealed class SourceRef
{
    public required SourceRefId Id { get; init; }

    public required StreamSourceId Source { get; init; }

    public bool Visible { get; set; } = true;

    /// <summary>Silenced here only. The source keeps playing, as with a track.</summary>
    public bool Muted { get; set; }

    public Placement Placement { get; set; } = new();

    /// <summary>
    /// Fraction of the canvas width this source occupies. 1 is full frame,
    /// which is what a camera or a screen capture normally wants; 0.25 is the
    /// usual corner inset.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    public double GainDb { get; set; }

    public string Describe(StreamSetup setup)
    {
        var source = setup.SourceOf(Source);
        var name = source?.Name ?? "missing source";

        var state = (Visible, Muted) switch
        {
            (false, _) => "hidden",
            (true, true) => "muted",
            _ => "live",
        };

        var size = Scale >= 0.99
            ? "full frame"
            : $"{Math.Round(Scale * 100)} percent, {Placement.Describe()}";

        return $"{name}, {state}, {size}";
    }
}

/// <summary>
/// Something that can appear in a scene. Defined once for the whole setup and
/// referenced by any number of scenes.
/// </summary>
public sealed class StreamSource
{
    public required StreamSourceId Id { get; init; }

    public required string Name { get; set; }

    public required StreamSourceKind Kind { get; init; }

    /// <summary>A device path for a camera, a file path for media, empty otherwise.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// For media. A song over a static picture is the case this exists for:
    /// the picture is a still and the music loops under it until you cut away.
    /// </summary>
    public bool Loop { get; set; }

    /// <summary>Text for a text source, or the card composition for a card.</summary>
    public string Text { get; set; } = string.Empty;

    public CardComposition? Card { get; set; }

    /// <summary>
    /// Screen capture is deliberately not here. <c>x11grab</c> is picture only -
    /// desktop sound is a separate audio source - and claiming otherwise would
    /// build a filtergraph referring to a stream that does not exist, which
    /// fails at the moment you go live rather than when you set it up.
    /// </summary>
    public bool HasAudio => Kind is StreamSourceKind.Camera or StreamSourceKind.Video
        or StreamSourceKind.Music or StreamSourceKind.Microphone;

    public bool HasPicture => Kind is not (StreamSourceKind.Music or StreamSourceKind.Microphone);

    public string Describe() => Kind switch
    {
        StreamSourceKind.Camera => $"{Name}, camera",
        StreamSourceKind.Screen => $"{Name}, screen capture",
        StreamSourceKind.Microphone => $"{Name}, microphone",
        StreamSourceKind.Image => $"{Name}, image",
        StreamSourceKind.Video => $"{Name}, video{(Loop ? ", looping" : string.Empty)}",
        StreamSourceKind.Music => $"{Name}, music{(Loop ? ", looping" : string.Empty)}",
        StreamSourceKind.Card => $"{Name}, card",
        StreamSourceKind.Text => $"{Name}, text \"{Text}\"",
        _ => Name,
    };
}

public enum StreamSourceKind
{
    Camera,
    Screen,
    Microphone,
    Image,
    Video,
    Music,
    Card,
    Text,
}

/// <summary>
/// Everything the streamer view works on: the sources, the scenes built from
/// them, which scene is live, and where the stream is going.
/// </summary>
public sealed class StreamSetup
{
    public List<StreamSource> Sources { get; set; } = [];

    public List<Scene> Scenes { get; set; } = [];

    public List<StreamTarget> Targets { get; set; } = [];

    /// <summary>
    /// What is on air. Null when nothing has been set up yet, which the view
    /// says out loud rather than showing an empty box.
    /// </summary>
    public SceneId? LiveScene { get; set; }

    public bool IsLive { get; set; }

    public StreamSource? SourceOf(StreamSourceId id) => Sources.FirstOrDefault(s => s.Id == id);

    public Scene? SceneOf(SceneId id) => Scenes.FirstOrDefault(s => s.Id == id);

    public Scene? Live => LiveScene is { } id ? SceneOf(id) : null;

    /// <summary>
    /// The scene a number key selects. One-based, because "scene 1" is what a
    /// person says, and the key that switches to it is the same number.
    /// </summary>
    public Scene? ByNumber(int number) =>
        number >= 1 && number <= Scenes.Count ? Scenes[number - 1] : null;

    public int NumberOf(SceneId id) => Scenes.FindIndex(s => s.Id == id) + 1;

    /// <summary>
    /// Deliberately empty until you make something. A stream setup guessed on
    /// your behalf is a setup you have to audit before you dare go live.
    /// </summary>
    public static StreamSetup Empty() => new();

    /// <summary>
    /// The arrangement almost everyone builds first, offered as one command
    /// rather than as fifteen. Nothing here is on air until you say so.
    /// </summary>
    public static StreamSetup Starter()
    {
        var camera = new StreamSource
        {
            Id = StreamIds.NewSource(), Name = "Face cam", Kind = StreamSourceKind.Camera,
        };

        var microphone = new StreamSource
        {
            Id = StreamIds.NewSource(), Name = "Microphone", Kind = StreamSourceKind.Microphone,
        };

        var screen = new StreamSource
        {
            Id = StreamIds.NewSource(), Name = "Screen", Kind = StreamSourceKind.Screen,
        };

        var setup = new StreamSetup { Sources = [camera, microphone, screen] };

        var face = new Scene { Id = StreamIds.NewScene(), Name = "Face cam" };
        face.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = camera.Id });
        face.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = microphone.Id });

        var sharing = new Scene { Id = StreamIds.NewScene(), Name = "Screen share" };
        sharing.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = screen.Id });
        sharing.Sources.Add(new SourceRef
        {
            Id = StreamIds.NewRef(),
            Source = camera.Id,
            Scale = 0.25,
            Placement = new Placement(9),
        });
        sharing.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = microphone.Id });

        setup.Scenes.Add(face);
        setup.Scenes.Add(sharing);
        setup.LiveScene = face.Id;

        return setup;
    }
}

public readonly record struct SceneId(string Value) : IStableId
{
    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct SourceRefId(string Value) : IStableId
{
    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct StreamSourceId(string Value) : IStableId
{
    public override string ToString() => Value ?? string.Empty;
}

public static class StreamIds
{
    public static SceneId NewScene() => new($"sc-{Guid.NewGuid().ToString("n")[..8]}");

    public static SourceRefId NewRef() => new($"sr-{Guid.NewGuid().ToString("n")[..8]}");

    public static StreamSourceId NewSource() => new($"ss-{Guid.NewGuid().ToString("n")[..8]}");
}
