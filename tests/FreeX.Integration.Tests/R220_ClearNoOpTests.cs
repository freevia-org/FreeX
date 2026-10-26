using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r220: the Clear* family -- "remove what is not there". The structural counterpart to the
/// equal-value setter, and just as ordinary a gesture: Delete over empty cells, Clear Print Area on
/// a sheet that has none, Clear &gt; Comments over a selection that carries none. The ribbon leaves
/// these enabled whether or not there is anything to clear.
/// <para>
/// Three of these guards are worth the reading. ClearContents already returned early on a null
/// scope -- it knew there was nothing to clear and simply never said so. ClearComments reuses the
/// address list the r164 rewrite already computes, so there is no second predicate to keep in step
/// with the loop. And ClearConditionalFormats decides by REFERENCE equality: the rebuild loop adds
/// untouched rules by reference and shrunk ones as fresh clones, so "same count, every element the
/// same object" is exactly "the loop changed nothing".
/// </para>
/// </summary>
public sealed class R220_ClearNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint fromRow, uint fromCol, uint toRow, uint toCol) =>
        new(new CellAddress(sheet.Id, fromRow, fromCol), new CellAddress(sheet.Id, toRow, toCol));

    [Fact]
    public void PressingDeleteOverEmptyCells_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearContentsCommand(sheet.Id, Range(sheet, 1, 1, 10, 5)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PressingDeleteOverCellsThatHoldSomething_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        var outcome = new ClearContentsCommand(sheet.Id, Range(sheet, 1, 1, 10, 5)).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void ClearingAPrintAreaThatIsNotSet_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearPrintAreaCommand(sheet.Id).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingAPrintAreaThatIsSet_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.PrintArea = Range(sheet, 1, 1, 5, 3);

        var outcome = new ClearPrintAreaCommand(sheet.Id).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.PrintAreas.Should().BeEmpty();
    }

    [Fact]
    public void ClearingASheetBackgroundThatIsNotSet_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearWorksheetBackgroundCommand(sheet.Id).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingAllowEditRangesWhenThereAreNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearAllowEditRangesCommand(sheet.Id).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingHyperlinksOverASelectionThatHasNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearHyperlinksCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingHyperlinksOutsideTheOnlyLinkedCell_ReportsNoOp()
    {
        // The selection matters, not the sheet: a link two columns away must not make this a change.
        var (sheet, ctx) = Fixture();
        sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 9)] = "https://example.invalid";

        new ClearHyperlinksCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, 1, 9));
    }

    [Fact]
    public void ClearingHyperlinksOverALinkedCell_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.Hyperlinks[address] = "https://example.invalid";

        var outcome = new ClearHyperlinksCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Hyperlinks.Should().NotContainKey(address);
    }

    [Fact]
    public void ClearingCommentsOverASelectionThatHasNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearCommentsCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingCommentsOverACommentedCell_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.Comments[address] = "A note";

        var outcome = new ClearCommentsCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Comments.Should().NotContainKey(address);
    }

    [Fact]
    public void ClearingConditionalFormatsWhereThereAreNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearConditionalFormatsCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingConditionalFormatsThatDoNotOverlapTheSelection_ReportsNoOp()
    {
        // Every rule survives by reference, so the reference-equality test reports no change.
        var (sheet, ctx) = Fixture();
        sheet.ConditionalFormats.Add(Rule(sheet, Range(sheet, 20, 1, 25, 1)));

        new ClearConditionalFormatsCommand(sheet.Id, Range(sheet, 1, 1, 4, 4)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.ConditionalFormats.Should().HaveCount(1);
    }

    [Fact]
    public void ClearingAConditionalFormatThatCoversTheSelection_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.ConditionalFormats.Add(Rule(sheet, Range(sheet, 1, 1, 4, 4)));

        var outcome = new ClearConditionalFormatsCommand(sheet.Id, Range(sheet, 1, 1, 4, 4))
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ShrinkingAConditionalFormatsRange_DoesNotReportNoOp()
    {
        // The subtlest direction: the rule survives, but as a fresh clone with a smaller range. A
        // count-only test would have called this no change and silently dropped the shrink.
        var (sheet, ctx) = Fixture();
        sheet.ConditionalFormats.Add(Rule(sheet, Range(sheet, 1, 1, 10, 1)));

        var outcome = new ClearConditionalFormatsCommand(sheet.Id, Range(sheet, 1, 1, 4, 1))
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ConditionalFormats.Should().HaveCount(1);
        sheet.ConditionalFormats[0].AppliesTo.Should().NotBe(Range(sheet, 1, 1, 10, 1));
    }

    [Fact]
    public void UnprotectingASheetWithNothingLeftToUnprotect_ReportsNoOp()
    {
        // Note what this fixture has to do to BE a no-op: a fresh sheet ships with two default
        // ProtectionPermissions, so unprotecting an untouched sheet still empties that list -- a
        // real change to what gets written back. The obvious version of this guard, comparing
        // IsProtected alone, would have suppressed it. Belt and braces either way:
        // ProtectionWorkflowSession only issues this command for a protected sheet.
        var (sheet, ctx) = Fixture();
        sheet.ProtectionPermissions.Clear();

        new UnprotectSheetCommand(sheet.Id, password: null).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void UnprotectingAnUntouchedSheet_StillClearsItsDefaultPermissions()
    {
        var (sheet, ctx) = Fixture();
        sheet.ProtectionPermissions.Should().NotBeEmpty("a fresh sheet ships with two defaults");

        var outcome = new UnprotectSheetCommand(sheet.Id, password: null).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ProtectionPermissions.Should().BeEmpty();
    }

    [Fact]
    public void UnprotectingASheetThatOnlyCarriesPreservedMetadata_DoesNotReportNoOp()
    {
        // The clause that makes the guard complete. An unprotected sheet loaded from a file can
        // still carry a ProtectionMetadata bag, and clearing it changes what gets written back --
        // so a guard on IsProtected alone would have suppressed a real edit.
        var (sheet, ctx) = Fixture();
        sheet.ProtectionMetadata = new NativeXmlPreserveBag();

        var outcome = new UnprotectSheetCommand(sheet.Id, password: null).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ProtectionMetadata.Should().BeNull();
    }

    [Fact]
    public void UnprotectingAProtectedSheet_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.IsProtected = true;

        var outcome = new UnprotectSheetCommand(sheet.Id, password: null).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.IsProtected.Should().BeFalse();
    }

    private static ConditionalFormat Rule(Sheet sheet, GridRange range) =>
        new()
        {
            AppliesTo = range,
        };
}
