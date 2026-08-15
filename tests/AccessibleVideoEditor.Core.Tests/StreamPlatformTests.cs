using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The platforms genuinely differ, and the difference has to be stated rather
/// than smoothed over: a moderation key that appears to work and does nothing
/// is how somebody stays in a chat you believe you removed them from.
/// </summary>
public class ChatCapabilityTests
{
    [Fact]
    public void No_platform_offers_pinning_and_each_says_what_it_has_instead()
    {
        foreach (var platform in new[] { StreamPlatform.Twitch, StreamPlatform.YouTube, StreamPlatform.Facebook })
        {
            var capabilities = ChatCapabilities.For(platform);

            Assert.False(capabilities.Pin);
            Assert.NotEmpty(capabilities.Explain(ChatAction.Pin, platform));
        }

        Assert.Contains("announcement", ChatCapabilities.For(StreamPlatform.Twitch)
            .Explain(ChatAction.Pin, StreamPlatform.Twitch));
    }

    [Fact]
    public void Facebook_has_no_timeout_and_names_blocking_as_the_nearest_thing()
    {
        var facebook = ChatCapabilities.For(StreamPlatform.Facebook);

        Assert.False(facebook.Timeout);
        Assert.Contains("blocking", facebook.Explain(ChatAction.Timeout, StreamPlatform.Facebook));
    }

    [Fact]
    public void Twitch_reads_without_credentials_and_the_others_do_not()
    {
        Assert.Contains("anonymously", ChatCapabilities.For(StreamPlatform.Twitch).ReadRequires);
        Assert.Contains("API key", ChatCapabilities.For(StreamPlatform.YouTube).ReadRequires);
        Assert.Contains("page access token", ChatCapabilities.For(StreamPlatform.Facebook).ReadRequires);
    }

    [Fact]
    public void Read_only_says_so_and_says_what_would_change_it()
    {
        var spoken = ChatCapabilities.For(StreamPlatform.Twitch)
            .Describe(StreamPlatform.Twitch, hasReadCredentials: true, hasModeratorCredentials: false);

        Assert.Contains("read only", spoken);
        Assert.Contains("moderator scopes", spoken);
    }

    [Fact]
    public void With_credentials_it_lists_what_is_actually_possible()
    {
        var spoken = ChatCapabilities.For(StreamPlatform.Twitch)
            .Describe(StreamPlatform.Twitch, true, true);

        Assert.Contains("time out", spoken);
        Assert.Contains("ban", spoken);
        Assert.Contains("announce", spoken);
    }
}

public class YouTubeChatTests
{
    [Fact]
    public void The_live_chat_id_is_found_in_a_videos_response()
    {
        const string json = """
            {"items":[{"id":"abc","liveStreamingDetails":{"activeLiveChatId":"chat-123"}}]}
            """;

        Assert.Equal("chat-123", YouTubeChatClient.LiveChatIdFrom(json));
    }

    [Fact]
    public void A_video_that_is_not_live_yields_nothing_rather_than_throwing()
    {
        Assert.Equal(string.Empty, YouTubeChatClient.LiveChatIdFrom("""{"items":[{"id":"abc"}]}"""));
        Assert.Equal(string.Empty, YouTubeChatClient.LiveChatIdFrom("not json"));
    }

    [Fact]
    public void Messages_come_back_with_their_badges_and_ids()
    {
        const string json = """
            {
              "nextPageToken": "page2",
              "pollingIntervalMillis": 4000,
              "items": [{
                "id": "msg-1",
                "snippet": { "type": "textMessageEvent", "displayMessage": "hello there" },
                "authorDetails": {
                  "displayName": "Ann", "channelId": "UC123",
                  "isChatModerator": true, "isChatSponsor": true
                }
              }]
            }
            """;

        var page = YouTubeChatClient.ParsePage(json);

        Assert.Equal("page2", page.NextPageToken);
        Assert.Equal(4000, page.PollingIntervalMs);

        var message = Assert.Single(page.Messages);

        Assert.Equal("Ann", message.Author);
        Assert.Equal("hello there", message.Text);
        Assert.Equal("msg-1", message.Id);
        Assert.Equal("UC123", message.AuthorId);
        Assert.True(message.Badges.HasFlag(ChatBadge.Moderator));
        Assert.True(message.Badges.HasFlag(ChatBadge.Subscriber));
        Assert.True(message.CanModerate);
    }

    [Fact]
    public void A_super_chat_is_a_donation_and_a_new_member_is_a_subscription()
    {
        const string json = """
            {"items":[
              {"id":"a","snippet":{"type":"superChatEvent","displayMessage":"take my money"},
               "authorDetails":{"displayName":"Ann"}},
              {"id":"b","snippet":{"type":"newSponsorEvent","displayMessage":""},
               "authorDetails":{"displayName":"Bob"}}
            ]}
            """;

        var messages = YouTubeChatClient.ParsePage(json).Messages;

        Assert.Equal(ChatKind.Donation, messages[0].Kind);
        Assert.Equal(ChatKind.Subscribe, messages[1].Kind);
    }

