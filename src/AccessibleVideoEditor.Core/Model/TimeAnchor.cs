namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A point in the programme expressed relative to a spine element, never as an
/// absolute time.
/// </summary>
public readonly record struct TimeAnchor(ElementId Element, double Offset = 0)
{
    public override string ToString() => $"{Element}+{Offset:0.###}";
}
