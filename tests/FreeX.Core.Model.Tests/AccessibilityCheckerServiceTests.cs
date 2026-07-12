using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void AccessibilityIssueKind_TaxonomyCoversCoreWorkbookContentFamilies()
    {
        Enum.GetNames<AccessibilityIssueKind>().Should().BeEquivalentTo(
        [
            nameof(AccessibilityIssueKind.MergedCells),
            nameof(AccessibilityIssueKind.MissingAltText),
            nameof(AccessibilityIssueKind.GenericAltText),
            nameof(AccessibilityIssueKind.ChartMissingTitle),
            nameof(AccessibilityIssueKind.GenericChartTitle),
            nameof(AccessibilityIssueKind.HyperlinkDisplayTextIsUrl),
            nameof(AccessibilityIssueKind.DefaultWorksheetName),
            nameof(AccessibilityIssueKind.HiddenSheetWithContent),
            nameof(AccessibilityIssueKind.HiddenRowWithContent),
            nameof(AccessibilityIssueKind.HiddenColumnWithContent),
            nameof(AccessibilityIssueKind.TableMissingHeaderText),
            nameof(AccessibilityIssueKind.TableDefaultHeaderText),
            nameof(AccessibilityIssueKind.TableDuplicateHeaderText),
            nameof(AccessibilityIssueKind.TableMissingHeaderRow),
            nameof(AccessibilityIssueKind.BlankRowOrColumnInTable),
            nameof(AccessibilityIssueKind.ChartMissingAxisTitle),
            nameof(AccessibilityIssueKind.GenericChartAxisTitle),
            nameof(AccessibilityIssueKind.LowContrastCellText),
            nameof(AccessibilityIssueKind.LowContrastChartText),
            nameof(AccessibilityIssueKind.LowContrastObjectText)
        ]);
    }

    private static void AddNoBlankContrastRule(
        Sheet sheet,
        CellAddress address,
        int priority,
        bool stopIfTrue,
        CellColor fontColor,
        CellColor fillColor)
    {
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            Priority = priority,
            RuleType = CfRuleType.NoBlanks,
            StopIfTrue = stopIfTrue,
            FormatIfTrue = new CellStyle
            {
                FontColor = fontColor,
                FillColor = fillColor
            }
        });
    }
}
