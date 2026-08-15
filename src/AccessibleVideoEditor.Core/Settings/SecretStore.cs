using System.Text.Json;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Core.Settings;

/// <summary>
/// Stream keys and access tokens, kept apart from everything else.
///
/// A separate file for three reasons, all of them practical:
///
/// <list type="bullet">
/// <item>It can be given <b>owner-only permissions</b>; the settings file does
/// not need them and would lose them the first time something rewrote it.</item>
/// <item>Settings can be copied, shared or pasted into a bug report without
/// handing over your broadcast. A stream key lets anyone stream as you.</item>
/// <item>Backing up your configuration and backing up your credentials are
/// different decisions, and keeping them in one file forces them to be the
/// same one.</item>
/// </list>
///
/// Nothing in here is ever spoken, written to a status line, or put in a log -
/// only <i>whether</i> a secret is set.
/// </summary>
public sealed class SecretStore
{
    private Dictionary<string, string> _values = [];

    public static string FilePath => Path.Combine(AppSettings.DirectoryPath, "secrets.json");

    public static SecretStore Load(string? path = null)
    {
        var file = path ?? FilePath;
        var store = new SecretStore();

        try
        {
            if (File.Exists(file))
            {
                store._values =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];
            }
        }
        catch (Exception)
        {
            // An unreadable secrets file means no secrets, not a failure to
            // start. Everything that needs one already says so plainly.
        }

        return store;
    }

    public string Save(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(_values, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

            Protect(file);

            return "saved";
        }
        catch (Exception exception)
        {
            return $"could not save: {exception.Message}";
        }
    }

    /// <summary>
    /// Owner read and write only. Best effort - on a platform without Unix
    /// permissions this does nothing, and the file is no worse off than any
    /// other file there.
    /// </summary>
    public static void Protect(string file)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception)
        {
        }
    }

    public string Get(string key) => _values.GetValueOrDefault(key, string.Empty);

    public bool Has(string key) => Get(key).Length > 0;

    public void Set(string key, string value)
    {
        if (value.Length == 0) _values.Remove(key);
        else _values[key] = value;
    }

    public void Clear(string key) => _values.Remove(key);

    /// <summary>The names of the secrets that are set. Names only, never values.</summary>
    public IReadOnlyCollection<string> Names => _values.Keys;

    // ---- the things that are actually stored ------------------------------

    public string StreamKey(StreamPlatform platform) => Get(KeyFor(platform));

    public void SetStreamKey(StreamPlatform platform, string key) => Set(KeyFor(platform), key);

    public static string KeyFor(StreamPlatform platform) => $"stream-key.{platform}";

    /// <summary>A Twitch OAuth token. Without one, chat is read-only.</summary>
    public string TwitchToken
    {
        get => Get("twitch.token");
        set => Set("twitch.token", value);
    }

    /// <summary>
    /// A YouTube Data API key. Enough to <i>read</i> a public live chat, which
    /// is most of what is wanted; posting and moderating need OAuth.
    /// </summary>
    public string YouTubeApiKey
    {
        get => Get("youtube.apiKey");
        set => Set("youtube.apiKey", value);
    }

    public string YouTubeOAuthToken
    {
        get => Get("youtube.oauth");
        set => Set("youtube.oauth", value);
    }

    /// <summary>A Facebook page access token with live video permissions.</summary>
    public string FacebookToken
    {
        get => Get("facebook.token");
        set => Set("facebook.token", value);
    }

    /// <summary>
    /// What is set, said in a way that is safe out loud. Answering "is my
    /// Twitch key saved" has to be possible without saying the key.
    /// </summary>
    public string Describe()
    {
        if (_values.Count == 0) return "nothing saved";

        var names = _values.Keys.OrderBy(k => k).Select(Friendly);

        return $"saved: {string.Join(", ", names)}";
    }

    private static string Friendly(string key) => key switch
    {
        "twitch.token" => "twitch token",
        "youtube.apiKey" => "youtube api key",
        "youtube.oauth" => "youtube sign-in",
        "facebook.token" => "facebook token",
        _ => key.Replace("stream-key.", string.Empty).ToLowerInvariant() + " stream key",
    };
}
