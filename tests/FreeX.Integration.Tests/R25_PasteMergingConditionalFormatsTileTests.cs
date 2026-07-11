using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R25-clipboard-paste-remaining-2: Paste Special "All merging conditional formats" never tiled its
/// copied cell content into a destination range larger than the copied source -- unlike every other
/// Paste Special content kind, which correctly repeats/tiles the source across the whole selected
/// destination (the classic single-cell-to-range fill gesture). PasteCommandFactory.cs deliberately
/// excluded this one content kind from the tile path, so pasting a single cell into a 1x3 destination
/// only wrote the first cell and left the rest untouched.
/// </summary>
public sealed class R25_PasteMergingConditionalFormatsTileTests
{
    [Fact]
    public void PasteCommandFactory_AllMergingConditionalFormats_TilesSingleCellAcrossLargerDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Copy A1 (value 42, no conditional formatting).
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceRange = new GridRange(sourceStart, sourceStart);
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        // Select B1:B3 (a 1x3 destination) and Paste Special > "All merging conditional formats".
        var destinationStart = new CellAddress(sheet.Id, 1, 2);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 3, 2));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Excel tiles the source across the whole selected destination, exactly like every other
        // Paste Special content kind: B1=B2=B3=42.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(42));

        command.Revert(ctx);

        sheet.GetCell(new CellAddress(sheet.Id, 1, 2)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2)).Should().BeNull();
    }

    /// <summary>
    /// Regression guard for the sibling case the fix must not break: when the copied source DOES
    /// carry a conditional-format rule and the destination is NOT larger than the source (the
    /// existing, already-working non-tiled path), the rule must still be merged in at the mapped
    /// destination exactly as before.
    /// </summary>
    [Fact]
    public void PasteCommandFactory_AllMergingConditionalFormats_NonTiledStillMergesRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 2, 1);
        var sourceRange = new GridRange(sourceStart, sourceEnd);
        var destinationStart = new CellAddress(sheet.Id, 4, 3);
        var destinationEnd = new CellAddress(sheet.Id, 5, 3);

        var sourceRule = new ConditionalFormat
        {
            AppliesTo = sourceRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true },
            Priority = 1
        };
        sheet.ConditionalFormats.Add(sourceRule);

        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        // Destination range matches the source's own shape (2x1), so no tiling should occur -- this
        // must behave exactly as it did before the fix.
        var destinationRange = new GridRange(destinationStart, destinationEnd);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            [(sourceStart, sourceCell.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(42));
        sheet.ConditionalFormats.Should().HaveCount(2);
        var pastedRule = sheet.ConditionalFormats.Single(rule => rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationEnd));
        pastedRule.Value1.Should().Be("10");

        command.Revert(ctx);

        sheet.GetCell(destinationStart).Should().BeNull();
        sheet.ConditionalFormats.Should().Equal(sourceRule);
    }
}
