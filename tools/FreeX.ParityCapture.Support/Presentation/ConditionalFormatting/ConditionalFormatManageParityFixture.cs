using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Deterministic state used by the cross-platform Manage Conditional Formatting visual-evidence
/// capture. Production openers continue to use the user's current worksheet and selection.
/// </summary>
public static class ConditionalFormatManageParityFixture
{
    public const int DialogWidth = 560;
    public const int DialogHeight = 420;

    public static GridRange CreateRange(SheetId sheetId) =>
        new(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 6, 3));

    public static IReadOnlyList<ConditionalFormat> CreateRules(SheetId sheetId)
    {
        var range = CreateRange(sheetId);
        var greaterThanOrEqualRule = new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThanOrEqual,
            Value1 = "1600",
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(156, 87, 0),
                FillColor = new CellColor(255, 235, 156)
            }
        };
        var dataBarRule = ConditionalFormatPresetGalleryPlanner.CreateDataBarRule("SolidBlue", range)
            ?? throw new InvalidOperationException("The parity fixture requires the SolidBlue data-bar preset.");
        dataBarRule.Priority = 2;

        return [greaterThanOrEqualRule, dataBarRule];
    }
}
