using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Commands;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The interface and the code agree.
///
/// These read the GTK sources as text rather than referencing the project,
/// because the front end needs a display to load and these questions are about
/// what is written down, not about what runs. Three separate ways of naming the
/// same action have to line up - a registry id, a menu action name, and a
/// handler - and nothing in the compiler checks that they do.
///
/// An audit once claimed these checks "run over the tree". They did not exist,
/// and six commands with documented keys had no handler at all: pressing them
/// did nothing, which reads as the application missing the key rather than as
/// the feature being absent.
/// </summary>
public partial class WiringTests
{
    [GeneratedRegex(@"win\.([A-Za-z0-9_]+)")]
    private static partial Regex MenuAction();

    [GeneratedRegex(@"(?:Parameterised)?Action\(""([A-Za-z0-9_.]+)""")]
    private static partial Regex HandlerName();

    [GeneratedRegex(@"Run\(""([A-Za-z0-9_.]+)""\)")]
    private static partial Regex RunTarget();

    [GeneratedRegex(@"\(""([a-z]+\.[A-Za-z0-9_]+)"",\s*""win\.")]
    private static partial Regex MenuCommandId();

    private static string GtkDirectory([CallerFilePath] string thisFile = "") =>
        Path.Combine(
            Directory.GetParent(thisFile)!.Parent!.Parent!.FullName,
            "src",
            "AccessibleVideoEditor.Gtk");

    private static string GtkSource()
    {
        var directory = GtkDirectory();
        Assert.True(Directory.Exists(directory), $"could not find the GTK sources at {directory}");

        return string.Join(
            '\n',
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f)
                     .Select(File.ReadAllText));
    }

    private static HashSet<string> Matches(Regex pattern, string text) =>
        pattern.Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Every_menu_item_reaches_a_handler()
    {
        // A GTK menu item pointing at an action nobody registered is not an
        // error: it renders, it is focusable, and choosing it does nothing.
        var source = GtkSource();
        var actions = Matches(MenuAction(), source);
        var handlers = Matches(HandlerName(), source);

        // Guard against the whole file passing because a regex stopped matching:
        // an empty set has nothing missing from it, so this would go green if the
        // sources moved or the Action helper were renamed.
        Assert.True(actions.Count > 100, $"only found {actions.Count} menu actions");
        Assert.True(handlers.Count > 100, $"only found {handlers.Count} handlers");

        Assert.Empty(actions.Except(handlers).Order());
    }

    [Fact]
    public void Every_command_the_code_invokes_by_name_exists()
    {
        var source = GtkSource();
        var missing = Matches(RunTarget(), source).Except(Matches(HandlerName(), source)).Order();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_menu_label_comes_from_a_real_registry_command()
    {
        // Labels and accelerators are looked up by id, and the menu builder
        // skips an id it cannot find. Two View items were missing for months
        // because of exactly this, and nothing said so.
        var ids = CommandRegistry.All.Select(c => c.Id).ToHashSet();
        var unknown = Matches(MenuCommandId(), GtkSource()).Except(ids).Order();

        Assert.Empty(unknown);
    }

    [Fact]
    public void No_two_commands_share_a_title()
    {
        // Two ids with one title is one feature documented twice: F1 reads it
        // out twice and the palette offers the same thing on two rows, which
        // reads as two features that must differ somehow.
        var duplicates = CommandRegistry.All
            .GroupBy(c => c.Title)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.Id))}")
            .Order();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void The_keys_held_for_unbuilt_commands_are_not_claimed_by_anything_else()
    {
        // Workflows, nudge, move-to-track and export presets are designed and
        // unbuilt. Their keys were removed from the registry rather than left
        // pointing at nothing; this pins them so a future command cannot quietly
        // take one and make the roadmap's reservation false.
        string[] held = ["Ctrl+Alt+K", "Ctrl+Alt+Shift+K", "Alt+Left", "Alt+Right", "Ctrl+F2"];

        var taken = CommandRegistry.All
            .Where(c => held.Contains(c.DefaultBinding) || held.Contains(c.Alternate))
            .Select(c => $"{c.Id} took {c.DefaultBinding}")
            .Order();

        Assert.Empty(taken);
    }
}
