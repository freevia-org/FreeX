using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Tests.DrawingUI;

/// <summary>
/// R93 backlog item "picture-resize-precise-custom-sizes": setting an exact height/width via the
/// Format Picture / Size dialog fields was reported not to land on the exact typed value.
///
/// Live investigation traced the full path through the real dialog entry points --
/// <see cref="FormatPicturePlanner.TryCreateResult(string?, string?, string?, bool, string?, out FormatPicturePlanner.FormatObjectResult?, out string?)"/>
/// (the combined Format Picture/Shape/TextBox dialog) and
/// <see cref="ObjectSizeDialogPlanner.TryCreateSize(string?, string?, ObjectSizeDialogField, out ObjectSizeDialogSize, out ObjectSizeDialogField)"/>
/// (the standalone Size dialog) -- and found neither ever reformats the box the user actually typed
/// into before parsing it back: both call <see cref="FormatPicturePlanner.TryParseNumber"/> directly
/// on the raw box text, and <c>FormatSize</c>/rounding is only ever applied to the OTHER,
/// lock-aspect-computed dimension for on-screen display, never to the dimension the caller supplies.
/// These tests exercise that path with a value practically guaranteed to trip an intermediate
/// rounding/round-trip bug (a non-terminating-decimal DIP value, as produced by converting a
/// typed inches/cm value) and therefore pass on current main -- they are regression coverage for a
/// NOT-A-BUG finding, not a fail-before/pass-after fix.
/// </summary>
public sealed class R93_PictureResizePrecisionPlannerTests
{
    public R93_PictureResizePrecisionPlannerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    private const double PreciseWidth = 173.42857142857142; // e.g. 3.17in * 96/1.75 style conversion remainder
    private const double PreciseHeight = 88.07142857142857;

    [Fact]
    public void FormatObjectDialog_TypedWidthAndHeight_LandExactly_LockAspectOff()
    {
        var submission = new FormatPicturePlanner.FormatObjectSubmission(
            PreciseWidth.ToString("R", CultureInfo.InvariantCulture),
            PreciseHeight.ToString("R", CultureInfo.InvariantCulture),
            "0",
            LockAspectRatio: false,
            AltText: null);

        FormatPicturePlanner.TryCreateResult(submission, out var result, out var error).Should().BeTrue(error);
        result!.Width.Should().Be(PreciseWidth);
        result.Height.Should().Be(PreciseHeight);
    }

    [Fact]
    public void FormatObjectDialog_TypedWidthOnly_LandsExactly_WhileLockAspectSyncsOnlyHeight()
    {
        // Simulates: user types an exact width while lock-aspect-ratio is on. The dialog host would
        // call SyncHeightFromWidth to refresh the height box for display, but Accept() still reads the
        // WIDTH box text verbatim -- it must never be re-derived from the synced/rounded height.
        var aspectRatio = FormatPicturePlanner.AspectRatio(200, 100); // 2.0, matching a typical picture
        var syncedHeight = FormatPicturePlanner.SyncHeightFromWidth(
            PreciseWidth.ToString("R", CultureInfo.InvariantCulture), aspectRatio);
        syncedHeight.Should().NotBeNull();

        var submission = new FormatPicturePlanner.FormatObjectSubmission(
            PreciseWidth.ToString("R", CultureInfo.InvariantCulture),
            FormatPicturePlanner.FormatSize(syncedHeight!.Value), // what the height box would display
            "0",
            LockAspectRatio: true,
            AltText: null);

        FormatPicturePlanner.TryCreateResult(submission, out var result, out var error).Should().BeTrue(error);
        result!.Width.Should().Be(PreciseWidth, "the field the user actually typed must never be rounded");
    }

    [Fact]
    public void FormatObjectDialog_SameParsingPath_AppliesToShapesAndTextBoxes()
    {
        // BuildFormatCommands / TryCreateResult have no picture-specific branch for width/height --
        // the same exact-value guarantee therefore covers DrawingShapeModel and TextBoxModel too.
        var submission = new FormatPicturePlanner.FormatObjectSubmission(
            PreciseWidth.ToString("R", CultureInfo.InvariantCulture),
            PreciseHeight.ToString("R", CultureInfo.InvariantCulture),
            "45",
            LockAspectRatio: false,
            AltText: "a shape");

        FormatPicturePlanner.TryCreateResult(submission, out var result, out var error).Should().BeTrue(error);
        result!.Width.Should().Be(PreciseWidth);
        result.Height.Should().Be(PreciseHeight);
    }

    [Fact]
    public void ObjectSizeDialog_TypedWidthAndHeight_LandExactly()
    {
        var submission = new ObjectSizeDialogSubmission(
            PreciseWidth.ToString("R", CultureInfo.InvariantCulture),
            PreciseHeight.ToString("R", CultureInfo.InvariantCulture),
            ObjectSizeDialogField.Height);

        ObjectSizeDialogPlanner.TryCreateSize(submission, out var result, out _).Should().BeTrue();
        result.Width.Should().Be(PreciseWidth);
        result.Height.Should().Be(PreciseHeight);
    }

    [Fact]
    public void ObjectSizeDialog_TypedHeightOnly_LandsExactly_WhileLockAspectSyncsOnlyWidth()
    {
        var originalSize = new ObjectSizeDialogSize(200, 100);
        var syncedWidth = ObjectSizeDialogPlanner.SyncWidthFromHeight(
            PreciseHeight.ToString("R", CultureInfo.InvariantCulture), originalSize);
        syncedWidth.Should().NotBeNull();

        var submission = new ObjectSizeDialogSubmission(
            ObjectSizeDialogPlanner.FormatSize(syncedWidth!.Value), // what the width box would display
            PreciseHeight.ToString("R", CultureInfo.InvariantCulture),
            ObjectSizeDialogField.Height);

        ObjectSizeDialogPlanner.TryCreateSize(submission, out var result, out _).Should().BeTrue();
        result.Height.Should().Be(PreciseHeight, "the field the user actually typed must never be rounded");
    }
}
