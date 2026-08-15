namespace AccessibleVideoEditor.Core.Commands;

/// <summary>
/// A named sequence of commands, saved and replayed as one action.
///
/// This costs almost nothing to support because every action already exists as
/// a registry entry with an ID - a workflow is just a list of those IDs with
/// their arguments. "Lower third with my name, four seconds, bottom centre"
/// becomes one palette entry instead of five keystrokes and two prompts.
///
/// Workflows appear in the palette alongside built-in commands, so there is no
/// separate place to go looking for them.
/// </summary>
public sealed class Workflow
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Optional binding. Unbound workflows are still reachable from the palette.</summary>
    public string? Binding { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];

    /// <summary>
    /// Every step must name a real command. Validated on load rather than on
    /// run, so a workflow broken by a rename is reported once at startup
    /// instead of failing halfway through an edit.
    /// </summary>
    public IReadOnlyList<string> Validate() =>
        Steps.Where(s => CommandRegistry.ById(s.CommandId) is null)
             .Select(s => $"unknown command '{s.CommandId}'")
             .ToList();

    public string Announce() =>
        $"{Name}, {Steps.Count} step{(Steps.Count == 1 ? "" : "s")}" +
        (Description is null ? string.Empty : $". {Description}");
}

public sealed class WorkflowStep
{
    public required string CommandId { get; set; }

    /// <summary>
    /// Arguments the command would otherwise prompt for. A step with its
    /// arguments filled in runs silently; one without still prompts, which is
    /// how a workflow can be partly parameterised.
    /// </summary>
    public Dictionary<string, string> Arguments { get; set; } = [];
}
