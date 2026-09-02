using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r211: two more of r208's confirmed FreeX no-op-capable commands -- and the round where the
/// "mirror ALL of the guard" rule earned its keep again.
/// <para>
/// <c>SetSplitPanesCommand</c> looked like a two-field compare. It is not: establishing a real split
/// also CLEARS any freeze. Reporting a no-op on matching split positions alone would have suppressed
/// that clear -- a suppressed real edit, which r204 recorded as strictly worse than the phantom undo
/// entry being removed. The test below pins exactly that case.
/// </para>
/// </summary>
public sealed class R211_EqualValueSetterNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingTheSheetsOwnSplit_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;

        new SetSplitPanesCommand(sheet.Id, 5, 3).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingTheSameSplitWhileAFreezeRemains_DoesNotReportNoOp()
    {
        // The trap: the split positions match, but Apply would still clear the freeze. Calling that
        // a no-op would silently keep the freeze the user asked to replace.
        var (sheet, ctx) = Fixture();
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;

        var outcome = new SetSplitPanesCommand(sheet.Id, 5, 3).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse("establishing the split still has to clear the freeze");
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void ClearingAnAlreadyAbsentSplit_ReportsNoOp()
    {
        // The toggle-off path on a sheet with no split, and with a freeze that must be left alone.
        var (sheet, ctx) = Fixture();
        sheet.FrozenRows = 2;

        new SetSplitPanesCommand(sheet.Id, null, null).Apply(ctx).IsNoOp.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2, "a null split must never tear down an unrelated freeze");
    }

    [Fact]
    public void ChangingTheSplit_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SplitRow = 5;

        var outcome = new SetSplitPanesCommand(sheet.Id, 9, null).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.SplitRow.Should().Be(9u);
    }

    [Fact]
    public void ReApplyingAPicturesOwnCrop_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            CropLeft = 0.10,
            CropTop = 0.05,
            CropRight = 0.10,
            CropBottom = 0.05,
        };
        sheet.Pictures.Add(picture);

        new SetPictureCropCommand(sheet.Id, picture.Id, 0.10, 0.05, 0.10, 0.05).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingOneCropEdge_DoesNotReportNoOp()
    {
        // Crop edges are FRACTIONS (the guard requires left + right < 1), not percentages.
        // All four edges are written, so all four must be compared -- a three-edge check would call
        // this a no-op and drop the change.
        var (sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            CropLeft = 0.10,
            CropTop = 0.05,
            CropRight = 0.10,
            CropBottom = 0.05,
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureCropCommand(sheet.Id, picture.Id, 0.10, 0.05, 0.10, 0.20).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.CropBottom.Should().Be(0.20);
    }
}
