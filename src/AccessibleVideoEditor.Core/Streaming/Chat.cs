namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// Live chat, which is the part of streaming that existing tools serve worst
/// and that matters most.
///
/// Everything here exists to answer one question: <b>what has to be said out
/// loud, and what must not be?</b> A busy chat read message by message is
/// unusable - it talks over you while you are talking, and you still miss the
/// one line that needed an answer. So chat is filtered, rate-limited and
/// summarised, and the things that actually need you get their own earcon.
/// </summary>
public sealed record ChatMessage(
    StreamPlatform Platform,
    string Author,
    string Text,
    double At,
    ChatKind Kind = ChatKind.Message,
    ChatBadge Badges = ChatBadge.None,
    bool FirstTime = false,
    string Id = "",
    string AuthorId = "")
{
    /// <summary>
    /// Moderation needs to name the exact message and the exact person, not the
    /// display name - two people can be called the same thing, and deleting the
    /// wrong message is not undoable. Empty when the platform did not say.
    /// </summary>
    public bool CanModerate => Id.Length > 0 || AuthorId.Length > 0;

    /// <summary>Cheap and good enough: a question mark, or an opening question word.</summary>
    public bool IsQuestion =>
        Text.Contains('?')
        || QuestionWords.Any(w =>
            Text.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] QuestionWords =
        ["how", "what", "why", "when", "where", "which", "who", "can", "does", "is", "are", "do"];

    public bool Mentions(string name) =>
        name.Length > 0 && Text.Contains(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Which buckets this message falls into. A message can be in several -
    /// a first-time chatter asking a question is both, and the earcon for the
    /// most urgent one wins.
    /// </summary>
    public ChatCategory Categorise(string myName)
    {
        var category = ChatCategory.None;

        if (Kind != ChatKind.Message) return ChatCategory.Event;
        if (Mentions(myName)) category |= ChatCategory.Mention;
        if (FirstTime) category |= ChatCategory.FirstTime;
        if (IsQuestion) category |= ChatCategory.Question;
        if (Badges.HasFlag(ChatBadge.Moderator)) category |= ChatCategory.Moderator;
        if (Badges.HasFlag(ChatBadge.Subscriber)) category |= ChatCategory.Subscriber;

        return category;
    }

    /// <summary>
    /// The earcon that plays before it is read. Ordered by how much it wants
    /// you: being named beats a first-timer beats a question beats the rest.
    /// </summary>
    public static string? EarconFor(ChatCategory category) =>
        category.HasFlag(ChatCategory.Event) ? "chat-event"
        : category.HasFlag(ChatCategory.Mention) ? "chat-mention"
        : category.HasFlag(ChatCategory.FirstTime) ? "chat-first-time"
        : category.HasFlag(ChatCategory.Question) ? "chat-question"
        : category.HasFlag(ChatCategory.Moderator) ? "chat-moderator"
        : null;

    /// <summary>
    /// What is read. The author is dropped when the same person speaks twice
    /// running, and the platform only when it has changed - repeating either on
    /// every line is what makes chat readers exhausting.
    /// </summary>
    public string Speak(bool sameAuthorAsLast, bool samePlatformAsLast, bool announcePlatform)
    {
        var parts = new List<string>();

        if (announcePlatform && !samePlatformAsLast) parts.Add(Platform.ToString().ToLowerInvariant());

        if (Kind != ChatKind.Message)
        {
            parts.Add(Kind switch
            {
                ChatKind.Follow => $"{Author} followed",
                ChatKind.Subscribe => $"{Author} subscribed",
                ChatKind.Raid => $"{Author} raided",
                ChatKind.Donation => $"{Author} donated",
                _ => $"{Author}: {Text}",
            });

            if (Kind is ChatKind.Raid or ChatKind.Donation && Text.Length > 0) parts.Add(Text);

            return string.Join(", ", parts);
        }

        parts.Add(sameAuthorAsLast ? Text : $"{Author}: {Text}");

        return string.Join(", ", parts);
    }
}

public enum ChatKind
{
    Message,
    Follow,
    Subscribe,
    Raid,
    Donation,
    System,
}

[Flags]
public enum ChatBadge
{
    None = 0,
    Subscriber = 1,
    Moderator = 2,
    Broadcaster = 4,
    Vip = 8,
}

[Flags]
public enum ChatCategory
{
    None = 0,
    Mention = 1,
    FirstTime = 2,
    Question = 4,
    Moderator = 8,
    Subscriber = 16,
    Event = 32,
}

/// <summary>
/// One platform's chat, kept separately.
///
/// Separate rather than merged because that is how you keep track of who you
/// are talking to: a reply typed into the wrong platform goes to the wrong
/// audience. The unified reading is a view over these, not a replacement for
/// them.
/// </summary>
public sealed class ChatChannel(StreamPlatform platform)
{
    private readonly List<ChatMessage> _messages = [];

    public StreamPlatform Platform { get; } = platform;

    public string Name => Platform.ToString().ToLowerInvariant() + " chat";

    public bool Connected { get; set; }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>
    /// How many have arrived since you last read to the end. This is the number
    /// that gets spoken - "eleven waiting" - rather than reading eleven lines.
    /// </summary>
    public int Unread { get; private set; }

    /// <summary>
    /// Where you are reading. Null means you are at the live end and new
    /// messages should be read as they arrive; a number means you have scrolled
    /// back, and nothing new interrupts until you return.
    /// </summary>
    public int? ReadingAt { get; private set; }

    public bool IsAtLiveEnd => ReadingAt is null;

    public void Add(ChatMessage message)
    {
        _messages.Add(message);

        // Cap the history rather than growing without bound through an
        // eight-hour stream. Two thousand is far more than anyone scrolls back.
        if (_messages.Count > 2000)
        {
            _messages.RemoveRange(0, 500);
            if (ReadingAt is { } at) ReadingAt = Math.Max(0, at - 500);
        }

        if (!IsAtLiveEnd) Unread++;
    }

    /// <summary>
    /// Step back through history. The first press from the live end lands on
    /// the newest message rather than the one before it, because the newest is
    /// what you just heard and want repeated.
    /// </summary>
    public ChatMessage? Older()
    {
        if (_messages.Count == 0) return null;

        ReadingAt = ReadingAt is null
            ? _messages.Count - 1
            : Math.Max(0, ReadingAt.Value - 1);

        return _messages[ReadingAt.Value];
    }

    public ChatMessage? Newer()
    {
        if (_messages.Count == 0 || ReadingAt is null) return null;

        if (ReadingAt.Value >= _messages.Count - 1)
        {
            ReturnToLive();
            return null;
        }

        ReadingAt++;

        return _messages[ReadingAt.Value];
    }

    /// <summary>Back to following along live, and the waiting count is cleared.</summary>
    public void ReturnToLive()
    {
        ReadingAt = null;
        Unread = 0;
    }

    public ChatMessage? Current =>
        ReadingAt is { } at && at < _messages.Count ? _messages[at] : _messages.LastOrDefault();

    public string Describe() =>
        !Connected ? $"{Name}, not connected"
        : _messages.Count == 0 ? $"{Name}, connected, nothing yet"
        : IsAtLiveEnd ? $"{Name}, {_messages.Count} messages, live"
        : $"{Name}, reading back, {Unread} waiting";
}

/// <summary>
/// Every platform's chat at once, and the rules for what gets spoken.
/// </summary>
public sealed class ChatStore
{
    private readonly Dictionary<StreamPlatform, ChatChannel> _channels = [];

    /// <summary>Your own name, so being addressed can be picked out of the noise.</summary>
    public string MyName { get; set; } = string.Empty;

    /// <summary>
    /// Which buckets are read aloud as they arrive. Everything still arrives
    /// and can be read back; this is only about what interrupts you.
    /// </summary>
    public ChatCategory Speaking { get; set; } =
        ChatCategory.Mention | ChatCategory.FirstTime | ChatCategory.Question | ChatCategory.Event;

    /// <summary>
    /// When true, every message is read. Fine for a quiet chat and impossible
    /// for a busy one, so it is a deliberate choice rather than the default.
    /// </summary>
    public bool SpeakEverything { get; set; }

    public IReadOnlyCollection<ChatChannel> Channels => _channels.Values;

    public ChatChannel Channel(StreamPlatform platform)
    {
        if (!_channels.TryGetValue(platform, out var channel))
        {
            channel = new ChatChannel(platform);
            _channels[platform] = channel;
        }

        return channel;
    }

    public int TotalUnread => _channels.Values.Sum(c => c.Unread);

    private ChatMessage? _lastSpoken;

    /// <summary>
    /// Takes a message in and says what, if anything, to read out.
    ///
    /// Rate limiting is the important part. Past <see cref="Burst"/> messages
    /// inside <see cref="BurstWindow"/> seconds the reading stops and a count
    /// takes over, because a chat reader that cannot be out-talked is a chat
    /// reader you turn off.
    /// </summary>
    public ChatAnnouncement Receive(ChatMessage message)
    {
        var channel = Channel(message.Platform);
        channel.Add(message);

        var category = message.Categorise(MyName);

        if (!channel.IsAtLiveEnd)
        {
            return new ChatAnnouncement(null, null, category, Suppressed: true);
        }

        var wanted = SpeakEverything || category == ChatCategory.None
            ? SpeakEverything
            : (category & Speaking) != 0;

        if (!wanted) return new ChatAnnouncement(null, null, category, Suppressed: true);

        _recent.Add(message.At);
        _recent.RemoveAll(t => t < message.At - BurstWindow);

        if (_recent.Count > Burst)
        {
            // Still earcon it: the sound is what tells you something wants you
            // even when the words have been held back.
            return new ChatAnnouncement(
                null,
                ChatMessage.EarconFor(category),
                category,
                Suppressed: true);
        }

        var text = message.Speak(
            sameAuthorAsLast: _lastSpoken?.Author == message.Author
                              && _lastSpoken?.Platform == message.Platform,
            samePlatformAsLast: _lastSpoken?.Platform == message.Platform,
            announcePlatform: _channels.Count > 1);

        _lastSpoken = message;

        return new ChatAnnouncement(text, ChatMessage.EarconFor(category), category, Suppressed: false);
    }

    public const int Burst = 6;
    public const double BurstWindow = 4;

    private readonly List<double> _recent = [];

    /// <summary>
    /// What to say when you ask how chat is doing, or when a burst has been
    /// held back. One sentence, all platforms.
    /// </summary>
    public string Summarise()
    {
        if (_channels.Count == 0) return "no chat connected";

        var connected = _channels.Values.Where(c => c.Connected).ToList();
        if (connected.Count == 0) return "no chat connected";

        var waiting = TotalUnread;

        var parts = connected.Select(c =>
            $"{c.Platform.ToString().ToLowerInvariant()} {c.Messages.Count}");

        return waiting > 0
            ? $"{string.Join(", ", parts)}, {waiting} waiting"
            : string.Join(", ", parts);
    }
}

/// <summary>What the front end should do about one arriving message.</summary>
public readonly record struct ChatAnnouncement(
    string? Speak,
    string? Earcon,
    ChatCategory Category,
    bool Suppressed);
