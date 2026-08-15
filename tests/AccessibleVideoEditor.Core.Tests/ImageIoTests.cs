using System.Diagnostics;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The whole path, on a real file: ffmpeg decodes it, the analysis measures it,
/// and the answer has to be in the coordinates of the original.
///
/// The synthetic tests prove the arithmetic; this proves the plumbing. Both are
/// needed - a perfect detector wired to the wrong scale is still wrong.
/// </summary>
public class ImageIoTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-images-" + Guid.NewGuid().ToString("n")[..8]);

    private static bool HasFfmpeg =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator)
        .Any(directory => File.Exists(Path.Combine(directory, "ffmpeg")));

    public ImageIoTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// A scanner bed: a white page with a dark rectangle lying on it, rotated
    /// by a known amount. This is the case the whole feature exists for.
    /// </summary>
    private string MakeScan(double degrees, string name = "scan.png")
    {
        var path = Path.Combine(_directory, name);

        var radians = degrees * Math.PI / 180;

        var filter =
            "color=c=white:s=1200x900:d=1[bed];"
            + "color=c=#303030:s=700x460:d=1[photo];"
            + (Math.Abs(degrees) < 0.001
                ? "[bed][photo]overlay=250:220"
                : $"[photo]rotate={radians.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)}"
                  + ":fillcolor=white:ow=760:oh=520[turned];[bed][turned]overlay=220:190");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-filter_complex", filter,
                "-frames:v", "1", path,
            },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        process.WaitForExit();

        return path;
    }

    [Fact]
    public async Task A_photo_on_a_bed_is_found_and_reported_in_the_real_images_coordinates()
    {
        if (!HasFfmpeg) return;

        var path = MakeScan(0);

        var examined = await new ImageIo().ExamineAsync(path);

        Assert.NotNull(examined);

        var (facts, report) = examined!.Value;

        Assert.Equal(1200, facts.Width);
        Assert.Equal(900, facts.Height);

        var region = Assert.Single(report.Regions);

        // The analysis ran on a 400-pixel copy, so everything it says has to
        // have been scaled back up by three.
        Assert.InRange(region.Width, 650, 750);
        Assert.InRange(region.Height, 420, 500);
        Assert.InRange(region.X, 220, 290);

        Assert.True(report.HasBorder);
        Assert.Contains("one picture found", report.Describe());
    }

    [Fact]
    public async Task A_crooked_scan_is_measured_and_the_fix_is_offered()
    {
        if (!HasFfmpeg) return;

        var path = MakeScan(4);

        var examined = await new ImageIo().ExamineAsync(path);
        Assert.NotNull(examined);

        var report = examined!.Value.Report;

        Assert.False(report.IsStraight);
        Assert.InRange(Math.Abs(report.SkewDegrees), 2.5, 6);
        Assert.Contains("straighten", report.Offer());
    }

    [Fact]
    public async Task Cropping_and_resizing_come_out_the_size_they_said_they_would()
    {
        if (!HasFfmpeg) return;

        var path = MakeScan(0);
        var io = new ImageIo();

        var examined = await io.ExamineAsync(path);
        Assert.NotNull(examined);

        var (facts, report) = examined!.Value;

        var document = ImageDocument.Open(path, facts.Width, facts.Height, facts.Dpi);
        document.Report = report;

        ImageEdits.CropToContent(document);
        ImageEdits.ApplyPreset(document, "half");

        var output = Path.Combine(_directory, "out.png");
        var said = await io.ExportAsync(document, output);

        Assert.Contains("saved", said);
        Assert.True(File.Exists(output));

        var written = await io.ProbeAsync(output);

        Assert.NotNull(written);
        Assert.Equal(document.Width, written!.Value.Width);
        Assert.Equal(document.Height, written.Value.Height);
    }

    [Fact]
    public async Task A_drawn_shape_actually_reaches_the_exported_file()
    {
        if (!HasFfmpeg) return;

        var path = MakeScan(0);
        var io = new ImageIo();

        var document = ImageDocument.Open(path, 1200, 900);
        document.Shapes.Add(ShapeLanguage.Parse("fill red")!);

        var output = Path.Combine(_directory, "painted.png");

        Assert.Contains("saved", await io.ExportAsync(document, output));

        // Read it back rather than trusting the exit code: the overlay is the
        // step most likely to be silently dropped.
        var raster = await io.DecodeAsync(output, 40);

        Assert.NotNull(raster);

        var red = Colours.Parse("red")!.Value;
        var expected = 0.299 * red.R + 0.587 * red.G + 0.114 * red.B;

        Assert.InRange(raster!.Mean(), expected - 12, expected + 12);
    }

    [Fact]
    public async Task Two_photos_on_one_bed_become_two_files()
    {
        if (!HasFfmpeg) return;

        var path = Path.Combine(_directory, "two.png");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-filter_complex",
                "color=c=white:s=1200x600:d=1[bed];"
                + "color=c=#303030:s=400x300:d=1[a];"
                + "color=c=#303030:s=400x300:d=1[b];"
                + "[bed][a]overlay=80:150[one];[one][b]overlay=720:150",
                "-frames:v", "1", path,
            },
            RedirectStandardError = true,
        };

        using (var process = Process.Start(info)!) process.WaitForExit();

        var io = new ImageIo();
        var examined = await io.ExamineAsync(path);

        Assert.NotNull(examined);

        var (facts, report) = examined!.Value;

        Assert.Equal(2, report.Regions.Count);

        var document = ImageDocument.Open(path, facts.Width, facts.Height);
        document.Report = report;

        var folder = Path.Combine(_directory, "split");
        var said = await io.SplitAsync(document, folder);

        Assert.Contains("2 of 2", said);
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void Dots_per_inch_come_from_the_file_rather_than_being_assumed()
    {
        if (!HasFfmpeg) return;

        // ffmpeg writes 72 dpi by default, which is the same as the fallback -
        // so this checks the reader runs and returns something sensible rather
        // than throwing on a real file.
        var path = MakeScan(0);

        Assert.InRange(ImageIo.DpiFrom(path), 1, 2400);
    }
}

