using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
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
