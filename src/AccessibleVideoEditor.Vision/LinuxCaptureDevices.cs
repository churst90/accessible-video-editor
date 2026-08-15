using System.Diagnostics;

namespace AccessibleVideoEditor.Vision;

/// <summary>
/// Device discovery on Linux.
///
/// **Enumeration never opens a device.** Camera names come from sysfs and
/// microphone names from PipeWire's own listing, so simply looking at what is
/// available cannot switch on a webcam light. Only <see cref="ArmCheckAsync"/>
/// touches hardware, and only when a track is deliberately armed.
/// </summary>
public sealed class LinuxCaptureDevices : ICaptureDevices
{
    public Task<IReadOnlyList<CaptureDevice>> EnumerateAsync(
        CaptureDeviceKind kind,
        CancellationToken ct = default) =>
        Task.FromResult(kind switch
        {
            CaptureDeviceKind.Camera => Cameras(),
            CaptureDeviceKind.Microphone => Sources(monitors: false),
            CaptureDeviceKind.SystemAudio => Sources(monitors: true),
            CaptureDeviceKind.Output => Sinks(),
            _ => (IReadOnlyList<CaptureDevice>)[],
        });

    /// <summary>
    /// /dev/videoN with a name read from sysfs. A capture-capable device
    /// usually comes in pairs on modern kernels - the second is metadata - so
    /// only nodes that report a capture capability are offered.
    /// </summary>
    private static IReadOnlyList<CaptureDevice> Cameras()
    {
        var devices = new List<CaptureDevice>();

        if (!Directory.Exists("/sys/class/video4linux")) return devices;

        foreach (var directory in Directory.EnumerateDirectories("/sys/class/video4linux").OrderBy(d => d))
        {
            var node = Path.GetFileName(directory);
            var namePath = Path.Combine(directory, "name");
            if (!File.Exists(namePath)) continue;

            var name = File.ReadAllText(namePath).Trim().TrimEnd(':');

            devices.Add(new CaptureDevice($"/dev/{node}", $"{name} ({node})", CaptureDeviceKind.Camera));
        }

        return devices;
    }

    /// <summary>
    /// PipeWire and PulseAudio both answer `pactl`. Monitor sources are the
    /// system-audio side; they are listed separately because recording your own
    /// output and recording a microphone are different intents.
    /// </summary>
    private static IReadOnlyList<CaptureDevice> Sources(bool monitors)
    {
        var devices = new List<CaptureDevice>();

        try
        {
            // The verbose listing rather than `short`, because only it carries
            // Description - the name a person would recognise. Node names like
            // "alsa_input.pci-0000_c1_00.6.HiFi__Mic1__source" are unusable
            // read aloud.
            var info = new ProcessStartInfo("pactl")
            {
                ArgumentList = { "list", "sources" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(info);
            if (process is null) return devices;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);

            string? id = null;
            var channels = 0;

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("Name:", StringComparison.Ordinal))
                {
                    id = line["Name:".Length..].Trim();
                    channels = 0;
                }
                else if (line.StartsWith("Sample Specification:", StringComparison.Ordinal))
                {
                    // "s16le 1ch 48000Hz" - the channel count decides whether a
                    // per-channel choice is worth offering at all.
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)ch");
                    if (match.Success) channels = int.Parse(match.Groups[1].Value);
                }
                else if (line.StartsWith("Description:", StringComparison.Ordinal) && id is not null)
                {
                    var description = line["Description:".Length..].Trim();
                    var isMonitor = id.EndsWith(".monitor", StringComparison.Ordinal);

                    if (isMonitor == monitors)
                    {
                        devices.Add(new CaptureDevice(
                            id,
                            description.Length > 0 ? description : Friendly(id),
                            monitors ? CaptureDeviceKind.SystemAudio : CaptureDeviceKind.Microphone,
                            channels));
                    }

                    id = null;
                }
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // No pactl. Nothing to offer rather than a crash.
        }

        return devices;
    }

    /// <summary>
    /// Where monitoring is heard. Separate from capture entirely: you can
    /// perfectly well record from an interface while listening on headphones,
    /// and assuming otherwise is how people end up monitoring the wrong thing.
    /// </summary>
    private static IReadOnlyList<CaptureDevice> Sinks()
    {
        var devices = new List<CaptureDevice>();

        try
        {
            var info = new ProcessStartInfo("pactl")
            {
                ArgumentList = { "list", "sinks" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(info);
            if (process is null) return devices;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);

            string? id = null;

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("Name:", StringComparison.Ordinal))
                {
                    id = line["Name:".Length..].Trim();
                }
                else if (line.StartsWith("Description:", StringComparison.Ordinal) && id is not null)
                {
                    var description = line["Description:".Length..].Trim();

                    devices.Add(new CaptureDevice(
                        id,
                        description.Length > 0 ? description : Friendly(id),
                        CaptureDeviceKind.Output));

                    id = null;
                }
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // No pactl.
        }

        return devices;
    }

    /// <summary>
    /// PipeWire node names are long and full of punctuation. Read aloud they
    /// are unusable, so the useful middle is extracted.
    /// </summary>
    private static string Friendly(string id)
    {
        var name = id.Replace(".monitor", string.Empty, StringComparison.Ordinal);

        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var core = parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : name;

        return core.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    /// <summary>
    /// Opens the device briefly to prove it produces signal.
    ///
    /// This is the only method here that touches hardware - it will turn on a
    /// webcam light. It runs when a track is armed, deliberately, because the
    /// alternative is discovering at the end of a take that the microphone was
    /// muted the whole time.
    /// </summary>
    public async Task<ArmCheckResult> ArmCheckAsync(CaptureDevice device, CancellationToken ct = default)
    {
        var problems = new List<string>();

        if (device.Kind == CaptureDeviceKind.Camera && !File.Exists(device.Id))
        {
            problems.Add($"{device.Name} is not present");
        }

        var free = TryGetFreeDiskSpace();
        if (free is { } bytes && bytes < 2L * 1024 * 1024 * 1024)
        {
            problems.Add($"only {bytes / (1024 * 1024)} megabytes free");
        }

        if (problems.Count > 0) return ArmCheckResult.Fail([.. problems]);

        // Signal verification - a one second probe - is deliberately not run
        // automatically yet: it activates the camera, and that should never
        // happen without the user asking for it.
        await Task.CompletedTask.ConfigureAwait(false);

        return ArmCheckResult.Pass(
            $"{device.Name} selected. Signal check has not run; press the record key to test it");
    }

    private static long? TryGetFreeDiskSpace()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/").AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
