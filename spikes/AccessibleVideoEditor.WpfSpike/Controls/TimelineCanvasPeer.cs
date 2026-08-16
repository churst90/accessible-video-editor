using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.WpfSpike.Controls;

/// <summary>
/// The load-bearing file, and the second half of what this spike proves.
///
/// The canvas is pixels. Nothing in it is a widget, so a screen reader has
/// nothing to read unless the control describes itself - which is exactly the
/// problem GTK sidesteps by putting a real <c>GtkListBox</c> beside the
/// drawing. WPF's answer is a peer tree, and the question is whether that
/// answer actually reaches NVDA and JAWS.
///
/// <b>Shape: a list of tracks.</b> Not a custom control type, not a data grid.
/// A list is what every screen reader already navigates well, and it is the
/// same shape the GTK client exposes - so if this reads, the two heads are
/// structurally one design rather than two ideas.
/// </summary>
public sealed class TimelineCanvasPeer(TimelineCanvas owner) : FrameworkElementAutomationPeer(owner)
{
    private List<AutomationPeer>? _children;

    protected override string GetClassNameCore() => "TimelineCanvas";

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.List;

    protected override string GetNameCore() => "Timeline";

    protected override string GetLocalizedControlTypeCore() => "timeline";

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    /// <summary>
    /// One row per track, built once and reused. Rebuilding the peers on every
    /// keypress hands the screen reader a new set of objects each time, and a
    /// reader that has just been told its object is gone says nothing useful
    /// about where it now is.
    /// </summary>
    protected override List<AutomationPeer> GetChildrenCore() =>
        _children ??= [.. owner.Tracks.Select(track => (AutomationPeer)new TrackRowPeer(owner, track, this))];

    /// <summary>
    /// Says the rows changed contents, without saying they were replaced. This
    /// is the call that makes a moving cursor observable to a screen reader
    /// rather than something it has to be told about out of band.
    /// </summary>
    public void Invalidate()
    {
        foreach (var child in GetChildrenCore().OfType<TrackRowPeer>())
        {
            child.Refresh();
        }
    }
}

/// <summary>
/// One track, as a screen reader sees it: a list item whose name is the same
/// sentence the Linux client speaks, from the same method.
///
/// It implements <see cref="ISelectionItemProvider"/> so the focused track
/// reads as selected. Without it, Up and Down change what is announced but
/// nothing tells the reader which row is current, and four rows with no
/// position in them is not a list you can navigate.
///
/// Everything here is overridden because <see cref="AutomationPeer"/> requires
/// it. The ones that matter are Name, ControlType and IsSelected; the rest are
/// answered honestly and briefly.
/// </summary>
public sealed class TrackRowPeer(TimelineCanvas owner, Track track, AutomationPeer container)
    : AutomationPeer, ISelectionItemProvider
{
    private string _lastName = string.Empty;

    // ---- the three that carry the meaning ----------------------------------

    protected override string GetNameCore() => $"{track.Name}. {owner.DescribeTrack(track)}";

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.ListItem;

    public bool IsSelected => owner.EditCursor.FocusedTrack == track.Id;

    // ---- identity ----------------------------------------------------------

    protected override string GetClassNameCore() => "TimelineTrack";

    protected override string GetAutomationIdCore() => track.Id.ToString();

    protected override string GetItemTypeCore() => "track";

    protected override string GetLocalizedControlTypeCore() => "track";

    protected override string GetHelpTextCore() =>
        "Left and Right move along this track. Up and Down change track.";

    protected override string GetItemStatusCore() =>
        track.Muted ? "audio muted" : track.Locked ? "locked" : string.Empty;

    // ---- state -------------------------------------------------------------

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsKeyboardFocusableCore() => true;

    protected override bool HasKeyboardFocusCore() => IsSelected;

    protected override bool IsOffscreenCore() => false;

    protected override bool IsPasswordCore() => false;

    protected override bool IsRequiredForFormCore() => false;

    protected override AutomationOrientation GetOrientationCore() =>
        AutomationOrientation.Horizontal;

    protected override string GetAcceleratorKeyCore() => string.Empty;

    protected override string GetAccessKeyCore() => string.Empty;

    protected override AutomationPeer GetLabeledByCore() => null!;

    protected override List<AutomationPeer> GetChildrenCore() => [];

    // ---- geometry ----------------------------------------------------------
    //
    // The whole canvas rather than the lane's own strip. A lane's rectangle is
    // known to TimelineLayout, but a wrong rectangle is worse than a coarse
    // one: it moves a magnifier or a touch cursor to the wrong place, and this
    // spike is not asking that question.

    protected override Rect GetBoundingRectangleCore()
    {
        if (!owner.IsVisible) return default;

        var origin = owner.PointToScreen(new Point(0, 0));

        return new Rect(origin.X, origin.Y, owner.ActualWidth, owner.ActualHeight);
    }

    protected override Point GetClickablePointCore() =>
        owner.IsVisible ? owner.PointToScreen(new Point(0, 0)) : new Point(double.NaN, double.NaN);

    protected override void SetFocusCore() => owner.Focus();

    // ---- patterns ----------------------------------------------------------

    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : null!;

    public IRawElementProviderSimple? SelectionContainer => ProviderFromPeer(container);

    // Selection follows the cursor; it is not something a client sets. Said
    // here rather than left to throw, because a provider that throws makes a
    // screen reader treat the whole element as broken.
    public void AddToSelection() { }

    public void RemoveFromSelection() { }

    public void Select() { }

    /// <summary>
    /// Raises a name change only when the name really changed.
    ///
    /// Firing on every keypress regardless makes the screen reader talk over
    /// the announcement the application just pushed - two voices saying nearly
    /// the same thing, which is the failure that gets a feature turned off
    /// rather than reported.
    /// </summary>
    public void Refresh()
    {
        var name = GetNameCore();
        if (name == _lastName) return;

        var previous = _lastName;
        _lastName = name;

        RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previous, name);
    }
}
