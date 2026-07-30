using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R93 backlog item "picture-resize-precise-custom-sizes": setting an exact height/width via the
/// Format Picture size fields was reported not to land on the exact requested value. Exercised
/// through the real <see cref="ResizePictureCommand"/> (the actual IWorkbookCommand entry point
/// behind the dialog), not a hand-built model, per the round-93 test rule. Live investigation found
/// ResizePictureCommand.Apply already assigns picture.Width/picture.Height verbatim from the
/// constructor args -- no Math.Round, no EMU round-trip, and no LockAspectRatio override applied
/// inside the command (that dimension-sync math lives one layer up, in
/// FreeX.App.Presentation.DrawingUI.FormatPicturePlanner/ObjectSizeDialogPlanner, which compute the
/// OTHER dimension from the typed one and never touch the typed value itself) -- so these tests
/// pass on current main and serve as regression coverage, not a fail-before/pass-after fix.
/// </summary>
public sealed class R93_ResizePictureCommandPrecisionTests
{
    [Fact]
    public void Apply_HonoursPreciseFractionalWidthAndHeightExactly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Width = 240,
            Height = 140,
            LockAspectRatio = true
        };
        sheet.Pictures.Add(picture);

        // A precise, non-round-numbered custom size -- the kind of value the Format Picture size
        // fields produce (e.g. converted from inches/cm), not a value that happens to survive
        // rounding by coincidence.
        const double preciseWidth = 173.428;
        const double preciseHeight = 88.071;

        var command = new ResizePictureCommand(sheet.Id, picture.Id, preciseWidth, preciseHeight);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        picture.Width.Should().Be(preciseWidth, "the typed width must land exactly, matching Excel");
        picture.Height.Should().Be(preciseHeight, "the typed height must land exactly, matching Excel");

        command.Revert(ctx);
        picture.Width.Should().Be(240);
        picture.Height.Should().Be(140);
    }

    [Fact]
    public void Apply_SingleDimensionResize_LeavesLockAspectRatioFlagUntouchedAndOtherDimensionAsPassed()
    {
        // The command itself must never recompute the "other" dimension from LockAspectRatio --
        // that math belongs to the caller (FormatPicturePlanner/ObjectSizeDialogPlanner). This
        // guards against a regression where the command starts silently overriding a precise
        // caller-supplied value because LockAspectRatio happens to be true.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Width = 200,
            Height = 100,
            LockAspectRatio = true
        };
        sheet.Pictures.Add(picture);

        // Caller passes a width/height pair that does NOT preserve the original 2:1 aspect ratio --
        // simulating the caller already having computed the locked dimension (or the user having
        // unlocked aspect ratio) before invoking the command.
        var command = new ResizePictureCommand(sheet.Id, picture.Id, 91.5, 91.5);
        command.Apply(ctx).Success.Should().BeTrue();

        picture.Width.Should().Be(91.5);
        picture.Height.Should().Be(91.5);
        picture.LockAspectRatio.Should().BeTrue("resizing must not silently change the lock-aspect-ratio flag");
    }
}