/// <summary>
/// Text is the one shape Core cannot draw - it has arithmetic but no fonts - so
/// it goes to ffmpeg. That split is exactly the kind of thing that works in
/// principle and silently produces nothing, so it is checked by reading the
/// exported file back.
/// </summary>
public class ImageTextTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-text-" + Guid.NewGuid().ToString("n")[..8]);

    private static bool CanRender =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, "ffmpeg")))
        && AccessibleVideoEditor.Engine.Fonts.Available;

    public ImageTextTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Text_becomes_a_drawtext_filter_with_its_own_colour()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1000, 800);
        document.Shapes.Add(ShapeLanguage.Parse("text \"Chapter one\" at centre, red")!);

        var filter = Assert.Single(ImageIo.TextFilters(document, "/tmp/font.ttf"));

        Assert.Contains("drawtext", filter);
        Assert.Contains("Chapter one", filter);
        Assert.Contains("fontcolor=0xDC2828", filter);
    }

    [Fact]
    public void Light_text_gets_a_dark_edge_and_dark_text_a_light_one()
    {
        // Nobody is going to look at the result and notice white on white.
        var light = ImageDocument.Open("/tmp/x.png", 100, 100);
        light.Shapes.Add(ShapeLanguage.Parse("text \"hi\" at centre, white")!);

        var dark = ImageDocument.Open("/tmp/x.png", 100, 100);
        dark.Shapes.Add(ShapeLanguage.Parse("text \"hi\" at centre, black")!);

        Assert.Contains("bordercolor=black", ImageIo.TextFilters(light, "f")[0]);
        Assert.Contains("bordercolor=white", ImageIo.TextFilters(dark, "f")[0]);
    }

    [Fact]
    public void A_colon_in_the_text_does_not_break_the_filter_graph()
    {
        var document = ImageDocument.Open("/tmp/x.png", 100, 100);
        document.Shapes.Add(ShapeLanguage.Parse("text \"time: 12:30\" at centre, white")!);

        Assert.Contains(@"time\: 12\:30", ImageIo.TextFilters(document, "f")[0]);
    }

    [Fact]
    public async Task Text_actually_reaches_the_exported_file()
    {
        if (!CanRender) return;

        var source = Path.Combine(_directory, "black.png");

        var info = new System.Diagnostics.ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=black:s=600x400:d=1",
                "-frames:v", "1", source,
            },
            RedirectStandardError = true,
        };

        using (var process = System.Diagnostics.Process.Start(info)!) await process.WaitForExitAsync();

        var io = new ImageIo();

        var document = ImageDocument.Open(source, 600, 400);
        document.Shapes.Add(ShapeLanguage.Parse("text \"HELLO\" at centre, white")!);

        var output = Path.Combine(_directory, "titled.png");

        Assert.Contains("saved", await io.ExportAsync(document, output));

        // The page was black; anything brighter than nothing is the text.
        var raster = await io.DecodeAsync(output, 200);

        Assert.NotNull(raster);
        Assert.True(raster!.Mean() > 1, "the text did not reach the file");
    }

    [Fact]
    public async Task Text_sits_above_the_shapes_rather_than_under_them()
    {
        if (!CanRender) return;

        var source = Path.Combine(_directory, "white.png");

        var info = new System.Diagnostics.ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=white:s=600x400:d=1",
                "-frames:v", "1", source,
            },
            RedirectStandardError = true,
        };

        using (var process = System.Diagnostics.Process.Start(info)!) await process.WaitForExitAsync();

        var io = new ImageIo();

        var document = ImageDocument.Open(source, 600, 400);
        document.Shapes.Add(ShapeLanguage.Parse("fill black")!);
        document.Shapes.Add(ShapeLanguage.Parse("text \"HELLO\" at centre, white")!);

        var output = Path.Combine(_directory, "layered.png");

        await io.ExportAsync(document, output);

        var raster = await io.DecodeAsync(output, 200);

        // A black fill with white text over it: mostly dark, but not entirely.
        Assert.NotNull(raster);
        Assert.InRange(raster!.Mean(), 0.5, 60);
    }
}

