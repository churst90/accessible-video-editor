using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Core.Tests;

public class SceneTests
{
    private static (StreamSetup Setup, Scene Scene, StreamSource Camera) Basic()
    {
        var setup = StreamSetup.Starter();

        return (setup, setup.Scenes[0], setup.Sources[0]);
    }

    [Fact]
    public void Cutting_to_a_scene_says_what_is_now_live_rather_than_just_its_name()
    {
        // The whole risk of scene switching is cutting to something that is not
        // showing what you think it is.
        var (setup, _, _) = Basic();

        var result = SceneOperations.Switch(setup, setup.Scenes[1].Id);

        Assert.True(result.Changed);
        Assert.Contains("Screen share", result.Description);
        Assert.Contains("Screen", result.Description);
        Assert.Contains("Face cam", result.Description);
    }

    [Fact]
    public void Cutting_to_a_scene_with_nothing_showing_warns()
    {
        var (setup, _, _) = Basic();
        var empty = new Scene { Id = StreamIds.NewScene(), Name = "Blank" };
        setup.Scenes.Add(empty);

        var result = SceneOperations.Switch(setup, empty.Id);

        Assert.Contains("empty scene", result.Description);
    }

    [Fact]
    public void Cutting_to_a_scene_with_no_audio_warns_about_the_audio()
    {
        var setup = StreamSetup.Starter();
        var silent = new Scene { Id = StreamIds.NewScene(), Name = "Slide" };
        silent.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = setup.Sources[2].Id });
        setup.Scenes.Add(silent);

        var result = SceneOperations.Switch(setup, silent.Id);

        Assert.Contains("no audio", result.Announce());
    }

    [Fact]
    public void One_source_lives_in_many_scenes_as_one_object()
    {
        // This is the whole reason scenes are worth having: renaming the camera
        // renames it everywhere rather than in one of five copies.
        var (setup, _, camera) = Basic();

        camera.Name = "Main camera";

        Assert.All(
            setup.Scenes.SelectMany(s => s.Sources).Where(r => r.Source == camera.Id),
            reference => Assert.Contains("Main camera", reference.Describe(setup)));
    }

    [Fact]
    public void The_same_source_can_be_full_frame_in_one_scene_and_an_inset_in_another()
    {
        var (setup, _, camera) = Basic();

        var full = setup.Scenes[0].Sources.First(s => s.Source == camera.Id);
        var inset = setup.Scenes[1].Sources.First(s => s.Source == camera.Id);

        Assert.Contains("full frame", full.Describe(setup));
        Assert.Contains("25 percent", inset.Describe(setup));
    }

    [Fact]
    public void A_source_cannot_be_added_to_the_same_scene_twice()
    {
        var (setup, scene, camera) = Basic();

        var result = SceneOperations.AddToScene(setup, scene.Id, camera.Id);

        Assert.False(result.Changed);
        Assert.Contains("already in", result.Description);
    }

    [Fact]
    public void Hiding_a_source_keeps_it_in_the_scene()
    {
        var (setup, scene, camera) = Basic();
        var reference = scene.Sources.First(s => s.Source == camera.Id);

        SceneOperations.ToggleVisible(setup, scene.Id, reference.Id);

        Assert.False(reference.Visible);
        Assert.Contains(reference, scene.Sources);
    }

    [Fact]
    public void Hiding_a_source_in_the_scene_that_is_on_air_says_so()
    {
        var (setup, scene, camera) = Basic();
        setup.IsLive = true;
        setup.LiveScene = scene.Id;

        var reference = scene.Sources.First(s => s.Source == camera.Id);
        var result = SceneOperations.ToggleVisible(setup, scene.Id, reference.Id);

        Assert.Contains("on air", result.Description);
    }

    [Fact]
    public void Reordering_says_where_it_landed_because_there_is_no_way_to_see_it()
    {
        var setup = StreamSetup.Starter();
        var scene = setup.Scenes[1];
        var camera = scene.Sources.First(s => s.Source == setup.Sources[0].Id);

        var result = SceneOperations.Reorder(setup, scene.Id, camera.Id, up: true);

        Assert.True(result.Changed);
        Assert.Contains("of 3", result.Description);
    }

    [Fact]
    public void Reordering_past_the_end_refuses_rather_than_silently_doing_nothing()
    {
        var setup = StreamSetup.Starter();
        var scene = setup.Scenes[0];

        var result = SceneOperations.Reorder(setup, scene.Id, scene.Sources[^1].Id, up: true);

        Assert.False(result.Changed);
        Assert.Contains("already at the front", result.Description);
    }

    [Fact]
    public void The_scene_on_air_cannot_be_deleted_out_from_under_the_audience()
    {
        var (setup, scene, _) = Basic();
        setup.IsLive = true;
        setup.LiveScene = scene.Id;

        var result = SceneOperations.RemoveScene(setup, scene.Id);

        Assert.False(result.Changed);
        Assert.Contains("on air", result.Description);
    }

    [Fact]
    public void Scenes_are_selected_by_the_number_a_person_would_say()
    {
        var setup = StreamSetup.Starter();

        Assert.Equal("Face cam", setup.ByNumber(1)!.Name);
        Assert.Equal(2, setup.NumberOf(setup.Scenes[1].Id));
        Assert.Null(setup.ByNumber(9));
    }

    // ---- going live ------------------------------------------------------

    [Fact]
    public void Preflight_finds_the_things_a_sighted_streamer_would_see_immediately()
    {
        var setup = StreamSetup.Empty();

        var problems = SceneOperations.PreflightWarnings(setup);

        Assert.Contains("there are no scenes", problems);
        Assert.Contains("no destination is enabled", problems);
    }

    [Fact]
    public void Preflight_catches_a_destination_that_is_enabled_but_not_set_up()
    {
        var setup = StreamSetup.Starter();
        setup.Targets.Add(StreamTarget.For(StreamPlatform.Twitch));

        Assert.Contains(
            SceneOperations.PreflightWarnings(setup),
            p => p.Contains("Twitch") && p.Contains("not set up"));
    }

    [Fact]
    public void A_fully_configured_setup_has_nothing_to_warn_about()
    {
        var setup = StreamSetup.Starter();
        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "secret";
        setup.Targets.Add(twitch);

        Assert.Empty(SceneOperations.PreflightWarnings(setup));
    }
}

