using AccessibleVideoEditor.Core.Editing;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// Undo for pictures.
///
/// Whole-document snapshots, for the same reason the project uses them: an
/// image document is a handful of numbers and a short list of shapes, and a
/// snapshot cannot drift out of step with the model the way a hand-written
/// inverse can. "Undo the crop" and "undo the resize that the crop happened to
/// change" are the same operation here, which is what makes it trustworthy.
///
/// Undoing <b>says what it undid and what the picture is now</b>. Without the
/// second half you know something moved but not where it landed, which is worse
/// than not having undo at all.
/// </summary>
public sealed class ImageHistory
{
    private readonly List<(string Label, ImageDocument State)> _undo = [];
    private readonly List<(string Label, ImageDocument State)> _redo = [];

    /// <summary>How many steps back are kept. Far more than anyone works in one sitting.</summary>
    public const int Depth = 100;

    public ImageDocument? Document { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoDepth => _undo.Count;

    /// <summary>Opening a picture starts a new history; there is nothing to go back to.</summary>
    public void Open(ImageDocument document)
    {
        Document = document;
        _undo.Clear();
        _redo.Clear();
    }

    public void Close()
    {
        Document = null;
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>
    /// Runs an edit, keeping the state before it. An operation that changes
    /// nothing is not recorded - undoing a refused edit and finding it did
    /// nothing twice is how a history becomes untrustworthy.
    /// </summary>
    public EditResult Do(string label, Func<ImageDocument, EditResult> edit)
    {
        if (Document is not { } document) return EditResult.NoChange("no picture is open");

        var before = document.Clone();
        var result = edit(document);

        if (!result.Changed) return result;

        _undo.Add((label, before));
        _redo.Clear();

        if (_undo.Count > Depth) _undo.RemoveAt(0);

        return result;
    }

    public EditResult Undo()
    {
        if (Document is not { } current) return EditResult.NoChange("no picture is open");
        if (_undo.Count == 0) return EditResult.NoChange("nothing to undo");

        var (label, state) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        _redo.Add((label, current.Clone()));

        Document = state;

        return EditResult.Ok($"undone {label}. {state.Describe()}");
    }

    public EditResult Redo()
    {
        if (Document is not { } current) return EditResult.NoChange("no picture is open");
        if (_redo.Count == 0) return EditResult.NoChange("nothing to redo");

        var (label, state) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        _undo.Add((label, current.Clone()));

        Document = state;

        return EditResult.Ok($"redone {label}. {state.Describe()}");
    }

    /// <summary>
    /// What would be undone, without undoing it. Asked before pressing the key,
    /// which is the only way to be sure the key is about to do what you think.
    /// </summary>
    public string Describe() =>
        _undo.Count == 0
            ? "nothing to undo"
            : $"{_undo.Count} steps back, the last was {_undo[^1].Label}";
}
