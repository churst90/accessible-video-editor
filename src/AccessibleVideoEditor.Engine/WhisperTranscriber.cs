using System.Diagnostics;
using System.Text.Json;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Drives the existing Whisper setup rather than replacing it: the venv at
/// <c>~/voice/venv</c> with <c>large-v3-turbo</c> cached does roughly 20x
/// realtime on this machine, and swapping to whisper.cpp buys nothing until
/// somebody else has to install this.
/// </summary>
public sealed class WhisperTranscriber
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;

    public WhisperTranscriber(string? pythonPath = null, string? scriptPath = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _pythonPath = pythonPath ?? Path.Combine(home, "voice", "venv", "bin", "python");
        _scriptPath = scriptPath ?? Path.Combine(
            home, ".claude", "skills", "video-edit", "scripts", "whisper_run.py");
    }

    public bool IsAvailable => File.Exists(_pythonPath) && File.Exists(_scriptPath);

    /// <summary>
    /// Produces word-level spans. Words are what make word-granularity
    /// navigation and split-on-word-boundary possible, so the word timings are
    /// kept rather than only the sentence ranges.
    /// </summary>
    public async Task<IReadOnlyList<SpanElement>> TranscribeAsync(
        Source source,
        string workDirectory,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                $"Whisper not found. Expected {_pythonPath} and {_scriptPath}.");
        }

        Directory.CreateDirectory(workDirectory);
        var outputPath = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(source.Path) + ".words.json");

        var info = new ProcessStartInfo(_pythonPath)
        {
            ArgumentList = { _scriptPath, source.Path, "--out", outputPath },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("Could not start Whisper.");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Whisper failed: {error.Trim()}");
        }

        source.TranscriptPath = outputPath;
        return Parse(await File.ReadAllTextAsync(outputPath, ct).ConfigureAwait(false), source.Id);
    }

    internal static IReadOnlyList<SpanElement> Parse(string json, SourceId source)
    {
        using var document = JsonDocument.Parse(json);
        var spans = new List<SpanElement>();

        if (!document.RootElement.TryGetProperty("segments", out var segments)) return spans;

        foreach (var segment in segments.EnumerateArray())
        {
            var words = new List<Word>();

            if (segment.TryGetProperty("words", out var wordArray))
            {
                words.AddRange(wordArray.EnumerateArray().Select(w => new Word(
                    w.TryGetProperty("word", out var text) ? (text.GetString() ?? string.Empty).Trim() : string.Empty,
                    w.TryGetProperty("start", out var start) ? start.GetDouble() : 0,
                    w.TryGetProperty("end", out var end) ? end.GetDouble() : 0)));
            }

            spans.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source,
                SourceIn = segment.TryGetProperty("start", out var s) ? s.GetDouble() : 0,
                SourceOut = segment.TryGetProperty("end", out var e) ? e.GetDouble() : 0,
                Text = segment.TryGetProperty("text", out var t) ? (t.GetString() ?? string.Empty).Trim() : string.Empty,
                Words = words,
            });
        }

        return spans;
    }
}
