using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r221: the Paste* family, and a guard shape worth naming. Eleven of these commands accumulate a
/// record of what they wrote -- an <c>affected</c> list, an <c>_added</c> list, a <c>pastedRules</c>
/// list -- so the no-op decision can be made AFTER the loop, on that record. There is no mirror to
/// keep in step with the mutation: an empty record IS the proof that nothing was written, whatever
/// combination of empty source, filtered mapping or skipped destination produced it. Compare the
/// hand-listed field comparisons of r218, which have to be re-checked every time Apply changes.
/// <para>
/// The limit is stated rather than implied: this catches "there was nothing to paste", not "the
/// pasted values equalled what was already there". The second needs a value-by-value comparison and
/// is not claimed.
/// </para>
/// <para>
/// The gestures are ordinary. Paste Special offers Comments, Formats, Validation and the rest
/// whether or not the copied range carries any of them, and Excel's own behaviour of skipping a
/// merge that would collide with one already at the destination is written into
/// PasteMergedRegionsCommand as a comment -- so pasting merges onto already-merged cells adds
/// nothing, by design.
/// </para>
/// </summary>
public sealed class R221_PasteNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint fromRow, uint fromCol, uint toRow, uint toCol) =>
        new(new CellAddress(sheet.Id, fromRow, fromCol), new CellAddress(sheet.Id, toRow, toCol));

    [Fact]
    public void PastingPicturesFromARangeThatHasNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PastePicturesCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), [], transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingAPicture_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var source = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };

        var outcome = new PastePicturesCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), [source], transpose: false)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Pictures.Should().HaveCount(1);
    }

    [Fact]
    public void PastingShapesFromARangeThatHasNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteShapesCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), [], transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingTextBoxesFromARangeThatHasNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteTextBoxesCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), [], transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingCommentsFromARangeThatCarriesNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteCommentsCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingACommentThatExists_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "A note";

        var outcome = new PasteCommentsCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 10, 1));
    }

    [Fact]
    public void PastingConditionalFormatsFromARangeThatHasNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteConditionalFormatsCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingAConditionalFormat_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, 1, 1, 3, 3) });

        var outcome = new PasteConditionalFormatsCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ConditionalFormats.Should().HaveCount(2);
    }

    [Fact]
    public void PastingMergedRegionsFromARangeWithNone_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteMergedRegionsCommand(
                sheet.Id, Range(sheet, 1, 1, 3, 3), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingAMergeOntoCellsAlreadyMerged_ReportsNoOp()
    {
        // Not an empty source -- the command's own comment says a destination that already overlaps
        // an existing merge is left alone, matching Excel. So this paste has something to copy and
        // still adds nothing, which is exactly why the decision belongs after the loop rather than
        // in a "is the source empty" test up front.
        var (_, sheet, ctx) = Fixture();
        sheet.AddMergedRegion(Range(sheet, 1, 1, 2, 2));
        sheet.AddMergedRegion(Range(sheet, 10, 1, 11, 2));

        new PasteMergedRegionsCommand(
                sheet.Id, Range(sheet, 1, 1, 2, 2), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.MergedRegions.Should().HaveCount(2);
    }

    [Fact]
    public void PastingAMergeOntoFreeCells_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.AddMergedRegion(Range(sheet, 1, 1, 2, 2));

        var outcome = new PasteMergedRegionsCommand(
                sheet.Id, Range(sheet, 1, 1, 2, 2), new CellAddress(sheet.Id, 10, 1), transpose: false)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.MergedRegions.Should().HaveCount(2);
    }

    [Fact]
    public void PastingFormatsWithNothingToPaste_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();

        new PasteFormatsCommand(sheet.Id, []).Apply(ctx).IsNoOp.Should().BeTrue();
    }
}
