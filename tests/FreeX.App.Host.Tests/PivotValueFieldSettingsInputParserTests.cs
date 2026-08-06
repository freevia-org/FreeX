using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotValueFieldPlannerTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("14", 14)]
    [InlineData(" 165 ", 165)]
    public void TryParseOptionalNumberFormatId_AcceptsBlankOrWholeNumber(string input, int? expected)
    {
        PivotValueFieldPlanner.TryParseOptionalNumberFormatId(input, out var numberFormatId)
            .Should().BeTrue();

        numberFormatId.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void TryParseOptionalNumberFormatId_RejectsNonIntegers(string input)
    {
        PivotValueFieldPlanner.TryParseOptionalNumberFormatId(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(" #,##0.0 \"kg\" ", "#,##0.0 \"kg\"")]
    public void ResolveOptionalNumberFormatCode_TrimsBlankToNull(string input, string? expected)
    {
        PivotValueFieldPlanner.ResolveOptionalNumberFormatCode(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(4, null, 4)]
    [InlineData(null, "#,##0.0 \"kg\"", 164)]
    [InlineData(4, "#,##0.0 \"kg\"", 164)]
    [InlineData(165, "#,##0.0 \"kg\"", 165)]
    public void ResolveNumberFormatIdForCode_AssignsCustomIdWhenFormatCodeIsPresent(
        int? inputId,
        string? inputCode,
        int? expected)
    {
        PivotValueFieldPlanner.ResolveNumberFormatIdForCode(inputId, inputCode).Should().Be(expected);
    }

    [Theory]
    [InlineData("General", null)]
    [InlineData("Number", 2)]
    [InlineData("Number 0 decimals", 1)]
    [InlineData("Number with thousands", 4)]
    [InlineData("Comma 0 decimals", 3)]
    [InlineData("Comma red negatives", 38)]
    [InlineData("Percentage", 10)]
    [InlineData("Percentage 0 decimals", 9)]
    [InlineData("Date", 14)]
    [InlineData("Day Month", 16)]
    [InlineData("Month Year", 17)]
    [InlineData("Currency", 7)]
    [InlineData("Currency red negatives", 8)]
    [InlineData("Short Date", 14)]
    [InlineData("Long Date", 15)]
    [InlineData("Time", 21)]
    [InlineData("Time AM/PM", 18)]
    [InlineData("Elapsed Time", 46)]
    [InlineData("Fraction", 12)]
    [InlineData("Fraction two digits", 13)]
    [InlineData("Scientific", 11)]
    [InlineData("Scientific compact", 48)]
    [InlineData("Text", 49)]
    [InlineData("Accounting 0 decimals", 42)]
    [InlineData("Accounting no symbol", 43)]
    [InlineData("Accounting no symbol 0 decimals", 41)]
    [InlineData("Accounting", 44)]
    public void ResolvePresetNumberFormatId_MapsExcelStylePresetLabels(string label, int? expected)
    {
        PivotValueFieldPlanner.ResolvePresetNumberFormatId(label).Should().Be(expected);
    }

    [Theory]
    [InlineData("General", "General")]
    [InlineData("Number", "0.00")]
    [InlineData("Number with thousands", "#,##0.00")]
    [InlineData("Currency", "$#,##0.00")]
    [InlineData("Currency red negatives", "$#,##0.00;[Red]($#,##0.00)")]
    [InlineData("Short Date", "m/d/yy")]
    [InlineData("Elapsed Time", "[h]:mm:ss")]
    [InlineData("Scientific compact", "##0.0E+0")]
    [InlineData("Text", "@")]
    [InlineData("Accounting 0 decimals", "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)")]
    [InlineData("Accounting no symbol", "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)")]
    [InlineData("Accounting no symbol 0 decimals", "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)")]
    public void ResolvePresetNumberFormatCode_MapsExcelStylePresetLabels(string label, string expected)
    {
        PivotValueFieldPlanner.ResolvePresetNumberFormatCode(label).Should().Be(expected);
    }

    [Theory]
    [InlineData("General", null)]
    [InlineData("0.00", 2)]
    [InlineData("#,##0.00", 4)]
    [InlineData("$#,##0.00", 7)]
    [InlineData("$#,##0.00;[Red]($#,##0.00)", 8)]
    [InlineData("m/d/yy", 14)]
    [InlineData("[h]:mm:ss", 46)]
    [InlineData("#,##0.0 \"kg\"", null)]
    public void ResolveBuiltInNumberFormatIdForCode_MapsKnownPresetCodes(string formatCode, int? expected)
    {
        PivotValueFieldPlanner.ResolveBuiltInNumberFormatIdForCode(formatCode).Should().Be(expected);
    }

    [Fact]
    public void NumberFormatPresets_ExposeExcelStyleLabels()
    {
        PivotValueFieldPlanner.NumberFormatPresets
            .Select(preset => preset.Label)
            .Should()
            .Contain([
                "General",
                "Number 0 decimals",
                "Number",
                "Comma 0 decimals",
                "Number with thousands",
                "Comma red negatives",
                "Currency",
                "Currency red negatives",
                "Accounting 0 decimals",
                "Accounting no symbol",
                "Accounting no symbol 0 decimals",
                "Accounting",
                "Date",
                "Short Date",
                "Long Date",
                "Day Month",
                "Month Year",
                "Time",
                "Time AM/PM",
                "Elapsed Time",
                "Percentage",
                "Percentage 0 decimals",
                "Fraction",
                "Fraction two digits",
                "Scientific",
                "Scientific compact",
                "Text"
            ]);
    }

    [Fact]
    public void NumberFormatPresets_PreferShortDateAsCanonicalBuiltIn14Label()
    {
        var firstBuiltIn14 = PivotValueFieldPlanner.NumberFormatPresets
            .First(preset => preset.NumberFormatId == 14);

        firstBuiltIn14.Label.Should().Be("Short Date");
    }

    [Fact]
    public void NumberFormatPresets_UseSharedBuiltInCatalogCodes()
    {
        foreach (var preset in PivotValueFieldPlanner.NumberFormatPresets)
        {
            BuiltInNumberFormatCatalog.TryResolveFormatCode(preset.NumberFormatId, out var formatCode)
                .Should().BeTrue();

            preset.FormatCode.Should().Be(formatCode);
        }
    }

    [Theory]
    [InlineData(PivotShowValuesAs.RunningTotalIn)]
    [InlineData(PivotShowValuesAs.RankSmallest)]
    [InlineData(PivotShowValuesAs.PercentOfParentTotal)]
    public void TryValidateShowValuesAs_RequiresBaseFieldForBaseFieldModes(PivotShowValuesAs showValuesAs)
    {
        PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, null, null, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Select a base field for this Show Values As calculation.");

        PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, 1, null, out error)
            .Should()
            .BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(PivotShowValuesAs.DifferenceFrom)]
    [InlineData(PivotShowValuesAs.PercentDifferenceFrom)]
    public void TryValidateShowValuesAs_RequiresBaseItemForDifferenceModes(PivotShowValuesAs showValuesAs)
    {
        PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, 1, "", out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a base item for this Show Values As calculation.");

        PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, 1, "Q1", out error)
            .Should()
            .BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void DialogPlanner_MapsSelectionsAndCreatesResult()
    {
        var field = new PivotDataFieldModel(0, "Amount", "count")
        {
            ShowValuesAs = PivotShowValuesAs.None
        };

        PivotValueFieldPlanner.FindSummaryFunctionIndex("average").Should().Be(2);
        PivotValueFieldPlanner.FindShowValuesAsIndex(PivotShowValuesAs.DifferenceFrom).Should().Be(5);
        PivotValueFieldPlanner.FindBaseFieldIndex(1, sourceHeaderCount: 3).Should().Be(2);
        PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(PivotShowValuesAs.PercentOfGrandTotal).Should().BeFalse();

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            sourceHeaders: [],
            customName: " Average Amount ",
            summaryFunctionIndex: 2,
            showValuesAsIndex: 5,
            baseFieldSelectedIndex: 3,
            baseItemText: " Q1 ",
            numberFormatId: 4,
            numberFormatCode: "#,##0.00");

        result.Name.Should().Be("Average Amount");
        result.SummaryFunction.Should().Be("average");
        result.ShowValuesAs.Should().Be(PivotShowValuesAs.DifferenceFrom);
        result.BaseFieldIndex.Should().Be(2);
        result.BaseItem.Should().Be("Q1");
        result.NumberFormatId.Should().Be(4);
        result.NumberFormatCode.Should().Be("#,##0.00");
    }

    [Fact]
    public void DialogPlanner_UpdatesAutoGeneratedCaptionWhenSummaryFunctionChanges()
    {
        var field = new PivotDataFieldModel(1, "Sum of Loan Amount", "sum");

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            sourceHeaders: ["Region", "Loan Amount"],
            customName: "Sum of Loan Amount",
            summaryFunctionIndex: 1,
            showValuesAsIndex: 0,
            baseFieldSelectedIndex: 0,
            baseItemText: null,
            numberFormatId: null,
            numberFormatCode: null);

        result.Name.Should().Be("Count of Loan Amount");
        result.SummaryFunction.Should().Be("count");
    }

    [Fact]
    public void DialogPlanner_PreservesExplicitCustomCaptionWhenSummaryFunctionChanges()
    {
        var field = new PivotDataFieldModel(1, "Loan Total", "sum");

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            sourceHeaders: ["Region", "Loan Amount"],
            customName: "Loan Total",
            summaryFunctionIndex: 1,
            showValuesAsIndex: 0,
            baseFieldSelectedIndex: 0,
            baseItemText: null,
            numberFormatId: null,
            numberFormatCode: null);

        result.Name.Should().Be("Loan Total");
        result.SummaryFunction.Should().Be("count");
    }
}