public class StreamTargetTests
{
    [Fact]
    public void A_stream_key_is_never_in_anything_that_gets_spoken()
    {
        // Speech is often on a speaker in a room with other people in it, and a
        // stream key lets anyone broadcast as you.
        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "live_12345_supersecret";

        var setup = new StreamSetup { Targets = [twitch] };
        var settings = EncoderSettings.ForTargets(setup.Targets);

        Assert.DoesNotContain("supersecret", twitch.Describe());
        Assert.DoesNotContain("supersecret", settings.Describe());
        Assert.DoesNotContain("supersecret", AccessibleVideoEditor.Engine.StreamEncoder.Describe(setup, settings));
    }

    [Fact]
    public void The_key_still_reaches_the_url_that_ffmpeg_is_given()
    {
        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "abc";

        Assert.Equal("rtmp://live.twitch.tv/app/abc", twitch.Url);
    }

    [Fact]
    public void The_encode_is_limited_by_the_strictest_destination_and_says_which()
    {
        // One encode goes to every service, so Twitch's ceiling is everybody's
        // ceiling - and "why is my YouTube stream only 6000" has to have an
        // answer.
        var settings = EncoderSettings.ForTargets(
        [
            StreamTarget.For(StreamPlatform.YouTube),
            StreamTarget.For(StreamPlatform.Twitch),
        ]);

        Assert.Equal(6000, settings.VideoBitrateKbps);
        Assert.Equal("Twitch", settings.LimitedBy);
        Assert.Contains("limited by Twitch", settings.Describe());
    }

    [Fact]
    public void A_disabled_destination_does_not_hold_the_others_back()
    {
        var facebook = StreamTarget.For(StreamPlatform.Facebook);
        facebook.Enabled = false;

        var settings = EncoderSettings.ForTargets([StreamTarget.For(StreamPlatform.YouTube), facebook]);

        Assert.Equal("YouTube", settings.LimitedBy);
    }

    [Fact]
    public void With_nothing_configured_the_settings_are_the_ones_least_likely_to_fail()
    {
        var settings = EncoderSettings.ForTargets([]);

        Assert.Equal(6000, settings.VideoBitrateKbps);
        Assert.Contains("nothing configured", settings.Describe());
    }
}

