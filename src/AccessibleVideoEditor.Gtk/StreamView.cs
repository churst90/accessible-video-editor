using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Playback;
using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The streamer view: scenes, sources, preview and one chat per platform.
///
/// Four areas, and <c>Ctrl+`</c> goes round them. That is the whole navigation
/// model, and it is one key rather than four because while you are live you are
/// also talking, and anything that needs thinking about is a thing you will get
/// wrong on air.
///
/// Each area is a real list, so within an area it is Up and Down and the screen
/// reader is doing the reading. Nothing here draws its own focus ring or
/// invents its own navigation.
/// </summary>
public sealed class StreamView
{
    private readonly Func<IAnnouncer> _announcer;
    private readonly StreamEncoder _encoder = new();
    private readonly TwitchChatClient _twitch = new();
    private readonly YouTubeChatClient _youtube = new();
    private readonly FacebookChatClient _facebook = new();
    private readonly TwitchModeration _moderation = new();

    private readonly AppSettings _settings;
    private readonly SecretStore _secrets;

    private readonly StreamHealthMonitor _health = new();
    private readonly MusicPlayer _music = new();
    private readonly LevelReader _levels = new();
    private readonly LevelMonitor _meter = new();
    private bool _monitoring;
    private double _meterSeconds;
    private double _liveSeconds;

    public Playlist Playlist { get; } = new();

    private Gtk_.ListBox _scenes = null!;
    private Gtk_.ListBox _sources = null!;
    private Gtk_.ListBox _chat = null!;
    private Gtk_.Label _preview = null!;
    private Gtk_.Label _status = null!;
    private Gtk_.Entry _reply = null!;
    private Gtk_.Stack _chatStack = null!;

    private readonly List<SceneId> _sceneRows = [];
    private readonly List<SourceRefId> _sourceRows = [];

    private StreamAreaRef _area;

    public StreamSetup Setup { get; private set; } = StreamSetup.Empty();

    public ChatStore Chat { get; } = new();

    public bool IsLive => Setup.IsLive;

    public StreamView(Func<IAnnouncer> announcer, AppSettings settings, SecretStore secrets)
    {
        _announcer = announcer;
        _settings = settings;
        _secrets = secrets;

        // Destinations and their keys are joined here and nowhere else: the
        // settings file knows there is a Twitch target, the secret store knows
        // its key, and only this line ever holds both.
        Setup = settings.BuildStreamSetup(secrets);

        Chat.MyName = settings.DisplayName;
        Chat.Speaking = settings.Behaviour.ChatSpeaking;
        Chat.SpeakEverything = settings.Behaviour.SpeakEveryChatMessage;

        _moderation.Token = secrets.TwitchToken;
        _moderation.ClientId = secrets.Get("twitch.clientId");

        _youtube.ApiKey = secrets.YouTubeApiKey;
        _youtube.OAuthToken = secrets.YouTubeOAuthToken;
        _facebook.Token = secrets.FacebookToken;

        foreach (var client in new IChatClient[] { _twitch, _youtube, _facebook })
        {
            client.Received += message => OnUiThread(() => Receive(message));
        }

        _twitch.Status += text => OnUiThread(() => Say(text, urgent: true));
        _youtube.Status += text => OnUiThread(() => Say(text, urgent: true));
        _facebook.Status += text => OnUiThread(() => Say(text, urgent: true));

        // The encoder already knows whether frames are dropping. Nobody was
        // listening, and a graph would be no use here anyway.
        _encoder.Progress += stats => OnUiThread(() => OnHealth(stats));
        _encoder.Trouble += text => OnUiThread(() =>
        {
            Earcon(AccessibleVideoEditor.Speech.Earcon.Refused);
            Say(text, urgent: true);
        });
    }

