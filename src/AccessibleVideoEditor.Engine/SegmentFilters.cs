using System.Globalization;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// Builds the ffmpeg arguments for one segment.
///
/// One segment at a time, into a cache keyed by content, is what makes editing
/// affordable: changing a line re-renders that line, and reordering the video
/// re-renders nothing at all. It is also the only shape that can be tested -
/// a single command for a whole programme is untestable by construction.
/// </summary>
public static class SegmentFilters
{
    /// <summary>
    /// Renders one segment to a standalone file: the right slice of the right
    /// source, retimed, faded and muted as the segment says, normalised to the
    /// project canvas so that concatenating them later cannot fail on a size or
    /// frame rate mismatch.
    /// </summary>
    public static IReadOnlyList<string> Build(
        Project project,
        PlacedElement placed,
        RenderQuality quality,
        string sourcePath,
        string output)
    {
        var settings = project.Settings;
        var height = quality == RenderQuality.Draft ? 540 : settings.CanvasHeight;
        var width = quality == RenderQuality.Draft
            ? (int)Math.Round(height * (double)settings.CanvasWidth / settings.CanvasHeight / 2) * 2
            : settings.CanvasWidth;

        var arguments = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };

        var element = placed.Element;
        var media = placed.Media;

        var still = IsStill(project, placed);

        if (still)
        {
            // A still has one frame and no duration, so it is looped for as
            // long as it is meant to be on screen. Seeking into it would be
            // meaningless.
            arguments.AddRange([
                "-loop", "1", "-framerate", Number(settings.Fps),
                "-t", Number(placed.Duration), "-i", sourcePath,
                "-f", "lavfi", "-i", $"anullsrc=r=48000:cl=stereo:d={Number(placed.Duration)}",
            ]);
        }
        else if (media is { } range)
        {
            // Seeking before the input is the fast path; ffmpeg decodes only
            // what it needs rather than the whole file up to that point.
            arguments.AddRange(["-ss", Number(range.In), "-t", Number(range.Duration), "-i", sourcePath]);
        }
        else
        {
            // Cards, holes and pauses have no media, so one is synthesised.
            arguments.AddRange([
                "-f", "lavfi", "-i", BackgroundSource(element, width, height, settings.Fps, placed.Duration),
                "-f", "lavfi", "-i", $"anullsrc=r=48000:cl=stereo:d={Number(placed.Duration)}",
            ]);
        }

        var video = VideoChain(element, placed, width, height, settings.Fps, still);

        // The programme track's effects apply to the spine; anything on another
        // track is an overlay item and is treated where those are mixed.
        var audio = AudioChain(element, placed, project.ProgrammeTrack.Effects);

        arguments.AddRange(["-vf", video, "-af", audio]);

        arguments.AddRange([
            "-c:v", "libx264",
            "-preset", quality == RenderQuality.Draft ? "veryfast" : "medium",
            "-crf", quality == RenderQuality.Draft ? "28" : "18",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
            "-t", Number(placed.Duration),
            output,
        ]);

