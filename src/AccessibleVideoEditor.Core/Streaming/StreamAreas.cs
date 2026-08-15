using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// The regions of the streamer view, and moving between them.
///
/// Four things have to be reachable while you are live, and none of them can
/// cost more than one key: what scene is on air, what is in it, what chat is
/// saying, and what you look like. <c>Ctrl+`</c> goes round them and
/// <c>Ctrl+Shift+`</c> goes back - one key rather than four to learn, and it
/// falls under the hand that is not on the arrow keys.
///
/// Chat gets one area <i>per platform</i>. Merging Twitch and YouTube into one
/// list would mean a reply could land on the wrong service, which is a mistake
/// you cannot take back.
/// </summary>
public static class StreamAreas
{
    public static IReadOnlyList<StreamAreaRef> For(StreamSetup setup, ChatStore chat)
    {
        var areas = new List<StreamAreaRef>
        {
            new(StreamArea.Scenes, null, "scenes"),
            new(StreamArea.Sources, null, "sources"),
            new(StreamArea.Preview, null, "preview"),
        };

        foreach (var channel in chat.Channels.OrderBy(c => c.Platform.ToString()))
        {
            areas.Add(new StreamAreaRef(StreamArea.Chat, channel.Platform, channel.Name));
        }

        // Somewhere to go even before anything is connected, so the key never
        // does nothing and leaves you wondering whether it is broken.
        if (areas.All(a => a.Kind != StreamArea.Chat))
        {
            areas.Add(new StreamAreaRef(StreamArea.Chat, null, "chat, nothing connected"));
        }

        return areas;
    }

    public static StreamAreaRef Cycle(
        IReadOnlyList<StreamAreaRef> areas,
        StreamAreaRef current,
        bool forward)
    {
        if (areas.Count == 0) return current;

        var index = -1;
        for (var i = 0; i < areas.Count; i++)
        {
            if (areas[i] == current) { index = i; break; }
        }

        if (index < 0) return areas[0];

        return areas[(index + (forward ? 1 : areas.Count - 1)) % areas.Count];
    }
}

public readonly record struct StreamAreaRef(StreamArea Kind, StreamPlatform? Platform, string Name);

public enum StreamArea
{
    Scenes,
    Sources,
    Preview,
    Chat,
}

/// <summary>
/// Every edit the streamer view can make. Separate from timeline editing
/// because the consequences are different: going live is not undoable, and a
/// scene switch is felt by an audience the instant it happens.
/// </summary>
public static class SceneOperations
{
    public static EditResult AddScene(StreamSetup setup, string name)
    {
        var scene = new Scene { Id = StreamIds.NewScene(), Name = name };
        setup.Scenes.Add(scene);

        setup.LiveScene ??= scene.Id;

        return EditResult.Ok($"scene {setup.Scenes.Count}, {name}");
    }

    public static EditResult RemoveScene(StreamSetup setup, SceneId id)
    {
        if (setup.SceneOf(id) is not { } scene) return EditResult.NoChange("no such scene");

        if (setup.IsLive && setup.LiveScene == id)
        {
            return EditResult.NoChange($"{scene.Name} is on air; cut to another scene first");
        }

        setup.Scenes.Remove(scene);

        if (setup.LiveScene == id) setup.LiveScene = setup.Scenes.FirstOrDefault()?.Id;

        return EditResult.Ok($"removed {scene.Name}");
    }

    public static EditResult RenameScene(StreamSetup setup, SceneId id, string name)
    {
        if (setup.SceneOf(id) is not { } scene) return EditResult.NoChange("no such scene");

        scene.Name = name;

        return EditResult.Ok($"renamed to {name}");
    }

    /// <summary>
    /// Cut to a scene. Announced with what is now live rather than just the
    /// name, because the whole risk of scene switching is cutting to something
    /// that is not showing what you think it is.
    /// </summary>
    public static EditResult Switch(StreamSetup setup, SceneId id)
    {
        if (setup.SceneOf(id) is not { } scene) return EditResult.NoChange("no such scene");

        if (setup.LiveScene == id) return EditResult.NoChange($"{scene.Name} is already live");

        setup.LiveScene = id;

        var warnings = new List<string>();

        if (scene.Sources.All(s => !s.Visible))
        {
            warnings.Add("nothing in it is showing");
        }
        else if (!scene.Sources.Any(s =>
                     s.Visible && !s.Muted && setup.SourceOf(s.Source)?.HasAudio == true))
        {
            warnings.Add("no audio in this scene");
        }

        return EditResult.Ok(scene.Describe(setup), warnings);
    }

    public static EditResult AddSource(StreamSetup setup, StreamSource source)
    {
        setup.Sources.Add(source);

        return EditResult.Ok($"added {source.Describe()}");
    }

    /// <summary>
    /// Put an existing source into a scene. This is the reuse that makes scenes
    /// worth having - the same lower third in five scenes stays one object.
    /// </summary>
    public static EditResult AddToScene(
        StreamSetup setup,
        SceneId sceneId,
        StreamSourceId sourceId,
        double scale = 1.0,
        Placement? placement = null)
    {
        if (setup.SceneOf(sceneId) is not { } scene) return EditResult.NoChange("no such scene");
        if (setup.SourceOf(sourceId) is not { } source) return EditResult.NoChange("no such source");

        if (scene.Sources.Any(s => s.Source == sourceId))
        {
            return EditResult.NoChange($"{source.Name} is already in {scene.Name}");
        }

        scene.Sources.Add(new SourceRef
        {
            Id = StreamIds.NewRef(),
            Source = sourceId,
            Scale = scale,
            Placement = placement ?? new Placement(),
        });

        return EditResult.Ok($"{source.Name} added to {scene.Name}");
    }