public class ChatTests
{
    private static ChatMessage Message(
        string author,
        string text,
        double at = 0,
        ChatBadge badges = ChatBadge.None,
        bool first = false,
        StreamPlatform platform = StreamPlatform.Twitch) =>
        new(platform, author, text, at, ChatKind.Message, badges, first);

    // ---- what gets picked out of the noise --------------------------------

    [Fact]
    public void Being_named_is_the_most_urgent_thing_in_chat()
    {
        var store = new ChatStore { MyName = "Cody" };

        var category = Message("bob", "hey Cody nice stream").Categorise("Cody");

        Assert.True(category.HasFlag(ChatCategory.Mention));
        Assert.Equal("chat-mention", ChatMessage.EarconFor(category));
    }

    [Fact]
    public void A_first_time_chatter_asking_something_is_both_and_the_more_urgent_wins()
    {
        var message = Message("newcomer", "how did you set that up?", first: true);
        var category = message.Categorise("Cody");

        Assert.True(category.HasFlag(ChatCategory.FirstTime));
        Assert.True(category.HasFlag(ChatCategory.Question));
        Assert.Equal("chat-first-time", ChatMessage.EarconFor(category));
    }

    [Fact]
    public void Questions_are_found_without_a_question_mark()
    {
        Assert.True(Message("bob", "how do you do that").IsQuestion);
        Assert.True(Message("bob", "what mic is that").IsQuestion);
        Assert.False(Message("bob", "that is a nice mic").IsQuestion);
    }

    [Fact]
    public void An_ordinary_message_gets_no_earcon_at_all()
    {
        Assert.Null(ChatMessage.EarconFor(Message("bob", "hello").Categorise("Cody")));
    }

    // ---- what is read aloud ----------------------------------------------

    [Fact]
    public void The_same_person_twice_running_is_not_named_twice()
    {
        // Repeating the name on every line is what makes chat readers
        // exhausting to listen to.
        var store = new ChatStore { MyName = "Cody", SpeakEverything = true };
        store.Channel(StreamPlatform.Twitch).Connected = true;

        var first = store.Receive(Message("bob", "hello"));
        var second = store.Receive(Message("bob", "still here"));

        Assert.Equal("bob: hello", first.Speak);
        Assert.Equal("still here", second.Speak);
    }

    [Fact]
    public void The_platform_is_named_only_when_more_than_one_is_connected()
    {
        var store = new ChatStore { SpeakEverything = true };
        store.Channel(StreamPlatform.Twitch).Connected = true;

        Assert.Equal("bob: hello", store.Receive(Message("bob", "hello")).Speak);

        store.Channel(StreamPlatform.YouTube).Connected = true;

        var later = store.Receive(Message("ann", "hi", platform: StreamPlatform.YouTube));

        Assert.StartsWith("youtube", later.Speak);
    }

    [Fact]
    public void By_default_only_the_messages_that_want_you_interrupt()
    {
        var store = new ChatStore { MyName = "Cody" };

        Assert.Null(store.Receive(Message("bob", "nice")).Speak);
        Assert.NotNull(store.Receive(Message("bob", "hey Cody")).Speak);
        Assert.NotNull(store.Receive(Message("ann", "what mic is that")).Speak);
    }

    [Fact]
    public void A_burst_stops_being_read_and_becomes_a_count()
    {
        // A chat reader that cannot be out-talked is a chat reader you turn off.
        var store = new ChatStore { SpeakEverything = true };
        store.Channel(StreamPlatform.Twitch).Connected = true;

        var spoken = 0;

        for (var i = 0; i < 20; i++)
        {
            if (store.Receive(Message($"user{i}", "hype", at: i * 0.1)).Speak is not null) spoken++;
        }

        Assert.True(spoken <= ChatStore.Burst + 1, $"{spoken} messages were read out of 20");
        Assert.Contains("twitch 20", store.Summarise());
    }

    [Fact]
    public void A_held_back_message_still_earcons_so_you_know_it_happened()
    {
        var store = new ChatStore { MyName = "Cody" };
        store.Channel(StreamPlatform.Twitch).Connected = true;

        ChatAnnouncement last = default;

        for (var i = 0; i < 12; i++)
        {
            last = store.Receive(Message($"user{i}", "hey Cody", at: i * 0.1));
        }

        Assert.True(last.Suppressed);
        Assert.Equal("chat-mention", last.Earcon);
    }