    [Fact]
    public void A_first_timer_is_whoever_has_not_been_heard_from_yet_this_session()
    {
        // YouTube has no first-message flag, so it means first time today -
        // which is the more useful meaning while you are live anyway.
        const string json = """
            {"items":[
              {"id":"a","snippet":{"type":"textMessageEvent","displayMessage":"hi"},
               "authorDetails":{"displayName":"Ann"}},
              {"id":"b","snippet":{"type":"textMessageEvent","displayMessage":"again"},
               "authorDetails":{"displayName":"Ann"}}
            ]}
            """;

        var seen = new HashSet<string>();
        var messages = YouTubeChatClient.ParsePage(json, seen).Messages;

        Assert.True(messages[0].FirstTime);
        Assert.False(messages[1].FirstTime);
    }

    [Fact]
    public void Googles_reason_is_dug_out_rather_than_reported_as_refused()
    {
        var reason = YouTubeChatClient.Reason("""{"error":{"message":"API key not valid"}}""");

        Assert.Equal("API key not valid", reason);
    }

    [Fact]
    public void A_malformed_page_is_skipped_rather_than_ending_the_chat()
    {
        var page = YouTubeChatClient.ParsePage("{ broken");

        Assert.Empty(page.Messages);
        Assert.Equal(5000, page.PollingIntervalMs);
    }
}

public class FacebookChatTests
{
    [Fact]
    public void Comments_carry_the_ids_that_moderation_needs()
    {
        const string json = """
            {"data":[{"id":"c-1","from":{"name":"Ann","id":"u-1"},"message":"hello"}]}
            """;

        var (id, message) = Assert.Single(FacebookChatClient.Parse(json));

        Assert.Equal("c-1", id);
        Assert.Equal("Ann", message.Author);
        Assert.Equal("u-1", message.AuthorId);
        Assert.True(message.CanModerate);
    }

    [Fact]
    public void A_comment_with_no_author_still_reads()
    {
        var (_, message) = Assert.Single(FacebookChatClient.Parse("""{"data":[{"id":"c","message":"hi"}]}"""));

        Assert.Equal("someone", message.Author);
    }

    [Fact]
    public void Facebooks_reason_is_reported()
    {
        Assert.Contains(
            "expired",
            FacebookChatClient.Reason("""{"error":{"message":"Session has expired"}}"""));
    }
}

public class TwitchModerationTests
{
    [Fact]
    public void Moderating_without_credentials_says_exactly_which_one_is_missing()
    {
        var moderation = new TwitchModeration();

        Assert.Equal("a twitch token", moderation.Missing);

        moderation.Token = "t";
        Assert.Equal("a twitch client id", moderation.Missing);

        moderation.ClientId = "c";
        Assert.Equal("the channel to moderate", moderation.Missing);

        moderation.BroadcasterId = "1";
        Assert.True(moderation.Ready);
    }

    [Fact]
    public async Task Without_credentials_nothing_pretends_to_have_worked()
    {
        var moderation = new TwitchModeration();

        Assert.Contains("needs a twitch token", await moderation.BanAsync("someone", 600));
        Assert.Contains("needs a twitch token", await moderation.DeleteMessageAsync("id"));
        Assert.Contains("needs a twitch token", await moderation.AnnounceAsync("hello"));
    }

    [Fact]
    public void Durations_are_spoken_in_the_units_a_person_would_use()
    {
        Assert.Equal("30 seconds", TwitchModeration.Spoken(30));
        Assert.Equal("10 minutes", TwitchModeration.Spoken(600));
        Assert.Equal("2 hours", TwitchModeration.Spoken(7200));
    }
}

public class StreamHealthTests
{
    [Fact]
    public void A_statistics_line_becomes_numbers()
    {
        const string line =
            "frame= 1234 fps= 30 q=25.0 size=    4096kB time=00:00:41.20 "
            + "bitrate=8143.1kbits/s dup=0 drop=12 speed=1.00x";

        var stats = StreamHealth.Parse(line);

        Assert.NotNull(stats);
        Assert.Equal(1234, stats!.Value.Frames);
        Assert.Equal(30, stats.Value.Fps);
        Assert.Equal(12, stats.Value.Dropped);
        Assert.Equal(8143.1, stats.Value.BitrateKbps, 1);
        Assert.Equal(1.0, stats.Value.Speed, 2);
        Assert.False(stats.Value.IsBehind);
    }

    [Fact]
    public void Anything_that_is_not_a_statistics_line_is_ignored()
    {
        Assert.Null(StreamHealth.Parse("[flv @ 0x55] Failed to update header"));
    }

    [Fact]
    public void The_lines_that_mean_real_trouble_are_recognised()
    {
        Assert.Equal("the connection dropped", StreamHealth.Trouble("av_interleaved_write_frame(): Broken pipe"));
        Assert.Contains("refused", StreamHealth.Trouble("Connection refused")!);
        Assert.Null(StreamHealth.Trouble("frame= 10 fps=30"));
    }

