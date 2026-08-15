using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Vision;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The scanner bed, built in four lines and asserted on.
///
/// This is why the analysis works on a raster in Core rather than on a file in
/// the engine: "a 4 by 6 photo dropped on the bed sideways with white round it"
/// is a thing a test can construct, so the detection can be checked rather than
/// eyeballed. Analysis that can only be verified by looking at it is analysis a
/// blind person cannot trust.
/// </summary>
public class ScannerBedTests
{
    /// <summary>A white bed with dark rectangles on it, which is what a scan is.</summary>
    private static Raster Bed(int width, int height, params PixelRect[] photos)
    {
        var raster = Raster.Blank(width, height, 250);

        foreach (var photo in photos)
        {
            raster.Fill(photo.X, photo.Y, photo.Width, photo.Height, 90);
        }

        return raster;
    }

    [Fact]
    public void One_photo_on_a_bed_is_found_where_it_actually_is()
    {
        var raster = Bed(400, 300, new PixelRect(60, 40, 240, 160));

        var region = Assert.Single(ImageAnalysis.DetectRegions(raster));

        Assert.InRange(region.X, 58, 62);
        Assert.InRange(region.Y, 38, 42);
        Assert.InRange(region.Width, 238, 242);
        Assert.InRange(region.Height, 158, 162);
    }

    [Fact]
    public void Three_photos_on_one_bed_are_three_photos_rather_than_one_big_box()
    {
        // Treating them as a single content rectangle would crop to a box
        // containing all three plus the gaps between them.
        var raster = Bed(
            400, 300,
            new PixelRect(20, 20, 100, 80),
            new PixelRect(160, 20, 100, 80),
            new PixelRect(20, 150, 100, 80));

        Assert.Equal(3, ImageAnalysis.DetectRegions(raster).Count);
    }

    [Fact]
    public void Dust_on_the_glass_is_not_a_photograph()
    {
        var raster = Bed(400, 300, new PixelRect(60, 40, 240, 160));
        raster.Fill(10, 280, 2, 2, 20);

        Assert.Single(ImageAnalysis.DetectRegions(raster));
    }

    [Fact]
    public void A_dark_bed_works_as_well_as_a_white_one()
    {
        // Some scanner lids are black on purpose, so the background is measured
        // rather than assumed.
        var raster = Raster.Blank(400, 300, 10);
        raster.Fill(60, 40, 240, 160, 200);

        var region = Assert.Single(ImageAnalysis.DetectRegions(raster));

        Assert.InRange(region.Width, 238, 242);
    }

    [Fact]
    public void The_whitespace_around_it_is_reported_side_by_side()
    {
        // "There is white around it" is not actionable. "Fifteen percent on the
        // left" is.
        var raster = Bed(400, 300, new PixelRect(60, 40, 240, 160));

        var report = ImageAnalysis.Examine(raster, 4000, 3000);
        var (left, right, top, bottom) = report.Margins;

        Assert.True(report.HasBorder);
        Assert.InRange(left, 14, 16);
        Assert.InRange(right, 24, 26);
        Assert.InRange(top, 12, 14);
        Assert.InRange(bottom, 32, 34);
    }

    [Fact]
    public void Regions_are_scaled_back_to_the_real_image()
    {
        // The analysis runs on a small copy; everything it says has to be in
        // the coordinates of the file it came from.
        var raster = Bed(400, 300, new PixelRect(60, 40, 240, 160));

        var report = ImageAnalysis.Examine(raster, 4000, 3000);

        Assert.InRange(report.Regions[0].Width, 2380, 2420);
        Assert.InRange(report.Regions[0].X, 580, 620);
    }

    [Fact]
    public void A_landscape_photo_is_reported_as_landscape_and_says_how_much_of_the_bed_it_fills()
    {
        var raster = Bed(400, 300, new PixelRect(60, 40, 240, 160));

        var spoken = ImageAnalysis.Examine(raster, 400, 300).Describe();

        Assert.Contains("one picture found", spoken);
        Assert.Contains("landscape", spoken);
        Assert.Contains("percent of the scan", spoken);
    }

    [Fact]
    public void Several_pictures_are_offered_as_a_split()
    {
        var raster = Bed(400, 300, new PixelRect(20, 20, 120, 90), new PixelRect(200, 20, 120, 90));

        var report = ImageAnalysis.Examine(raster, 400, 300);

        Assert.Contains("2 pictures found", report.Describe());
        Assert.Contains("split into 2 files", report.Offer());
    }

    [Fact]
    public void An_empty_bed_says_it_is_empty_rather_than_inventing_a_photograph()
    {
        var raster = Raster.Blank(200, 200, 250);

        Assert.Equal("this looks empty", ImageAnalysis.Examine(raster, 200, 200).Describe());
    }

    // ---- straightness ----------------------------------------------------

    [Fact]
    public void A_straight_picture_is_reported_as_straight()
    {
        var raster = Bed(300, 200, new PixelRect(40, 40, 220, 120));

        Assert.True(ImageAnalysis.Examine(raster, 300, 200).IsStraight);
    }

    [Fact]
    public void A_tilted_edge_is_measured_and_the_direction_is_right()
    {
        // A horizontal edge that drops as it goes right is rotated clockwise,
        // and that is what has to be said - "rotated" without a direction is
        // half an answer.
        var raster = Raster.Blank(300, 200, 240);

        for (var x = 0; x < 300; x++)
        {
            var edge = 60 + (int)Math.Round(x * Math.Tan(5 * Math.PI / 180));

            for (var y = edge; y < 200; y++) raster.Fill(x, y, 1, 1, 40);
        }

        var skew = ImageAnalysis.EstimateSkew(raster);

        Assert.InRange(Math.Abs(skew), 3.5, 6.5);
        Assert.True(skew > 0, "an edge that drops to the right is clockwise");
    }

    [Fact]
    public void A_crooked_scan_says_so_and_offers_the_fix()
    {
        var raster = Raster.Blank(300, 200, 240);

        for (var x = 0; x < 300; x++)
        {
            var edge = 60 + (int)Math.Round(x * Math.Tan(4 * Math.PI / 180));

            for (var y = edge; y < 200; y++) raster.Fill(x, y, 1, 1, 40);
        }

        var report = ImageAnalysis.Examine(raster, 300, 200);

        Assert.False(report.IsStraight);
        Assert.Contains("rotated", report.Describe());
        Assert.Contains("straighten", report.Offer());
    }
}

public class ImageDocumentTests
{
    private static ImageDocument Photo() => ImageDocument.Open("/tmp/photo.jpg", 3000, 2000, 300);

