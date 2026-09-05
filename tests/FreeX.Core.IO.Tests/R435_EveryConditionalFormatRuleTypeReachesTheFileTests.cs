using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r435: every <see cref="CfRuleType"/> must come back as ITSELF after an .xlsx round trip.
///
/// <para>Exhaustive over the enum rather than a sample: r421 covered CellValue, r433 ColorScale and
/// r434 DataBar and IconSet, leaving twelve untested. Rule TYPE is the one field where a loss cannot
/// be subtle -- a DuplicateValues rule that returns as CellValue highlights a completely different
/// set of cells, and the sheet still looks deliberately formatted. The reader has no way to tell the
/// rule changed meaning.</para>
///
/// <para>Driven from <c>Enum.GetValues</c> so a rule type added later is covered the day it appears,
/// which is the difference between this and writing sixteen cases by hand.</para>
/// </summary>
public sealed class R435_EveryConditionalFormatRuleTypeReachesTheFileTests
{
    /// <summary>
    /// Gives each rule type the companion fields it needs to be meaningful. A text rule with no text,
    /// or a formula rule with no formula, describes nothing -- and a writer that skipped it would be
    /// correct to, which would make this sweep report false positives rather than defects. Same
    /// interdependence discipline as r419, r428 and r432.
    /// </summary>
    private static void Configure(ConditionalFormat rule)
    {
        switch (rule.RuleType)
        {
            case CfRuleType.CellValue:
                rule.Operator = CfOperator.GreaterThan;
                rule.Value1 = "1";
                break;

            case CfRuleType.Formula:
                rule.FormulaText = "A1>1";
                break;

            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
                rule.TextRuleText = "probe";
                break;

            case CfRuleType.DateOccurring:
                rule.DateOccurringPeriod = "today";
                break;

            case CfRuleType.ColorScale:
                rule.UseThreeColorScale = true;
                break;

            case CfRuleType.IconSet:
                rule.IconSetStyle = "3TrafficLights1";
                break;
        }
    }

    private static ConditionalFormat? RoundTrip(CfRuleType ruleType)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        var rule = new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
            RuleType = ruleType,
            FormatIfTrue = new CellStyle { Bold = true },
        };

        Configure(rule);
        sheet.ConditionalFormats.Add(rule);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream).Sheets[0].ConditionalFormats.FirstOrDefault();
    }

    [Fact]
    public void EveryRuleTypeSurvivesAsItself()
    {
        var ruleTypes = Enum.GetValues<CfRuleType>();

        ruleTypes.Should().HaveCountGreaterThanOrEqualTo(
            10, "the enum query must still reach the rule types rather than matching a shrunken set");

        var lost = new List<string>();

        foreach (var ruleType in ruleTypes)
        {
            var reloaded = RoundTrip(ruleType);

            if (reloaded is null)
                lost.Add($"{ruleType}: the rule vanished entirely");
            else if (reloaded.RuleType != ruleType)
                lost.Add($"{ruleType}: came back as {reloaded.RuleType}");
        }

        lost.Should().BeEmpty(
            "a rule that changes type highlights a different set of cells while still looking like " +
            "deliberate formatting, so nothing on the sheet says the meaning moved:\n" +
            string.Join("\n", lost));
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText)]
    [InlineData(CfRuleType.NotContainsText)]
    [InlineData(CfRuleType.BeginsWith)]
    [InlineData(CfRuleType.EndsWith)]
    public void ATextRuleKeepsTheTextItMatchesOn(CfRuleType ruleType)
    {
        // The type surviving is not enough for these four: a ContainsText rule that keeps its type
        // but loses its needle matches nothing, or everything, depending on how the reader treats an
        // empty pattern -- and either way it stops doing what the author asked.
        RoundTrip(ruleType)!.TextRuleText
            .Should().Be("probe", "the text is the rule's entire condition");
    }

    [Fact]
    public void AFormulaRuleKeepsItsFormula()
    {
        RoundTrip(CfRuleType.Formula)!.FormulaText
            .Should().NotBeNullOrEmpty("a formula rule with no formula has no condition left");
    }
}
