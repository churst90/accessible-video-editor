namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A named point you can navigate to at marker granularity. Anchored like
/// everything else, so markers survive ripple.
/// </summary>
public sealed class Marker
{
    public required MarkerId Id { get; init; }
    public required TimeAnchor At { get; set; }
    public string Label { get; set; } = string.Empty;
    public MarkerKind Kind { get; set; } = MarkerKind.User;

    public string Describe() =>
        Label.Length > 0 ? $"{Kind.ToString().ToLowerInvariant()} marker, {Label}" : "marker";
}

public enum MarkerKind
{
    User,

    /// <summary>Becomes a YouTube chapter on publish.</summary>
    Chapter,

    /// <summary>Raised by the frame review or capture monitor; shows in the To-Do pane.</summary>
    Issue,
}