    [Fact]
    public void Dropping_frames_is_said_once_and_recovering_is_said_too()
    {
        // Something that fires on every sample gets turned off within a minute,
        // and then it protects nobody.
        var monitor = new StreamHealthMonitor();

        Assert.False(monitor.Update(new StreamStats(30, 30, 0, 0, 6000, 1), 1).IsSomething);

        var warned = monitor.Update(new StreamStats(60, 30, 20, 0, 6000, 1), 2);
        Assert.Equal(StreamAlertKind.Dropping, warned.Kind);

        var again = monitor.Update(new StreamStats(90, 30, 40, 0, 6000, 1), 3);
        Assert.False(again.IsSomething);

        var recovered = monitor.Update(new StreamStats(120, 30, 40, 0, 6000, 1), 4);
        Assert.Equal(StreamAlertKind.Recovered, recovered.Kind);
    }

    [Fact]
    public void An_encoder_falling_behind_real_time_is_worth_interrupting_for()
    {
        var monitor = new StreamHealthMonitor();

        var alert = monitor.Update(new StreamStats(30, 24, 0, 0, 4000, 0.82), 1);

        Assert.Equal(StreamAlertKind.Behind, alert.Kind);
        Assert.Contains("behind", alert.Speak!);
    }

    [Fact]
    public void A_quiet_stream_gets_an_occasional_routine_report_and_nothing_else()
    {
        var monitor = new StreamHealthMonitor { ReportEvery = 60 };

        Assert.False(monitor.Update(new StreamStats(30, 30, 0, 0, 6000, 1), 10).IsSomething);

        var routine = monitor.Update(new StreamStats(1800, 30, 0, 0, 6000, 1), 61);

        Assert.Equal(StreamAlertKind.Routine, routine.Kind);
        Assert.Contains("minutes live", routine.Speak!);
    }

    [Fact]
    public void Asking_when_nothing_is_streaming_says_so()
    {
        Assert.Equal("not streaming", new StreamHealthMonitor().Describe(live: false));
    }
}

public class PlaylistTests
{
    private static Playlist Three()
    {
        var playlist = new Playlist();

        playlist.AddRange([
            new PlaylistTrack("/a.mp3", "One"),
            new PlaylistTrack("/b.mp3", "Two"),
            new PlaylistTrack("/c.mp3", "Three"),
        ]);

        return playlist;
    }

    [Fact]
    public void What_is_playing_and_what_is_next_are_said_together()
    {
        // The second question always follows the first, and asking twice while
        // live is one key too many.
        var playlist = Three();

        var spoken = playlist.Start();

        Assert.Contains("One", spoken);
        Assert.Contains("next, Two", spoken);
        Assert.Contains("1 of 3", spoken);
    }

    [Fact]
    public void Repeat_one_repeats_when_a_track_ends_and_is_ignored_when_you_ask_for_the_next()
    {
        // Pressing next and hearing the same song again is not what anybody
        // means by repeat.
        var playlist = Three();
        playlist.Start();
        playlist.Repeat = RepeatMode.One;

        playlist.Next(userAsked: false);
        Assert.Equal("One", playlist.Current!.Name);

        playlist.Next(userAsked: true);
        Assert.Equal("Two", playlist.Current!.Name);
    }

    [Fact]
    public void Stopping_at_the_end_says_so_rather_than_going_quiet()
    {
        var playlist = Three();
        playlist.Repeat = RepeatMode.None;
        playlist.Start(2);

        var spoken = playlist.Next();

        Assert.Contains("end of", spoken);
        Assert.False(playlist.Playing);
    }

    [Fact]
    public void The_playlist_wraps_when_it_is_meant_to()
    {
        var playlist = Three();
        playlist.Start(2);

        playlist.Next();

        Assert.Equal("One", playlist.Current!.Name);
    }

    [Fact]
    public void Shuffling_does_not_interrupt_what_is_playing()
    {
        var playlist = Three();
        playlist.Start();

        var before = playlist.Current;
        var spoken = playlist.ToggleShuffle();

        Assert.Equal(before, playlist.Current);
        Assert.Contains("still playing", spoken);
    }

    [Fact]
    public void Turning_shuffle_off_puts_the_list_back_in_order()
    {
        var playlist = Three();
        playlist.ToggleShuffle();
        playlist.ToggleShuffle();
        playlist.Start();

        Assert.Equal("One", playlist.Current!.Name);
        Assert.Equal("Two", playlist.NextUp!.Name);
    }

    [Fact]
    public void Removing_the_track_that_is_playing_says_that_is_what_happened()
    {
        var playlist = Three();
        playlist.Start();

        Assert.Contains("it was playing", playlist.Remove(0));
    }

    [Fact]
    public void An_empty_playlist_says_so_rather_than_doing_nothing()
    {
        var playlist = new Playlist();

        Assert.Equal("the playlist is empty", playlist.Start());
        Assert.Equal("the playlist is empty", playlist.Next());
        Assert.Contains("empty", playlist.Describe());
    }

    [Fact]
    public void Repeat_cycles_through_all_three_modes_and_says_which()
    {
        var playlist = new Playlist();

        Assert.Contains("this track", playlist.CycleRepeat());
        Assert.Contains("stopping at the end", playlist.CycleRepeat());
        Assert.Contains("whole playlist", playlist.CycleRepeat());
    }
}
