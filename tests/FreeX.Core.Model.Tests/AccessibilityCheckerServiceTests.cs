using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, .. parts]);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find workspace file: {Path.Combine(parts)}");
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