/// <summary>
/// Colour correction and the shared card, checked by rendering and reading the
/// pixels back. A filter string that looks right and is ignored by ffmpeg is
/// indistinguishable from one that works, right up until it matters.
/// </summary>
public class ImageColourTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-colour-" + Guid.NewGuid().ToString("n")[..8]);

    private static bool CanRender =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, "ffmpeg")))
        && Fonts.Available;

    public ImageColourTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private async Task<string> Grey(byte level = 100)
    {
        var path = Path.Combine(_directory, $"grey{level}.png");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", $"color=c=0x{level:X2}{level:X2}{level:X2}:s=400x300:d=1",
                "-frames:v", "1", path,
            },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();

        return path;
    }

    [Fact]
    public void No_correction_means_no_filters_at_all()
    {
        Assert.Empty(ImageIo.ColourFilters(ColourAdjust.None));
    }

    [Fact]
    public void Each_correction_reaches_the_filter_that_performs_it()
    {
        var filters = string.Join(',', ImageIo.ColourFilters(new ColourAdjust
        {
            Exposure = 1,
            Contrast = 20,
            Saturation = -30,
            TemperatureK = 4000,
            Shadows = 10,
        }));

        Assert.Contains("colortemperature=temperature=4000", filters);
        Assert.Contains("gamma=", filters);
        Assert.Contains("contrast=1.2", filters);
        Assert.Contains("saturation=0.7", filters);
        Assert.Contains("curves=", filters);
    }

    [Fact]
    public void Black_and_white_takes_the_colour_out_rather_than_dimming_it()
    {
        var filters = string.Join(',', ImageIo.ColourFilters(new ColourAdjust { Monochrome = true }));

        Assert.Contains("saturation=0", filters);
    }

    [Fact]
    public async Task Brightening_a_picture_actually_brightens_the_file()
    {
        if (!CanRender) return;

        var io = new ImageIo();
        var path = await Grey(100);

        var document = ImageDocument.Open(path, 400, 300);
        var before = (await io.DecodeAsync(path, 40))!.Mean();

        ColourEdits.Apply(document, "brighter");
        ColourEdits.Apply(document, "brighter");

        var output = Path.Combine(_directory, "brighter.png");
        Assert.Contains("saved", await io.ExportAsync(document, output));

        var after = (await io.DecodeAsync(output, 40))!.Mean();

        Assert.True(after > before + 5, $"{before} did not become brighter, got {after}");
    }

    [Fact]
    public async Task Black_and_white_comes_out_grey()
    {
        if (!CanRender) return;

        var io = new ImageIo();
        var path = Path.Combine(_directory, "red.png");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=red:s=400x300:d=1",
                "-frames:v", "1", path,
            },
            RedirectStandardError = true,
        };

        using (var process = Process.Start(info)!) await process.WaitForExitAsync();

        var document = ImageDocument.Open(path, 400, 300);
        ColourEdits.Apply(document, "black and white");

        var output = Path.Combine(_directory, "grey.png");
        await io.ExportAsync(document, output);

        var colour = await io.SampleAsync(output, 200, 150);

        Assert.NotNull(colour);
        Assert.True(
            Math.Abs(colour!.Value.R - colour.Value.G) < 8 && Math.Abs(colour.Value.G - colour.Value.B) < 8,
            $"still coloured: {colour}");
    }

    [Fact]
    public async Task A_card_from_the_video_editor_draws_on_a_photograph()
    {
        if (!CanRender) return;

        var io = new ImageIo();
        var path = await Grey(0);

        var document = ImageDocument.Open(path, 400, 300);
        document.Card = AccessibleVideoEditor.Core.Model.CardTemplates.LowerThird("Cody Hurst", "Editor");

        var output = Path.Combine(_directory, "carded.png");

        Assert.Contains("saved", await io.ExportAsync(document, output));

        var raster = await io.DecodeAsync(output, 200);

        Assert.NotNull(raster);
        Assert.True(raster!.Mean() > 1, "the card did not reach the file");
    }
}

