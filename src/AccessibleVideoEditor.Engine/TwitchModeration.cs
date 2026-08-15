using System.Net.Http.Json;
using System.Text.Json;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Twitch moderation, over Helix.
///
/// <b>Not over IRC.</b> Twitch removed chat commands like <c>/ban</c> and
/// <c>/timeout</c> from IRC in 2023; sending them now does nothing at all, and
/// does it silently. Any client still doing that appears to work and does not,
/// which is the exact failure this application cannot have - you would believe
/// someone had been removed from your chat when they had not.
///
/// Helix wants numeric user ids rather than names, so names are looked up and
/// cached. It also wants your application's client id alongside the token.
/// </summary>
public sealed class TwitchModeration(HttpClient? http = null)
{
    private const string Helix = "https://api.twitch.tv/helix";

    private readonly HttpClient _http = http ?? new HttpClient();
    private readonly Dictionary<string, string> _userIds = new(StringComparer.OrdinalIgnoreCase);

    public string Token { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>The channel being moderated, as a numeric id.</summary>
    public string BroadcasterId { get; set; } = string.Empty;

    /// <summary>You, as a numeric id. Usually the same as the broadcaster.</summary>
    public string ModeratorId { get; set; } = string.Empty;

    public bool Ready => Token.Length > 0 && ClientId.Length > 0 && BroadcasterId.Length > 0;

    public string Missing =>
        Token.Length == 0 ? "a twitch token"
        : ClientId.Length == 0 ? "a twitch client id"
        : BroadcasterId.Length == 0 ? "the channel to moderate"
        : string.Empty;

    /// <summary>
    /// Turns a login name into the numeric id Helix needs. Cached, because
    /// moderating the same person twice should not cost two round trips while
    /// you are live.
    /// </summary>
    public async Task<string?> UserIdAsync(string login, CancellationToken ct = default)
    {
        if (login.Length == 0) return null;
        if (_userIds.TryGetValue(login, out var cached)) return cached;

        var body = await GetAsync($"{Helix}/users?login={Uri.EscapeDataString(login)}", ct).ConfigureAwait(false);
        if (body is null) return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            var id = document.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .Select(u => u.GetProperty("id").GetString())
                .FirstOrDefault();

            if (id is not null) _userIds[login] = id;

            return id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A timeout and a ban are the same endpoint; the difference is whether a
    /// duration is given. Announced with the duration so "timed out" is never
    /// ambiguous about for how long.
    /// </summary>
    public async Task<string> BanAsync(string login, int? seconds, string reason = "")
    {
        if (!Ready) return $"moderating twitch needs {Missing}";

        var userId = await UserIdAsync(login).ConfigureAwait(false);
        if (userId is null) return $"there is no twitch user called {login}";

        var payload = new
        {
            data = new
            {
                user_id = userId,
                duration = seconds,
                reason = reason.Length > 0 ? reason : null,
            },
        };

        var result = await PostAsync(
            $"{Helix}/moderation/bans?broadcaster_id={BroadcasterId}&moderator_id={Moderator}",
            payload).ConfigureAwait(false);

        return result ?? (seconds is null
            ? $"{login} banned"
            : $"{login} timed out for {Spoken(seconds.Value)}");
    }

    public async Task<string> UnbanAsync(string login)
    {
        if (!Ready) return $"moderating twitch needs {Missing}";

        var userId = await UserIdAsync(login).ConfigureAwait(false);
        if (userId is null) return $"there is no twitch user called {login}";

        var result = await SendAsync(
            HttpMethod.Delete,
            $"{Helix}/moderation/bans?broadcaster_id={BroadcasterId}&moderator_id={Moderator}&user_id={userId}",
            null).ConfigureAwait(false);

        return result ?? $"{login} unbanned";
    }

    /// <summary>Removes one message. Twitch has no way to remove it for one viewer only.</summary>
    public async Task<string> DeleteMessageAsync(string messageId)
    {
        if (!Ready) return $"moderating twitch needs {Missing}";

        var result = await SendAsync(
            HttpMethod.Delete,
            $"{Helix}/moderation/chat?broadcaster_id={BroadcasterId}"
            + $"&moderator_id={Moderator}&message_id={Uri.EscapeDataString(messageId)}",
            null).ConfigureAwait(false);

        return result ?? "message deleted";
    }

    /// <summary>
    /// Twitch has no pin. An announcement is the nearest thing it does have -
    /// a highlighted message from the channel - so that is what the pin key
    /// offers instead of doing nothing.
    /// </summary>
    public async Task<string> AnnounceAsync(string message, string colour = "primary")
    {
        if (!Ready) return $"announcing on twitch needs {Missing}";

        var result = await PostAsync(
            $"{Helix}/chat/announcements?broadcaster_id={BroadcasterId}&moderator_id={Moderator}",
            new { message, color = colour }).ConfigureAwait(false);

        return result ?? "announced";
    }

    private string Moderator => ModeratorId.Length > 0 ? ModeratorId : BroadcasterId;

    public static string Spoken(int seconds) =>
        seconds >= 3600 ? $"{seconds / 3600} hours"
        : seconds >= 60 ? $"{seconds / 60} minutes"
        : $"{seconds} seconds";

    // ---- plumbing --------------------------------------------------------

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Task<string?> PostAsync(string url, object payload) =>
        SendAsync(HttpMethod.Post, url, JsonContent.Create(payload));

    /// <summary>Returns null when it worked, or the reason when it did not.</summary>
    private async Task<string?> SendAsync(HttpMethod method, string url, HttpContent? content)
    {
        try
        {
            using var request = Request(method, url);
            request.Content = content;

            using var response = await _http.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            return $"twitch refused: {Reason(body)}";
        }
        catch (Exception exception)
        {
            return $"could not reach twitch: {exception.Message}";
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);

        request.Headers.Authorization = new("Bearer", Token.Replace("oauth:", string.Empty));
        request.Headers.Add("Client-Id", ClientId);

        return request;
    }

    public static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "no reason given";
            }
        }
        catch (Exception)
        {
        }

        return "no reason given";
    }
}
