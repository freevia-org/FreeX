using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for round-40 "clear-delete" bucket:
///
/// R40-commands-clear-delete-3-1: Clear Contents (Delete key / ribbon Clear > Clear Contents)
/// must clear the cell's value/formula but KEEP the hyperlink (and its style) -- only Clear All /
/// Clear Hyperlinks removes the hyperlink itself.
///
/// R40-commands-clear-delete-3-2: "Clear Rules from Selected Cells" must only remove a
/// conditional-format rule from the selected cells -- when the rule's range extends beyond the
/// selection, the un-selected portion keeps the rule (shrink, don't delete).
/// </summary>
public sealed class R40_ClearDeleteTests
{
    [Fact]
    public void ClearContentsCommand_OnHyperlinkedCell_ClearsValueButKeepsHyperlink()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var cell = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.SetCell(cell, Cell.FromValue(new TextValue("Visit site")));
        sheet.Hyperlinks[cell] = "https://example.com/";
        sheet.HyperlinkMetadata[cell] = new HyperlinkMetadata(
            LinkType: HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "Example");

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(cell, cell)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(cell).Should().Be(BlankValue.Instance);
        // The hyperlink (and its metadata) must survive a plain Clear Contents -- Excel only drops
        // it via Clear All / Clear Hyperlinks.
        sheet.Hyperlinks.Should().ContainKey(cell).WhoseValue.Should().Be("https://example.com/");
        sheet.HyperlinkMetadata.Should().ContainKey(cell);
    }

    [Fact]
    public void ClearContentsCommand_OnHyperlinkedCell_UndoRestoresOriginalValue()
    {
        // Sibling/no-regression case: undo must still bring back the original value and hyperlink
        // exactly as before, now that the hyperlink is no longer force-cleared and re-added.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var cell = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.SetCell(cell, Cell.FromValue(new TextValue("Visit site")));
        sheet.Hyperlinks[cell] = "https://example.com/";

        var command = new ClearContentsCommand(sheet.Id, new GridRange(cell, cell));
        command.Apply(ctx);
        command.Revert(ctx);

        sheet.GetValue(cell).Should().Be(new TextValue("Visit site"));
        sheet.Hyperlinks.Should().ContainKey(cell).WhoseValue.Should().Be("https://example.com/");
    }

    [Fact]
    public void ClearContentsCommand_CutSourceOnHyperlinkedCell_StillRemovesHyperlinkAtSource()
    {
        // No-regression: the cross-sheet Cut+Paste fallback clears the *source* range after the
        // destination has already been populated (hyperlink included), so the source's hyperlink
        // must still be removed there -- only the plain (non-cut) Clear Contents path now preserves it.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var cell = new CellAddress(sheet.Id, 1, 1); // A1

        sheet.SetCell(cell, Cell.FromValue(new TextValue("Visit site")));
        sheet.Hyperlinks[cell] = "https://example.com/";

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(cell, cell), isCutSource: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Hyperlinks.Should().NotContainKey(cell);
    }

    [Fact]
    public void ClearConditionalFormatsCommand_ClearingPartOfRuleRange_KeepsRuleOnRest()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Rule covers A1:A10; user selects only A1:A3 and clicks "Clear Rules from Selected Cells".
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var outcome = new ClearConditionalFormatsCommand(sheet.Id, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // The rule must still exist, now covering only the un-selected remainder A4:A10.
        sheet.ConditionalFormats.Should().ContainSingle();
        sheet.ConditionalFormats[0].AppliesTo.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 10, 1)));
        sheet.ConditionalFormats[0].AllRanges.Any(r => r.Contains(new CellAddress(sheet.Id, 1, 1)))
            .Should().BeFalse();
    }

    [Fact]
    public void ClearConditionalFormatsCommand_ClearingWholeRuleRange_RemovesRule()
    {
        // No-regression: when the selection fully covers the rule's range, the rule is still
        // removed entirely (nothing left of it to keep).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        // Selection is a superset of the rule's range.
        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 1));

        var outcome = new ClearConditionalFormatsCommand(sheet.Id, clearRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ClearConditionalFormatsCommand_ClearingPartialRange_UndoRestoresFullRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var command = new ClearConditionalFormatsCommand(sheet.Id, clearRange);
        command.Apply(ctx);
        command.Revert(ctx);

        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(rule);
        sheet.ConditionalFormats[0].AppliesTo.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 1)));
    }
}
