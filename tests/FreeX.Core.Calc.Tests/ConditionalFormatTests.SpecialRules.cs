using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void Top10_HighlightsTopRankedValues()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(20)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            AboveAverage = true,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
    }

    [Fact]
    public void Top10_HighlightsAllValuesTiedAtRankBoundary()
    {
        // Excel's Top/Bottom N highlights every cell whose value ranks within the top N,
        // ties included -- so more than N cells can end up highlighted when the Nth-ranked
        // value repeats. Top 1 over {10, 10, 5} must highlight BOTH 10s, not just the first.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(5)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            AboveAverage = true,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(
            new CellColor(198, 239, 206),
            "both cells tied for the top-1 slot must highlight, matching Excel's tie-inclusive Top/Bottom N");
        GetCell(vp, 3, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
    }

    [Fact]
    public void DuplicateValues_HighlightsOnlyRepeatedDisplayValues()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("North")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("South")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("north")));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132));
    }

    [Fact]
    public void TextBlankAndErrorRules_RenderVisibleStyles()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("urgent follow-up")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(BlankValue.Instance));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(ErrorValue.Value));

        var blue = new CellStyle { FillColor = new CellColor(189, 215, 238) };
        var red = new CellStyle { FillColor = new CellColor(255, 199, 206) };
        var gray = new CellStyle { FillColor = new CellColor(217, 217, 217) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormatIfTrue = blue
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 2,
            RuleType = CfRuleType.Blanks,
            FormatIfTrue = gray
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 3,
            RuleType = CfRuleType.Errors,
            FormatIfTrue = red
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(189, 215, 238));
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(217, 217, 217));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    [Fact]
    public void DateOccurring_HighlightsDatesInSelectedPeriod()
    {
        var (wb, sheet) = MakeWorkbook();
        var today = DateTime.Today;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(today.AddDays(-3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(today.AddDays(-8)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days",
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
    }

    [Fact]
    public void DuplicateValues_TreatsNumberAndTextWithSameDisplayAsDuplicates()
    {
        // Excel-parity round 6: measured against Range.DisplayFormat in real Excel -- Duplicate Values
        // compares what the cells display, so the number 1 and the text "1" ARE duplicates of each other
        // (an earlier round assumed Excel keyed by type and value; Excel does not).
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("1")));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "the numeric 1 displays the same as the text \"1\"");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "the text \"1\" displays the same as the numeric 1");
    }

    [Fact]
    public void DuplicateValues_DoesNotTreatBooleanAndTextWithSameDisplayAsDuplicates()
    {
        // Same bug class as above: boolean TRUE and the text "TRUE" must not collide.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new BoolValue(true)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("TRUE")));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
    }

    [Fact]
    public void AboveAverage_WithStdDevBand_FlagsOnlyValuesBeyondOneStdDev()
    {
        // "N standard deviations above average" — mean + 1*stdDev, not the plain mean.
        // Values: 1, 2, 3, 4, 100. Mean = 22. Sample stdDev (STDEV) ≈ 43.716.
        // mean + 1*stdDev ≈ 65.716, so only 100 is above the band; 1-4 are not (even
        // though they're all below the plain mean too, this asserts the *band* math).
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new NumberValue(100)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            StdDevCount = 1,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 3, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 4, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 5, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206),
            "100 is more than mean+1*stdDev above the range average");
    }

    [Fact]
    public void DuplicateValues_UsesDateValuesInsteadOfTreatingDatesAsBlank()
    {
        var (wb, sheet) = MakeWorkbook();
        var today = DateTime.Today;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(today));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(today.AddDays(1)));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
    }
}
