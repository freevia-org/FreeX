using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r434: data-bar and icon-set conditional formats must survive an .xlsx round trip.
///
/// <para>The two remaining rule types after r421 (cell-value rules) and r433 (colour scales). Both
/// share the family's failure mode -- a rule that loses a field still paints, just differently -- but
/// each has a twist of its own.</para>
///
/// <para>A data bar that loses <c>ShowValue</c> hides the numbers behind the bars, leaving a column
/// the reader can only compare by eye. An icon set that loses <c>Reverse</c> inverts the meaning of
/// every icon in the range: green ticks become red crosses on exactly the rows the author was
/// flagging as good. Neither looks broken; the second is actively misleading.</para>
///
/// <para>Several fields here default to TRUE -- <c>DataBarShowValue</c>, <c>DataBarGradient</c>,
/// <c>IconSetShowValue</c> -- so each is probed with the opposite of its own default, the rule this
/// suite has now needed in eight separate models.</para>
/// </summary>
public sealed class R434_DataBarAndIconSetRulesReachTheFileTests
{
    private static ConditionalFormat? RoundTrip(ConditionalFormat rule)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        rule.AppliesTo = GridRange.Parse("A1:A5", sheet.Id);
        sheet.ConditionalFormats.Add(rule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets[0].ConditionalFormats.FirstOrDefault();
    }

    [Fact]
    public void ADataBarKeepsItsColourAndBounds()
    {
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(200, 30, 60),
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "5",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "95",
        });

        reloaded.Should().NotBeNull("the rule must survive before its fields can be compared");
        reloaded!.DataBarColor.Should().Be(new RgbColor(200, 30, 60), "the bar colour is the rule's whole appearance");
        reloaded.DataBarMinThresholdType.Should().Be(CfThresholdType.Number);
        reloaded.DataBarMinThresholdValue.Should().Be("5", "bounds decide how long each bar is drawn");
        reloaded.DataBarMaxThresholdValue.Should().Be("95");
    }

    [Fact]
    public void ADataBarKeepsItsDefaultTrueFlagsWhenTurnedOff()
    {
        // DataBarShowValue and DataBarGradient both default to TRUE, so probing them as true would
        // pass against a writer that emitted nothing. Turning them OFF is the case that can fail.
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = false,
            DataBarGradient = false,
        });

        reloaded!.DataBarShowValue.Should().BeFalse(
            "hiding the numbers is a deliberate choice; losing it leaves a column comparable only by eye");
        reloaded.DataBarGradient.Should().BeFalse("gradient defaults to true, so losing this looks like nothing happened");
    }

    [Fact]
    public void AnIconSetKeepsItsStyleAndDirection()
    {
        // Reverse is the sharpest field in this file: inverting it turns the author's "good" rows
        // red and their "bad" rows green, while the sheet still looks like a working icon set.
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetReverse = true,
            IconSetShowValue = false,
        });

        reloaded!.IconSetStyle.Should().Be("3TrafficLights1", "the style is which icons appear at all");
        reloaded.IconSetReverse.Should().BeTrue(
            "a lost reverse flag inverts every icon's meaning while still looking like a working rule");
        reloaded.IconSetShowValue.Should().BeFalse("this defaults to true, so losing it is invisible");
    }

    [Fact]
    public void APlainRuleGainsNoBarOrIcons()
    {
        // Every assertion above checks that something set survives, so a reader that invented a data
        // bar or icon set would satisfy them all -- and an invented rule paints a sheet the author
        // left unformatted.
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
        });

        reloaded!.RuleType.Should().Be(CfRuleType.CellValue, "a cell-value rule must not come back as a data bar");
        reloaded.IconSetStyle.Should().BeNull("a rule with no icon set must not acquire one");
    }
}
