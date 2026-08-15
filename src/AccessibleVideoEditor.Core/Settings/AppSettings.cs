using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Core.Settings;

/// <summary>
/// Everything that belongs to <b>you and this machine</b> rather than to one
/// project.
///
/// The rule for deciding where something lives: if copying a project to another
/// computer should carry the setting with it, it is a project setting; if it
/// would be wrong or dangerous to carry it, it belongs here. A canvas size
/// travels with the project. Your microphone, your stream key and how much
/// speech you like do not.
///
/// <see cref="Defaults"/> is the exception that proves the rule - it holds the
/// <i>project settings a new project starts from</i>, so "I always work at 30
/// frames per second" is said once rather than on every project.
/// </summary>
public sealed class AppSettings
{
    // ---- who you are -----------------------------------------------------

    /// <summary>
    /// Used to pick your name out of chat. Nothing else reads it, and it is
    /// separate from any account name because people rarely get called by
    /// their login.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    // ---- what a new project starts from ----------------------------------

    public ProjectSettings Defaults { get; set; } = new();

    // ---- how the application behaves -------------------------------------

    public BehaviourSettings Behaviour { get; set; } = new();

    // ---- devices ---------------------------------------------------------

    public DeviceSettings Devices { get; set; } = new();

    // ---- streaming -------------------------------------------------------

    /// <summary>
    /// Destinations and chat channels, <b>without their keys</b>. Keys and
    /// tokens live in <see cref="SecretStore"/>, which is a separate file with
    /// tighter permissions - so this file can be read, copied, or pasted into a
    /// bug report without handing anyone your broadcast.
    /// </summary>
    public StreamSettings Streaming { get; set; } = new();

    // ---- where the tools are ---------------------------------------------

    public ToolPaths Tools { get; set; } = new();

    // ---- loading and saving ----------------------------------------------

    public static string DirectoryPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "video");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static JsonSerializerOptions Json => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Never throws. A settings file that cannot be read is replaced by the
    /// defaults and said out loud, because refusing to start over a stray comma
    /// is a worse outcome than starting fresh.
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public string Save(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));

            return "settings saved";
        }
        catch (Exception exception)
        {
            return $"could not save settings: {exception.Message}";
        }
    }

    /// <summary>
    /// The stream setup as configured, with keys and tokens filled in from the
    /// secret store. This is the only place the two are ever joined.
    /// </summary>
    public StreamSetup BuildStreamSetup(SecretStore secrets)
    {
        var setup = StreamSetup.Empty();

        foreach (var configured in Streaming.Targets)
        {
            var target = StreamTarget.For(configured.Platform);

            target.Name = configured.Name.Length > 0 ? configured.Name : target.Name;
            target.Enabled = configured.Enabled;

            if (configured.Server.Length > 0) target.Server = configured.Server;

            target.Key = secrets.StreamKey(configured.Platform);

            setup.Targets.Add(target);
        }

        return setup;
    }

    /// <summary>
    /// Remembers a destination without ever putting its key in this file. The
    /// key goes to the secret store; only the fact that there is one comes back
    /// here.
    /// </summary>
    public void RememberTarget(StreamTarget target)
    {
        var existing = Streaming.Targets.FirstOrDefault(t => t.Platform == target.Platform);

        if (existing is null)
        {
            existing = new ConfiguredTarget { Platform = target.Platform };
            Streaming.Targets.Add(existing);
        }

        existing.Name = target.Name;
        existing.Server = target.Server;
        existing.Enabled = target.Enabled;
    }
}

public sealed class BehaviourSettings
{
    public Verbosity Verbosity { get; set; } = Verbosity.Terse;

    public bool Earcons { get; set; } = true;

    /// <summary>Follow the playhead as it plays, rather than staying put.</summary>
    public bool FollowPlayback { get; set; } = true;

    /// <summary>Which chat categories interrupt you. See <see cref="ChatCategory"/>.</summary>
    public ChatCategory ChatSpeaking { get; set; } =
        ChatCategory.Mention | ChatCategory.FirstTime | ChatCategory.Question | ChatCategory.Event;

    public bool SpeakEveryChatMessage { get; set; }

    /// <summary>
    /// How many messages inside <see cref="ChatBurstWindow"/> seconds before
    /// chat stops being read and becomes a count. Adjustable because how much
    /// speech is too much is genuinely personal.
    /// </summary>
    public int ChatBurst { get; set; } = 6;

    public double ChatBurstWindow { get; set; } = 4;

    /// <summary>Where renders go when nothing else is said.</summary>
    public string OutputDirectory { get; set; } = string.Empty;
}

public sealed class DeviceSettings
{
    public string? Camera { get; set; }

    public string? Microphone { get; set; }

    public string? MonitorOutput { get; set; }

    /// <summary>
    /// For an interface with more inputs than you use - the Focusrite case.
    /// Remembered per machine because it describes the hardware in front of
    /// you, not the video you are making.
    /// </summary>
    public int? InputChannel { get; set; }
}

public sealed class StreamSettings
{
    public List<ConfiguredTarget> Targets { get; set; } = [];

    /// <summary>The Twitch channel to read, which is usually your own.</summary>
    public string TwitchChannel { get; set; } = string.Empty;

    /// <summary>
    /// The live video being watched on YouTube. It changes for every broadcast,
    /// so it is remembered but expected to be replaced.
    /// </summary>
    public string YouTubeVideoId { get; set; } = string.Empty;

    public string FacebookLiveVideoId { get; set; } = string.Empty;

    /// <summary>Connect the chats that are configured as soon as the view opens.</summary>
    public bool ConnectChatOnOpen { get; set; } = true;

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public double Fps { get; set; } = 30;
}

public sealed class ConfiguredTarget
{
    public StreamPlatform Platform { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Server { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

public sealed class ToolPaths
{
    public string Ffmpeg { get; set; } = "ffmpeg";

    public string Ffprobe { get; set; } = "ffprobe";

    public string Claude { get; set; } = "claude";

    /// <summary>The Whisper virtual environment, which is machine-specific.</summary>
    public string WhisperPython { get; set; } = string.Empty;

    public string CacheDirectory { get; set; } = string.Empty;
}