    [Fact]
    public void A_picture_describes_itself_the_way_a_person_would_ask_about_it()
    {
        var spoken = Photo().Describe();

        Assert.Contains("3000 by 2000", spoken);
        Assert.Contains("3 by 2", spoken);
        Assert.Contains("landscape", spoken);
        Assert.Contains("10 by 6.7 inches at 300 dpi", spoken);
    }

    [Fact]
    public void The_aspect_is_said_as_a_ratio_people_use()
    {
        Assert.Equal("3 by 2", Photo().Ratio());
        Assert.Equal("16 by 9", ImageDocument.Open("x", 1920, 1080).Ratio());
        Assert.Equal("1 by 1", ImageDocument.Open("x", 500, 500).Ratio());
    }

    [Fact]
    public void An_awkward_ratio_falls_back_to_a_decimal_rather_than_saying_something_useless()
    {
        // "997 by 500" is arithmetic, not information.
        Assert.Contains("to 1", ImageDocument.Open("x", 997, 500).Ratio());
    }
}

public class ImageResizeTests
{
    private static ImageDocument Photo() => ImageDocument.Open("/tmp/photo.jpg", 3000, 2000, 300);

    [Fact]
    public void Resizing_says_the_new_size_the_old_size_and_what_it_costs()
    {
        var document = Photo();

        var spoken = ImageEdits.Resize(document, 1500, 1000).Description;

        Assert.Contains("1500 by 1000", spoken);
        Assert.Contains("was 3000 by 2000", spoken);
        Assert.Contains("inches", spoken);
        Assert.Contains("megabytes", spoken);
    }

    [Fact]
    public void Enlarging_past_the_original_warns_before_it_happens()
    {
        var result = ImageEdits.Resize(Photo(), 6000, 4000);

        Assert.Contains("softer", result.Announce());
    }

    [Fact]
    public void Changing_the_shape_says_that_the_shape_has_changed()
    {
        var result = ImageEdits.Resize(Photo(), 1000, 1000);

        Assert.Contains("shape has changed", result.Announce());
        Assert.Contains("1 by 1", result.Announce());
    }

    [Fact]
    public void The_arrow_key_path_says_only_the_size()
    {
        // The full report on every press would be unusable at speed.
        var document = Photo();

        var spoken = ImageEdits.Nudge(document, horizontal: true, 100).Description;

        Assert.Equal("3100 by 2067", spoken);
    }

    [Fact]
    public void With_the_shape_locked_one_dimension_follows_the_other()
    {
        var document = Photo();

        ImageEdits.Nudge(document, horizontal: true, -1500);

        Assert.Equal(1500, document.Width);
        Assert.Equal(1000, document.Height);
    }

    [Fact]
    public void With_the_shape_unlocked_it_does_not()
    {
        var document = Photo();
        ImageEdits.ToggleAspectLock(document);

        ImageEdits.Nudge(document, horizontal: true, -1500);

        Assert.Equal(1500, document.Width);
        Assert.Equal(2000, document.Height);
    }

    [Fact]
    public void Fit_1080_fits_inside_it_rather_than_stretching_to_it()
    {
        var document = Photo();

        ImageEdits.ApplyPreset(document, "fit 1080");

        Assert.Equal(1620, document.Width);
        Assert.Equal(1080, document.Height);
    }

    [Fact]
    public void Half_and_double_do_what_they_say()
    {
        var document = Photo();

        ImageEdits.ApplyPreset(document, "half");
        Assert.Equal((1500, 1000), (document.Width, document.Height));

        ImageEdits.ApplyPreset(document, "double");
        Assert.Equal((3000, 2000), (document.Width, document.Height));
    }

    [Fact]
    public void A_preset_that_does_not_exist_is_refused_by_name()
    {
        Assert.Contains("no preset called", ImageEdits.ApplyPreset(Photo(), "enormous").Description);
    }

    [Fact]
    public void A_picture_cannot_be_resized_to_nothing()
    {
        Assert.False(ImageEdits.Resize(Photo(), 0, 100).Changed);
    }
}

public class ImageCropTests
{
    private static ImageDocument Scan()
    {
        var document = ImageDocument.Open("/tmp/scan.png", 1000, 800, 300);

        document.Report = new ScanReport(
            [new PixelRect(100, 80, 600, 400)],
            new PixelRect(100, 80, 600, 400),
            2.5,
            1000,
            800);

        return document;
    }

    [Fact]
    public void Cropping_to_the_picture_uses_what_the_analysis_found()
    {
        var document = Scan();

        var result = ImageEdits.CropToContent(document);

        Assert.Equal(new PixelRect(100, 80, 600, 400), document.Crop);
        Assert.Contains("600 by 400", result.Description);
        Assert.Contains("percent removed", result.Description);
    }

    [Fact]
    public void Cropping_before_anything_has_been_measured_says_so()
    {
        var document = ImageDocument.Open("/tmp/x.png", 100, 100);

        Assert.Contains("nothing has been measured", ImageEdits.CropToContent(document).Description);
    }

    [Fact]
    public void A_square_anchored_top_centre_is_one_instruction()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1000, 800);

        var result = ImageEdits.CropToRatio(document, 1, new Placement(8));

        Assert.Equal(800, document.Crop.Width);
        Assert.Equal(800, document.Crop.Height);
        Assert.Contains("square", result.Description);
        Assert.True(document.Crop.Y < 100, "anchored at the top");
    }

    [Fact]
    public void A_crop_never_leaves_the_picture()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1000, 800);

        ImageEdits.CropTo(document, new PixelRect(-50, -50, 5000, 5000));

        Assert.Equal(new PixelRect(0, 0, 1000, 800), document.Crop);
    }

    [Fact]
    public void Moving_an_edge_says_which_edge_how_much_is_cut_and_what_is_left()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1000, 800);

        var spoken = ImageEdits.NudgeEdge(document, CropEdge.Left, 100).Description;

        Assert.Contains("left edge", spoken);
        Assert.Contains("100 pixels cut", spoken);
        Assert.Contains("10 percent", spoken);
        Assert.Contains("900 by 800", spoken);
    }

    [Fact]
    public void An_edge_that_has_run_out_says_so_rather_than_doing_nothing()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1000, 800);

        Assert.Contains("already at the end", ImageEdits.NudgeEdge(document, CropEdge.Left, -10).Description);
    }

    [Fact]
    public void An_edge_cannot_be_pushed_past_the_other_one()
    {
        var document = ImageDocument.Open("/tmp/x.png", 100, 100);

        Assert.False(ImageEdits.NudgeEdge(document, CropEdge.Left, 200).Changed);
    }

    [Fact]
    public void Resetting_goes_back_to_the_whole_picture()
    {
        var document = Scan();
        ImageEdits.CropToContent(document);

        ImageEdits.ResetCrop(document);

        Assert.Equal(new PixelRect(0, 0, 1000, 800), document.Crop);
    }
}

