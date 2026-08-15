using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

public class SceneComposerTests
{
    private static EncoderSettings Settings => new(1920, 1080, 30, 6000, 160, 2, "test");

    [Fact]
    public void A_song_over_a_static_picture_loops_both_of_them()
    {
        // The case the user asked for by name: a still and a track that carry
        // on until you cut away, rather than ending the scene when the song
        // does.
        var setup = StreamSetup.Empty();

        var picture = new StreamSource
        {
            Id = StreamIds.NewSource(), Name = "Holding slide",
            Kind = StreamSourceKind.Image, Path = "/tmp/slide.png",
        };

        var song = new StreamSource
        {
            Id = StreamIds.NewSource(), Name = "Bed",
            Kind = StreamSourceKind.Music, Path = "/tmp/bed.mp3", Loop = true,
        };

        setup.Sources.AddRange([picture, song]);

        var scene = new Scene { Id = StreamIds.NewScene(), Name = "Back soon" };
        scene.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = picture.Id });
        scene.Sources.Add(new SourceRef { Id = StreamIds.NewRef(), Source = song.Id });
        setup.Scenes.Add(scene);

        var plan = SceneComposer.Build(setup, scene, Settings);
        var arguments = string.Join(' ', plan.Inputs);

        Assert.Contains("-loop 1", arguments);
        Assert.Contains("-stream_loop -1", arguments);
        Assert.Contains("/tmp/slide.png", arguments);
        Assert.Contains("/tmp/bed.mp3", arguments);
    }

    [Fact]
    public void A_scene_with_nothing_showing_goes_to_black_rather_than_failing_to_start()
    {
        // Black on air is recoverable. A stream that will not start is not.
        var setup = StreamSetup.Starter();
        var scene = setup.Scenes[0];

        foreach (var reference in scene.Sources) reference.Visible = false;

        var plan = SceneComposer.Build(setup, scene, Settings);

        Assert.Contains("color=c=black", string.Join(' ', plan.Inputs));
        Assert.Contains("anullsrc", string.Join(' ', plan.Inputs));
        Assert.Equal("0:v", plan.VideoLabel);
    }

    [Fact]
    public void Sources_are_laid_over_each_other_in_the_order_the_scene_lists_them()
    {
        var setup = StreamSetup.Starter();
        var plan = SceneComposer.Build(setup, setup.Scenes[1], Settings);

        Assert.Contains("[0:v][v0]overlay", plan.FilterComplex);
        Assert.Contains("[s0][v1]overlay", plan.FilterComplex);
    }

    [Fact]
    public void A_full_frame_source_is_not_scaled_into_a_corner()
    {
        var setup = StreamSetup.Starter();
        var scene = setup.Scenes[1];
        var screen = scene.Sources[0];

        var (x, y) = SceneComposer.Position(screen, 1920, 1080, 1920);

        Assert.Equal((0, 0), (x, y));
    }

    [Fact]
    public void A_quarter_size_inset_in_the_bottom_right_stays_on_the_canvas()
    {
        var reference = new SourceRef
        {
            Id = StreamIds.NewRef(),
            Source = StreamIds.NewSource(),
            Scale = 0.25,
            Placement = new Placement(3),
        };

        var (x, y) = SceneComposer.Position(reference, 1920, 1080, 480);

        Assert.True(x + 480 <= 1920);
        Assert.True(y + 270 <= 1080);
        Assert.True(x > 960, "a bottom-right inset should be on the right half");
        Assert.True(y > 540, "a bottom-right inset should be in the lower half");
    }

    [Fact]
    public void A_muted_source_contributes_no_audio_to_the_mix()
    {
        var setup = StreamSetup.Starter();
        var scene = setup.Scenes[0];

        foreach (var reference in scene.Sources) reference.Muted = true;

        var plan = SceneComposer.Build(setup, scene, Settings);

        Assert.Contains("anullsrc", string.Join(' ', plan.Inputs));
    }
}

public class StreamArgumentTests
{
    [Fact]
    public void Two_destinations_are_one_encode_sent_twice_rather_than_two_encodes()
    {
        var setup = StreamSetup.Starter();

        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "aaa";
        var youtube = StreamTarget.For(StreamPlatform.YouTube);
        youtube.Key = "bbb";

        setup.Targets.AddRange([twitch, youtube]);

        var settings = EncoderSettings.ForTargets(setup.Targets);
        var arguments = StreamEncoder.BuildArguments(setup, setup.Live!, settings);

        var joined = string.Join(' ', arguments);

        Assert.Single(arguments, a => a == "-c:v");
        Assert.Contains("-f tee", joined);
        Assert.Contains("onfail=ignore", joined);
        Assert.Contains("rtmp://live.twitch.tv/app/aaa", joined);
        Assert.Contains("rtmp://a.rtmp.youtube.com/live2/bbb", joined);
    }

