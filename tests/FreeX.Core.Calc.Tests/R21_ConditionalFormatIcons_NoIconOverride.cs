using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R21-conditional-format-render-2: an icon-set rule with a per-bucket "No Cell Icon" override
/// (CfIconOverride("NoIcons", ...)) must suppress the icon entirely for cells that fall into that
/// bucket, matching Excel, instead of falling back to drawing the bucket's raw icon-set glyph.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void R21_IconSet_NoIconsOverride_SuppressesIconForThatBucket()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "70")
        ]);
        // Middle bucket (value 50) is overridden to "No Cell Icon" via the dialog's sentinel
        // CfIconOverride("NoIcons", 0); the other two buckets keep their default traffic-light icons.
        cf.IconOverrides.AddRange([
            new CfIconOverride("3TrafficLights1", 0),
            new CfIconOverride("NoIcons", 0),
            new CfIconOverride("3TrafficLights1", 2)
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Values 10, 50, 90 with thresholds at 40% (42) and 70% (66) of range [10,90] → buckets 0, 1, 2.
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().BeNull("the 'No Cell Icon' override must suppress the icon entirely, not draw a fallback arrow glyph");
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));
    }
}