public class ScanFixTests
{
    private static ImageDocument Crooked()
    {
        var document = ImageDocument.Open("/tmp/scan.png", 1000, 800, 300);

        document.Report = new ScanReport(
            [new PixelRect(100, 80, 600, 400)],
            new PixelRect(100, 80, 600, 400),
            2.4,
            1000,
            800);

        return document;
    }

    [Fact]
    public void Fixing_a_scan_straightens_and_crops_in_one()
    {
        var document = Crooked();

        var result = ImageEdits.FixScan(document);

        Assert.Contains("straightened by 2.4 degrees", result.Description);
        Assert.Contains("cropped to 600 by 400", result.Description);
        Assert.Equal(-2.4, document.RotationDegrees, 2);
    }

    [Fact]
    public void Straightening_turns_the_other_way_from_the_tilt()
    {
        // A picture rotated clockwise is fixed by turning anticlockwise, and
        // saying the wrong direction would be worse than saying nothing.
        var document = Crooked();

        var result = ImageEdits.Straighten(document);

        Assert.Contains("anticlockwise", result.Description);
        Assert.Equal(-2.4, document.RotationDegrees, 2);
    }

    [Fact]
    public void Something_already_straight_is_left_alone()
    {
        var document = Crooked();
        document.Report = document.Report! with { SkewDegrees = 0.1 };

        Assert.False(ImageEdits.Straighten(document).Changed);
    }

    [Fact]
    public void A_bed_with_several_pictures_warns_that_only_one_was_used()
    {
        var document = Crooked();

        document.Report = document.Report! with
        {
            Regions = [new PixelRect(100, 80, 300, 200), new PixelRect(500, 80, 300, 200)],
        };

        Assert.Contains("2 pictures here", ImageEdits.FixScan(document).Announce());
    }

    [Fact]
    public void A_sideways_photo_is_turned_and_the_new_shape_is_said()
    {
        var document = ImageDocument.Open("/tmp/x.png", 1200, 800);

        var result = ImageEdits.Rotate(document, 1);

        Assert.Equal((800, 1200), (document.Width, document.Height));
        Assert.Contains("portrait", result.Description);
    }
}

public class ShapeLanguageTests
{
    [Fact]
    public void A_circle_is_said_the_way_a_person_would_say_it()
    {
        var shape = ShapeLanguage.Parse("circle at centre, radius 20 percent, white");

        Assert.NotNull(shape);
        Assert.Equal(ShapeKind.Ellipse, shape!.Kind);
        Assert.Equal("white", shape.Colour);
        Assert.Equal(0.2, shape.Size, 3);
        Assert.Equal(5, shape.Placement.Cell);
    }

    [Fact]
    public void Cell_names_beat_their_own_prefixes()
    {
        // "top left" must not be read as "top".
        var shape = ShapeLanguage.Parse("rectangle at top left, 30 percent, red");

        Assert.Equal(7, shape!.Placement.Cell);
    }

    [Fact]
    public void A_line_has_two_ends()
    {
        var shape = ShapeLanguage.Parse("line from top left to bottom right, yellow");

        Assert.Equal(ShapeKind.Line, shape!.Kind);
        Assert.Equal(7, shape.Placement.Cell);
        Assert.Equal(3, shape.To!.Value.Cell);
    }

    [Fact]
    public void A_longer_colour_name_beats_a_shorter_one_inside_it()
    {
        Assert.Equal("dark blue", ShapeLanguage.Parse("fill dark blue")!.Colour);
    }

    [Fact]
    public void Text_keeps_its_quoted_words_and_they_are_not_read_as_commands()
    {
        var shape = ShapeLanguage.Parse("text \"circle at the top\" at bottom centre, white");

        Assert.Equal(ShapeKind.Text, shape!.Kind);
        Assert.Equal("circle at the top", shape.Text);
        Assert.Equal(2, shape.Placement.Cell);
    }

    [Fact]
    public void A_gradient_takes_two_colours_and_a_direction()
    {
        var shape = ShapeLanguage.Parse("gradient navy to black, left to right");

        Assert.Equal(ShapeKind.Gradient, shape!.Kind);
        Assert.Equal("navy", shape.Colour);
        Assert.Equal("black", shape.SecondColour);
        Assert.False(shape.Vertical);
    }

    [Fact]
    public void A_cell_number_works_for_anyone_who_prefers_them()
    {
        Assert.Equal(3, ShapeLanguage.Parse("circle at cell 3, radius 10 percent, red")!.Placement.Cell);
    }

    [Fact]
    public void Nonsense_is_refused_rather_than_guessed_at()
    {
        Assert.Null(ShapeLanguage.Parse("make it look nice"));
        Assert.Null(ShapeLanguage.Parse(string.Empty));
        Assert.Contains("say something like", ShapeLanguage.Help());
    }

    [Fact]
    public void Every_example_in_the_help_actually_parses()
    {
        // Help that does not work is worse than no help.
        foreach (var example in ShapeLanguage.Examples)
        {
            Assert.NotNull(ShapeLanguage.Parse(example));
        }
    }

    [Fact]
    public void A_shape_reads_back_as_the_sentence_that_would_make_it()
    {
        var shape = ShapeLanguage.Parse("circle at centre, radius 20 percent, white");

        Assert.Equal("circle at centre, radius 20 percent, white", shape!.Describe());
    }
}

public class CanvasTests
{
    [Fact]
    public void A_fill_says_how_far_it_went()
    {
        // The surprise in a fill is always how far it went - through a gap you
        // did not know was there, or stopped by an edge you did not know about.
        var canvas = new Canvas(100, 100);
        canvas.FillRect(0, 0, 100, 100, (255, 255, 255));
        canvas.FillRect(50, 0, 2, 100, (0, 0, 0));

        var result = canvas.FloodFill(10, 10, (255, 0, 0));

        Assert.InRange(result.Share, 45, 51);
        Assert.True(result.Bounds.Right <= 51, "the fill stopped at the line");
        Assert.Contains("filled", result.Describe(100, 100));
    }

    [Fact]
    public void A_fill_with_nowhere_to_go_says_nothing_was_filled()
    {
        var canvas = new Canvas(10, 10);

        Assert.Contains("nothing was filled", canvas.FloodFill(-5, -5, (255, 0, 0)).Describe(10, 10));
    }

    [Fact]
    public void What_is_on_the_canvas_can_be_said_without_describing_it()
    {
        var canvas = new Canvas(100, 100);
        canvas.FillRect(0, 0, 100, 100, (15, 25, 70));
        canvas.FillRect(0, 0, 100, 20, (255, 255, 255));

        var spoken = canvas.Describe();

        Assert.Contains("navy", spoken);
        Assert.Contains("white", spoken);
        Assert.StartsWith("80 percent navy", spoken);
    }