    private static void OnUiThread(System.Action action) =>
        GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
        {
            action();
            return false;
        });

    // ---- widgets ---------------------------------------------------------

    public Gtk_.Widget Build()
    {
        _scenes = List();
        _sources = List();
        _chat = List();

        _preview = Gtk_.Label.New(string.Empty);
        _preview.Xalign = 0;
        _preview.Yalign = 0;
        _preview.Wrap = true;
        _preview.MarginTop = 10;
        _preview.MarginBottom = 10;
        _preview.MarginStart = 12;
        _preview.MarginEnd = 12;
        _preview.Focusable = true;

        // Focusable so Ctrl+` can land on it and it reads out. A pane you can
        // cycle to but not focus is a pane the key appears to skip.
        _preview.SetName("preview");

        _status = Gtk_.Label.New(string.Empty);
        _status.Xalign = 0;
        _status.Wrap = true;
        _status.AddCssClass("readout");

        _reply = Gtk_.Entry.New();
        _reply.PlaceholderText = "Reply, then Enter";
        _reply.OnActivate += (_, _) => SendReply();

        _chatStack = Gtk_.Stack.New();
        _chatStack.Vexpand = true;
        _chatStack.AddNamed(Scrolled(_chat), "chat");

        var left = Column("Scenes", _scenes, "Sources", _sources);
        var right = Gtk_.Box.New(Gtk_.Orientation.Vertical, 6);

        right.Append(Heading("Preview"));
        right.Append(Scrolled(_preview));
        right.Append(Heading("Chat"));
        right.Append(_chatStack);
        right.Append(_reply);
        right.Hexpand = true;

        var split = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 10);
        left.SetSizeRequest(340, -1);
        split.Append(left);
        split.Append(right);

        var root = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        root.Append(_status);
        root.Append(split);

        _area = Areas()[0];

        Refresh();

        return root;
    }

    private static Gtk_.ListBox List()
    {
        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;
        list.Vexpand = true;

        return list;
    }

    private static Gtk_.Widget Scrolled(Gtk_.Widget child)
    {
        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(child);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");

        return scroller;
    }

    private static Gtk_.Label Heading(string text)
    {
        var label = Gtk_.Label.New(text.ToUpperInvariant());
        label.Xalign = 0;
        label.AddCssClass("pane-heading");

        return label;
    }

    private static Gtk_.Box Column(string firstName, Gtk_.Widget first, string secondName, Gtk_.Widget second)
    {
        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 6);

        box.Append(Heading(firstName));
        box.Append(Scrolled(first));
        box.Append(Heading(secondName));
        box.Append(Scrolled(second));

        return box;
    }

    private static Gtk_.ListBoxRow Row(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        label.MarginTop = 7;
        label.MarginBottom = 7;
        label.MarginStart = 10;
        label.MarginEnd = 10;
        label.Wrap = true;

        var row = Gtk_.ListBoxRow.New();
        row.SetChild(label);

        return row;
    }

    // ---- areas -----------------------------------------------------------

    private IReadOnlyList<StreamAreaRef> Areas() => StreamAreas.For(Setup, Chat);

    /// <summary>
    /// <c>Ctrl+`</c>. Announced by name and with what is in it, because the
    /// name alone does not tell you whether anything is there.
    /// </summary>
    public void CycleArea(bool forward)
    {
        var areas = Areas();
        _area = StreamAreas.Cycle(areas, _area, forward);

        FocusArea();

        Say($"{_area.Name}. {AreaSummary()}");
    }

    private void FocusArea()
    {
        switch (_area.Kind)
        {
            case StreamArea.Scenes: _scenes.GrabFocus(); break;
            case StreamArea.Sources: _sources.GrabFocus(); break;
            case StreamArea.Preview: _preview.GrabFocus(); break;
            case StreamArea.Chat: _chat.GrabFocus(); break;
        }
    }

    private string AreaSummary() => _area.Kind switch
    {
        StreamArea.Scenes => Setup.Scenes.Count == 0
            ? "no scenes yet; press N to make one"
            : $"{Setup.Scenes.Count} scenes, {Setup.Live?.Name ?? "none"} selected",

        StreamArea.Sources => Setup.Live is { } scene
            ? scene.Sources.Count == 0
                ? $"{scene.Name} is empty; press A to add a source"
                : $"{scene.Sources.Count} in {scene.Name}"
            : "no scene selected",

        StreamArea.Preview => PreviewText(),

        StreamArea.Chat => _area.Platform is { } platform
            ? Chat.Channel(platform).Describe()
            : "nothing connected; press C to connect a Twitch channel",

        _ => string.Empty,
    };

    public string CurrentAreaName => _area.Name;

    // ---- what is on screen -----------------------------------------------

    public void Refresh()
    {
        RefreshScenes();
        RefreshSources();

        _preview.SetText(PreviewText());
        _status.SetText(StatusText());
    }

    private void RefreshScenes()
    {
        Clear(_scenes);
        _sceneRows.Clear();

        if (Setup.Scenes.Count == 0)
        {
            _scenes.Append(Row("no scenes yet. N makes one, or Shift+N for a starter setup"));
            return;
        }

        foreach (var scene in Setup.Scenes)
        {
            var number = Setup.NumberOf(scene.Id);
            var live = Setup.LiveScene == scene.Id
                ? Setup.IsLive ? ", on air" : ", selected"
                : string.Empty;

            _scenes.Append(Row($"{number}. {scene.Describe(Setup)}{live}"));
            _sceneRows.Add(scene.Id);
        }
    }

    private void RefreshSources()
    {
        Clear(_sources);
        _sourceRows.Clear();

        if (Setup.Live is not { } scene)
        {
            _sources.Append(Row("no scene selected"));
            return;
        }

        if (scene.Sources.Count == 0)
        {
            _sources.Append(Row($"{scene.Name} is empty. A adds a source"));
            return;
        }

        // Front first, because "what is on top" is the question you are
        // actually asking when you look at a scene.
        foreach (var reference in Enumerable.Reverse(scene.Sources))
        {
            _sources.Append(Row(reference.Describe(Setup)));
            _sourceRows.Add(reference.Id);
        }
    }

    private string PreviewText()
    {
        if (Setup.Live is not { } scene) return "No scene selected.";

        var lines = new List<string>
        {
            Setup.IsLive ? $"ON AIR — {scene.Name}" : $"Off air — {scene.Name} selected",
            string.Empty,
            scene.Describe(Setup),
        };

        var problems = SceneOperations.PreflightWarnings(Setup);

        if (problems.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Before going live:");
            lines.AddRange(problems.Select(p => $"  · {p}"));
        }

        var settings = EncoderSettings.ForTargets(Setup.Targets);
        lines.Add(string.Empty);
        lines.Add(settings.Describe());

        foreach (var target in Setup.Targets)
        {
            lines.Add($"  · {target.Describe()}");
        }

        return string.Join('\n', lines);
    }

    private string StatusText() =>
        (Setup.IsLive ? "ON AIR" : "off air")
        + $"   scene: {Setup.Live?.Name ?? "none"}"
        + $"   area: {_area.Name}"
        + $"   chat: {Chat.Summarise()}";

    private static void Clear(Gtk_.ListBox list)
    {
        while (list.GetFirstChild() is { } child) list.Remove(child);
    }

    // ---- scenes ----------------------------------------------------------

    private SceneId? SelectedScene()
    {
        var index = _scenes.GetSelectedRow()?.GetIndex() ?? -1;

        return index >= 0 && index < _sceneRows.Count ? _sceneRows[index] : Setup.LiveScene;
    }

    private SourceRefId? SelectedSource()
    {
        var index = _sources.GetSelectedRow()?.GetIndex() ?? -1;

        return index >= 0 && index < _sourceRows.Count ? _sourceRows[index] : null;
    }

    /// <summary>
    /// Cut to a scene by its number. One key, no confirmation - that is the
    /// point of a scene - but always announced with what is now live.
    /// </summary>
    public void SwitchToNumber(int number)
    {
        if (Setup.ByNumber(number) is not { } scene)
        {
            Say($"there is no scene {number}", urgent: true);
            return;
        }

        var result = SceneOperations.Switch(Setup, scene.Id);

        Earcon(AccessibleVideoEditor.Speech.Earcon.SceneSwitch);
        Refresh();
        Say(result.Announce(), urgent: true);
    }

    public void AddScene(string name)
    {
        Announce(SceneOperations.AddScene(Setup, name));
    }

    public void UseStarterSetup()
    {
        if (Setup.Scenes.Count > 0)
        {
            Say("there are already scenes here; the starter setup only builds into an empty one", urgent: true);
            return;
        }

        var targets = Setup.Targets;
        Setup = StreamSetup.Starter();
        Setup.Targets.AddRange(targets);

        Refresh();
        Say($"starter setup: {string.Join(", ", Setup.Scenes.Select(s => s.Name))}. "
            + "Face cam is selected, nothing is on air", urgent: true);
    }

    public void RemoveScene()
    {
        if (SelectedScene() is not { } id) return;

        Announce(SceneOperations.RemoveScene(Setup, id));
    }

    public void RenameScene(string name)
    {
        if (SelectedScene() is not { } id) return;

        Announce(SceneOperations.RenameScene(Setup, id, name));
    }

    // ---- sources ---------------------------------------------------------

    public void AddSource(StreamSource source, double scale = 1.0, Placement? placement = null)
    {
        SceneOperations.AddSource(Setup, source);

        if (Setup.LiveScene is { } scene)
        {
            Announce(SceneOperations.AddToScene(Setup, scene, source.Id, scale, placement));
            return;
        }

        Refresh();
        Say($"{source.Describe()} added, but there is no scene to put it in", urgent: true);
    }

    public void ToggleSourceVisible()
    {
        if (Setup.LiveScene is not { } scene || SelectedSource() is not { } reference) return;

        Announce(SceneOperations.ToggleVisible(Setup, scene, reference));
    }

    public void ToggleSourceMuted()
    {
        if (Setup.LiveScene is not { } scene || SelectedSource() is not { } reference) return;

        Announce(SceneOperations.ToggleMuted(Setup, scene, reference));
    }

    public void ReorderSource(bool forward)
    {
        if (Setup.LiveScene is not { } scene || SelectedSource() is not { } reference) return;

        Announce(SceneOperations.Reorder(Setup, scene, reference, forward));
    }

    public void RemoveSource()
    {
        if (Setup.LiveScene is not { } scene || SelectedSource() is not { } reference) return;

        Announce(SceneOperations.RemoveFromScene(Setup, scene, reference));
    }

    public void PlaceSource(Placement placement, double? scale)
    {
        if (Setup.LiveScene is not { } scene || SelectedSource() is not { } reference) return;

        Announce(SceneOperations.Place(Setup, scene, reference, placement, scale));
    }

    // ---- chat ------------------------------------------------------------

    private void Receive(ChatMessage message)
    {
        var announcement = Chat.Receive(message);

        _chat.Append(Row($"{message.Author}: {message.Text}"));

        if (announcement.Earcon is { } earcon) Earcon(EarconFor(earcon));
        if (announcement.Speak is { } text) Say(text, urgent: false);

        _status.SetText(StatusText());
    }

    private static Earcon EarconFor(string name) => name switch
    {
        "chat-mention" => AccessibleVideoEditor.Speech.Earcon.ChatMention,
        "chat-first-time" => AccessibleVideoEditor.Speech.Earcon.ChatFirstTime,
        "chat-question" => AccessibleVideoEditor.Speech.Earcon.ChatQuestion,
        "chat-moderator" => AccessibleVideoEditor.Speech.Earcon.ChatModerator,
        _ => AccessibleVideoEditor.Speech.Earcon.ChatEvent,
    };

    public async void ConnectTwitch(string channel, string? token = null)
    {
        Say($"connecting to {channel} on twitch");

        var result = await _twitch.ConnectAsync(channel, token ?? _secrets.TwitchToken);

        Chat.Channel(StreamPlatform.Twitch).Connected = _twitch.Connected;

        if (_twitch.Connected)
        {
            _settings.Streaming.TwitchChannel = channel;
            _settings.Save();

            // Helix works in numeric ids, so the channel name is resolved once
            // here rather than on every moderation key.
            _moderation.BroadcasterId = await _moderation.UserIdAsync(channel) ?? string.Empty;
        }

        Refresh();
        Say(result, urgent: true);
    }

    public async void ConnectYouTube(string videoId)
    {
        Say("connecting to youtube chat");

        _youtube.ApiKey = _secrets.YouTubeApiKey;
        _youtube.OAuthToken = _secrets.YouTubeOAuthToken;

        var result = await _youtube.ConnectAsync(videoId);

        Chat.Channel(StreamPlatform.YouTube).Connected = _youtube.Connected;

        if (_youtube.Connected)
        {
            _settings.Streaming.YouTubeVideoId = videoId;
            _settings.Save();
        }

        Refresh();
        Say(result, urgent: true);
    }

    public async void ConnectFacebook(string liveVideoId)
    {
        Say("connecting to facebook comments");

        _facebook.Token = _secrets.FacebookToken;

        var result = await _facebook.ConnectAsync(liveVideoId);

        Chat.Channel(StreamPlatform.Facebook).Connected = _facebook.Connected;

        if (_facebook.Connected)
        {
            _settings.Streaming.FacebookLiveVideoId = liveVideoId;
            _settings.Save();
        }

        Refresh();
        Say(result, urgent: true);
    }

    /// <summary>Connects everything that is configured, when the view opens.</summary>
    public void ConnectConfiguredChats()
    {
        var streaming = _settings.Streaming;

        if (!streaming.ConnectChatOnOpen) return;

        if (streaming.TwitchChannel.Length > 0) ConnectTwitch(streaming.TwitchChannel);
        if (streaming.YouTubeVideoId.Length > 0 && _secrets.YouTubeApiKey.Length > 0)
        {
            ConnectYouTube(streaming.YouTubeVideoId);
        }

        if (streaming.FacebookLiveVideoId.Length > 0 && _secrets.FacebookToken.Length > 0)
        {
            ConnectFacebook(streaming.FacebookLiveVideoId);
        }
    }

    public void ReadBack(bool older)
    {
        if (_area.Platform is not { } platform)
        {
            Say(Chat.Summarise(), urgent: true);
            return;
        }

        var channel = Chat.Channel(platform);
        var message = older ? channel.Older() : channel.Newer();

        _status.SetText(StatusText());

        Say(message is null
            ? $"back to live, {channel.Messages.Count} messages"
            : message.Speak(false, true, false), urgent: true);
    }

    public void ReturnToLive()
    {
        if (_area.Platform is not { } platform) return;

        Chat.Channel(platform).ReturnToLive();

        _status.SetText(StatusText());
        Say("following chat live", urgent: true);
    }

    private async void SendReply()
    {
        var text = _reply.GetText();
        if (text.Length == 0) return;

        // Sent to the platform whose pane you are in, never to "all chats".
        // A reply that lands on the wrong service cannot be taken back.
        if (_area.Platform is not StreamPlatform.Twitch)
        {
            Say("this chat cannot be replied to yet; move to the twitch pane with Control backtick", urgent: true);
            return;
        }

        _reply.SetText(string.Empty);

        Say(await _twitch.SendAsync(text), urgent: true);
    }

    // ---- going live ------------------------------------------------------

    /// <summary>
    /// Reads the preflight list rather than starting, so the answer to "am I
    /// ready" costs one key and no risk.
    /// </summary>
    public void Preflight()
    {
        var problems = SceneOperations.PreflightWarnings(Setup);

        Say(problems.Count == 0
            ? $"ready. {StreamEncoder.Describe(Setup, EncoderSettings.ForTargets(Setup.Targets))}"
            : $"{problems.Count} to fix: {string.Join(". ", problems)}", urgent: true);
    }

    public async void ToggleLive()
    {
        if (Setup.IsLive)
        {
            var stopped = _encoder.Stop(Setup);

            Earcon(AccessibleVideoEditor.Speech.Earcon.OffAir);
            Refresh();
            Say(stopped, urgent: true);

            return;
        }

        var problems = SceneOperations.PreflightWarnings(Setup);

        if (problems.Count > 0)
        {
            Earcon(AccessibleVideoEditor.Speech.Earcon.Refused);
            Say($"not going live: {string.Join(". ", problems)}", urgent: true);

            return;
        }

        var settings = EncoderSettings.ForTargets(Setup.Targets);

        Say(StreamEncoder.Describe(Setup, settings));

        var result = await _encoder.StartAsync(Setup, Setup.Live!, settings);

        Earcon(Setup.IsLive ? AccessibleVideoEditor.Speech.Earcon.OnAir : AccessibleVideoEditor.Speech.Earcon.Refused);
        Refresh();
        Say(result, urgent: true);
    }

    /// <summary>
    /// The key goes to the secret store and the destination goes to the
    /// settings, and the two files are separate on purpose - settings can be
    /// copied or pasted into a bug report; a stream key cannot.
    /// </summary>
    public void AddTarget(StreamPlatform platform, string key)
    {
        var target = Setup.Targets.FirstOrDefault(t => t.Platform == platform)
                     ?? StreamTarget.For(platform);

        target.Key = key;

        if (!Setup.Targets.Contains(target)) Setup.Targets.Add(target);

        _secrets.SetStreamKey(platform, key);
        _secrets.Save();

        _settings.RememberTarget(target);
        _settings.Save();

        Refresh();

        // Saved, and never read back.
        Say($"{target.Name} saved to your settings", urgent: true);
    }

    public void SetSecret(string name, string value)
    {
        _secrets.Set(name, value);
        _secrets.Save();

        _moderation.Token = _secrets.TwitchToken;
        _moderation.ClientId = _secrets.Get("twitch.clientId");
        _youtube.ApiKey = _secrets.YouTubeApiKey;
        _youtube.OAuthToken = _secrets.YouTubeOAuthToken;
        _facebook.Token = _secrets.FacebookToken;

        Say("saved", urgent: true);
    }

    /// <summary>Says what is saved without ever saying any of it.</summary>
    public void DescribeSecrets() => Say(_secrets.Describe(), urgent: true);

    /// <summary>R from anywhere in the view. Typing is then typing, not commands.</summary>
    public void FocusReply()
    {
        _reply.GrabFocus();
        Say("reply. Enter sends, Tab leaves it");
    }

    public void Shutdown()
    {
        _twitch.Disconnect();
        _youtube.Disconnect();
        _facebook.Disconnect();

        _levels.Stop();
        _music.Dispose();

        if (Setup.IsLive) _encoder.Stop(Setup);
    }

    // ---- how the stream is doing -----------------------------------------

    private void OnHealth(StreamStats stats)
    {
        _liveSeconds += 0.5;

        var alert = _health.Update(stats, _liveSeconds);
        if (!alert.IsSomething) return;

        Earcon(alert.Kind switch
        {
            StreamAlertKind.Dropping or StreamAlertKind.Behind => AccessibleVideoEditor.Speech.Earcon.Refused,
            StreamAlertKind.Recovered => AccessibleVideoEditor.Speech.Earcon.Confirmed,
            _ => AccessibleVideoEditor.Speech.Earcon.Boundary,
        });

        Say(alert.Speak!, urgent: alert.Kind is StreamAlertKind.Dropping or StreamAlertKind.Behind);
        _status.SetText(StatusText());
    }

    /// <summary>Asked for by a key rather than waiting for the next report.</summary>
    public void ReportHealth() => Say(_health.Describe(Setup.IsLive), urgent: true);

    /// <summary>
    /// The audible VU meter, watching what is going out. The same meter the
    /// track editor uses - one thing to learn, and the zones mean the same in
    /// both places.
    /// </summary>
    public void ToggleMonitoring()
    {
        if (_monitoring)
        {
            _levels.Stop();
            _monitoring = false;
            Say("meter off", urgent: true);

            return;
        }

        var microphone = Setup.Live?.Sources
            .Where(r => r.Visible && !r.Muted)
            .Select(r => Setup.SourceOf(r.Source))
            .FirstOrDefault(source => source?.Kind == StreamSourceKind.Microphone);

        if (microphone is null)
        {
            Say("there is no live microphone in this scene to meter", urgent: true);
            return;
        }

        _meter.Reset();
        _meterSeconds = 0;
        _monitoring = true;

        _levels.Start(
            microphone.Path.Length > 0 ? microphone.Path : "@DEFAULT_SOURCE@",
            level => OnUiThread(() => OnLevel(level)),
            error => OnUiThread(() =>
            {
                _monitoring = false;
                Say(error, urgent: true);
            }));

        Say($"metering {microphone.Name}", urgent: true);
    }

    private void OnLevel(double db)
    {
        _meterSeconds += 0.05;

        if (_meter.Observe(db, _meterSeconds) is { } spoken) Say(spoken, urgent: true);

        _announcer().Earcon(AccessibleVideoEditor.Speech.Earcon.Boundary);
    }

    // ---- music -----------------------------------------------------------

    /// <summary>
    /// Starting the music checks something a sighted streamer would find out
    /// the hard way: whether anything in the scene is actually capturing
    /// desktop audio. Music you can hear and your viewers cannot is a mistake
    /// that survives a whole stream.
    /// </summary>
    public void PlayMusic()
    {
        if (Playlist.Tracks.Count == 0)
        {
            Say("the playlist is empty; add music with Shift+A", urgent: true);
            return;
        }

        Say(Playlist.Playing ? Playlist.Announce() : Playlist.Start(), urgent: true);

        StartCurrentTrack();
        WarnIfViewersCannotHearIt();
    }

    public void NextTrack()
    {
        Say(Playlist.Next(), urgent: true);
        StartCurrentTrack();
    }

    public void PreviousTrack()
    {
        Say(Playlist.Previous(), urgent: true);
        StartCurrentTrack();
    }

    public void StopMusic()
    {
        Playlist.Stop();
        Say(_music.Stop(), urgent: true);
    }

    public void ShuffleMusic() => Say(Playlist.ToggleShuffle(), urgent: true);

    public void AnnounceMusic() => Say(Playlist.Announce(), urgent: true);

    public void AddMusic(string path)
    {
        Playlist.Add(new PlaylistTrack(path, System.IO.Path.GetFileNameWithoutExtension(path)));

        Refresh();
        Say(Playlist.Describe(), urgent: true);
    }

    private void StartCurrentTrack()
    {
        if (!Playlist.Playing || Playlist.Current is not { } track) return;

        var result = _music.Play(track.Path);

        if (result != "playing") Say(result, urgent: true);
    }

    private void WarnIfViewersCannotHearIt()
    {
        var capturing = Setup.Live?.Sources.Any(r =>
            r.Visible && !r.Muted && Setup.SourceOf(r.Source)?.Kind == StreamSourceKind.Microphone) ?? false;

        if (!capturing)
        {
            Say("nothing in this scene is capturing desktop audio, so your viewers will not hear the music");
        }
    }

    /// <summary>Called by the window's existing tick; advances the playlist at the end of a track.</summary>
    public void Tick()
    {
        if (!Playlist.Playing || !_music.HasFinished()) return;

        Say(Playlist.Next(userAsked: false));
        StartCurrentTrack();
    }

    // ---- moderation ------------------------------------------------------

    private ChatMessage? SelectedMessage()
    {
        var index = _chat.GetSelectedRow()?.GetIndex() ?? -1;
        var platform = _area.Platform;

        if (platform is null) return null;

        var messages = Chat.Channel(platform.Value).Messages;

        return index >= 0 && index < messages.Count ? messages[index] : messages.LastOrDefault();
    }

    /// <summary>
    /// Every moderation key comes through here, so the same three checks happen
    /// every time: is there a message, does this platform support it, and are
    /// the credentials there. A key that appears to work and does nothing is
    /// the one failure this cannot have.
    /// </summary>
    public async void Moderate(ChatAction action, int? seconds = null)
    {
        if (_area.Platform is not { } platform)
        {
            Say("move to a chat pane first with Control backtick", urgent: true);
            return;
        }

        var capabilities = ChatCapabilities.For(platform);

        if (!capabilities.Can(action))
        {
            Earcon(AccessibleVideoEditor.Speech.Earcon.Refused);
            Say(capabilities.Explain(action, platform), urgent: true);

            return;
        }

        if (SelectedMessage() is not { } message)
        {
            Say("no message selected", urgent: true);
            return;
        }

        Say(await Perform(action, platform, message, seconds), urgent: true);
    }

    private async Task<string> Perform(
        ChatAction action,
        StreamPlatform platform,
        ChatMessage message,
        int? seconds) => platform switch
    {
        StreamPlatform.Twitch => action switch
        {
            ChatAction.Delete => await _moderation.DeleteMessageAsync(message.Id),
            ChatAction.Timeout => await _moderation.BanAsync(message.Author, seconds ?? 600),
            ChatAction.Ban => await _moderation.BanAsync(message.Author, null),
            ChatAction.Announce => await _moderation.AnnounceAsync(message.Text),
            _ => "nothing to do",
        },

        StreamPlatform.YouTube => action switch
        {
            ChatAction.Delete => await _youtube.DeleteAsync(message.Id),
            ChatAction.Timeout => await _youtube.BanAsync(message.AuthorId, seconds ?? 600),
            ChatAction.Ban => await _youtube.BanAsync(message.AuthorId, null),
            _ => "nothing to do",
        },

        StreamPlatform.Facebook => action switch
        {
            // Facebook hides rather than deletes, and the difference is worth
            // saying: a hidden comment is still there for its author.
            ChatAction.Delete => await _facebook.HideAsync(message.Id),
            ChatAction.Ban => await _facebook.BlockAsync(
                _settings.Streaming.FacebookLiveVideoId, message.AuthorId),
            _ => "nothing to do",
        },

        _ => "that platform has no chat",
    };

    /// <summary>What can be done here, given what is configured. Answers "why did that not work".</summary>
    public void DescribeCapabilities()
    {
        if (_area.Platform is not { } platform)
        {
            Say(string.Join(". ", Chat.Channels.Select(c =>
                ChatCapabilities.For(c.Platform).Describe(
                    c.Platform, c.Connected, HasModeratorCredentials(c.Platform)))), urgent: true);

            return;
        }

        Say(ChatCapabilities.For(platform).Describe(
            platform,
            Chat.Channel(platform).Connected,
            HasModeratorCredentials(platform)), urgent: true);
    }

    private bool HasModeratorCredentials(StreamPlatform platform) => platform switch
    {
        StreamPlatform.Twitch => _moderation.Ready,
        StreamPlatform.YouTube => _secrets.YouTubeOAuthToken.Length > 0,
        StreamPlatform.Facebook => _secrets.FacebookToken.Length > 0,
        _ => false,
    };

    // ---- speech ----------------------------------------------------------

    private void Announce(EditResult result)
    {
        Refresh();
        Say(result.Announce(), urgent: true);
    }

    private void Say(string text, bool urgent = false) =>
        _announcer().Say(text, urgent ? AnnouncePriority.Urgent : AnnouncePriority.Normal);

    private void Earcon(Earcon earcon) => _announcer().Earcon(earcon);
}
