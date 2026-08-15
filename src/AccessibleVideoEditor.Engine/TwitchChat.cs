using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Reading a Twitch chat is plain IRC over TLS, and reading it <i>anonymously</i>
/// needs no account at all - you connect as <c>justinfan</c> and any number.
/// That matters here: it means chat works the first time the view is opened,
/// with nothing to register and no key to store. An account is only needed to
/// send or to moderate.
///
/// Twitch is the first platform for exactly this reason. YouTube and Facebook
/// both require an OAuth application before a single message can be read, so
/// they are behind the same interface and say plainly what they are waiting
/// for rather than failing silently.
/// </summary>
public sealed class TwitchChatClient : IChatClient
{
    private TcpClient? _tcp;
    private SslStream? _stream;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cancel;

    public StreamPlatform Platform => StreamPlatform.Twitch;

    public bool Connected { get; private set; }

    public string Channel { get; private set; } = string.Empty;

    /// <summary>Raised on a background thread; the front end marshals it.</summary>
    public event Action<ChatMessage>? Received;

    public event Action<string>? Status;

    /// <summary>
    /// <paramref name="token"/> is an OAuth token for sending. Without one the
    /// connection is read-only, which is a legitimate way to use this and is
    /// said out loud rather than looking like a failure.
    /// </summary>
    public async Task<string> ConnectAsync(string channel, string? token = null, string? nick = null)
    {
        if (Connected) return $"already connected to {Channel}";

        Channel = channel.TrimStart('#').ToLowerInvariant();

        if (Channel.Length == 0) return "no channel name given";

        try
        {
            _cancel = new CancellationTokenSource();
            _tcp = new TcpClient();

            await _tcp.ConnectAsync("irc.chat.twitch.tv", 6697).ConfigureAwait(false);

            _stream = new SslStream(_tcp.GetStream());
            await _stream.AuthenticateAsClientAsync("irc.chat.twitch.tv").ConfigureAwait(false);

            _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };
            var reader = new StreamReader(_stream, Encoding.UTF8);

            // Tags carry the badges and the first-message flag, which is where
            // "a first-time chatter is asking something" comes from.
            await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands").ConfigureAwait(false);

            var anonymous = token is not { Length: > 0 };

            await _writer.WriteLineAsync(
                anonymous ? "PASS SCHMOOPIIE" : $"PASS oauth:{token!.TrimStart("oauth:".ToCharArray())}")
                .ConfigureAwait(false);

            await _writer.WriteLineAsync(
                $"NICK {(anonymous ? $"justinfan{Random.Shared.Next(10000, 99999)}" : nick ?? Channel)}")
                .ConfigureAwait(false);

            await _writer.WriteLineAsync($"JOIN #{Channel}").ConfigureAwait(false);

            Connected = true;

            _ = Task.Run(() => ReadLoopAsync(reader, _cancel.Token));

            return anonymous
                ? $"reading {Channel} on twitch, read only"
                : $"connected to {Channel} on twitch";
        }
        catch (Exception exception)
        {
            Connected = false;
            return $"could not connect to twitch: {exception.Message}";
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                if (line.StartsWith("PING", StringComparison.Ordinal))
                {
                    // Missing a PONG is how a chat connection quietly dies
                    // twenty minutes into a stream.
                    if (_writer is not null)
                    {
                        await _writer.WriteLineAsync("PONG :tmi.twitch.tv").ConfigureAwait(false);
                    }

                    continue;
                }

                if (TwitchIrc.Parse(line) is { } message) Received?.Invoke(message);
            }
        }
        catch (Exception exception)
        {
            Status?.Invoke($"twitch chat disconnected: {exception.Message}");
        }
        finally
        {
            Connected = false;
        }
    }

    public async Task<string> SendAsync(string text)
    {
        if (!Connected || _writer is null) return "not connected";

        try
        {
            await _writer.WriteLineAsync($"PRIVMSG #{Channel} :{text}").ConfigureAwait(false);
            return "sent";
        }
        catch (Exception exception)
        {
            return $"could not send: {exception.Message}";
        }
    }

    public void Disconnect()
    {
        _cancel?.Cancel();
        _writer?.Dispose();
        _stream?.Dispose();
        _tcp?.Dispose();

        _writer = null;
        _stream = null;
        _tcp = null;
        Connected = false;
    }
}

/// <summary>
/// The parsing, separated from the socket so it can be tested against real
/// lines without a network.
/// </summary>
public static class TwitchIrc
{
    public static ChatMessage? Parse(string line, double at = 0)
    {
        if (line.Length == 0) return null;

        var tags = new Dictionary<string, string>();
        var rest = line;

        if (rest.StartsWith('@'))
        {
            var end = rest.IndexOf(' ');
            if (end < 0) return null;

            foreach (var pair in rest[1..end].Split(';'))
            {
                var equals = pair.IndexOf('=');
                if (equals > 0) tags[pair[..equals]] = pair[(equals + 1)..];
            }

            rest = rest[(end + 1)..];
        }

        var space = rest.IndexOf(' ');
        if (space < 0) return null;

        var prefix = rest[..space].TrimStart(':');
        rest = rest[(space + 1)..];

        var command = rest.Split(' ')[0];

        var body = string.Empty;
        var colon = rest.IndexOf(" :", StringComparison.Ordinal);
        if (colon >= 0) body = rest[(colon + 2)..];

        var author = tags.GetValueOrDefault("display-name")
                     ?? prefix.Split('!')[0];

        if (author.Length == 0) author = prefix.Split('!')[0];

        var badges = BadgesFrom(tags.GetValueOrDefault("badges", string.Empty));

        return command switch
        {
            "PRIVMSG" => new ChatMessage(
                StreamPlatform.Twitch,
                author,
                body,
                at,
                ChatKind.Message,
                badges,
                tags.GetValueOrDefault("first-msg") == "1",
                tags.GetValueOrDefault("id", string.Empty),
                tags.GetValueOrDefault("user-id", string.Empty)),

            "USERNOTICE" => new ChatMessage(
                StreamPlatform.Twitch,
                author,
                body,
                at,
                tags.GetValueOrDefault("msg-id") switch
                {
                    "raid" => ChatKind.Raid,
                    "sub" or "resub" or "subgift" or "submysterygift" => ChatKind.Subscribe,
                    _ => ChatKind.System,
                },
                badges),

            _ => null,
        };
    }

    public static ChatBadge BadgesFrom(string badges)
    {
        var result = ChatBadge.None;

        foreach (var badge in badges.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = badge.Split('/')[0];

            result |= name switch
            {
                "moderator" => ChatBadge.Moderator,
                "broadcaster" => ChatBadge.Broadcaster,
                "subscriber" or "founder" => ChatBadge.Subscriber,
                "vip" => ChatBadge.Vip,
                _ => ChatBadge.None,
            };
        }

        return result;
    }
}

public interface IChatClient
{
    StreamPlatform Platform { get; }

    bool Connected { get; }

    event Action<ChatMessage>? Received;

    Task<string> SendAsync(string text);

    void Disconnect();
}
