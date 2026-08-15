namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// What each platform actually lets you do.
///
/// These differ, and pretending otherwise is the worst option available: a
/// moderation key that appears to work and does nothing is how someone stays in
/// a chat you thought you had removed them from. So every capability is stated,
/// and where a platform has no equivalent the application says what the nearest
/// thing is rather than failing silently.
/// </summary>
public readonly record struct ChatCapabilities(
    bool Read,
    bool Send,
    bool Delete,
    bool Timeout,
    bool Ban,
    bool Pin,
    bool Announce,
    string ReadRequires,
    string ModerateRequires)
{
    public static ChatCapabilities For(StreamPlatform platform) => platform switch
    {
        // Reading needs nothing at all - an anonymous IRC connection. Everything
        // else goes through Helix and needs a token with moderator scopes.
        // Twitch removed chat commands like /ban from IRC in 2023, so anything
        // that still sends them is quietly doing nothing.
        StreamPlatform.Twitch => new(
            Read: true, Send: true, Delete: true, Timeout: true, Ban: true,
            Pin: false, Announce: true,
            ReadRequires: "nothing; Twitch chat reads anonymously",
            ModerateRequires: "a Twitch token with moderator scopes"),

        // An API key reads a public live chat. Posting, deleting and banning
        // are OAuth as the channel owner. YouTube has no pin.
        StreamPlatform.YouTube => new(
            Read: true, Send: true, Delete: true, Timeout: true, Ban: true,
            Pin: false, Announce: false,
            ReadRequires: "a YouTube API key and the live video's id",
            ModerateRequires: "signing in to YouTube as the channel"),

        // Facebook hides rather than deletes, and blocks rather than times out.
        StreamPlatform.Facebook => new(
            Read: true, Send: true, Delete: true, Timeout: false, Ban: true,
            Pin: false, Announce: false,
            ReadRequires: "a page access token and the live video's id",
            ModerateRequires: "a page token with moderation permissions"),

        _ => new(false, false, false, false, false, false, false,
            "an RTMP destination has no chat", "an RTMP destination has no chat"),
    };

    /// <summary>
    /// Spoken when something is asked for that this platform does not have.
    /// Names the nearest equivalent, because "no" without an alternative is
    /// where people give up.
    /// </summary>
    public string Explain(ChatAction action, StreamPlatform platform)
    {
        var name = platform.ToString().ToLowerInvariant();

        return action switch
        {
            ChatAction.Pin when platform == StreamPlatform.Twitch =>
                "twitch has no pin; an announcement is the nearest thing, on control shift P",

            ChatAction.Pin =>
                $"{name} has no way to pin a message from outside its own app",

            ChatAction.Timeout when platform == StreamPlatform.Facebook =>
                "facebook has no timeout; blocking the person from the page is the nearest thing",

            ChatAction.Announce =>
                $"{name} has no announcements",

            _ => $"{name} does not support that",
        };
    }

    public bool Can(ChatAction action) => action switch
    {
        ChatAction.Send => Send,
        ChatAction.Delete => Delete,
        ChatAction.Timeout => Timeout,
        ChatAction.Ban => Ban,
        ChatAction.Pin => Pin,
        ChatAction.Announce => Announce,
        _ => Read,
    };

    /// <summary>What is possible right now, given what is actually configured.</summary>
    public string Describe(StreamPlatform platform, bool hasReadCredentials, bool hasModeratorCredentials)
    {
        var name = platform.ToString().ToLowerInvariant();

        if (!hasReadCredentials) return $"{name} needs {ReadRequires}";

        var verbs = new List<string> { "read" };

        if (hasModeratorCredentials)
        {
            if (Send) verbs.Add("reply");
            if (Delete) verbs.Add("delete");
            if (Timeout) verbs.Add("time out");
            if (Ban) verbs.Add("ban");
            if (Announce) verbs.Add("announce");
        }

        return hasModeratorCredentials
            ? $"{name}: {string.Join(", ", verbs)}"
            : $"{name}: read only. {ModerateRequires} to do more";
    }
}

public enum ChatAction
{
    Read,
    Send,
    Delete,
    Timeout,
    Ban,
    Pin,
    Announce,
}
