using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

// Round-149 finding accessibility-checks F1: AccessibilityCheckerService.Contrast.cs graded a
// matched conditional-format rule's contrast by swapping in rule.FormatIfTrue wholesale instead
// of merging it onto the cell's base style. A fill-only dxf (FreeX's own "highlight cells
// greater than" quick rule -- see MainWindow.DataFilterCommands.cs CfRuleButton_Click, which
// builds `FormatIfTrue = new CellStyle { FillColor = ... }` with nothing else set -- and many of
// Excel's built-in Highlight-Cell-Rules presets author dxfs the same way) therefore silently
// fabricated CellStyle's hard defaults (FontColor=Black, Bold=false) in place of the cell's real
// font, hiding real low-contrast text (e.g. a themed white heading over a matching CF fill)
// behind a fabricated black-on-fill pass. The real renderers (ConditionalFormatRenderEvaluator.
// ExtractStyle, ViewportConditionalFormatEvaluator.MergeStyles) already fall back to the base
// cell's font when the dxf doesn't carry an explicit override; the accessibility checker now
// does the same via the new MergeConditionalFormatContrastStyle helper.
public sealed class R149_AccessibilityCheckerFillOnlyConditionalFormatContrastTests
{
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenFillOnlyConditionalFormatMatchesWhiteFontCell()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var whiteFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.White
        });
        sheet.SetCell(address, new Cell
        {
            Value = new NumberValue(150),
            StyleId = whiteFontStyle
        });

        // Mirrors FreeX's own quick "highlight cells greater than" rule: only a fill color is
        // set on the dxf, nothing about the font.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 255, 0)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenSharedRangeAlwaysTrueFillOnlyRuleMatchesWhiteFontCell()
    {
        // Exercises the shared-applies-to-range "always true" fast path (NoBlanks/NoErrors,
        // GetAlwaysTrueTextValueStyle) rather than the per-cell rule loop, since that path
        // returns one precomputed style object for every cell in the range and had the same
        // wholesale-swap defect.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 1, 1);
        var whiteFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.White
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Status"),
            StyleId = whiteFontStyle
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.NoBlanks,
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 255, 0)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_DoesNotFlagLowContrastCellText_WhenFillOnlyConditionalFormatMatchesBlackFontCell()
    {
        // Sibling/no-regression case: a base cell that genuinely has (or defaults to) a black
        // font over a fill-only CF's light fill is still a real, sufficient-contrast pass -- the
        // fix must not turn every fill-only CF match into a false-positive low-contrast flag.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new Cell
        {
            Value = new NumberValue(150)
            // No StyleId set: falls back to the workbook's default style (black font).
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 255, 0)
            }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenConditionalFormatDxfExplicitlyOverridesToLowContrastFont()
    {
        // Sibling/no-regression case: a dxf that DOES carry its own explicit (non-default) font
        // color must still win over the base cell's font, exactly as before the fix.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var blackFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black
        });
        sheet.SetCell(address, new Cell
        {
            Value = new NumberValue(150),
            StyleId = blackFontStyle
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
    }
}