    [Fact]
    public void Shapes_paint_where_they_say_they_will()
    {
        var canvas = new Canvas(100, 100);

        ShapeLanguage.Parse("rectangle at top left, 20 percent, red")!.DrawOn(canvas);

        var (r, _, _, a) = canvas.At(16, 16);

        Assert.Equal(220, r);
        Assert.Equal(255, a);
        Assert.Equal(0, canvas.At(90, 90).A);
    }

    [Fact]
    public void A_shape_reports_how_much_of_the_picture_it_covers()
    {
        var canvas = new Canvas(100, 100);

        var spoken = ShapeLanguage.Parse("fill navy")!.DrawOn(canvas);

        Assert.Contains("covering 100 percent", spoken);
    }

    [Fact]
    public void A_written_png_is_a_png_and_can_be_read_back_by_something_else()
    {
        var path = Path.Combine(Path.GetTempPath(), $"canvas-{Guid.NewGuid():n}.png");

        try
        {
            var canvas = new Canvas(8, 8);
            canvas.FillRect(0, 0, 8, 8, (255, 0, 0));
            canvas.WritePng(path);

            var bytes = File.ReadAllBytes(path);

            Assert.True(bytes.Length > 60);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
            Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(bytes, bytes.Length - 8, 4));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class ColourTests
{
    [Fact]
    public void Colours_are_named_before_they_are_valued()
    {
        // "#3d6fd6" is not a colour to anyone who cannot see it.
        Assert.Equal("blue", Colours.NameOf(61, 111, 214));
        Assert.Equal("white", Colours.NameOf(250, 250, 250));
        Assert.StartsWith("red, #", Colours.Describe(220, 40, 40));
    }

    [Fact]
    public void Naming_is_weighted_the_way_an_eye_weighs_it()
    {
        // Plain distance in RGB calls a dark blue and a dark green the same
        // thing, which is the one mistake that makes naming useless.
        Assert.NotEqual(Colours.NameOf(25, 45, 120), Colours.NameOf(25, 90, 45));
    }

    [Fact]
    public void A_name_or_a_hex_value_both_work()
    {
        Assert.Equal(((byte)220, (byte)40, (byte)40), Colours.Parse("red"));
        Assert.Equal(((byte)18, (byte)52, (byte)86), Colours.Parse("#123456"));
        Assert.Null(Colours.Parse("splendid"));
    }

    [Fact]
    public void Qualifiers_people_actually_say_are_understood()
    {
        var light = Colours.Parse("light blue")!.Value;
        var dark = Colours.Parse("dark blue")!.Value;

        Assert.True(Colours.Luminance(light.R, light.G, light.B)
                    > Colours.Luminance(dark.R, dark.G, dark.B));
    }

    [Fact]
    public void Contrast_is_reported_on_the_scale_the_guidelines_use()
    {
        Assert.Contains("comfortable", Colours.DescribeContrast((255, 255, 255), (0, 0, 0)));
        Assert.Contains("too low", Colours.DescribeContrast((200, 200, 200), (255, 255, 255)));
    }
}

/// <summary>
/// Undo, for pictures. The whole document model was shaped for this: every
/// operation is a decision rather than a change to pixels, so going back is a
/// matter of putting the old decisions in place.
/// </summary>
public class ImageHistoryTests
{
    private static ImageHistory Open()
    {
        var history = new ImageHistory();
        var document = ImageDocument.Open("/tmp/photo.jpg", 3000, 2000, 300);

        document.Report = new ScanReport(
            [new PixelRect(100, 80, 2000, 1400)],
            new PixelRect(100, 80, 2000, 1400),
            2.4,
            3000,
            2000);

        history.Open(document);

        return history;
    }

    [Fact]
    public void Undoing_says_what_it_undid_and_what_the_picture_is_now()
    {
        // Without the second half you know something moved but not where it
        // landed, which is worse than not having undo at all.
        var history = Open();

        history.Do("cropping to the picture", ImageEdits.CropToContent);

        var spoken = history.Undo().Description;

        Assert.StartsWith("undone cropping to the picture", spoken);
        Assert.Contains("3000 by 2000", spoken);
    }

    [Fact]
    public void The_picture_actually_goes_back()
    {
        var history = Open();

        history.Do("crop", ImageEdits.CropToContent);
        Assert.Equal(2000, history.Document!.Width);

        history.Undo();

        Assert.Equal(3000, history.Document!.Width);
        Assert.Equal(new PixelRect(0, 0, 3000, 2000), history.Document.Crop);
    }

    [Fact]
    public void Everything_can_be_undone_in_the_order_it_was_done()
    {
        var history = Open();

        history.Do("straighten", ImageEdits.Straighten);
        history.Do("crop", ImageEdits.CropToContent);
        history.Do("resize", document => ImageEdits.Scale(document, 0.5));

        Assert.Equal(1000, history.Document!.Width);

        history.Undo();
        Assert.Equal(2000, history.Document!.Width);

        history.Undo();
        Assert.Equal(3000, history.Document!.Width);

        history.Undo();
        Assert.Equal(0, history.Document!.RotationDegrees, 3);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Redo_puts_it_back()
    {
        var history = Open();

        history.Do("crop", ImageEdits.CropToContent);
        history.Undo();

        var spoken = history.Redo().Description;

        Assert.StartsWith("redone crop", spoken);
        Assert.Equal(2000, history.Document!.Width);
    }

    [Fact]
    public void Doing_something_new_throws_away_the_redo()
    {
        var history = Open();

        history.Do("crop", ImageEdits.CropToContent);
        history.Undo();
        history.Do("resize", document => ImageEdits.Scale(document, 0.5));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_refused_edit_is_not_recorded()
    {
        // Undoing a refused edit and finding it did nothing twice is how a
        // history stops being trustworthy.
        var history = Open();

        history.Do("resize", document => ImageEdits.Resize(document, 0, 0));

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Drawing_and_removing_a_shape_are_both_undoable()
    {
        var history = Open();

        history.Do("drawing", document =>
        {
            document.Shapes.Add(ShapeLanguage.Parse("fill navy")!);

            return AccessibleVideoEditor.Core.Editing.EditResult.Ok("drawn");
        });

        Assert.Single(history.Document!.Shapes);

        history.Undo();

        Assert.Empty(history.Document!.Shapes);
    }

    [Fact]
    public void Opening_a_new_picture_starts_a_new_history()
    {
        // Undoing past the moment a picture was opened would land you in a
        // different picture.
        var history = Open();
        history.Do("crop", ImageEdits.CropToContent);

        history.Open(ImageDocument.Open("/tmp/other.jpg", 100, 100));

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void What_would_be_undone_can_be_asked_without_undoing_it()
    {
        var history = Open();

        Assert.Equal("nothing to undo", history.Describe());

        history.Do("cropping to the picture", ImageEdits.CropToContent);

        Assert.Contains("cropping to the picture", history.Describe());
        Assert.Contains("1 steps back", history.Describe());
    }

    [Fact]
    public void With_nothing_open_undo_says_so_rather_than_throwing()
    {
        var history = new ImageHistory();

        Assert.Contains("no picture is open", history.Undo().Description);
        Assert.Contains("no picture is open", history.Do("x", d => AccessibleVideoEditor.Core.Editing.EditResult.Ok("y")).Description);
    }
}

/// <summary>
/// The pointer you can hear. Sweeping is the fast, vague half of pointing;
/// naming a cell is the exact, slow half. Both have to agree about where things
/// are.
/// </summary>
public class ImagePointerTests
{
    [Fact]
    public void Up_is_high_and_down_is_low()
    {
        // Every other mapping is something you have to remember rather than
        // something you already know.
        var pointer = new ImagePointer();

        pointer.MoveTo(0.5, 0);
        var top = pointer.Tone.PitchHz;

        pointer.MoveTo(0.5, 1);
        var bottom = pointer.Tone.PitchHz;

        Assert.True(top > bottom);
        Assert.Equal(ImagePointer.HighPitch, top, 1);
        Assert.Equal(ImagePointer.LowPitch, bottom, 1);
    }

    [Fact]
    public void Left_pans_left_and_right_pans_right()
    {
        var pointer = new ImagePointer();

        pointer.MoveTo(0, 0.5);
        Assert.Equal(-1, pointer.Tone.Pan, 3);

        pointer.MoveTo(1, 0.5);
        Assert.Equal(1, pointer.Tone.Pan, 3);

        pointer.MoveTo(0.5, 0.5);
        Assert.Equal(0, pointer.Tone.Pan, 3);
    }

    [Fact]
    public void The_pointer_and_the_cell_names_agree_with_each_other()
    {
        var pointer = new ImagePointer();

        pointer.MoveTo(new Placement(7));

        Assert.Equal(7, pointer.Placement.Cell);
        Assert.Contains("top left", pointer.Describe(1000, 800));
    }

    [Fact]
    public void It_cannot_be_pushed_off_the_picture()
    {
        var pointer = new ImagePointer();
        pointer.MoveTo(0, 0);

        Assert.False(pointer.Move(-1, 0));
        Assert.Equal(0, pointer.X, 5);
    }

    [Fact]
    public void Only_crossing_into_a_new_cell_is_worth_saying()
    {
        // Two numbers on every press is unusable at speed; silence is unusable
        // at all.
        var pointer = new ImagePointer();
        pointer.MoveTo(0.5, 0.5);

        var before = pointer.Placement;
        pointer.Move(0.02, 0);

        Assert.Null(pointer.CrossedInto(before));

        before = pointer.Placement;
        pointer.Move(1, 0);

        Assert.NotNull(pointer.CrossedInto(before));
    }

    [Fact]
    public void The_step_gets_finer_and_says_so()
    {
        var pointer = new ImagePointer();

        Assert.Contains("thirds", pointer.DescribeStep());
        Assert.Contains("tenths", pointer.Finer());
        Assert.Contains("thirds", pointer.Coarser());
    }

    [Fact]
    public void The_position_is_given_in_percentages_and_in_pixels()
    {
        // One is what you can picture, the other is what you need to type in.
        var pointer = new ImagePointer();
        pointer.MoveTo(0.25, 0.5);

        var spoken = pointer.Describe(1000, 800);

        Assert.Contains("25 percent across", spoken);
        Assert.Contains("50 percent down", spoken);
        Assert.Contains("250 by 400", spoken);
    }
}

/// <summary>
/// Colour correction in the units photographers use, and the measurement that
/// says which one to reach for.
/// </summary>
public class ColourAdjustTests
{
    private static ImageDocument Photo() => ImageDocument.Open("/tmp/p.jpg", 1000, 800);

    [Fact]
    public void Corrections_are_said_in_stops_and_kelvin_rather_than_as_numbers()
    {
        var document = Photo();

        ColourEdits.Apply(document, "brighter");

        Assert.Contains("exposure up a third of a stop", document.Colour.Describe());

        ColourEdits.Apply(document, "warmer");

        Assert.Contains("warmer, 6100 kelvin", document.Colour.Describe());
    }

    [Fact]
    public void Warmer_lowers_the_temperature_because_kelvin_runs_backwards()
    {
        var document = Photo();

        ColourEdits.Apply(document, "warmer");

        Assert.True(document.Colour.TemperatureK < 6500);
        Assert.Contains("warmer", document.Colour.Describe());
    }

    [Fact]
    public void Each_correction_is_a_nudge_so_it_can_be_applied_twice()
    {
        var document = Photo();

        ColourEdits.Apply(document, "brighter");
        ColourEdits.Apply(document, "brighter");

        Assert.Equal(0.66, document.Colour.Exposure, 2);
    }

    [Fact]
    public void A_correction_that_has_run_out_says_so_rather_than_doing_nothing()
    {
        var document = Photo();

        for (var i = 0; i < 20; i++) ColourEdits.Apply(document, "brighter");

        Assert.Contains("as far as it goes", ColourEdits.Apply(document, "brighter").Description);
    }

    [Fact]
    public void Reset_puts_everything_back()
    {
        var document = Photo();

        ColourEdits.Apply(document, "punchier");
        ColourEdits.Apply(document, "warmer");
        ColourEdits.Apply(document, "reset");

        Assert.False(document.Colour.IsAnything);
        Assert.Equal("no colour changes", document.Colour.Describe());
    }

    [Fact]
    public void An_unknown_correction_is_refused_by_name()
    {
        Assert.Contains("no correction called", ColourEdits.Apply(Photo(), "cinematic").Description);
    }

    [Fact]
    public void A_dark_picture_is_measured_and_brighter_is_suggested()
    {
        // This is the half of colour correction that normally happens by
        // looking, and the advice uses the same words the commands are called.
        var dark = Raster.Blank(100, 100, 30);

        var advice = ColourEdits.Advise(dark);

        Assert.Contains("average brightness", advice);
        Assert.Contains("brighter", advice);
    }

    [Fact]
    public void A_flat_picture_is_told_to_be_punchier()
    {
        var flat = Raster.Blank(100, 100, 128);
        flat.Fill(0, 0, 100, 50, 140);

        Assert.Contains("punchier", ColourEdits.Advise(flat));
    }

    [Fact]
    public void Crushed_blacks_are_reported_as_a_number()
    {
        var raster = Raster.Blank(100, 100, 150);
        raster.Fill(0, 0, 100, 30, 1);

        Assert.Contains("crushed to black", ColourEdits.Advise(raster));
    }

    [Fact]
    public void A_well_exposed_picture_is_left_alone()
    {
        var raster = Raster.Blank(100, 100, 130);

        for (var y = 0; y < 100; y++) raster.Fill(0, y, 100, 1, (byte)(20 + y * 2));

        Assert.Contains("Nothing obvious to correct", ColourEdits.Advise(raster));
    }

    [Fact]
    public void Colour_changes_are_undoable_like_everything_else()
    {
        var history = new ImageHistory();
        history.Open(Photo());

        history.Do("warmer", document => ColourEdits.Apply(document, "warmer"));
        Assert.True(history.Document!.Colour.IsAnything);

        history.Undo();

        Assert.False(history.Document!.Colour.IsAnything);
    }
}

/// <summary>
/// The curve, made of things that can be said. A levels graph is only a picture
/// of five numbers, and the numbers have names photographers already use.
/// </summary>
public class LevelsTests
{
    private static ImageDocument Photo() => ImageDocument.Open("/tmp/p.jpg", 1000, 800);

    /// <summary>A flat scan: nothing near black, nothing near white.</summary>
    private static Raster Flat()
    {
        var raster = Raster.Blank(100, 100, 120);

        for (var y = 0; y < 100; y++) raster.Fill(0, y, 100, 1, (byte)(90 + y / 2));

        return raster;
    }

    [Fact]
    public void Auto_levels_finds_where_the_picture_starts_and_stops_and_says_the_numbers()
    {
        // The automatic answer has to be adjustable rather than merely
        // accepted, which means saying what it chose.
        var document = Photo();

        var result = LevelEdits.Auto(document, Flat());

        Assert.True(result.Changed);
        Assert.InRange(document.Levels.BlackPoint, 85, 95);
        Assert.InRange(document.Levels.WhitePoint, 135, 145);
        Assert.Contains("black point", result.Description);
        Assert.Contains("opening it up by", result.Description);
    }

    [Fact]
    public void Auto_levels_warns_when_it_is_stretching_the_picture_hard()
    {
        var document = Photo();

        var result = LevelEdits.Auto(document, Flat());

        Assert.Contains("banding", result.Announce());
    }

    [Fact]
    public void A_picture_of_one_tone_is_refused_rather_than_stretched_into_nonsense()
    {
        var document = Photo();

        var result = LevelEdits.Auto(document, Raster.Blank(50, 50, 128));

        Assert.False(result.Changed);
        Assert.Contains("almost all one tone", result.Description);
    }

    [Fact]
    public void The_points_are_read_back_as_numbers_that_mean_something()
    {
        var levels = new Levels { BlackPoint = 20, WhitePoint = 230, Midtones = 8 };

        var spoken = levels.Describe();

        Assert.Contains("black point 20", spoken);
        Assert.Contains("white point 230", spoken);
        Assert.Contains("midtones up 8", spoken);
    }

    [Fact]
    public void A_hard_stretch_says_how_much_of_the_range_is_left()
    {
        var levels = new Levels { BlackPoint = 100, WhitePoint = 160 };

        Assert.Contains("stretching 24 percent", levels.Describe());
        Assert.InRange(levels.RangeUsed, 23, 25);
    }

    [Fact]
    public void The_black_point_cannot_be_pushed_past_the_white_one()
    {
        var document = Photo();
        document.Levels = new Levels { BlackPoint = 100, WhitePoint = 120 };

        LevelEdits.Apply(document, "raise the black point");

        Assert.True(document.Levels.BlackPoint <= document.Levels.WhitePoint - 16);
    }

    [Fact]
    public void Resetting_puts_the_curve_back()
    {
        var document = Photo();

        LevelEdits.Apply(document, "midtones up");
        LevelEdits.Apply(document, "reset levels");

        Assert.False(document.Levels.IsAnything);
        Assert.Equal("levels untouched", document.Levels.Describe());
    }

    [Fact]
    public void An_unknown_level_is_refused_by_name()
    {
        Assert.Contains("no level called", LevelEdits.Apply(Photo(), "s-curve").Description);
    }

    // ---- the histogram, read out -----------------------------------------

    [Fact]
    public void The_histogram_is_five_numbers_rather_than_two_hundred_and_fifty_six()
    {
        var raster = Raster.Blank(100, 100, 128);
        raster.Fill(0, 0, 100, 25, 10);

        var zones = ToneZones.Of(raster);

        Assert.InRange(zones.Blacks, 24, 26);
        Assert.InRange(zones.Midtones, 74, 76);
        Assert.Contains("blacks 25", zones.Describe());
    }

    [Fact]
    public void The_shape_of_the_picture_is_said_as_a_sentence_first()
    {
        Assert.Contains("solid black", ToneZones.Of(Raster.Blank(50, 50, 5)).Summarise());
        Assert.Contains("solid white", ToneZones.Of(Raster.Blank(50, 50, 250)).Summarise());
        Assert.Contains("flat", ToneZones.Of(Raster.Blank(50, 50, 128)).Summarise());
        Assert.Contains("shadows", ToneZones.Of(Raster.Blank(50, 50, 60)).Summarise());
    }

    [Fact]
    public void Levels_are_undoable_like_everything_else()
    {
        var history = new ImageHistory();
        history.Open(Photo());

        history.Do("auto levels", document => LevelEdits.Auto(document, Flat()));
        Assert.True(history.Document!.Levels.IsAnything);

        history.Undo();

        Assert.False(history.Document!.Levels.IsAnything);
    }
}

/// <summary>
/// A batch is the one operation here that can go wrong a hundred times before
/// anybody notices, so what travels and what does not is the whole design.
/// </summary>
public class BatchJobTests
{
    [Fact]
    public void The_corrections_travel_but_the_geometry_does_not()
    {
        // A photograph lands somewhere different on the bed every time, so
        // carrying one crop rectangle across a hundred files would ruin them.
        var document = ImageDocument.Open("/tmp/p.jpg", 1000, 800);
        document.Report = new ScanReport(
            [new PixelRect(100, 80, 600, 400)], new PixelRect(100, 80, 600, 400), 2, 1000, 800);

        ImageEdits.FixScan(document);
        ColourEdits.Apply(document, "warmer");

        var job = BatchJob.From(document);

        Assert.True(job.FixEachScan);
        Assert.True(job.Colour.IsAnything);
        Assert.Contains("from its own measurement", job.Describe());
    }

    [Fact]
    public void What_it_will_do_is_a_sentence_before_it_is_a_hundred_files()
    {
        var job = new BatchJob
        {
            FixEachScan = true,
            AutoLevels = true,
            FitWidth = 1920,
            FitHeight = 1080,
        };

        var spoken = job.Describe();

        Assert.Contains("straighten and crop each one", spoken);
        Assert.Contains("each picture's own histogram", spoken);
        Assert.Contains("fit inside 1920 by 1080", spoken);
    }

    [Fact]
    public void A_job_that_does_nothing_says_so_rather_than_sounding_busy()
    {
        Assert.Equal(
            "copy each picture unchanged",
            new BatchJob { FixEachScan = false }.Describe());
    }

    [Fact]
    public void Output_names_do_not_collide_with_the_originals()
    {
        var job = new BatchJob();

        Assert.Equal("scan-edited.png", job.NameFor("/photos/scan.jpg"));
    }

    [Fact]
    public void The_result_leads_with_the_count_and_then_the_failures()
    {
        // Nobody wants to hear a hundred successes; everybody wants to hear the
        // three that did not work and why.
        var result = new BatchResult(
            [
                new BatchItem("/a.png", true, "saved"),
                new BatchItem("/b.png", false, "could not be read"),
                new BatchItem("/c.png", true, "saved"),
            ],
            "/out");

        var spoken = result.Describe();

        Assert.StartsWith("2 of 3 written to /out", spoken);
        Assert.Contains("b.png, could not be read", spoken);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void A_long_list_of_failures_is_cut_short_rather_than_read_out_in_full()
    {
        var items = Enumerable.Range(0, 9)
            .Select(i => new BatchItem($"/f{i}.png", false, "broken"))
            .ToList();

        Assert.Contains("and 4 more", new BatchResult(items, "/out").Describe());
    }

    [Fact]
    public void An_empty_folder_says_so()
    {
        Assert.Contains("no pictures", new BatchResult([], "/out").Describe());
    }
}

/// <summary>
/// Per-channel levels: the only thing that reaches a cast the temperature
/// control cannot. Temperature moves the picture along one axis; a yellowed
/// page is off in a direction that axis does not pass through.
/// </summary>
public class ChannelLevelTests
{
    private static ImageDocument Photo() => ImageDocument.Open("/tmp/p.jpg", 1000, 800);

    /// <summary>A picture with a known cast: too much red, not enough blue.</summary>
    private static ColourRaster Warm()
    {
        var raster = ColourRaster.Blank(60, 60, (180, 150, 120));

        // Some spread, so the channels have a range to be stretched to.
        raster.Fill(0, 0, 60, 20, (200, 170, 140));
        raster.Fill(0, 40, 60, 20, (160, 130, 100));

        return raster;
    }

    [Fact]
    public void A_cast_is_said_as_a_direction_rather_than_as_three_numbers()
    {
        // "A warm cast" is something you can act on; "red 180, green 150, blue
        // 120" is arithmetic you would have to do first.
        var cast = ColourCast.Of(Warm());

        Assert.Equal("warm", cast.Name);
        Assert.False(cast.IsNeutral);
        Assert.Contains("a warm cast", cast.Describe());
        Assert.Contains("Red 180", cast.Describe());
    }

    [Fact]
    public void A_neutral_picture_is_reported_as_neutral()
    {
        var grey = ColourRaster.Blank(20, 20, (128, 128, 128));

        Assert.True(ColourCast.Of(grey).IsNeutral);
        Assert.Contains("neutral", ColourCast.Of(grey).Describe());
    }

    [Fact]
    public void A_green_cast_is_named_green_rather_than_lumped_in_with_warm()
    {
        var green = ColourRaster.Blank(20, 20, (120, 180, 120));

        Assert.Equal("green", ColourCast.Of(green).Name);
    }

    [Fact]
    public void Auto_colour_levels_stretches_each_channel_to_its_own_range()
    {
        var document = Photo();

        var result = LevelEdits.AutoColour(document, Warm());

        Assert.True(result.Changed);
        Assert.True(document.Levels.HasChannels);

        // Blue was the weakest channel, so its white point comes down furthest.
        Assert.True(document.Levels.Blue.WhitePoint < document.Levels.Red.WhitePoint);
        Assert.Contains("warm cast removed", result.Description);
    }

    [Fact]
    public void An_already_neutral_picture_says_the_correction_changed_little()
    {
        var document = Photo();
        var raster = ColourRaster.Blank(40, 40, (100, 100, 100));
        raster.Fill(0, 0, 40, 10, (200, 200, 200));

        var result = LevelEdits.AutoColour(document, raster);

        if (result.Changed) Assert.Contains("close to neutral", result.Announce());
    }

    // ---- balancing on a point --------------------------------------------

    [Fact]
    public void Balancing_on_a_spot_that_should_be_grey_neutralises_the_picture()
    {
        // The eyedropper, without pointing: it uses a fact about the scene
        // rather than an assumption about the average.
        var document = Photo();
        var raster = ColourRaster.Blank(40, 40, (200, 170, 140));

        var result = LevelEdits.NeutraliseAt(document, raster, 0.5, 0.5);

        Assert.True(result.Changed);

        // Red was brightest and stays put; the weaker channels are lifted by
        // bringing their white points down.
        Assert.Equal(255, document.Levels.Red.WhitePoint);
        Assert.True(document.Levels.Green.WhitePoint < 255);
        Assert.True(document.Levels.Blue.WhitePoint < document.Levels.Green.WhitePoint);
    }

    [Fact]
    public void Balancing_on_something_too_dark_is_refused_with_a_reason()
    {
        var document = Photo();
        var raster = ColourRaster.Blank(20, 20, (8, 6, 5));

        var result = LevelEdits.NeutraliseAt(document, raster, 0.5, 0.5);

        Assert.False(result.Changed);
        Assert.Contains("too dark", result.Description);
    }

    [Fact]
    public void Balancing_on_something_blown_out_is_refused_too()
    {
        var document = Photo();
        var raster = ColourRaster.Blank(20, 20, (255, 254, 253));

        Assert.Contains("blown out", LevelEdits.NeutraliseAt(document, raster, 0.5, 0.5).Description);
    }

    [Fact]
    public void Balancing_on_something_already_neutral_says_so_rather_than_pretending()
    {
        var document = Photo();
        var raster = ColourRaster.Blank(20, 20, (180, 180, 180));

        Assert.False(LevelEdits.NeutraliseAt(document, raster, 0.5, 0.5).Changed);
    }

    [Fact]
    public void A_patch_is_measured_rather_than_a_single_pixel()
    {
        // One pixel is noise; a patch is a measurement.
        var raster = ColourRaster.Blank(20, 20, (100, 100, 100));
        raster.Fill(10, 10, 1, 1, (255, 0, 0));

        var (r, _, _) = raster.PatchAt(10, 10);

        Assert.InRange(r, 100, 140);
    }

    // ---- nudges -----------------------------------------------------------

    [Fact]
    public void Less_red_takes_the_red_down_and_says_where_it_landed()
    {
        var document = Photo();

        var result = LevelEdits.Channel(document, "less red");

        Assert.Equal(247, document.Levels.Red.WhitePoint);
        Assert.Contains("red", result.Description);
    }

    [Fact]
    public void Each_channel_is_named_separately_when_read_back()
    {
        var levels = Levels.None
            .WithChannel(0, new ChannelLevels(0, 240))
            .WithChannel(2, new ChannelLevels(10, 255));

        var spoken = levels.Describe();

        Assert.Contains("red to 240", spoken);
        Assert.Contains("blue from 10", spoken);
        Assert.DoesNotContain("green", spoken);
    }

    [Fact]
    public void Channel_levels_are_undoable_like_everything_else()
    {
        var history = new ImageHistory();
        history.Open(Photo());

        history.Do("auto colour", document => LevelEdits.AutoColour(document, Warm()));
        Assert.True(history.Document!.Levels.HasChannels);

        history.Undo();

        Assert.False(history.Document!.Levels.HasChannels);
    }
}

/// <summary>
/// Face detection, not recognition: it says "there is a face there", never
/// "that is Cody". Framing needs only a position, and a position is something
/// arithmetic can find.
/// </summary>
public class FaceFinderTests
{
    /// <summary>A frame with a patch of skin on a plain background.</summary>
    private static byte[] Frame(
        int width,
        int height,
        (int X, int Y, int W, int H)? face,
        (byte R, byte G, byte B) skin,
        (byte R, byte G, byte B)? background = null)
    {
        var pixels = new byte[width * height * 3];
        var back = background ?? ((byte)30, (byte)60, (byte)140);

        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = back.Item1;
            pixels[i + 1] = back.Item2;
            pixels[i + 2] = back.Item3;
        }

        if (face is { } box)
        {
            for (var y = box.Y; y < box.Y + box.H; y++)
            {
                for (var x = box.X; x < box.X + box.W; x++)
                {
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;

                    var at = (y * width + x) * 3;

                    pixels[at] = skin.R;
                    pixels[at + 1] = skin.G;
                    pixels[at + 2] = skin.B;
                }
            }
        }

        return pixels;
    }

    [Theory]
    [InlineData(241, 194, 165)]   // very pale
    [InlineData(224, 172, 105)]   // light
    [InlineData(198, 134, 66)]    // medium
    [InlineData(141, 85, 36)]     // dark
    [InlineData(92, 57, 30)]      // very dark
    public void Skin_is_recognised_across_the_range_of_skin_tones(byte r, byte g, byte b)
    {
        // The usual red-green-blue rules are tuned on pale skin and quietly
        // fail on everyone else, which is why this works on chrominance.
        Assert.True(FaceFinder.IsSkin(r, g, b), $"{r},{g},{b} was not recognised");
    }

    [Theory]
    [InlineData(30, 60, 140)]     // blue wall
    [InlineData(40, 120, 60)]     // green plant
    [InlineData(200, 200, 200)]   // white wall
    [InlineData(10, 10, 10)]      // shadow
    public void Things_that_are_not_skin_are_not_called_skin(byte r, byte g, byte b)
    {
        Assert.False(FaceFinder.IsSkin(r, g, b), $"{r},{g},{b} was called skin");
    }

    [Fact]
    public void A_face_is_found_where_it_actually_is()
    {
        var frame = Frame(160, 120, (60, 30, 40, 50), (198, 134, 66));

        var found = FaceFinder.Find(frame, 160, 120);

        Assert.NotNull(found);
        Assert.InRange(found!.CentreX, 0.45, 0.55);
        Assert.InRange(found.Width, 0.2, 0.3);
    }

    [Fact]
    public void An_empty_room_finds_nothing_rather_than_inventing_a_face()
    {
        Assert.Null(FaceFinder.Find(Frame(160, 120, null, default), 160, 120));
    }

    [Fact]
    public void A_wooden_door_is_not_a_face()
    {
        // Wood sits inside the skin band, so shape is what rules it out: a face
        // is roughly as tall as it is wide, and a door fills the frame.
        var frame = Frame(160, 120, (0, 0, 160, 30), (198, 134, 66));

        Assert.Null(FaceFinder.Find(frame, 160, 120));
    }

    [Fact]
    public void A_speck_of_noise_is_not_a_face()
    {
        Assert.Null(FaceFinder.Find(Frame(160, 120, (10, 10, 3, 3), (198, 134, 66)), 160, 120));
    }

    [Fact]
    public void The_larger_of_two_faces_is_the_one_reported()
    {
        // Somebody walking past behind you does not become the subject.
        var frame = Frame(160, 120, (20, 20, 20, 24), (198, 134, 66));

        for (var y = 50; y < 100; y++)
        {
            for (var x = 90; x < 130; x++)
            {
                var at = (y * 160 + x) * 3;

                frame[at] = 198;
                frame[at + 1] = 134;
                frame[at + 2] = 66;
            }
        }

        var found = FaceFinder.Find(frame, 160, 120);

        Assert.NotNull(found);
        Assert.InRange(found!.CentreX, 0.6, 0.75);
    }

    [Fact]
    public void A_short_frame_is_refused_rather_than_read_past_its_end()
    {
        Assert.Null(FaceFinder.Find(new byte[10], 160, 120));
    }

    // ---- what the tones say ----------------------------------------------

    [Fact]
    public void Being_off_to_one_side_pans_the_tone_that_way()
    {
        var left = ViewfinderSonifier.Evaluate(new FramingError(true, 0.2, 1.0 / 3, 0.28));
        var right = ViewfinderSonifier.Evaluate(new FramingError(true, 0.8, 1.0 / 3, 0.28));

        Assert.True(left.Pan < -0.5);
        Assert.True(right.Pan > 0.5);
    }

    [Fact]
    public void Being_high_or_low_in_the_frame_moves_the_pitch()
    {
        var high = ViewfinderSonifier.Evaluate(new FramingError(true, 0.5, 0.1, 0.28));
        var low = ViewfinderSonifier.Evaluate(new FramingError(true, 0.5, 0.7, 0.28));

        Assert.True(high.PitchHz > low.PitchHz);
    }

    [Fact]
    public void Being_framed_is_silence_which_is_the_whole_point()
    {
        var framed = ViewfinderSonifier.Evaluate(new FramingError(true, 0.5, 1.0 / 3, 0.28));

        Assert.True(framed.Locked);
        Assert.True(framed.Silent);
        Assert.Equal("framed", framed.Guidance);
    }

    [Fact]
    public void With_no_face_it_says_so_rather_than_guiding_you_towards_nothing()
    {
        var lost = ViewfinderSonifier.Evaluate(FramingError.NoFace);

        Assert.False(lost.Locked);
        Assert.Equal("no face detected", lost.Guidance);
    }
}
