using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Serialization;

/// <summary>Reads and writes <c>project.json</c>, the canonical document.</summary>
public static class ProjectJson
{
    public const string FileName = "project.json";

    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        options.Converters.Add(new StableIdJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialise(Project project) =>
        JsonSerializer.Serialize(project, Options);

    public static Project Deserialise(string json)
    {
        var project = JsonSerializer.Deserialize<Project>(json, Options)
                      ?? throw new InvalidDataException("project.json is empty.");

        if (project.SchemaVersion > Project.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"project.json is schema version {project.SchemaVersion}; this build understands " +
                $"up to {Project.CurrentSchemaVersion}.");
        }

        return project;
    }

    public static async Task SaveAsync(Project project, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);

        // Write to a temp file and move, so a crash mid-save cannot leave a
        // half-written project behind.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Serialise(project), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);

        project.RootPath = directory;
    }

    public static async Task<Project> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, FileName);
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var project = Deserialise(json);
        project.RootPath = directory;
        return project;
    }
}