        return arguments;
    }

    /// <summary>
    /// A card's background, as a lavfi source. Gradients are drawn with the
    /// gradients filter; everything else is a flat colour.
    /// </summary>
    public static string BackgroundSource(
        SpineElement element,
        int width,
        int height,
        double fps,
        double duration)
    {
        var size = $"{width}x{height}";
        var rate = Number(fps);
        var length = Number(Math.Max(0.04, duration));

        if (element is not CardElement card) return $"color=c=black:s={size}:r={rate}:d={length}";

        var background = card.Composition.Background;

        return background.Kind switch
        {
            BackgroundKind.Gradient =>
                $"gradients=s={size}:r={rate}:d={length}"
                + $":c0={Colour(background.Colour)}:c1={Colour(background.SecondColour)}"
                + $":x0=0:y0=0:{GradientEnd(background.Direction, width, height)}",

            BackgroundKind.Solid => $"color=c={Colour(background.Colour)}:s={size}:r={rate}:d={length}",

            _ => $"color=c=black:s={size}:r={rate}:d={length}",
        };
    }

    private static string GradientEnd(GradientDirection direction, int width, int height) =>
        direction switch
        {
            GradientDirection.Horizontal => $"x1={width}:y1=0",
            GradientDirection.Diagonal => $"x1={width}:y1={height}",
            _ => $"x1=0:y1={height}",
        };

    /// <summary>ffmpeg wants 0x prefixed hex, not the # form a person writes.</summary>
    public static string Colour(string hex)
    {
        var text = hex.Trim().TrimStart('#');

        return text.Length is 6 && text.All(char.IsAsciiHexDigit)
            ? "0x" + text.ToUpperInvariant()
            : "black";
    }

    /// <summary>
    /// Scale to the canvas, pad rather than crop, retime, then fade. Order
    /// matters: fading before the retime would fade at the wrong speed.
    /// </summary>
    /// <summary>True when this segment plays a photograph rather than footage.</summary>
    public static bool IsStill(Project project, PlacedElement placed) =>
        placed.Media is { } media && project.SourceOf(media.Source)?.Kind == SourceKind.Image;

    public static string VideoChain(
        SpineElement element,
        PlacedElement placed,
        int width,
        int height,
        double fps,
        bool still = false)
    {
        var steps = new List<string>();

        if (still && element.KenBurns != KenBurns.None)
        {
            // Oversample first: zoompan works on the scaled image, and zooming
            // a frame that is already at output size makes it visibly soft.
            steps.Add($"scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase");
            steps.Add(KenBurnsFilter(element.KenBurns, placed.Duration, fps, width, height));
        }

        steps.AddRange([
            $"scale={width}:{height}:force_original_aspect_ratio=decrease",
            $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black",
            "setsar=1",
            $"fps={Number(fps)}",
        ]);

        if (Math.Abs(element.Speed - 1.0) > 0.001 && element.Speed > 0)
        {
            steps.Add($"setpts={Number(1 / element.Speed)}*PTS");
        }

        if (element.Hidden) steps.Add("geq=0:128:128");

        if (element.FadeTarget is FadeTarget.Both or FadeTarget.Video)
        {
            if (element.FadeIn > 0) steps.Add($"fade=t=in:st=0:d={Number(element.FadeIn)}");

            if (element.FadeOut > 0)
            {
                var start = Math.Max(0, placed.Duration - element.FadeOut);
                steps.Add($"fade=t=out:st={Number(start)}:d={Number(element.FadeOut)}");
            }
        }

        return string.Join(',', steps);
    }

    /// <summary>
    /// The slow drift over a photograph. Held to a small range - a still that
    /// moves too far reads as a camera move rather than a photograph, which is
    /// the opposite of the intent.
    /// </summary>
    public static string KenBurnsFilter(KenBurns motion, double duration, double fps, int width, int height)
    {
        var frames = Math.Max(1, (int)Math.Round(duration * fps));
        var size = $"{width}x{height}";

        return motion switch
        {
            KenBurns.ZoomIn =>
                $"zoompan=z='min(zoom+0.0008,1.12)':d={frames}:s={size}:fps={Number(fps)}"
                + ":x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'",

            KenBurns.ZoomOut =>
                $"zoompan=z='max(1.12-0.0008*on,1.0)':d={frames}:s={size}:fps={Number(fps)}"
                + ":x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'",

            KenBurns.PanLeft =>
                $"zoompan=z=1.1:d={frames}:s={size}:fps={Number(fps)}"
                + $":x='(iw-iw/zoom)*(1-on/{frames})':y='ih/2-(ih/zoom/2)'",

            _ =>
                $"zoompan=z=1.1:d={frames}:s={size}:fps={Number(fps)}"
                + $":x='(iw-iw/zoom)*(on/{frames})':y='ih/2-(ih/zoom/2)'",
        };
    }

    /// <summary>
    /// Resample, retime, mute and fade. <c>atempo</c> is limited to 0.5-2.0 per
    /// instance, so extreme retimes are chained rather than clamped - clamping
    /// would silently desynchronise the sound from the picture.
    /// </summary>
    public static string AudioChain(
        SpineElement element,
        PlacedElement placed,
        IReadOnlyList<AudioEffect>? trackEffects = null)
    {
        var steps = new List<string> { "aresample=48000", "aformat=channel_layouts=stereo" };

        // Track effects first, then the segment's own. The microphone is fixed
        // before the take is: correcting one sentence against uncorrected room
        // tone would mean re-doing it once the track was treated.
        if (trackEffects is { Count: > 0 } && AudioEffectFilters.Build(trackEffects) is { Length: > 0 } onTrack)
        {
            steps.Add(onTrack);
        }

        if (element.Effects.Count > 0 && AudioEffectFilters.Build(element.Effects) is { Length: > 0 } onSegment)
        {
            steps.Add(onSegment);
        }

        if (Math.Abs(element.Speed - 1.0) > 0.001 && element.Speed > 0)
        {
            var remaining = element.Speed;

            while (remaining > 2.0)
            {
                steps.Add("atempo=2.0");
                remaining /= 2.0;
            }

            while (remaining < 0.5)
            {
                steps.Add("atempo=0.5");
                remaining /= 0.5;
            }

            steps.Add($"atempo={Number(remaining)}");
        }

        // Automation before the mute, so muting a segment that ducks still
        // silences it rather than the two fighting.
        if (AutomationFilters.Volume(element.Automation, placed.Duration) is { Length: > 0 } automated)
        {
            steps.Add(automated);
        }

        if (element.Muted) steps.Add("volume=0");

        if (element.FadeTarget is FadeTarget.Both or FadeTarget.Audio)
        {
            if (element.FadeIn > 0) steps.Add($"afade=t=in:st=0:d={Number(element.FadeIn)}");

            if (element.FadeOut > 0)
            {
                var start = Math.Max(0, placed.Duration - element.FadeOut);
                steps.Add($"afade=t=out:st={Number(start)}:d={Number(element.FadeOut)}");
            }
        }

        return string.Join(',', steps);
    }

    internal static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
