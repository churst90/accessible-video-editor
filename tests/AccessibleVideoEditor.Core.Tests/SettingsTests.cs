using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The rule these enforce: if copying a project to another computer should
/// carry a setting with it, it belongs to the project; if it would be wrong or
/// dangerous to carry it, it belongs to the application.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-settings-" + Guid.NewGuid().ToString("n")[..8]);

    private string File(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Settings_survive_a_round_trip()
    {
        var settings = new AppSettings { DisplayName = "Cody" };
        settings.Defaults.Fps = 60;
        settings.Behaviour.ChatBurst = 9;
        settings.Devices.Microphone = "focusrite";
        settings.Streaming.TwitchChannel = "codyhurst";

        settings.Save(File("settings.json"));

        var loaded = AppSettings.Load(File("settings.json"));

        Assert.Equal("Cody", loaded.DisplayName);
        Assert.Equal(60, loaded.Defaults.Fps);
        Assert.Equal(9, loaded.Behaviour.ChatBurst);
        Assert.Equal("focusrite", loaded.Devices.Microphone);
        Assert.Equal("codyhurst", loaded.Streaming.TwitchChannel);
    }

    [Fact]
    public void A_broken_settings_file_gives_defaults_rather_than_refusing_to_start()
    {
        // Refusing to open over a stray comma is a worse outcome than starting
        // fresh, and the application says which happened.
        Directory.CreateDirectory(_directory);
        System.IO.File.WriteAllText(File("settings.json"), "{ this is not json");

        var loaded = AppSettings.Load(File("settings.json"));

        Assert.Equal(string.Empty, loaded.DisplayName);
        Assert.Equal(30, loaded.Defaults.Fps);
    }

    [Fact]
    public void A_missing_settings_file_is_normal()
    {
        Assert.NotNull(AppSettings.Load(File("nothing.json")));
    }

    [Fact]
    public void The_settings_file_never_contains_a_stream_key()
    {
        // Settings can be copied, shared, or pasted into a bug report. A stream
        // key lets anyone broadcast as you, so the two cannot share a file.
        var settings = new AppSettings();
        var secrets = new SecretStore();

        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "live_supersecret_value";

        settings.RememberTarget(twitch);
        secrets.SetStreamKey(StreamPlatform.Twitch, twitch.Key);

        settings.Save(File("settings.json"));

        var written = System.IO.File.ReadAllText(File("settings.json"));

        Assert.DoesNotContain("supersecret", written);
        Assert.Contains("Twitch", written);
    }

    [Fact]
    public void The_setup_is_the_destinations_from_settings_and_the_keys_from_secrets()
    {
        var settings = new AppSettings();
        var secrets = new SecretStore();

        settings.RememberTarget(StreamTarget.For(StreamPlatform.Twitch));
        secrets.SetStreamKey(StreamPlatform.Twitch, "abc");

        var setup = settings.BuildStreamSetup(secrets);

        Assert.Single(setup.Targets);
        Assert.Equal("rtmp://live.twitch.tv/app/abc", setup.Targets[0].Url);
    }

    [Fact]
    public void Remembering_the_same_destination_twice_updates_it_rather_than_duplicating_it()
    {
        var settings = new AppSettings();

        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        settings.RememberTarget(twitch);

        twitch.Enabled = false;
        settings.RememberTarget(twitch);

        Assert.Single(settings.Streaming.Targets);
        Assert.False(settings.Streaming.Targets[0].Enabled);
    }
}

public class SecretStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), "video-secrets-" + Guid.NewGuid().ToString("n")[..8] + ".json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void Secrets_survive_a_round_trip()
    {
        var store = new SecretStore();
        store.SetStreamKey(StreamPlatform.YouTube, "yt-key");
        store.TwitchToken = "tw-token";
        store.Save(_file);

        var loaded = SecretStore.Load(_file);

        Assert.Equal("yt-key", loaded.StreamKey(StreamPlatform.YouTube));
        Assert.Equal("tw-token", loaded.TwitchToken);
    }

    [Fact]
    public void The_secrets_file_is_readable_only_by_its_owner()
    {
        // The settings file does not need this and would lose it the first time
        // something rewrote it, which is half the reason they are separate.
        if (OperatingSystem.IsWindows()) return;

        new SecretStore().Save(_file);

        var mode = File.GetUnixFileMode(_file);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Setting_a_secret_to_nothing_removes_it()
    {
        var store = new SecretStore();
        store.TwitchToken = "something";
        store.TwitchToken = string.Empty;

        Assert.False(store.Has("twitch.token"));
    }

    [Fact]
    public void What_is_saved_can_be_said_without_saying_any_of_it()
    {
        // "Is my Twitch key saved" has to be answerable out loud.
        var store = new SecretStore();
        store.SetStreamKey(StreamPlatform.Twitch, "live_supersecret");
        store.YouTubeApiKey = "AIzaSecret";

        var spoken = store.Describe();

        Assert.DoesNotContain("supersecret", spoken);
        Assert.DoesNotContain("AIzaSecret", spoken);
        Assert.Contains("twitch stream key", spoken);
        Assert.Contains("youtube api key", spoken);
    }

    [Fact]
    public void Nothing_saved_says_so()
    {
        Assert.Equal("nothing saved", new SecretStore().Describe());
    }
}
