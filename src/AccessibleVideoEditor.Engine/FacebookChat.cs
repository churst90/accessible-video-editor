using System.Net.Http.Json;
using System.Text.Json;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Facebook live comments, over the Graph API.
///
/// Facebook's model is different from the other two and the difference matters:
/// it <b>hides</b> comments rather than deleting them, and it <b>blocks</b>
/// people from the page rather than timing them out. Those are not the same
/// actions with different names, so they are not presented as if they were -
/// blocking is permanent and page-wide, which is a much bigger thing to do by
/// accident.
/// </summary>
public sealed class FacebookChatClient(HttpClient? http = null) : IChatClient
{
    private const string Graph = "https://graph.facebook.com/v21.0";

    private readonly HttpClient _http = http ?? new HttpClient();
    private CancellationTokenSource? _cancel;
    private string _videoId = string.Empty;
    private readonly HashSet<string> _seen = [];
    private readonly HashSet<string> _seenAuthors = [];

    public StreamPlatform Platform => StreamPlatform.Facebook;

    public bool Connected { get; private set; }

    public string Token { get; set; } = string.Empty;

    public event Action<ChatMessage>? Received;

    public event Action<string>? Status;

    public async Task<string> ConnectAsync(string liveVideoId, CancellationToken ct = default)
    {
        if (Token.Length == 0) return "facebook needs a page access token; press K to add one";
        if (liveVideoId.Length == 0) return "no facebook live video id given";

        _videoId = liveVideoId;

        try
        {
            // One request first, so a bad token is a sentence now rather than
            // silence for the whole stream.
            using var response = await _http.GetAsync(CommentsUrl(1), ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return $"facebook refused: {Reason(body)}";

            Connected = true;
            _cancel = new CancellationTokenSource();

            _ = Task.Run(() => PollAsync(_cancel.Token), CancellationToken.None);

            return "connected to facebook comments";
        }
        catch (Exception exception)
        {
            return $"could not connect to facebook: {exception.Message}";
        }
    }

    private string CommentsUrl(int limit) =>
        $"{Graph}/{_videoId}/comments"
        + $"?fields=id,from,message,created_time&order=reverse_chronological&limit={limit}"
        + $"&access_token={Uri.EscapeDataString(Token)}";

    /// <summary>
    /// Facebook has no streaming endpoint for live comments, so this polls.
    /// Five seconds is a compromise: faster burns the rate limit on a long
    /// stream, slower and a reply arrives too late to be a reply.
    /// </summary>
    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await _http.GetAsync(CommentsUrl(50), ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    Status?.Invoke($"facebook comments stopped: {Reason(body)}");
                    Connected = false;
                    return;
                }

                // Newest first from the API, so it is reversed to be read in the
                // order people actually said things.
                foreach (var (id, message) in Parse(body, _seenAuthors).Reverse())
                {
                    if (!_seen.Add(id)) continue;

                    Received?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Status?.Invoke($"facebook comments error: {exception.Message}");
            }

            await Task.Delay(5000, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Comment ids come back with the messages so duplicates can be dropped -
    /// polling the same window repeatedly is otherwise a machine that reads you
    /// the same comment every five seconds.
    /// </summary>
    public static IReadOnlyList<(string Id, ChatMessage Message)> Parse(
        string json,
        HashSet<string>? seenAuthors = null)
    {
        var results = new List<(string, ChatMessage)>();

        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data)) return results;

            foreach (var comment in data.EnumerateArray())
            {
                var id = comment.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                if (id.Length == 0) continue;

                var name = comment.TryGetProperty("from", out var from)
                           && from.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString() ?? "someone"
                    : "someone";

                var text = comment.TryGetProperty("message", out var message)
                    ? message.GetString() ?? string.Empty
                    : string.Empty;

                var first = seenAuthors is not null && seenAuthors.Add(name);

                var authorId = from.ValueKind == JsonValueKind.Object
                               && from.TryGetProperty("id", out var fromId)
                    ? fromId.GetString() ?? string.Empty
                    : string.Empty;

                results.Add((id, new ChatMessage(
                    StreamPlatform.Facebook, name, text, 0, ChatKind.Message, ChatBadge.None,
                    first, id, authorId)));
            }
        }
        catch (Exception)
        {
        }

        return results;
    }

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

    public async Task<string> SendAsync(string text)
    {
        if (Token.Length == 0) return "facebook needs a page access token";
        if (_videoId.Length == 0) return "not connected to a facebook live video";

        try
        {
            using var response = await _http.PostAsync(
                $"{Graph}/{_videoId}/comments?access_token={Uri.EscapeDataString(Token)}",
                JsonContent.Create(new { message = text })).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "sent"
                : $"facebook refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not send: {exception.Message}";
        }
    }

    /// <summary>Hidden rather than deleted, which is what Facebook actually does.</summary>
    public async Task<string> HideAsync(string commentId)
    {
        try
        {
            using var response = await _http.PostAsync(
                $"{Graph}/{commentId}?is_hidden=true&access_token={Uri.EscapeDataString(Token)}",
                null).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "comment hidden"
                : $"facebook refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not hide it: {exception.Message}";
        }
    }

    public async Task<string> DeleteAsync(string commentId)
    {
        try
        {
            using var response = await _http.DeleteAsync(
                $"{Graph}/{commentId}?access_token={Uri.EscapeDataString(Token)}").ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "comment deleted"
                : $"facebook refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not delete it: {exception.Message}";
        }
    }

    /// <summary>
    /// Page-wide and permanent, which is why the caller confirms first. This is
    /// not a timeout by another name.
    /// </summary>
    public async Task<string> BlockAsync(string pageId, string userId)
    {
        try
        {
            using var response = await _http.PostAsync(
                $"{Graph}/{pageId}/blocked?user={Uri.EscapeDataString(userId)}"
                + $"&access_token={Uri.EscapeDataString(Token)}",
                null).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? "blocked from the page"
                : $"facebook refused: {Reason(await response.Content.ReadAsStringAsync().ConfigureAwait(false))}";
        }
        catch (Exception exception)
        {
            return $"could not block them: {exception.Message}";
        }
    }

    public void Disconnect()
    {
        _cancel?.Cancel();
        Connected = false;
    }
}
