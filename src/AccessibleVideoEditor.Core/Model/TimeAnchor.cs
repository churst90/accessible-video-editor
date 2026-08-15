namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A point in the programme expressed relative to a spine element, never as an
/// absolute time.
///
/// This is the single rule that keeps ripple editing safe: overlays, titles,
/// b-roll and markers all ride along when the spine changes, because none of
/// them stores a number that the edit could invalidate. Absolute anchoring
/// would break on every ripple, silently.
/// </summary>
public readonly record struct TimeAnchor(ElementId Element, double Offset = 0)
{
    public override string ToString() => $"{Element}+{Offset:0.###}";
}