    [Fact]
    public void Follows_and_subs_are_announced_differently_from_messages()
    {
        var store = new ChatStore();
        var follow = new ChatMessage(StreamPlatform.Twitch, "ann", string.Empty, 0, ChatKind.Follow);

        var result = store.Receive(follow);

        Assert.Equal("ann followed", result.Speak);
        Assert.Equal("chat-event", result.Earcon);
    }

    // ---- reading back ----------------------------------------------------

    [Fact]
    public void Scrolling_back_stops_new_messages_interrupting_and_counts_them_instead()
    {
        var store = new ChatStore { SpeakEverything = true };
        var channel = store.Channel(StreamPlatform.Twitch);
        channel.Connected = true;

        store.Receive(Message("bob", "one"));
        channel.Older();

        var during = store.Receive(Message("ann", "two"));

        Assert.True(during.Suppressed);
        Assert.Equal(1, channel.Unread);
        Assert.False(channel.IsAtLiveEnd);
    }

    [Fact]
    public void The_first_step_back_repeats_the_newest_message_rather_than_skipping_it()
    {
        var store = new ChatStore();
        var channel = store.Channel(StreamPlatform.Twitch);

        store.Receive(Message("bob", "one"));
        store.Receive(Message("ann", "two"));

        Assert.Equal("two", channel.Older()!.Text);
        Assert.Equal("one", channel.Older()!.Text);
    }

    [Fact]
    public void Reading_forward_off_the_end_returns_you_to_live_and_clears_the_count()
    {
        var store = new ChatStore();
        var channel = store.Channel(StreamPlatform.Twitch);

        store.Receive(Message("bob", "one"));
        channel.Older();
        store.Receive(Message("ann", "two"));

        channel.Newer();
        channel.Newer();

        Assert.True(channel.IsAtLiveEnd);
        Assert.Equal(0, channel.Unread);
    }

    [Fact]
    public void Each_platform_keeps_its_own_history_so_a_reply_cannot_go_to_the_wrong_audience()
    {
        var store = new ChatStore();

        store.Receive(Message("bob", "twitch side"));
        store.Receive(Message("ann", "youtube side", platform: StreamPlatform.YouTube));

        Assert.Single(store.Channel(StreamPlatform.Twitch).Messages);
        Assert.Single(store.Channel(StreamPlatform.YouTube).Messages);
    }

    [Fact]
    public void An_unconnected_chat_says_so_rather_than_looking_like_a_quiet_one()
    {
        // A silent pane that never connected is indistinguishable from a chat
        // nobody is talking in, which is the worst failure here.
        Assert.Contains("not connected", new ChatChannel(StreamPlatform.YouTube).Describe());
        Assert.Equal("no chat connected", new ChatStore().Summarise());
    }
}

public class StreamAreaTests
{
    [Fact]
    public void There_is_always_somewhere_for_the_key_to_go()
    {
        var areas = StreamAreas.For(StreamSetup.Empty(), new ChatStore());

        Assert.Equal(4, areas.Count);
        Assert.Contains(areas, a => a.Kind == StreamArea.Chat);
    }

    [Fact]
    public void Every_connected_platform_gets_its_own_chat_area()
    {
        var chat = new ChatStore();
        chat.Channel(StreamPlatform.Twitch).Connected = true;
        chat.Channel(StreamPlatform.YouTube).Connected = true;

        var areas = StreamAreas.For(StreamSetup.Empty(), chat);

        Assert.Equal(2, areas.Count(a => a.Kind == StreamArea.Chat));
    }

    [Fact]
    public void Cycling_goes_round_in_both_directions()
    {
        var areas = StreamAreas.For(StreamSetup.Empty(), new ChatStore());

        var next = StreamAreas.Cycle(areas, areas[0], forward: true);
        Assert.Equal(areas[1], next);

        var wrapped = StreamAreas.Cycle(areas, areas[^1], forward: true);
        Assert.Equal(areas[0], wrapped);

        var back = StreamAreas.Cycle(areas, areas[0], forward: false);
        Assert.Equal(areas[^1], back);
    }
}
