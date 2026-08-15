using AccessibleVideoEditor.Core.Commands;

namespace AccessibleVideoEditor.Core.Tests;

public class CommandRegistryTests
{
    [Fact]
    public void Command_ids_are_unique()
    {
        var duplicates = CommandRegistry.All
            .GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void No_two_commands_claim_the_same_key_in_a_pane_where_both_are_live()
    {
        // A plain letter may mean one thing on a track header and another on
        // the timeline - that is what contexts are for. What must never happen
        // is two commands being live on one key in one pane, because there
        // would be no way to tell which fired.
        Assert.Empty(CommandRegistry.Conflicts());
    }

    [Fact]
    public void Reusing_a_key_across_panes_is_allowed_and_actually_used()
    {
        // M is mute on a track header and marker on the timeline. If this ever
        // stops being true the contexts have collapsed into one namespace and
        // the conflict test above is doing less than it looks.
        var mute = CommandRegistry.ById("track.mute")!;
        var marker = CommandRegistry.ById("edit.marker")!;

        Assert.Equal("M", mute.DefaultBinding);
        Assert.Equal("M", marker.DefaultBinding);
        Assert.False(mute.Context.Includes(CommandContext.Timeline));
        Assert.False(marker.Context.Includes(CommandContext.Tracks));
    }

    [Fact]
    public void Every_command_declares_where_its_binding_came_from()
    {
        // The registry is only honest if provenance is mandatory. This test is
        // what stops "I made it up" being quietly indistinguishable from
        // "this is what Premiere does".
        Assert.All(CommandRegistry.All, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Title));
            Assert.False(string.IsNullOrWhiteSpace(command.DefaultBinding));
            Assert.True(Enum.IsDefined(command.Origin));
        });
    }

    [Fact]
    public void Every_deliberate_deviation_from_Premiere_states_its_reason()
    {
        var unexplained = CommandRegistry.Deviations
            .Where(c => string.IsNullOrWhiteSpace(c.Description)
                        && CommandRegistry.All.Count(o => o.Origin == c.Origin
                                                          && !string.IsNullOrWhiteSpace(o.Description)) == 0)
            .Select(c => c.Id)
            .ToList();

        Assert.Empty(unexplained);
        Assert.NotEmpty(CommandRegistry.Deviations);
    }

    [Theory]
    [InlineData("play.rewind", "J", KeyOrigin.UniversalNle)]
    [InlineData("play.stop", "K", KeyOrigin.UniversalNle)]
    [InlineData("play.forward", "L", KeyOrigin.UniversalNle)]
    [InlineData("select.in", "I", KeyOrigin.UniversalNle)]
    [InlineData("select.out", "O", KeyOrigin.UniversalNle)]
    [InlineData("edit.marker", "M", KeyOrigin.UniversalNle)]
    public void The_shuttle_and_marking_keys_match_every_editor(string id, string key, KeyOrigin origin)
    {
        var command = CommandRegistry.ById(id)!;

        Assert.Equal(key, command.DefaultBinding);
        Assert.Equal(origin, command.Origin);
    }

    [Theory]
    [InlineData("file.importMedia", "Ctrl+I")]
    [InlineData("edit.insert", "Comma")]
    [InlineData("edit.overwrite", "Period")]
    [InlineData("edit.disable", "Shift+E")]
    [InlineData("granularity.coarser", "Minus")]
    public void Premiere_bindings_are_kept_where_Premiere_has_one(string id, string key)
    {
        var command = CommandRegistry.ById(id)!;

        Assert.Equal(key, command.DefaultBinding);
        Assert.Equal(KeyOrigin.Premiere, command.Origin);
    }

    [Fact]
    public void The_function_keys_are_stacked_one_domain_per_key()
    {
        // Plain does the common thing, Shift the variant, Ctrl the setup, so an
        // unfamiliar binding is guessable rather than memorised.
        Assert.Equal("F2", CommandRegistry.ById("render.master")!.DefaultBinding);
        Assert.Equal("Shift+F2", CommandRegistry.ById("render.draft")!.DefaultBinding);
        Assert.Equal("Ctrl+F2", CommandRegistry.ById("render.presets")!.DefaultBinding);

        Assert.Equal("F5", CommandRegistry.ById("track.arm")!.DefaultBinding);
        Assert.Equal("Shift+F5", CommandRegistry.ById("capture.record")!.DefaultBinding);
        Assert.Equal("Ctrl+F5", CommandRegistry.ById("capture.device")!.DefaultBinding);
    }

    [Fact]
    public void Premieres_export_key_survives_as_an_alternate()
    {
        // F2 is the render key here by request; Ctrl+M still works so muscle
        // memory from Premiere is not simply thrown away.
        Assert.Equal("Ctrl+M", CommandRegistry.ById("render.master")!.Alternate);
    }

    [Fact]
    public void Ctrl_T_makes_a_track_rather_than_a_title()
    {
        Assert.Equal("Ctrl+T", CommandRegistry.ById("track.add")!.DefaultBinding);
        Assert.NotEqual("Ctrl+T", CommandRegistry.ById("overlay.title")!.DefaultBinding);
    }

    [Fact]
    public void Ripple_delete_on_the_plain_Delete_key_is_a_declared_deviation()
    {
        // Premiere has these the other way round. Inverting them without saying
        // so would be the worst option; this test pins the declaration.
        var ripple = CommandRegistry.ById("edit.rippleDelete")!;
        var lift = CommandRegistry.ById("edit.lift")!;

        Assert.Equal("Delete", ripple.DefaultBinding);
        Assert.Equal("Shift+Delete", lift.DefaultBinding);
        Assert.Equal(KeyOrigin.DeviatesFromPremiere, ripple.Origin);
        Assert.Contains("other way round", ripple.Description);
    }

    [Fact]
    public void Deleting_a_track_lives_in_the_Tracks_pane_so_Delete_is_unambiguous()
    {
        var removeTrack = CommandRegistry.ById("track.remove")!;
        var rippleDelete = CommandRegistry.ById("edit.rippleDelete")!;

        Assert.Equal("Delete", removeTrack.DefaultBinding);
        Assert.Equal(CommandContext.Tracks, removeTrack.Context);
        Assert.Equal("Delete", rippleDelete.DefaultBinding);
        Assert.NotEqual(removeTrack.Context, rippleDelete.Context);
    }

    [Fact]
    public void Segment_edge_jumps_are_bound_to_shift_comma_and_shift_period()
    {
        Assert.Equal("Shift+Comma", CommandRegistry.ById("cursor.segmentStart")!.DefaultBinding);
        Assert.Equal("Shift+Period", CommandRegistry.ById("cursor.segmentEnd")!.DefaultBinding);
    }

    [Fact]
    public void Zoom_and_step_size_are_one_command_with_two_bindings()
    {
        Assert.Equal("Ctrl+Up", CommandRegistry.ById("granularity.coarser")!.Alternate);
        Assert.Equal("Ctrl+Down", CommandRegistry.ById("granularity.finer")!.Alternate);
    }

    [Fact]
    public void Context_help_offers_global_commands_plus_the_current_pane()
    {
        var timeline = CommandRegistry.InContext(CommandContext.Timeline).ToList();

        Assert.Contains(timeline, c => c.Id == "edit.split");
        Assert.Contains(timeline, c => c.Id == "file.save");
        Assert.DoesNotContain(timeline, c => c.Id == "track.remove");
    }

    [Theory]
    [InlineData("split", "edit.split")]
    [InlineData("paste", "edit.paste")]
    [InlineData("viewfinder", "capture.viewfinder")]
    [InlineData("heal", "edit.heal")]
    public void Palette_search_finds_the_command_you_would_ask_for(string query, string expected)
    {
        Assert.Equal(expected, CommandRegistry.Search(query).First().Id);
    }

    [Fact]
    public void Workflows_validate_against_the_registry()
    {
        var good = new Workflow
        {
            Name = "Name lower third",
            Steps = [new WorkflowStep { CommandId = "overlay.title" }],
        };

        var broken = new Workflow
        {
            Name = "Broken",
            Steps = [new WorkflowStep { CommandId = "overlay.titel" }],
        };

        Assert.Empty(good.Validate());
        Assert.Single(broken.Validate());
    }
}
