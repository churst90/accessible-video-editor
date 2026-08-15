using System.Globalization;
using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Streaming;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// One scene, as ffmpeg arguments.
///
/// A scene is a stack: a black canvas, then each visible source scaled and laid
/// over it in order, then every unmuted audio source mixed together. That is
/// all compositing is, and writing it out as a filtergraph rather than through
/// a compositor library keeps it testable - the arguments are a string an
/// assertion can read.
///
/// The same 3 by 3 placement language the card editor uses decides where a
/// source sits, so "camera, 25 percent, bottom right" means the same thing live
/// as it does in an edit.
/// </summary>
public static class SceneComposer
{
    /// <summary>
    /// Inputs and filtergraph for a scene. <paramref name="devices"/> supplies
    /// the real device paths, which are looked up rather than stored so that
    /// unplugging a camera between streams cannot silently change what is
    /// broadcast.
    /// </summary>
    public static ScenePlan Build(
        StreamSetup setup,
        Scene scene,
        EncoderSettings settings,
        Func<StreamSource, string>? devices = null)
    {
        var arguments = new List<string>();
        var video = new List<string>();
        var audio = new List<string>();

        var width = settings.Width;
        var height = settings.Height;
        var fps = Num(settings.Fps);

        // The canvas is always there, so a scene with nothing showing goes to
        // black rather than failing to start. Black on air is recoverable; a
        // stream that will not start is not.
        arguments.AddRange(["-f", "lavfi", "-i", $"color=c=black:s={width}x{height}:r={fps}"]);
        var index = 1;

        var stack = "[0:v]";
        var step = 0;

        foreach (var reference in scene.Sources)
        {
            if (setup.SourceOf(reference.Source) is not { } source) continue;
            if (!reference.Visible) continue;

            var path = devices?.Invoke(source) ?? source.Path;

            arguments.AddRange(InputFor(source, path, settings));

            if (source.HasPicture)
            {
                var target = Math.Max(2, (int)Math.Round(width * Math.Clamp(reference.Scale, 0.05, 1)) / 2 * 2);
                var (x, y) = Position(reference, width, height, target);

                video.Add($"[{index}:v]scale={target}:-2[v{step}]");
                video.Add($"{stack}[v{step}]overlay={x}:{y}[s{step}]");

                stack = $"[s{step}]";
                step++;
            }

            if (source.HasAudio && !reference.Muted)
            {
                var gain = reference.GainDb;

                audio.Add(Math.Abs(gain) < 0.01
                    ? $"[{index}:a]aresample=48000[a{audio.Count}]"
                    : $"[{index}:a]volume={Num(gain)}dB,aresample=48000[a{audio.Count}]");
            }

            index++;
        }

        var videoOut = stack.Trim('[', ']');

        // Silence rather than no audio track at all: a stream with no audio
        // stream is rejected by some ingests outright, and a silent one is at
        // least diagnosable by ear.
        if (audio.Count == 0)
        {
            arguments.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo"]);
            audio.Add($"[{index}:a]aresample=48000[a0]");
        }

        var mix = audio.Count == 1
            ? "[a0]anull[aout]"
            : string.Join(string.Empty, Enumerable.Range(0, audio.Count).Select(i => $"[a{i}]"))
              + $"amix=inputs={audio.Count}:normalize=0[aout]";

        var filters = video.Concat(audio).Append(mix).ToList();

        return new ScenePlan(arguments, string.Join(';', filters), videoOut, "aout");
    }

    /// <summary>
    /// The input arguments for one source. Looping is the case worth naming:
    /// a song over a static picture is a still looped forever and a track
    /// looped forever, and neither should end the stream when it runs out.
    /// </summary>
    public static IReadOnlyList<string> InputFor(StreamSource source, string path, EncoderSettings settings)
    {
        var fps = Num(settings.Fps);

        return source.Kind switch
        {
            StreamSourceKind.Camera =>
            [
                "-f", "v4l2", "-framerate", fps,
                "-video_size", $"{settings.Width}x{settings.Height}",
                "-i", Fallback(path, "/dev/video0"),
            ],

            StreamSourceKind.Screen =>
            [
                "-f", "x11grab", "-framerate", fps,
                "-video_size", $"{settings.Width}x{settings.Height}",
                "-i", Fallback(path, ":0.0"),
            ],

            StreamSourceKind.Microphone =>
                ["-f", "pulse", "-i", Fallback(path, "default")],

            StreamSourceKind.Image =>
                ["-loop", "1", "-framerate", fps, "-i", path],

            StreamSourceKind.Video or StreamSourceKind.Music =>
                source.Loop
                    ? ["-stream_loop", "-1", "-re", "-i", path]
                    : ["-re", "-i", path],

            StreamSourceKind.Card =>
            [
                "-f", "lavfi", "-i",
                CardSource(source, settings),
            ],

            StreamSourceKind.Text =>
            [
                "-f", "lavfi", "-i",
                $"color=c=black@0:s={settings.Width}x{settings.Height}:r={fps}",
            ],

            _ => ["-f", "lavfi", "-i", $"color=c=black:s={settings.Width}x{settings.Height}:r={fps}"],
        };
    }

    private static string CardSource(StreamSource source, EncoderSettings settings)
    {
        if (source.Card is not { } composition)
        {
            return $"color=c=black:s={settings.Width}x{settings.Height}:r={Num(settings.Fps)}";
        }

        var element = new CardElement
        {
            Id = Ids.NewElement(),
            Length = 1,
            Composition = composition,
        };

        // Reuses the editor's own background builder, so a card looks the same
        // live as it does in the programme.
        return SegmentFilters.BackgroundSource(
            element, settings.Width, settings.Height, settings.Fps, 1);
    }

    /// <summary>
    /// Top-left corner for an overlay, from the placement's anchor point. The
    /// anchor is applied so a source in a corner cell hugs that corner instead
    /// of hanging off the canvas.
    /// </summary>
    public static (int X, int Y) Position(SourceRef reference, int width, int height, int sourceWidth)
    {
        if (reference.Scale >= 0.99) return (0, 0);

        var sourceHeight = (int)Math.Round(sourceWidth * (double)height / width);

        var (nx, ny) = reference.Placement.Resolve();

        var x = nx * width - reference.Placement.Anchor.Horizontal switch
        {
            HorizontalAnchor.Left => 0,
            HorizontalAnchor.Right => sourceWidth,
            _ => sourceWidth / 2.0,
        };

        var y = ny * height - reference.Placement.Anchor.Vertical switch
        {
            VerticalAnchor.Top => 0,
            VerticalAnchor.Bottom => sourceHeight,
            _ => sourceHeight / 2.0,
        };

        return (
            (int)Math.Round(Math.Clamp(x, 0, Math.Max(0, width - sourceWidth))),
            (int)Math.Round(Math.Clamp(y, 0, Math.Max(0, height - sourceHeight))));
    }

    private static string Fallback(string value, string fallback) =>
        value.Length == 0 ? fallback : value;

    internal static string Num(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed record ScenePlan(
    IReadOnlyList<string> Inputs,
    string FilterComplex,
    string VideoLabel,
    string AudioLabel);
