using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R100-paste-special-all-using-theme-operation-1: "All using Source theme" + an arithmetic
/// Operation (Add/Subtract/Multiply/Divide) wrongly overwrote the destination's own formatting
/// with the source cell's style wholesale, via the catch-all branch in
/// PasteCommandFactory.TryComputeOperationFormatEdit.
///
/// AllUsingSourceTheme's own baseline (Operation==None) behavior is NOT the same as
/// ValuesAndSourceFormatting's: PasteCommandCellFactory.BuildPastedCell has no dedicated branch for
/// AllUsingSourceTheme at all, so it falls through to the exact same generic "mode==All" path
/// (BuildAllCell) that PasteSpecialContentKind.Default uses (the theme distinction only matters
/// cross-workbook; same-workbook it is identical to plain "All"). Default is deliberately excluded
/// from TryComputeOperationFormatEdit's eligible-kinds list (see
/// R26_PasteSpecialOperationDeepTests.DefaultContentKind_WithOperation_StaysPlainValueOnly_NoRegression),
/// so AllUsingSourceTheme must be excluded too -- Add/Subtract/Multiply/Divide should only combine
/// the value and must leave the destination's own fill/font/border/number-format untouched, matching
/// Default's tested behavior.
/// </summary>
public sealed class R100_PasteSpecialAllUsingThemeOperationTests
{
    [Fact]
    public void AllUsingSourceTheme_WithOperation_StaysPlainValueOnly_LikeDefault()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, NumberFormat = "0.00%" });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false, NumberFormat = "General" });

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.AllUsingSourceTheme));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(15));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeFalse("'All using Source theme' + an Operation must not pick up source formatting, same as plain All");
        pastedStyle.NumberFormat.Should().Be("General");
    }

    [Fact]
    public void AllUsingSourceTheme_WithoutOperation_StillMergesSourceStyleWholesale_NoRegression()
    {
        // Sibling case: with no Operation, "All using Source theme" must keep behaving exactly like
        // a plain "All" paste and carry the source's formatting wholesale (this is the behavior the
        // fix must NOT disturb -- only the Operation combo changes).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            BorderTop = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Bold = false });

        var sourceCell = Cell.FromValue(new NumberValue(5));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var destinationCell = Cell.FromValue(new NumberValue(10));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllUsingSourceTheme));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(5));
        var pastedStyle = wb.GetStyle(sheet.GetCell(destination)!.StyleId);
        pastedStyle.Bold.Should().BeTrue("with no Operation, 'All using Source theme' still carries the source's style wholesale");
        pastedStyle.BorderTop.Style.Should().Be(BorderStyle.Thin);
    }
}