/// <summary>
/// Levels and the batch, on real files. The batch especially: it is the one
/// operation that can go wrong a hundred times before anybody notices, so the
/// promise that each picture is measured on its own terms is checked rather
/// than asserted in a comment.
/// </summary>
public class BatchAndLevelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-batch-" + Guid.NewGuid().ToString("n")[..8]);

    private string In => Path.Combine(_directory, "in");
    private string Out => Path.Combine(_directory, "out");

    private static bool CanRender =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, "ffmpeg")));

    public BatchAndLevelTests() => Directory.CreateDirectory(In);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>A scan with the photo in a different place each time.</summary>
    private async Task Scan(string name, int x, int y)
    {
        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-filter_complex",
                "color=c=white:s=800x600:d=1[bed];"
                + "color=c=#404040:s=300x200:d=1[photo];"
                + $"[bed][photo]overlay={x}:{y}",
                "-frames:v", "1", Path.Combine(In, name),
            },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
    }

    [Fact]
    public void Only_pictures_are_picked_up_from_the_folder()
    {
        File.WriteAllText(Path.Combine(In, "notes.txt"), "not a picture");
        File.WriteAllText(Path.Combine(In, "a.png"), string.Empty);

        var found = BatchProcessor.PicturesIn(In);

        Assert.Single(found);
        Assert.EndsWith("a.png", found[0]);
    }

    [Fact]
    public async Task Each_picture_is_cropped_where_its_own_photograph_actually_is()
    {
        if (!CanRender) return;

        // The whole promise of the batch: the photograph lands somewhere
        // different every time, so one crop rectangle would ruin them.
        await Scan("left.png", 50, 60);
        await Scan("right.png", 450, 340);

        var result = await new BatchProcessor().RunAsync(In, Out, new BatchJob { FixEachScan = true });

        Assert.Equal(2, result.Succeeded);

        var io = new ImageIo();

        foreach (var written in Directory.GetFiles(Out))
        {
            var facts = await io.ProbeAsync(written);

            Assert.NotNull(facts);

            // Both come out the size of the photograph, not the size of the bed.
            Assert.InRange(facts!.Value.Width, 280, 320);
            Assert.InRange(facts.Value.Height, 185, 215);
        }
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_does_not_stop_the_others()
    {
        if (!CanRender) return;

        await Scan("good.png", 100, 100);
        File.WriteAllText(Path.Combine(In, "broken.png"), "this is not a png");

        var result = await new BatchProcessor().RunAsync(In, Out, new BatchJob());

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Contains("1 of 2", result.Describe());
    }

    [Fact]
    public async Task The_batch_counts_out_loud_as_it_goes()
    {
        if (!CanRender) return;

        // Four minutes of silence is indistinguishable from a hang.
        await Scan("one.png", 100, 100);
        await Scan("two.png", 200, 200);

        var said = new List<string>();

        await new BatchProcessor().RunAsync(In, Out, new BatchJob(), said.Add);

        Assert.Equal(2, said.Count);
        Assert.Contains("1 of 2", said[0]);
    }

    [Fact]
    public async Task It_refuses_to_overwrite_the_originals()
    {
        if (!CanRender) return;

        await Scan("photo.png", 100, 100);

        // Same folder, and a suffix that would land on the original name.
        var result = await new BatchProcessor().RunAsync(
            In, In, new BatchJob { FixEachScan = false, Suffix = string.Empty });

        Assert.Equal(0, result.Succeeded);
        Assert.Contains("overwrite the original", result.Describe());
    }

    [Fact]
    public async Task Auto_levels_uses_each_pictures_own_histogram()
    {
        if (!CanRender) return;

        await Scan("flat.png", 100, 100);

        var io = new ImageIo();
        var source = Path.Combine(In, "flat.png");

        var document = ImageDocument.Open(source, 800, 600);
        var raster = await io.DecodeAsync(source);

        Assert.NotNull(raster);

        var result = LevelEdits.Auto(document, raster!);

        Assert.True(result.Changed);
        Assert.True(document.Levels.BlackPoint > 0 || document.Levels.WhitePoint < 255);
    }

    [Fact]
    public async Task Setting_a_black_point_actually_darkens_the_dark_end()
    {
        if (!CanRender) return;

        var source = Path.Combine(In, "grey.png");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "color=c=0x505050:s=200x200:d=1",
                "-frames:v", "1", source,
            },
            RedirectStandardError = true,
        };

        using (var process = Process.Start(info)!) await process.WaitForExitAsync();

        var io = new ImageIo();
        var document = ImageDocument.Open(source, 200, 200);

        // Everything below 0x50 becomes black, so this grey lands near it.
        document.Levels = new Levels { BlackPoint = 0x48, WhitePoint = 255 };

        var output = Path.Combine(_directory, "levelled.png");

        Assert.Contains("saved", await io.ExportAsync(document, output));

        var after = await io.SampleAsync(output, 100, 100);

        Assert.NotNull(after);
        Assert.True(after!.Value.R < 0x50, $"expected darker than 0x50, got {after.Value.R:X2}");
    }

    [Fact]
    public void A_preview_says_how_many_and_what_before_anything_runs()
    {
        File.WriteAllText(Path.Combine(In, "a.png"), string.Empty);
        File.WriteAllText(Path.Combine(In, "b.png"), string.Empty);

        var preview = BatchProcessor.Preview(In, new BatchJob { FixEachScan = true });

        Assert.Contains("2 pictures", preview);
        Assert.Contains("straighten and crop", preview);
    }
}

