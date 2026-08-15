using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Owns the project, its derived <see cref="TimelineMap"/>, and undo.
///
/// Undo is whole-document snapshots rather than inverse commands. Projects are
/// small (a long video is a few hundred KB of JSON) and snapshots cannot drift
/// out of sync with the model the way hand-written inverses do. Stable element
/// IDs are what make it usable: after an undo the cursor is restored by naming
/// an element, not a time, so it lands where you were rather than where that
/// timestamp now happens to be.
/// </summary>
public sealed class EditSession
{
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    private TimelineMap? _map;

    public EditSession(Project project)
    {
        Project = project;
    }

    public Project Project { get; private set; }

    public int UndoDepth => _undo.Count;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Rebuilt lazily; invalidated by every mutation.</summary>
    public TimelineMap Map => _map ??= TimelineMap.Build(Project);

    public event EventHandler<EditAppliedEventArgs>? Applied;

    /// <summary>
    /// Runs a mutation, snapshotting first. <paramref name="mutate"/> returns
    /// what the announcer should say - every edit confirms itself out loud, so
    /// the description is a required return value rather than an afterthought.
    /// </summary>
    public EditResult Apply(string label, Func<Project, TimelineMap, EditResult> mutate)
    {
        var before = Snapshot.Of(Project, label);
        var result = mutate(Project, Map);

        if (!result.Changed)
        {
            return result;
        }

        _undo.Add(before);
        _redo.Clear();
        Invalidate();

        Applied?.Invoke(this, new EditAppliedEventArgs(result));
        return result;
    }

    public EditResult? Undo()
    {
        if (_undo.Count == 0) return null;

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(Snapshot.Of(Project, snapshot.Label));

        Project = snapshot.Restore();
        Invalidate();

        var result = EditResult.Ok($"undo {snapshot.Label}");
        Applied?.Invoke(this, new EditAppliedEventArgs(result));
        return result;
    }

    public EditResult? Redo()
    {
        if (_redo.Count == 0) return null;

        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(Snapshot.Of(Project, snapshot.Label));

        Project = snapshot.Restore();
        Invalidate();

        var result = EditResult.Ok($"redo {snapshot.Label}");
        Applied?.Invoke(this, new EditAppliedEventArgs(result));
        return result;
    }

    public void Invalidate() => _map = null;

    private sealed record Snapshot(string Json, string Label)
    {
        public static Snapshot Of(Project project, string label) =>
            new(ProjectJson.Serialise(project), label);

        public Project Restore() => ProjectJson.Deserialise(Json);
    }
}

/// <summary>
/// What happened, in the words the announcer will speak. Warnings are for the
/// things that would be obvious on a visual timeline and invisible here - an
/// overlay that had to be re-anchored, a title left with nothing under it.
/// </summary>
public sealed record EditResult(bool Changed, string Description, IReadOnlyList<string> Warnings)
{
    public static EditResult Ok(string description) => new(true, description, []);

    public static EditResult Ok(string description, IReadOnlyList<string> warnings) =>
        new(true, description, warnings);

    public static EditResult NoChange(string reason) => new(false, reason, []);

    /// <summary>The full utterance, warnings included.</summary>
    public string Announce() =>
        Warnings.Count == 0 ? Description : $"{Description}. {string.Join(". ", Warnings)}";
}

public sealed class EditAppliedEventArgs(EditResult result) : EventArgs
{
    public EditResult Result { get; } = result;
}
