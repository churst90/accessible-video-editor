namespace AccessibleVideoEditor.Speech;

/// <summary>
/// The application does not synthesise speech. It asks the screen reader to
/// speak.
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
