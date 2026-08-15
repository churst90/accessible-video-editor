using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Navigation;

/// <summary>
/// <b>The cursor belongs to the document, not to a pane.</b>
///
/// This is what makes F6 work. Moving from the scrubber to the transcript does
/// not "jump the cursor to the matching timestamp" - the cursor never moved,
/// the lens changed. Panes render <see cref="ProgrammeTime"/>; none of them
/// owns it.
/// </summary>
public sealed class DocumentCursor
{
    public double ProgrammeTime { get; private set; }

    public TrackId? FocusedTrack { get; set; }

    public Granularity Granularity { get; set; } = Granularity.Word;

    public TimeSelection? Selection { get; private set; }

    /// <summary>
    /// What the last deliberate move was about, so a destructive key can act on
    /// what you were just working with rather than making you say it twice.
    ///
    /// Marking in and out means you mean the range; stepping segment to segment
    /// means you mean the segment. Delete asking "which did you mean?" every
    /// time would be safer and unusable.
    /// </summary>
    public EditIntent Intent { get; private set; } = EditIntent.Segment;

    public void Intend(EditIntent intent) => Intent = intent;

    public event EventHandler<CursorMovedEventArgs>? Moved;

    public void MoveTo(double programmeTime, CursorMoveCause cause = CursorMoveCause.Navigation)
    {
        var previous = ProgrammeTime;
        ProgrammeTime = Math.Max(0, programmeTime);
        Moved?.Invoke(this, new CursorMovedEventArgs(previous, ProgrammeTime, cause));
    }

    public void SetSelectionStart(double programmeTime)
    {
        Selection = new TimeSelection(programmeTime, Selection?.End ?? programmeTime);
        Intent = EditIntent.Selection;
    }

    public void SetSelectionEnd(double programmeTime)
    {
        Selection = new TimeSelection(Selection?.Start ?? programmeTime, programmeTime);
        Intent = EditIntent.Selection;
    }

    public void ClearSelection()
    {
        Selection = null;
        Intent = EditIntent.Segment;
    }

    /// <summary>
    /// In the transcript pane a text selection <i>is</i> a time selection - same
    /// object, two renderings. This is the whole of "the two views work
    /// together".
    /// </summary>
    public void SelectRange(double start, double end)
    {
        Selection = new TimeSelection(start, end);
        Intent = EditIntent.Selection;
    }
}

/// <summary>
/// Secondary to splitting. A split leaves a persistent, navigable boundary you
/// can move back to; a selection is invisible state you have to remember, which
/// is the wrong default when you cannot see the screen.
/// </summary>
public readonly record struct TimeSelection(double Start, double End)
{
    /// <summary>
    /// What to say when a mark is set. A zero-length selection is a legitimate
    /// half-made one - you have set the in point and not yet the out - and
    /// calling that "no selection" makes the key look broken.
    /// </summary>
    public string DescribeMark(bool isStart) =>
        IsEmpty
            ? $"{(isStart ? "in" : "out")} point at {Timecode.FormatShort(isStart ? Start : End)}"
            : Describe();

    public double From => Math.Min(Start, End);
    public double To => Math.Max(Start, End);
    public double Length => To - From;
    public bool IsEmpty => Length < 1e-6;

    public bool Contains(double t) => t >= From && t < To;

    public string Describe() =>
        IsEmpty
            ? "no selection"
            : $"selection {Timecode.Speak(Length)}, {Timecode.FormatShort(From)} to {Timecode.FormatShort(To)}";
}

/// <summary>What the last deliberate move was about.</summary>
public enum EditIntent
{
    /// <summary>You were moving between segments, so you mean the segment.</summary>
    Segment,

    /// <summary>You marked in and out, so you mean the range.</summary>
    Selection,
}

public enum CursorMoveCause
{
    Navigation,
    PaneSwitch,
    Playback,
    Edit,
}

public sealed class CursorMovedEventArgs(double from, double to, CursorMoveCause cause) : EventArgs
{
    public double From { get; } = from;
    public double To { get; } = to;
    public CursorMoveCause Cause { get; } = cause;
}