    public static EditResult RemoveFromScene(StreamSetup setup, SceneId sceneId, SourceRefId refId)
    {
        if (setup.SceneOf(sceneId) is not { } scene) return EditResult.NoChange("no such scene");

        var reference = scene.Sources.FirstOrDefault(s => s.Id == refId);
        if (reference is null) return EditResult.NoChange("no such source in this scene");

        scene.Sources.Remove(reference);

        var name = setup.SourceOf(reference.Source)?.Name ?? "source";

        return EditResult.Ok($"{name} removed from {scene.Name}");
    }

    /// <summary>
    /// Hiding is not removing, exactly as on a track: the source stays in the
    /// scene, keeps its place and its size, and comes back with one key.
    /// </summary>
    public static EditResult ToggleVisible(StreamSetup setup, SceneId sceneId, SourceRefId refId)
    {
        var (scene, reference) = Find(setup, sceneId, refId);
        if (scene is null || reference is null) return EditResult.NoChange("no such source in this scene");

        reference.Visible = !reference.Visible;

        var name = setup.SourceOf(reference.Source)?.Name ?? "source";
        var live = setup.IsLive && setup.LiveScene == scene.Id ? " on air" : string.Empty;

        return EditResult.Ok($"{name} {(reference.Visible ? "showing" : "hidden")}{live}");
    }

    public static EditResult ToggleMuted(StreamSetup setup, SceneId sceneId, SourceRefId refId)
    {
        var (_, reference) = Find(setup, sceneId, refId);
        if (reference is null) return EditResult.NoChange("no such source in this scene");

        reference.Muted = !reference.Muted;

        var name = setup.SourceOf(reference.Source)?.Name ?? "source";

        return EditResult.Ok($"{name} {(reference.Muted ? "muted" : "unmuted")}");
    }

    /// <summary>
    /// Where a source sits, in the same 3 by 3 language cards already use, and
    /// how much of the frame it takes.
    /// </summary>
    public static EditResult Place(
        StreamSetup setup,
        SceneId sceneId,
        SourceRefId refId,
        Placement placement,
        double? scale = null)
    {
        var (_, reference) = Find(setup, sceneId, refId);
        if (reference is null) return EditResult.NoChange("no such source in this scene");

        reference.Placement = placement;
        if (scale is { } size) reference.Scale = Math.Clamp(size, 0.05, 1.0);

        var name = setup.SourceOf(reference.Source)?.Name ?? "source";

        return EditResult.Ok(
            reference.Scale >= 0.99
                ? $"{name} full frame"
                : $"{name}, {Math.Round(reference.Scale * 100)} percent, {placement.Describe()}");
    }

    /// <summary>
    /// Raise a source above the ones under it. Order is what decides who is in
    /// front, and there is no way to see that, so every move says where it
    /// landed.
    /// </summary>
    public static EditResult Reorder(StreamSetup setup, SceneId sceneId, SourceRefId refId, bool up)
    {
        if (setup.SceneOf(sceneId) is not { } scene) return EditResult.NoChange("no such scene");

        var index = scene.Sources.FindIndex(s => s.Id == refId);
        if (index < 0) return EditResult.NoChange("no such source in this scene");

        var target = up ? index + 1 : index - 1;

        if (target < 0 || target >= scene.Sources.Count)
        {
            return EditResult.NoChange(up ? "already at the front" : "already at the back");
        }

        (scene.Sources[index], scene.Sources[target]) = (scene.Sources[target], scene.Sources[index]);

        var name = setup.SourceOf(scene.Sources[target].Source)?.Name ?? "source";

        return EditResult.Ok($"{name} moved {(up ? "forward" : "back")}, {target + 1} of {scene.Sources.Count}");
    }

    /// <summary>
    /// The checks worth running before an audience sees the result. Every one
    /// of these is something a sighted streamer would notice in the preview
    /// window in the first second.
    /// </summary>
    public static IReadOnlyList<string> PreflightWarnings(StreamSetup setup)
    {
        var problems = new List<string>();

        if (setup.Scenes.Count == 0) problems.Add("there are no scenes");
        if (setup.Live is null) problems.Add("no scene is selected");

        var enabled = setup.Targets.Where(t => t.Enabled).ToList();

        if (enabled.Count == 0) problems.Add("no destination is enabled");

        foreach (var target in enabled.Where(t => !t.HasKey || t.Server.Length == 0))
        {
            problems.Add($"{target.Name} is enabled but not set up");
        }

        if (setup.Live is { } scene)
        {
            if (scene.Sources.All(s => !s.Visible)) problems.Add($"{scene.Name} has nothing showing");

            if (!scene.Sources.Any(s =>
                    s.Visible && !s.Muted && setup.SourceOf(s.Source)?.HasAudio == true))
            {
                problems.Add($"{scene.Name} has no audio");
            }
        }

        return problems;
    }

    private static (Scene? Scene, SourceRef? Ref) Find(
        StreamSetup setup,
        SceneId sceneId,
        SourceRefId refId)
    {
        var scene = setup.SceneOf(sceneId);

        return (scene, scene?.Sources.FirstOrDefault(s => s.Id == refId));
    }
}
