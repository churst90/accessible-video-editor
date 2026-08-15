using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Edl;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

// The CLI is a client of the same core the GUI uses, not a separate
// implementation. It exists so the engine stays testable headless, and so the
// Claude skill keeps a command-line surface to drive.

if (args.Length == 0)
{
    Usage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "new" when args.Length >= 2:
        {
            var project = Project.CreateDefault(args.Length >= 3 ? args[2] : Path.GetFileName(args[1]));
            await ProjectJson.SaveAsync(project, args[1]);
            await EdlWriter.ExportAsync(project, args[1]);
            Console.WriteLine($"Created {Path.Combine(args[1], ProjectJson.FileName)}");
            return 0;
        }

        case "export" when args.Length >= 2:
        {
            var project = await ProjectJson.LoadAsync(args[1]);
            await EdlWriter.ExportAsync(project, args[1]);
            Console.WriteLine($"Wrote {Path.Combine(args[1], "edit.md")}");
            return 0;
        }

        case "import" when args.Length >= 2:
        {
            // Reconcile against the existing project so stable IDs survive a
            // hand edit in pluma or a change made by the Claude skill.
            var existing = File.Exists(Path.Combine(args[1], ProjectJson.FileName))
                ? await ProjectJson.LoadAsync(args[1])
                : null;

            var project = await EdlReader.ReadFileAsync(Path.Combine(args[1], "edit.md"), existing);
            await ProjectJson.SaveAsync(project, args[1]);
            Console.WriteLine($"Imported {project.Spine.Count} elements.");
            return 0;
        }

        case "info" when args.Length >= 2:
        {
            var project = await ProjectJson.LoadAsync(args[1]);
            var map = TimelineMap.Build(project);

            Console.WriteLine($"{project.Name} - {Timecode.Format(map.Duration)}");
            Console.WriteLine($"{project.Tracks.Count} tracks, {project.Spine.Count} elements, " +
                              $"{project.Overlays.Count} overlays");

            foreach (var placed in map.Elements)
            {
                Console.WriteLine($"  {Timecode.Format(placed.ProgrammeStart),-13} {placed.Element.Describe()}");
            }

            var holes = project.Holes.ToList();
            if (holes.Count > 0)
            {
                Console.WriteLine($"\n{holes.Count} hole(s) block the master render:");
                foreach (var hole in holes)
                {
                    Console.WriteLine($"  {hole.Describe()}");
                }
            }

            return 0;
        }

        case "demo-edl":
        {
            var demo = AccessibleVideoEditor.Core.Samples.DemoProject.Create();
            var demoMap = TimelineMap.Build(demo);

            Console.WriteLine($"programme duration: {demoMap.Duration:0.###}");
            foreach (var placed in demoMap.Elements)
            {
                Console.WriteLine(
                    $"  {placed.ProgrammeStart,7:0.###} .. {placed.ProgrammeEnd,7:0.###}  " +
                    $"{placed.Element.GetType().Name,-13} media={(placed.Media is null ? "none" : "yes")}");
            }

            Console.WriteLine();
            Console.WriteLine(AccessibleVideoEditor.Playback.MpvEdl.Build(demo, demoMap));
            return 0;
        }

        case "render" when args.Length >= 2:
        {
            var project = await ProjectJson.LoadAsync(args[1]);
            var quality = args.Length > 2 && args[2] == "master"
                ? AccessibleVideoEditor.Engine.RenderQuality.Master
                : AccessibleVideoEditor.Engine.RenderQuality.Draft;

            var progress = new Progress<AccessibleVideoEditor.Engine.RenderProgress>(
                p => Console.WriteLine($"  {p.Stage} {p.Fraction * 100:0}%"));

            var output = await new AccessibleVideoEditor.Engine.FfmpegRenderEngine()
                .RenderAsync(project, quality, progress);

            Console.WriteLine($"wrote {output.Path} ({Timecode.Format(output.Duration)})");
            return 0;
        }

        case "demo-render":
        {
            // Renders the built-in demo project into a scratch directory, so
            // the whole pipeline can be exercised without a real project.
            var demo = AccessibleVideoEditor.Core.Samples.DemoProject.Create();
            demo.RootPath = args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "videoedit-demo-render");

            Directory.CreateDirectory(demo.RootPath);

            var progress = new Progress<AccessibleVideoEditor.Engine.RenderProgress>(
                p => Console.WriteLine($"  {p.Stage} {p.Fraction * 100:0}%"));

            var output = await new AccessibleVideoEditor.Engine.FfmpegRenderEngine()
                .RenderAsync(demo, AccessibleVideoEditor.Engine.RenderQuality.Draft, progress);

            Console.WriteLine($"wrote {output.Path} ({Timecode.Format(output.Duration)})");
            return 0;
        }

        case "beep":
        {
            // Proves the audio device opens and that a tone is audible, without
            // needing the whole application.
            var audio = AccessibleVideoEditor.Audio.SdlAudioOutput.TryOpen();

            if (audio is null)
            {
                Console.Error.WriteLine("audio device could not be opened (SDL2 missing or busy)");
                return 1;
            }

            Console.WriteLine("audio open; playing a rising sweep and every earcon");

            for (var db = -60.0; db <= 0; db += 6)
            {
                audio.Play(AccessibleVideoEditor.Audio.LevelSonifier.PitchFor(db), 0.08);
                await Task.Delay(110);
            }

            await Task.Delay(400);

            foreach (var earcon in Enum.GetValues<AccessibleVideoEditor.Speech.Earcon>())
            {
                Console.WriteLine($"  {earcon}");
                audio.Earcon(earcon);
                await Task.Delay(320);
            }

            await Task.Delay(400);
            audio.Dispose();
            return 0;
        }

        case "devices":
        {
            // Listing never opens a device: camera names come from sysfs and
            // microphones from pactl. Safe to run at any time.
            var devices = new AccessibleVideoEditor.Vision.LinuxCaptureDevices();

            foreach (var kind in new[]
                     {
                         AccessibleVideoEditor.Vision.CaptureDeviceKind.Camera,
                         AccessibleVideoEditor.Vision.CaptureDeviceKind.Microphone,
                         AccessibleVideoEditor.Vision.CaptureDeviceKind.SystemAudio,
                         AccessibleVideoEditor.Vision.CaptureDeviceKind.Output,
                     })
            {
                var found = await devices.EnumerateAsync(kind);
                Console.WriteLine($"\n{kind} ({found.Count})");

                foreach (var device in found)
                {
                    Console.WriteLine($"  {device.Describe()}");
                }
            }

            return 0;
        }

        case "keys":
        {
            foreach (var group in Enum.GetValues<CommandGroup>())
            {
                var commands = CommandRegistry.InGroup(group).ToList();
                if (commands.Count == 0) continue;

                Console.WriteLine($"\n{group}");
                foreach (var command in commands)
                {
                    Console.WriteLine($"  {command.DefaultBinding,-22} {command.Title}");
                }
            }

            return 0;
        }

        default:
            Usage();
            return 1;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

static void Usage()
{
    Console.WriteLine("""
        Accessible Video Editor - accessible video editor

          videocli new <dir> [name]   scaffold a project
          videocli info <dir>         print the timeline
          videocli export <dir>       write edit.md from project.json
          videocli import <dir>       reconcile edit.md back into project.json
          videocli devices            list cameras and microphones
          videocli beep               test the audio device and hear every earcon
          videocli render <dir> [master]   render a draft or master
          videocli demo-render [dir]       render the built-in demo project
          videocli keys               print the default keymap
        """);
}
