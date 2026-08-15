using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The only speech route: <c>gtk_accessible_announce</c>, which hands the text
/// to whatever screen reader is running. Orca speaks it with its own voice,
/// rate and interrupt behaviour, and the application never touches an audio
/// device to say a word.
/// </summary>
public sealed class GtkAnnouncer(Gtk_.Widget host, AccessibleVideoEditor.Audio.SdlAudioOutput? audio = null) : IAnnouncer
{
    public void Say(string text, AnnouncePriority priority = AnnouncePriority.Normal)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        host.Announce(text, Map(priority));
    }

    /// <summary>
    /// Earcons are sound rather than speech, so they go to the audio device
    /// directly. Silently ignored when there is no device, which is better than
    /// refusing to run.
    /// </summary>
    public void Earcon(Earcon earcon) => audio?.Earcon(earcon);

    /// <summary>
    /// GTK exposes two levels. High interrupts; medium does not. Progress maps
    /// to medium so a burst of cursor moves cannot stall behind itself.
    /// </summary>
    private static Gtk_.AccessibleAnnouncementPriority Map(AnnouncePriority priority) =>
        priority is AnnouncePriority.Urgent or AnnouncePriority.Normal
            ? Gtk_.AccessibleAnnouncementPriority.High
            : Gtk_.AccessibleAnnouncementPriority.Medium;
}
