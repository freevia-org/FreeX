using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r421: a conditional-format rule must survive an .xlsx round trip with the fields that decide
/// WHICH cells it highlights and HOW.
///
/// <para>r418 only established that a rule survives at all, which is the weakest useful claim. A rule
/// that comes back having lost its operator or its formula is worse than one that vanishes: the
/// sheet still shows highlighting, so nothing looks broken, but the wrong cells are highlighted --
/// and the user reads the colour as fact. A vanished rule at least prompts someone to recreate it.</para>
///
/// <para>Each case sets values distinct from the model's defaults. `Priority` defaults to 1,
/// `UseThreeColorScale` to false, and the colour-scale colours to a green/yellow/red set, so probes
/// equal to those would round-trip through a writer that emitted nothing.</para>
/// </summary>
public sealed class R421_ConditionalFormatRulesReachTheFileTests
{
    private static ConditionalFormat? RoundTrip(ConditionalFormat rule)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        rule.AppliesTo = GridRange.Parse("A1:A5", sheet.Id);
        sheet.ConditionalFormats.Add(rule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets[0].ConditionalFormats.FirstOrDefault();
    }

    [Theory]
    [InlineData(CfOperator.GreaterThan)]
    [InlineData(CfOperator.LessThan)]
    [InlineData(CfOperator.Equal)]
    [InlineData(CfOperator.NotEqual)]
    [InlineData(CfOperator.Between)]
    public void TheOperatorSurvives(CfOperator op)
    {
        // The operator decides which cells match. Losing it does not remove the highlighting, it
        // moves it -- which is why this is asserted per-operator rather than once.
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = op,
            Value1 = "3",
            Value2 = op == CfOperator.Between ? "4" : null,
        });

        reloaded.Should().NotBeNull("the rule must survive before its operator can be compared");
        reloaded!.Operator.Should().Be(op, "a rule with the wrong operator highlights the wrong cells");
    }

    [Fact]
    public void TheComparisonValueSurvives()
    {
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "42",
        });

        reloaded!.Value1.Should().Be("42", "the threshold is what the operator is comparing against");
    }

    [Fact]
    public void BothBoundsOfABetweenRuleSurvive()
    {
        // A Between rule that keeps only Value1 still highlights, with an open upper bound.
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "2",
            Value2 = "4",
        });

        reloaded!.Value1.Should().Be("2");
        reloaded.Value2.Should().Be("4", "losing the upper bound silently widens the rule");
    }

    /// <summary>
    /// Each rule must keep ITS OWN priority, which is the property that decides which rule wins
    /// where two overlap.
    /// </summary>
    /// <remarks>
    /// This replaced an assertion that a lone rule keeps a literal priority of 7. Measured: a single
    /// rule is written as <c>priority="1"</c> whatever the model says, and that is harmless --
    /// priority has no meaning except relative to other rules, and Excel renumbers on save too. The
    /// literal was an incidental value the format does not promise.
    /// <para>The real property was broken. Rules written in document order with priorities 5, 1, 9
    /// came back with the rule owning 1 wearing 5 and the rule owning 5 wearing 1: each kept its own
    /// operator and value, but wore a neighbour's priority. Precedence between overlapping rules was
    /// therefore INVERTED on load, silently -- nothing looks wrong, the wrong rule's colour just
    /// wins. Fixed in the mapper by sorting the captured priorities (paired with their container
    /// attributes) to match the order ClosedXML enumerates rules in.</para>
    /// </remarks>
    [Fact]
    public void EachRuleKeepsItsOwnPriority()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        // Deliberately unsorted, so document order differs from priority order -- the condition
        // under which the pairing used to break.
        foreach (var (op, value, priority) in new[]
                 {
                     (CfOperator.GreaterThan, "1", 5),
                     (CfOperator.LessThan, "2", 1),
                     (CfOperator.Equal, "3", 9),
                 })
        {
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
                RuleType = CfRuleType.CellValue,
                Operator = op,
                Value1 = value,
                Priority = priority,
                FormatIfTrue = new CellStyle { Bold = true },
            });
        }

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream).Sheets[0].ConditionalFormats;

        reloaded.Should().HaveCount(3, "all three rules must survive before their pairing can be judged");

        // Keyed by the rule's own identity, not by position: the enumeration order legitimately
        // changes, and asserting on order would fail for a reason that does not matter.
        reloaded.Single(rule => rule.Value1 == "1").Priority.Should().Be(
            5, "the greater-than rule was written with priority 5 and must not inherit another's");
        reloaded.Single(rule => rule.Value1 == "2").Priority.Should().Be(
            1, "the less-than rule owns priority 1, which makes it the winner where the rules overlap");
        reloaded.Single(rule => rule.Value1 == "3").Priority.Should().Be(9);
    }

    [Fact]
    public void TheAppliedFormatSurvives()
    {
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(0xFF, 0xEE, 0x00) },
        });

        reloaded!.FormatIfTrue.Should().NotBeNull("a rule with no format highlights nothing at all");
        reloaded.FormatIfTrue!.Bold.Should().BeTrue();
        reloaded.FormatIfTrue.FillColor.Should().Be(new CellColor(0xFF, 0xEE, 0x00));
    }

    [Fact]
    public void AColourScaleKeepsItsColours()
    {
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = new RgbColor(10, 20, 30),
            MidColor = new RgbColor(40, 50, 60),
            MaxColor = new RgbColor(70, 80, 90),
        });

        reloaded!.UseThreeColorScale.Should().BeTrue("a three-stop scale read back as two changes the gradient");
        reloaded.MinColor.Should().Be(new RgbColor(10, 20, 30));
        reloaded.MidColor.Should().Be(new RgbColor(40, 50, 60));
        reloaded.MaxColor.Should().Be(new RgbColor(70, 80, 90));
    }

    /// <summary>
    /// A colour scale's THRESHOLDS decide which value gets which colour.
    /// </summary>
    /// <remarks>
    /// r433: this exists because the test above was originally called
    /// "AColourScaleKeepsItsColoursAndThresholds" and asserted only the colours -- it never set a
    /// threshold at all. A test whose name claims more than its body checks is the quietest way a
    /// suite lies: anyone auditing coverage by name would have ticked thresholds off and moved on.
    /// The name was corrected and the missing half written here.
    /// <para>Thresholds matter as much as the colours: the same three colours anchored to percentile
    /// 50 rather than to a number of 42 paint an entirely different sheet, and both look like a
    /// working colour scale.</para>
    /// <para>Probe values differ from the model's defaults, which are Min / Percentile-50 / Max --
    /// a probe equal to a default round-trips through a writer that emits nothing (the r424 rule).</para>
    /// </remarks>
    [Fact]
    public void AColourScaleKeepsItsThresholds()
    {
        var reloaded = RoundTrip(new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "10",
            MidThresholdType = CfThresholdType.Percent,
            MidThresholdValue = "60",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "90",
        });

        reloaded.Should().NotBeNull("the rule must survive before its thresholds can be compared");

        reloaded!.MinThresholdType.Should().Be(
            CfThresholdType.Number, "Min is the default, so a lost type silently reverts to auto-scaling");
        reloaded.MinThresholdValue.Should().Be("10");

        reloaded.MidThresholdType.Should().Be(CfThresholdType.Percent, "the midpoint anchors the middle colour");
        reloaded.MidThresholdValue.Should().Be("60", "60 differs from the model default of 50");

        reloaded.MaxThresholdType.Should().Be(CfThresholdType.Number);
        reloaded.MaxThresholdValue.Should().Be("90");
    }

    [Fact]
    public void AnUnruledSheetGainsNoRule()
    {
        // The control: every assertion above checks that something set survives, so a reader that
        // invented a rule per sheet would satisfy all of them.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        new XlsxFileAdapter().Load(stream).Sheets[0].ConditionalFormats
            .Should().BeEmpty("a sheet with no rules must not acquire one");
    }
}