    [Fact]
    public void One_destination_skips_the_tee_so_its_error_is_not_swallowed()
    {
        var setup = StreamSetup.Starter();
        var twitch = StreamTarget.For(StreamPlatform.Twitch);
        twitch.Key = "aaa";
        setup.Targets.Add(twitch);

        var arguments = string.Join(' ', StreamEncoder.BuildArguments(
            setup, setup.Live!, EncoderSettings.ForTargets(setup.Targets)));

        Assert.DoesNotContain("-f tee", arguments);
        Assert.Contains("-f flv rtmp://live.twitch.tv/app/aaa", arguments);
    }

    [Fact]
    public void With_no_destination_the_same_pipeline_runs_as_a_local_preview()
    {
        // Going live and looking at yourself have to be the same pipeline, or
        // the preview can be fine while the stream is not.
        var setup = StreamSetup.Starter();

        var arguments = string.Join(' ', StreamEncoder.BuildArguments(
            setup, setup.Live!, EncoderSettings.ForTargets(setup.Targets)));

        Assert.Contains("-f null", arguments);
    }

    [Fact]
    public void Keyframes_land_on_the_interval_every_service_asks_for()
    {
        var setup = StreamSetup.Starter();

        var arguments = StreamEncoder.BuildArguments(
            setup, setup.Live!, new EncoderSettings(1920, 1080, 30, 6000, 160, 2, "test")).ToList();

        var gop = arguments[arguments.IndexOf("-g") + 1];

        Assert.Equal("60", gop);
    }
}

public class TwitchIrcTests
{
    [Fact]
    public void A_chat_line_becomes_a_message_with_its_badges()
    {
        var line = "@badges=moderator/1,subscriber/12;display-name=Bob;first-msg=0 "
                   + ":bob!bob@bob.tmi.twitch.tv PRIVMSG #cody :hello there";

        var message = TwitchIrc.Parse(line);

        Assert.NotNull(message);
        Assert.Equal("Bob", message!.Author);
        Assert.Equal("hello there", message.Text);
        Assert.True(message.Badges.HasFlag(ChatBadge.Moderator));
        Assert.True(message.Badges.HasFlag(ChatBadge.Subscriber));
        Assert.False(message.FirstTime);
    }

    [Fact]
    public void A_first_message_is_flagged_so_a_newcomer_can_be_picked_out()
    {
        var line = "@display-name=New;first-msg=1 :new!new@new.tmi.twitch.tv PRIVMSG #cody :hi";

        Assert.True(TwitchIrc.Parse(line)!.FirstTime);
    }

    [Fact]
    public void A_line_with_no_tags_at_all_still_parses()
    {
        var message = TwitchIrc.Parse(":bob!bob@bob.tmi.twitch.tv PRIVMSG #cody :no tags here");

        Assert.Equal("bob", message!.Author);
        Assert.Equal("no tags here", message.Text);
    }

    [Fact]
    public void A_message_containing_a_colon_keeps_all_of_it()
    {
        var message = TwitchIrc.Parse(":bob!bob@bob.tmi.twitch.tv PRIVMSG #cody :time is 12:30 now");

        Assert.Equal("time is 12:30 now", message!.Text);
    }

    [Fact]
    public void Raids_and_subscriptions_come_through_as_events_not_as_chat()
    {
        var raid = TwitchIrc.Parse("@display-name=Ann;msg-id=raid :tmi.twitch.tv USERNOTICE #cody");
        var sub = TwitchIrc.Parse("@display-name=Ann;msg-id=resub :tmi.twitch.tv USERNOTICE #cody");

        Assert.Equal(ChatKind.Raid, raid!.Kind);
        Assert.Equal(ChatKind.Subscribe, sub!.Kind);
    }

    [Fact]
    public void Server_chatter_is_ignored_rather_than_read_out()
    {
        Assert.Null(TwitchIrc.Parse(":tmi.twitch.tv 001 justinfan12345 :Welcome, GLHF!"));
        Assert.Null(TwitchIrc.Parse(string.Empty));
    }

    [Fact]
    public void A_moderation_tag_is_kept_so_the_right_message_can_be_removed()
    {
        // Deleting the wrong message is not undoable, so moderation names the
        // message and the account rather than the display name.
        var line = "@id=msg-77;user-id=4242;display-name=Bob "
                   + ":bob!bob@bob.tmi.twitch.tv PRIVMSG #cody :hello";

        var message = TwitchIrc.Parse(line);

        Assert.Equal("msg-77", message!.Id);
        Assert.Equal("4242", message.AuthorId);
        Assert.True(message.CanModerate);
    }
}
