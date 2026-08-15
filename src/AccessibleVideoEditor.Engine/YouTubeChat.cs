using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// YouTube live chat, over the Data API.
///
/// Reading needs only an <b>API key</b> and the live video's id, which is worth
/// knowing: it is far less to set up than the OAuth application that posting
/// and moderating require, and reading is most of what anyone wants. So the two
/// are kept separate - an API key gets you a working chat pane today, and
/// signing in later adds the rest.
///
/// YouTube tells you how often to poll and will throttle you if you ignore it,
/// so <c>pollingIntervalMillis</c> from each response is obeyed rather than
/// guessed at.
/// </summary>
public sealed class YouTubeChatClient(HttpClient? http = null) : IChatClient
{
    private readonly HttpClient _http = http ?? new HttpClient();
    private CancellationTokenSource? _cancel;
    private string _liveChatId = string.Empty;
    private string _pageToken = string.Empty;
    private readonly HashSet<string> _seenAuthors = [];

    public StreamPlatform Platform => StreamPlatform.YouTube;

    public bool Connected { get; private set; }

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Present only once you have signed in; unlocks everything but reading.</summary>
    public string OAuthToken { get; set; } = string.Empty;

    public event Action<ChatMessage>? Received;

    public event Action<string>? Status;

    /// <summary>
    /// Finds the live chat attached to a video, then follows it. The video id
    /// changes for every broadcast, which is why it is asked for each time
    /// rather than remembered as if it were permanent.
    /// </summary>
    public async Task<string> ConnectAsync(string videoId, CancellationToken ct = default)
    {
        if (ApiKey.Length == 0 && OAuthToken.Length == 0)
        {
            return "youtube needs an api key; press K in the streamer view to add one";
        }

        if (videoId.Length == 0) return "no youtube video id given";

        try
        {
            var url = "https://www.googleapis.com/youtube/v3/videos"
                      + $"?part=liveStreamingDetails&id={Uri.EscapeDataString(videoId)}"
                      + KeyQuery();

            using var response = await Send(HttpMethod.Get, url, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return $"youtube refused: {Reason(body)}";

            _liveChatId = LiveChatIdFrom(body);

            if (_liveChatId.Length == 0)
            {
                return "that video has no live chat; it may not be live yet";
            }

            Connected = true;
            _cancel = new CancellationTokenSource();

            _ = Task.Run(() => PollAsync(_cancel.Token), CancellationToken.None);

            return OAuthToken.Length > 0
                ? "connected to youtube chat"
                : "reading youtube chat, read only";
        }
        catch (Exception exception)
        {
            return $"could not connect to youtube: {exception.Message}";
        }
    }

    /// <summary>Pulls the live chat id out of a videos.list response.</summary>
    public static string LiveChatIdFrom(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement
                .GetProperty("items")
                .EnumerateArray()
                .Select(item => item.TryGetProperty("liveStreamingDetails", out var details)
                                && details.TryGetProperty("activeLiveChatId", out var id)
                    ? id.GetString() ?? string.Empty
                    : string.Empty)
                .FirstOrDefault(id => id.Length > 0) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var wait = 5000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = "https://www.googleapis.com/youtube/v3/liveChat/messages"
                          + $"?liveChatId={Uri.EscapeDataString(_liveChatId)}"
                          + "&part=snippet,authorDetails&maxResults=200"
                          + (_pageToken.Length > 0 ? $"&pageToken={_pageToken}" : string.Empty)
                          + KeyQuery();

                using var response = await Send(HttpMethod.Get, url, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Status?.Invoke($"youtube chat stopped: {Reason(body)}");
                    Connected = false;
                    return;
                }

                var page = ParsePage(body, _seenAuthors);

                _pageToken = page.NextPageToken;

                // YouTube says how often to come back and throttles anyone who
                // ignores it.
                wait = Math.Clamp(page.PollingIntervalMs, 2000, 30000);

                foreach (var message in page.Messages) Received?.Invoke(message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Status?.Invoke($"youtube chat error: {exception.Message}");
            }

            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The parsing, separate from the network so it can be tested against real
    /// responses without an account.
    ///
    /// YouTube has no first-message flag, so a first-timer is worked out from
    /// who has been seen this session. That is a slightly different meaning
    /// from Twitch's - first time <i>today</i> rather than ever - and it is the
    /// more useful one while you are live anyway.
    /// </summary>
    public static YouTubeChatPage ParsePage(string json, HashSet<string>? seenAuthors = null)
    {
        var messages = new List<ChatMessage>();
        var next = string.Empty;
        var interval = 5000;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("nextPageToken", out var token)) next = token.GetString() ?? string.Empty;

            if (root.TryGetProperty("pollingIntervalMillis", out var poll))
            {
                interval = poll.ValueKind == JsonValueKind.Number
                    ? poll.GetInt32()
                    : int.TryParse(poll.GetString(), out var parsed) ? parsed : 5000;
            }

            if (!root.TryGetProperty("items", out var items)) return new(messages, next, interval);

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("snippet", out var snippet)) continue;

                var author = item.TryGetProperty("authorDetails", out var details) ? details : default;

                var name = Text(author, "displayName");
                if (name.Length == 0) name = "someone";

                var badges = ChatBadge.None;
                if (Flag(author, "isChatOwner")) badges |= ChatBadge.Broadcaster;
                if (Flag(author, "isChatModerator")) badges |= ChatBadge.Moderator;
                if (Flag(author, "isChatSponsor")) badges |= ChatBadge.Subscriber;

                var type = Text(snippet, "type");

                var kind = type switch
                {
                    "superChatEvent" or "superStickerEvent" => ChatKind.Donation,
                    "newSponsorEvent" or "memberMilestoneChatEvent" => ChatKind.Subscribe,
                    "textMessageEvent" => ChatKind.Message,
                    _ => ChatKind.System,
                };

                var text = Text(snippet, "displayMessage");

                var first = seenAuthors is not null && name.Length > 0 && seenAuthors.Add(name);

                var messageId = Text(item, "id");
                var channelId = Text(author, "channelId");

                messages.Add(new ChatMessage(
                    StreamPlatform.YouTube, name, text, 0, kind, badges,
                    first && kind == ChatKind.Message, messageId, channelId));
            }
        }
        catch (Exception)
        {
            // A malformed page is skipped rather than ending the chat: the next
            // poll is five seconds away and usually fine.
        }

        return new YouTubeChatPage(messages, next, interval);
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Google's errors carry a usable sentence; digging it out is the
    /// difference between "youtube refused" and "the api key is not valid".
    /// </summary>
    public static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "no reason given";
            }
        }
        catch (Exception)
        {
        }

        return "no reason given";
    }

    private string KeyQuery() =>
        OAuthToken.Length > 0 || ApiKey.Length == 0 ? string.Empty : $"&key={Uri.EscapeDataString(ApiKey)}";

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        if (OAuthToken.Length > 0)
        {
            request.Headers.Authorization = new("Bearer", OAuthToken);
        }

        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<string> SendAsync(string text)
    {
        if (OAuthToken.Length == 0) return "signing in to youtube is needed to post";
        if (_liveChatId.Length == 0) return "not connected to a youtube chat";

        var payload = new
        {
            snippet = new
            {
                liveChatId = _liveChatId,
                type = "textMessageEvent",
                textMessageDetails = new { messageText = text },
            },
        };

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            request.Headers.Authorization = new("Bearer", OAuthToken);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "sent"
                : $"youtube refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not send: {exception.Message}";
        }
    }

    /// <summary>
    /// A timeout on YouTube is a temporary ban with a duration; a ban is the
    /// same call without one. There is no separate endpoint for each.
    /// </summary>
    public async Task<string> BanAsync(string channelId, int? seconds)
    {
        if (OAuthToken.Length == 0) return "signing in to youtube is needed to moderate";

        var payload = new
        {
            snippet = new
            {
                liveChatId = _liveChatId,
                type = seconds is null ? "permanent" : "temporary",
                banDurationSeconds = seconds,
                bannedUserDetails = new { channelId },
            },
        };

        return await PostAsync(
            "https://www.googleapis.com/youtube/v3/liveChat/bans?part=snippet",
            payload,
            seconds is null ? "banned" : $"timed out for {seconds} seconds").ConfigureAwait(false);
    }

    public async Task<string> DeleteAsync(string messageId)
    {
        if (OAuthToken.Length == 0) return "signing in to youtube is needed to moderate";

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"https://www.googleapis.com/youtube/v3/liveChat/messages?id={Uri.EscapeDataString(messageId)}");

            request.Headers.Authorization = new("Bearer", OAuthToken);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "message deleted"
                : $"youtube refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not delete: {exception.Message}";
        }
    }

    private async Task<string> PostAsync(string url, object payload, string success)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload),
            };

            request.Headers.Authorization = new("Bearer", OAuthToken);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? success
                : $"youtube refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not do that: {exception.Message}";
        }
    }

    public void Disconnect()
    {
        _cancel?.Cancel();
        Connected = false;
    }
}

public sealed record YouTubeChatPage(
    IReadOnlyList<ChatMessage> Messages,
    string NextPageToken,
    int PollingIntervalMs);
