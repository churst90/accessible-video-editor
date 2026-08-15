namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// One place the stream is going.
///
/// Streaming to several services at once is one encode sent to several
/// destinations, not several encodes - so every destination gets the same
/// picture, and the settings have to satisfy the <i>strictest</i> of them.
/// <see cref="EncoderSettings.ForTargets"/> is where that is worked out, and it
/// says out loud which service set the limit, because "why is my YouTube stream
/// only 6000 kbps" is otherwise unanswerable.
/// </summary>
public sealed class StreamTarget
{
    public required StreamPlatform Platform { get; init; }

    /// <summary>Shown and spoken. "Twitch" or, for a custom target, whatever you called it.</summary>
    public required string Name { get; set; }

    /// <summary>The ingest server, without the key.</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Never spoken, never written to the status line, never put in a log line.
    /// A stream key is a password that lets anyone broadcast as you, and speech
    /// is the one output that is often on a speaker in a room with other people
    /// in it.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool HasKey => Key.Length > 0;

    /// <summary>The URL ffmpeg is actually given.</summary>
    public string Url => Server.Length == 0
        ? string.Empty
        : $"{Server.TrimEnd('/')}/{Key}";

    /// <summary>
    /// What each service will take. Exceeding these does not fail politely - it
    /// buffers, drops frames, or gets the stream transcoded into mush - so they
    /// are treated as hard limits rather than suggestions.
    /// </summary>
    public int MaxBitrateKbps => Platform switch
    {
        StreamPlatform.Twitch => 6000,
        StreamPlatform.YouTube => 51000,
        StreamPlatform.Facebook => 4000,
        _ => 6000,
    };

    /// <summary>Says whether it is ready, and never says the key.</summary>
    public string Describe() =>
        $"{Name}, {(Enabled ? "enabled" : "disabled")}, "
        + (Server.Length == 0
            ? "no server set"
            : HasKey ? "key set" : "no key set");

    public static StreamTarget For(StreamPlatform platform) => new()
    {
        Platform = platform,
        Name = platform switch
        {
            StreamPlatform.Twitch => "Twitch",
            StreamPlatform.YouTube => "YouTube",
            StreamPlatform.Facebook => "Facebook",
            _ => "Custom RTMP",
        },
        Server = platform switch
        {
            StreamPlatform.Twitch => "rtmp://live.twitch.tv/app",
            StreamPlatform.YouTube => "rtmp://a.rtmp.youtube.com/live2",
            StreamPlatform.Facebook => "rtmps://live-api-s.facebook.com:443/rtmp",
            _ => string.Empty,
        },
    };
}

public enum StreamPlatform
{
    Twitch,
    YouTube,
    Facebook,
    Custom,
}

/// <summary>
/// What the single encode is set to. Derived from the destinations rather than
/// chosen, so going live to one more service cannot silently break the others.
/// </summary>
public readonly record struct EncoderSettings(
    int Width,
    int Height,
    double Fps,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    double KeyframeSeconds,
    string LimitedBy)
{
    public static EncoderSettings ForTargets(
        IReadOnlyList<StreamTarget> targets,
        int width = 1920,
        int height = 1080,
        double fps = 30)
    {
        var live = targets.Where(t => t.Enabled).ToList();

        var strictest = live.Count == 0
            ? null
            : live.Aggregate((a, b) => a.MaxBitrateKbps <= b.MaxBitrateKbps ? a : b);

        // 6000 is the sensible default when nothing is configured: it is what
        // the most restrictive common service takes, so it is never the reason
        // a first stream fails.
        var bitrate = strictest?.MaxBitrateKbps ?? 6000;

        return new EncoderSettings(
            width,
            height,
            fps,
            bitrate,
            160,
            2,
            strictest?.Name ?? "nothing configured");
    }

    /// <summary>
    /// Spoken before going live. Every number here changes what viewers see, so
    /// none of it is hidden behind a settings dialog you would have to go
    /// looking for.
    /// </summary>
    public string Describe() =>
        $"{Width} by {Height}, {Fps:0.#} frames per second, "
        + $"{VideoBitrateKbps} kilobits video, {AudioBitrateKbps} kilobits audio, "
        + $"limited by {LimitedBy}";
}
