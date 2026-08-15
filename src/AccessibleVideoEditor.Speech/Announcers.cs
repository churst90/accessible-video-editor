namespace AccessibleVideoEditor.Speech;

/// <summary>
/// The application does not synthesise speech. It asks the screen reader to
/// speak.
///
/// An earlier design had Accessible Video Editor talking to speech-dispatcher itself. That was
/// wrong twice over: spawning a process per utterance made every announcement
/// arrive late, and "interrupt" could not work at all, because the process
/// being killed had already exited after queueing its message. But the deeper
/// problem was the design, not the bug - an application that voices itself
/// duplicates the screen reader, ignores its rate and voice settings, and
/// fights it for the audio channel.
///
/// So there is exactly one route: the toolkit's announcement API, which on
/// GTK is <c>gtk_accessible_announce</c>. Orca speaks it, in Orca's voice, at
/// Orca's rate, with Orca's interrupt behaviour.
/// </summary>
public interface IAnnouncer
{
    void Say(string text, AnnouncePriority priority = AnnouncePriority.Normal);

    /// <summary>Non-speech feedback. Faster to perceive than words at navigation speed.</summary>
    void Earcon(Earcon earcon);
}

public enum AnnouncePriority
{
    /// <summary>Interrupts. Errors, refused actions, "where am I".</summary>
    Urgent,

    /// <summary>Interrupts. Edit confirmations.</summary>
    Normal,

    /// <summary>
    /// Superseded by anything newer. Cursor position while moving fast - the
    /// newest position is the only one worth hearing.
    /// </summary>
    Progress,
}

/// <summary>
/// Earcons carry what is too slow to speak while navigating: a cut boundary
/// passing under the cursor, a title coming on screen, entering b-roll. You
/// should be able to hold Right and hear the shape of the video go by.
///
/// These are the one thing the application does play itself, because they are
/// sound rather than speech and the screen reader has no notion of them.
/// </summary>
public enum Earcon
{
    Boundary,
    Transition,
    TitleOn,
    BrollEnter,
    BrollExit,
    HoleEnter,
    SelectionEdge,
    Start,
    End,
    Refused,
    Confirmed,

    // Live. Chat earcons exist so you can tell what kind of message arrived
    // without the words - the sound is faster than the sentence, and in a busy
    // chat the words are held back anyway.
    ChatMention,
    ChatFirstTime,
    ChatQuestion,
    ChatModerator,
    ChatEvent,
    SceneSwitch,
    OnAir,
    OffAir,
}

/// <summary>Used by tests and headless CLI runs.</summary>
public sealed class NullAnnouncer : IAnnouncer
{
    public List<string> Spoken { get; } = [];

    public void Say(string text, AnnouncePriority priority = AnnouncePriority.Normal) => Spoken.Add(text);

    public void Earcon(Earcon earcon) { }
}