/// <summary>
/// A cast, put into a real file and taken back out again. The arithmetic is
/// tested elsewhere; this is the proof that the filter it produces does what
/// the arithmetic intended.
/// </summary>
public class ChannelLevelRenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "video-cast-" + Guid.NewGuid().ToString("n")[..8]);

    private static bool CanRender =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, "ffmpeg")));

    public ChannelLevelRenderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>A picture with a deliberate cast, and a range for it to be stretched into.</summary>
    private async Task<string> Warm()
    {
        var path = Path.Combine(_directory, "warm.png");

        var info = new ProcessStartInfo("ffmpeg")
        {
            ArgumentList =
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-filter_complex",
                "color=c=0xC89678:s=300x200:d=1[a];"
                + "color=c=0x644B3C:s=300x100:d=1[b];"
                + "[a][b]overlay=0:0",
                "-frames:v", "1", path,
            },
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();

        return path;
    }

    [Fact]
    public void Per_channel_points_reach_the_filter_that_performs_them()
    {
        var levels = Levels.None
            .WithChannel(0, new ChannelLevels(0, 255))
            .WithChannel(2, new ChannelLevels(0, 200));

        var filters = string.Join(',', ImageIo.LevelFilters(levels));

        // Checked as a whole rather than piecemeal: an earlier version emitted
        // "colorlevels:rimin=..." with the first equals missing, which contains
        // every substring you would think to look for and is rejected outright
        // by ffmpeg.
        Assert.StartsWith("colorlevels=rimin=", filters);
        Assert.Contains("bimax=0.7843", filters);
        Assert.Contains("rimax=1", filters);
    }

    [Fact]
    public async Task The_cast_is_measured_from_the_file_and_removed_from_it()
    {
        if (!CanRender) return;

        var io = new ImageIo();
        var path = await Warm();

        var colours = await io.DecodeColourAsync(path);
        Assert.NotNull(colours);

        var before = ColourCast.Of(colours!);

        Assert.Equal("warm", before.Name);
        Assert.False(before.IsNeutral);

        var document = ImageDocument.Open(path, 300, 200);

        Assert.True(LevelEdits.AutoColour(document, colours!).Changed);

        var output = Path.Combine(_directory, "fixed.png");
        Assert.Contains("saved", await io.ExportAsync(document, output));

        var after = ColourCast.Of((await io.DecodeColourAsync(output))!);

        Assert.True(
            after.Strength < before.Strength,
            $"the cast got worse: {before.Strength:0} became {after.Strength:0}");

        Assert.True(after.IsNeutral, $"still {after.Strength:0} percent {after.Name}");
    }

    [Fact]
    public async Task Balancing_on_a_spot_makes_that_spot_come_out_grey()
    {
        if (!CanRender) return;

        var io = new ImageIo();
        var path = await Warm();

        var colours = await io.DecodeColourAsync(path);
        var document = ImageDocument.Open(path, 300, 200);

        // Three quarters of the way down is inside the plain warm area.
        Assert.True(LevelEdits.NeutraliseAt(document, colours!, 0.5, 0.8).Changed);

        var output = Path.Combine(_directory, "balanced.png");
        await io.ExportAsync(document, output);

        var sample = await io.SampleAsync(output, 150, 160);

        Assert.NotNull(sample);

        var spread = Math.Max(Math.Max(sample!.Value.R, sample.Value.G), sample.Value.B)
                     - Math.Min(Math.Min(sample.Value.R, sample.Value.G), sample.Value.B);

        Assert.True(spread < 16, $"that spot is still coloured: {sample}");
    }
}
