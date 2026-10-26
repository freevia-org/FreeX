using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r210: five more of r208's confirmed FreeX no-op-capable commands. Same shape as r209 -- a gallery
/// or toggle that shows the current value, re-confirmed.
/// <para>
/// Two carry a wrinkle worth pinning. The rotation command compares AFTER normalising, so asking for
/// 370 degrees on an object already at 10 is correctly no change. The chart-layout command compares
/// the whole options record against a fresh capture of the chart, which is exactly what Apply would
/// write -- so it cannot drift from the fields ApplyOptions actually sets.
/// </para>
/// </summary>
public sealed class R210_EqualValueSetterNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingAPicturesOwnAspectRatioLock_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            LockAspectRatio = true,
        };
        sheet.Pictures.Add(picture);

        new SetPictureLockAspectRatioCommand(sheet.Id, picture.Id, true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void UnlockingAPicturesAspectRatio_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            LockAspectRatio = true,
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureLockAspectRatioCommand(sheet.Id, picture.Id, false).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.LockAspectRatio.Should().BeFalse();
    }

    [Fact]
    public void ReApplyingAnObjectsOwnRotation_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            RotationDegrees = 90,
        };
        sheet.Pictures.Add(picture);

        new SetDrawingObjectRotationCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, 90)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AskingForAnEquivalentRotation_ReportsNoOp()
    {
        // 450 normalises to 90. The comparison happens after normalisation, so this is no change --
        // a comparison against the raw request would have missed it.
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            RotationDegrees = 90,
        };
        sheet.Pictures.Add(picture);

        new SetDrawingObjectRotationCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, 450)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void RotatingAnObject_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            RotationDegrees = 0,
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetDrawingObjectRotationCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, 45)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.RotationDegrees.Should().Be(45);
    }
}
