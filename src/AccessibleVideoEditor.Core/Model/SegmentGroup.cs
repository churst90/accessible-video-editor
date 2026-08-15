namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// A run of consecutive segments treated as one named thing - "the intro", "the
/// demo". What other editors call a compound or nested clip.
///
/// <b>It is a grouping, not a container.</b> The members stay in the spine where
/// they are; this records that they belong together. That is deliberate:
/// nesting would mean programme time had to be computed through a tree, and
/// every part of the application that maps time - navigation, the transcript,
/// overlay anchors, the render plan, the EDL - would need to learn about depth.
/// The value people actually want from a compound is "one object I can move",
/// and a grouping gives that without moving a single invariant.
///
/// It also keeps something nesting would take away. A nested clip is opaque:
/// you have to open it to reach what is inside. Here the members stay
/// individually reachable, and <see cref="Collapsed"/> only decides whether
/// navigation <i>stops</i> on them - so a group is one object when you are
/// moving it and ten segments when you are fixing one, without converting
/// between the two.
/// </summary>
public sealed class SegmentGroup
{
    public required GroupId Id { get; init; }

    /// <summary>The whole point. A group without a name is a selection.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Members in spine order. Held by ID rather than by index so that an edit
    /// elsewhere in the spine cannot silently change which segments are in the
    /// group.
    /// </summary>
    public List<ElementId> Members { get; set; } = [];

    /// <summary>
    /// Collapsed means navigation treats the whole group as one stop and the
    /// edit verbs act on all of it. Expanded means it behaves exactly as it did
    /// before it was grouped, and the name is just something the cursor
    /// mentions.
    /// </summary>
    public bool Collapsed { get; set; } = true;

    public string Describe(int count, double duration) =>
        $"{Name}, {count} segment{(count == 1 ? string.Empty : "s")}, {Timecode.Speak(duration)}"
        + (Collapsed ? string.Empty : ", expanded");
}
