using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace AccessibleVideoEditor.WpfSpike;

/// <summary>
/// The reason WPF was chosen over MAUI, and the first half of what this spike
/// exists to prove.
///
/// <c>gtk_accessible_announce</c> takes a priority, so an urgent message can
/// overtake a chatty one. MAUI's <c>SemanticScreenReader.Announce</c> has no
/// equivalent - one method, no priority, no interrupt - which would be a
/// regression from what the Linux client already does.
/// <see cref="AutomationPeer.RaiseNotificationEvent"/> is a real match:
/// <c>MostRecent</c> drops whatever is queued behind it, which is exactly what
/// urgency means here, and <c>All</c> queues, which is what a normal
/// announcement means.
///
/// <b>What to listen for:</b> hold an arrow key down. Each press announces a
/// position at <c>Progress</c>. If the priorities are working, you hear the
/// position you have arrived at rather than a queue of every position you
/// passed through - and an error spoken over the top of that stream should
/// interrupt it rather than wait its turn.
/// </summary>
public sealed class UiaAnnouncer(FrameworkElement host)
{
    private readonly string _activityId = "accessible-video-editor";

    public void Say(string text, Priority priority = Priority.Normal)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var peer = UIElementAutomationPeer.FromElement(host)
                   ?? UIElementAutomationPeer.CreatePeerForElement(host);

        if (peer is null) return;

        peer.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            priority switch
            {
                // Interrupts, and throws away anything already queued. What you
                // want from an error and from a fast-moving cursor alike, for
                // opposite reasons: one must be heard now, the other is only
                // worth hearing at its newest value.
                Priority.Urgent => AutomationNotificationProcessing.ImportantMostRecent,
                Priority.Progress => AutomationNotificationProcessing.MostRecent,
                _ => AutomationNotificationProcessing.All,
            },
            text,
            _activityId);
    }

    public enum Priority
    {
        Urgent,
        Normal,
        Progress,
    }
}
