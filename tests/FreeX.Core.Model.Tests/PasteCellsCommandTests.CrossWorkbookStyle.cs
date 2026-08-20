using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    // freex-cell-styles F1: a pasted Cell's StyleId is a raw index into the SOURCE workbook's own
    // private style table. Two independently-opened FreeX windows each build their own Workbook
    // with its own style table, so the SAME numeric index can (and, as this test proves, does)
    // resolve to a completely unrelated CellStyle in each one. Before the fix, PasteCellsCommand
    // wrote the source's raw StyleId straight into the destination sheet with no translation --
    // this test reproduces that exact corruption end-to-end and fails before the fix.
    [Fact]
    public void PasteCellsCommand_WithSourceStyles_TranslatesCrossWorkbookStyleIdInsteadOfReusingRawIndex()
    {
        var sourceWorkbook = new Workbook("SourceWindow");
        var sourceSheet = sourceWorkbook.AddSheet("Sheet1");

        // Pad the source workbook's style table with filler entries so the real style we care
        // about lands at a non-trivial index (3), matching the finding's own repro.
        sourceWorkbook.RegisterStyle(new CellStyle { FontSize = 12 });
        sourceWorkbook.RegisterStyle(new CellStyle { FontSize = 14 });
        var sourceStyle = new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(255, 0, 0),
            FillColor = new CellColor(255, 255, 0)
        };
        var sourceStyleId = sourceWorkbook.RegisterStyle(sourceStyle);
        sourceStyleId.Value.Should().Be(3, "the probe/finding both pin the source style at slot 3");

        var sourceCell = Cell.FromValue(new TextValue("copied"));
        sourceCell.StyleId = sourceStyleId;
        var sourceAddr = new CellAddress(sourceSheet.Id, 0, 0);
        sourceSheet.SetCell(sourceAddr, sourceCell);

        // A second, INDEPENDENT workbook (the destination window). Its own slot 3 is a
        // deliberately unrelated, near-default style -- exactly the "coincidentally occupied slot"
        // scenario the finding describes.
        var destWorkbook = new Workbook("DestWindow");
        var destSheet = destWorkbook.AddSheet("Sheet1");
        destWorkbook.RegisterStyle(new CellStyle { Italic = true });
        destWorkbook.RegisterStyle(new CellStyle { Underline = true });
        var unrelatedDestStyle = new CellStyle { FontSize = 9 };
        var unrelatedDestStyleId = destWorkbook.RegisterStyle(unrelatedDestStyle);
        unrelatedDestStyleId.Value.Should().Be(3);

        var destAddr = new CellAddress(destSheet.Id, 5, 5);
        var pastedCellPayload = Cell.FromValue(new TextValue("copied"));
        pastedCellPayload.StyleId = sourceStyleId; // raw index from the SOURCE workbook

        // This is what PasteCommandFactory now builds for a cross-window paste: the actual source
        // CellStyle content, resolved from the source workbook, keyed by destination address.
        var sourceStyles = new Dictionary<CellAddress, CellStyle> { [destAddr] = sourceWorkbook.GetStyle(sourceStyleId) };

        var command = new PasteCellsCommand(
            destSheet.Id,
            [(destAddr, pastedCellPayload)],
            sourceStyles: sourceStyles);

        var ctx = new TestCommandContext(destWorkbook);
        command.Apply(ctx).Success.Should().BeTrue();

        var pastedStyleId = destSheet.GetCell(destAddr)!.StyleId;
        pastedStyleId.Should().NotBe(unrelatedDestStyleId,
            "the raw source index must not be reused verbatim against the destination's own style table");

        var resolvedStyle = destWorkbook.GetStyle(pastedStyleId);
        resolvedStyle.Bold.Should().BeTrue();
        resolvedStyle.FontColor.Should().Be(new CellColor(255, 0, 0));
        resolvedStyle.FillColor.Should().Be(new CellColor(255, 255, 0));
    }

    // Sibling/no-regression: an ordinary SAME-workbook paste (the overwhelmingly common case,
    // where the caller has no reason to -- and does not -- supply sourceStyles) must keep working
    // exactly as before: the destination's own StyleId index is written through untranslated.
    // PasteCellsCommand_ReplacesValueAndStyleAndUndoRestores (PasteCellsCommandTests.Command.cs)
    // already covers this without any sourceStyles argument; this test additionally proves that
    // even when a caller DOES resolve and supply sourceStyles for a same-workbook paste (translating
    // through RegisterStyle's structural dedup), the result still lands on the identical existing
    // index rather than minting a redundant duplicate entry.
    [Fact]
    public void PasteCellsCommand_WithSourceStyles_SameWorkbookPasteResolvesToExistingIndex_NoDuplicateStyleAdded()
    {
        var workbook = new Workbook("SingleWindow");
        var sheet = workbook.AddSheet("Sheet1");

        var style = new CellStyle { Bold = true, FontColor = new CellColor(0, 128, 0) };
        var styleId = workbook.RegisterStyle(style);
        var styleCountBefore = workbook.StyleCount;

        var sourceAddr = new CellAddress(sheet.Id, 0, 0);
        var sourceCell = Cell.FromValue(new TextValue("copied"));
        sourceCell.StyleId = styleId;
        sheet.SetCell(sourceAddr, sourceCell);

        var destAddr = new CellAddress(sheet.Id, 2, 2);
        var pastedCellPayload = Cell.FromValue(new TextValue("copied"));
        pastedCellPayload.StyleId = styleId;

        var sourceStyles = new Dictionary<CellAddress, CellStyle> { [destAddr] = workbook.GetStyle(styleId) };
        var command = new PasteCellsCommand(sheet.Id, [(destAddr, pastedCellPayload)], sourceStyles: sourceStyles);

        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(destAddr)!.StyleId.Should().Be(styleId);
        workbook.StyleCount.Should().Be(styleCountBefore, "resolving the same style content back into its own workbook must dedupe, not grow the style table");
    }
}
