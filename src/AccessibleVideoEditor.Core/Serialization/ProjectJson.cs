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

        await SaveToAsync(project, Path.Combine(directory, FileName), cancellationToken).ConfigureAwait(false);

        project.RootPath = directory;
    }

    /// <summary>
    /// Writes to a named file rather than to a project directory, for the
    /// autosave beside the project. It deliberately does not set
    /// <see cref="Project.RootPath"/>: a quiet save must not change where the
    /// project thinks it lives.
    /// </summary>
    public static async Task SaveToAsync(Project project, string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        // Write to a temp file and move, so a crash mid-save cannot leave a
        // half-written project behind.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Serialise(project), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Reads a project from a named file, used to open a recovered autosave.
    /// <paramref name="rootPath"/> is the project's home, which is the folder
    /// rather than the file the work happened to be read out of.
    /// </summary>
    public static async Task<Project> LoadFromAsync(
        string path, string rootPath, CancellationToken cancellationToken = default)
    {
        var project = Deserialise(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        project.RootPath = rootPath;

        return project;
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
