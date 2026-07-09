using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotValueFieldPlannerTests
{
    private static readonly string[] Headers = ["Region", "Product", "Amount"];

    [Fact]
    public void AutomaticBaseField_CarriesResourceKeyAndFallback()
    {
        PivotValueFieldPlanner.AutomaticBaseField.ResourceKey
            .Should().Be("PivotValueFieldSettings_AutomaticBaseField");
        PivotValueFieldPlanner.AutomaticBaseField.FallbackText.Should().Be("(Automatic)");
        PivotValueFieldPlanner.AutomaticBaseFieldLabel.Should().Be("(Automatic)");
    }

    [Fact]
    public void SummaryFunctions_CarryResourceKeysFallbacksAndTokensInOrder()
    {
        PivotValueFieldPlanner.SummaryFunctions
            .Select(option => (option.ResourceKey, option.Label, option.Value))
            .Should()
            .Equal([
                ("PivotValueFieldSettings_SummarySum", "Sum", "sum"),
                ("PivotValueFieldSettings_SummaryCount", "Count", "count"),
                ("PivotValueFieldSettings_SummaryAverage", "Average", "average"),
                ("PivotValueFieldSettings_SummaryMax", "Max", "max"),
                ("PivotValueFieldSettings_SummaryMin", "Min", "min"),
                ("PivotValueFieldSettings_SummaryProduct", "Product", "product"),
                ("PivotValueFieldSettings_SummaryCountNumbers", "Count Numbers", "countNums"),
                ("PivotValueFieldSettings_SummaryStdDev", "StdDev", "stdDev"),
                ("PivotValueFieldSettings_SummaryStdDevp", "StdDevp", "stdDevP"),
                ("PivotValueFieldSettings_SummaryVar", "Var", "var"),
                ("PivotValueFieldSettings_SummaryVarp", "Varp", "varP"),
            ]);

        var (label, value) = PivotValueFieldPlanner.SummaryFunctions[0];
        label.Should().Be("Sum");
        value.Should().Be("sum");
    }

    [Fact]
    public void ShowValuesAsOptions_CarryResourceKeysFallbacksAndValuesInOrder()
    {
        PivotValueFieldPlanner.ShowValuesAsOptions
            .Select(option => (option.ResourceKey, option.Label, option.Value))
            .Should()
            .Equal([
                ("PivotValueFieldSettings_ShowNoCalculation", "No Calculation", PivotShowValuesAs.None),
                ("PivotValueFieldSettings_ShowPercentOfGrandTotal", "% of Grand Total", PivotShowValuesAs.PercentOfGrandTotal),
                ("PivotValueFieldSettings_ShowPercentOfRowTotal", "% of Row Total", PivotShowValuesAs.PercentOfRowTotal),
                ("PivotValueFieldSettings_ShowPercentOfColumnTotal", "% of Column Total", PivotShowValuesAs.PercentOfColumnTotal),
                ("PivotValueFieldSettings_ShowRunningTotalIn", "Running Total In", PivotShowValuesAs.RunningTotalIn),
                ("PivotValueFieldSettings_ShowDifferenceFrom", "Difference From", PivotShowValuesAs.DifferenceFrom),
                ("PivotValueFieldSettings_ShowPercentDifferenceFrom", "% Difference From", PivotShowValuesAs.PercentDifferenceFrom),
                ("PivotValueFieldSettings_ShowRankSmallest", "Rank Smallest to Largest", PivotShowValuesAs.RankSmallest),
                ("PivotValueFieldSettings_ShowRankLargest", "Rank Largest to Smallest", PivotShowValuesAs.RankLargest),
                ("PivotValueFieldSettings_ShowIndex", "Index", PivotShowValuesAs.Index),
                ("PivotValueFieldSettings_ShowPercentOfParentRowTotal", "% of Parent Row Total", PivotShowValuesAs.PercentOfParentRowTotal),
                ("PivotValueFieldSettings_ShowPercentOfParentColumnTotal", "% of Parent Column Total", PivotShowValuesAs.PercentOfParentColumnTotal),
                ("PivotValueFieldSettings_ShowPercentOfParentTotal", "% of Parent Total", PivotShowValuesAs.PercentOfParentTotal),
            ]);
    }

    [Fact]
    public void ValidationErrors_CarryResourceKeysAndFallbacks()
    {
        PivotValueFieldPlanner.DescribeValidationError(PivotShowValuesAsValidationError.None).Should().BeNull();

        PivotValueFieldPlanner.ValidationErrors.Should().Equal([
            new PivotValueFieldValidationErrorPlan(
                PivotShowValuesAsValidationError.MissingBaseField,
                "PivotValueFieldSettings_SelectBaseFieldMessage",
                "Select a base field for the chosen calculation."),
            new PivotValueFieldValidationErrorPlan(
                PivotShowValuesAsValidationError.MissingBaseItem,
                "PivotValueFieldSettings_EnterBaseItemMessage",
                "Enter a base item for the chosen calculation."),
        ]);

        PivotValueFieldPlanner.DescribeValidationError(PivotShowValuesAsValidationError.MissingBaseField)
            .Should()
            .Be(PivotValueFieldPlanner.ValidationErrors[0]);
    }

    [Fact]
    public void NumberFormatPresets_CarryResourceKeysFallbacksIdsAndCodesInOrder()
    {
        PivotValueFieldPlanner.NumberFormatPresets
            .Select(preset => (preset.ResourceKey, preset.Label, preset.NumberFormatId))
            .Should()
            .Contain([
                ("PivotValueFieldSettings_NumberFormatGeneral", "General", null),
                ("PivotValueFieldSettings_NumberFormatNumber0Decimals", "Number 0 decimals", 1),
                ("PivotValueFieldSettings_NumberFormatNumber", "Number", 2),
                ("PivotValueFieldSettings_NumberFormatNumberWithThousands", "Number with thousands", 4),
                ("PivotValueFieldSettings_NumberFormatCurrency", "Currency", 7),
                ("PivotValueFieldSettings_NumberFormatShortDate", "Short Date", 14),
                ("PivotValueFieldSettings_NumberFormatElapsedTime", "Elapsed Time", 46),
                ("PivotValueFieldSettings_NumberFormatText", "Text", 49),
            ]);

        foreach (var preset in PivotValueFieldPlanner.NumberFormatPresets)
        {
            BuiltInNumberFormatCatalog.TryResolveFormatCode(preset.NumberFormatId, out var formatCode)
                .Should().BeTrue();
            preset.FormatCode.Should().Be(formatCode);
        }
    }

    [Fact]
    public void NumberFormatPresets_PreferShortDateAsCanonicalBuiltIn14Label()
    {
        var firstBuiltIn14 = PivotValueFieldPlanner.NumberFormatPresets
            .First(preset => preset.NumberFormatId == 14);

        firstBuiltIn14.Label.Should().Be("Short Date");
    }

    [Theory]
    [InlineData("sum", 0)]
    [InlineData("average", 2)]
    [InlineData("varP", 10)]
    [InlineData("unknown", 0)]
    [InlineData(null, 0)]
    public void FindSummaryFunctionIndex_MatchesTokenCaseInsensitively(string? token, int expected) =>
        PivotValueFieldPlanner.FindSummaryFunctionIndex(token).Should().Be(expected);

    [Fact]
    public void SummaryFunctionFromIndex_ClampsOutOfRange()
    {
        PivotValueFieldPlanner.SummaryFunctionFromIndex(-5).Should().Be("sum");
        PivotValueFieldPlanner.SummaryFunctionFromIndex(999).Should().Be("varP");
    }

    [Fact]
    public void FindShowValuesAsIndex_RoundTripsWithFromIndex()
    {
        var index = PivotValueFieldPlanner.FindShowValuesAsIndex(PivotShowValuesAs.PercentOfColumnTotal);
        PivotValueFieldPlanner.ShowValuesAsFromIndex(index).Should().Be(PivotShowValuesAs.PercentOfColumnTotal);
    }

    [Fact]
    public void FindBaseFieldIndex_AddsOneForRealField_ZeroForAutomatic()
    {
        PivotValueFieldPlanner.FindBaseFieldIndex(1, Headers.Length).Should().Be(2);
        PivotValueFieldPlanner.FindBaseFieldIndex(null, Headers.Length).Should().Be(0);
        PivotValueFieldPlanner.FindBaseFieldIndex(99, Headers.Length).Should().Be(0);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(14, 14)]
    [InlineData(999, 0)]
    public void FindNumberFormatPresetIndex_MatchesFirstPresetById(int? numberFormatId, int expectedIndex)
    {
        PivotValueFieldPlanner.FindNumberFormatPresetIndex(numberFormatId)
            .Should()
            .Be(expectedIndex);
    }

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
    [InlineData("Number with thousands", 4)]
    [InlineData("Currency", 7)]
    [InlineData("Short Date", 14)]
    [InlineData("Elapsed Time", 46)]
    [InlineData("Text", 49)]
    public void ResolvePresetNumberFormatId_MapsFallbackLabels(string label, int? expected)
    {
        PivotValueFieldPlanner.ResolvePresetNumberFormatId(label).Should().Be(expected);
    }

    [Theory]
    [InlineData("General", "General")]
    [InlineData("Number", "0.00")]
    [InlineData("Number with thousands", "#,##0.00")]
    [InlineData("Currency", "$#,##0.00_);($#,##0.00)")]
    [InlineData("Currency red negatives", "$#,##0.00_);[Red]($#,##0.00)")]
    [InlineData("Short Date", "m/d/yy")]
    [InlineData("Elapsed Time", "[h]:mm:ss")]
    [InlineData("Scientific compact", "##0.0E+0")]
    [InlineData("Text", "@")]
    public void ResolvePresetNumberFormatCode_MapsFallbackLabels(string label, string expected)
    {
        PivotValueFieldPlanner.ResolvePresetNumberFormatCode(label).Should().Be(expected);
    }

    [Theory]
    [InlineData("General", null)]
    [InlineData("0.00", 2)]
    [InlineData("#,##0.00", 4)]
    [InlineData("$#,##0.00_);($#,##0.00)", 7)]
    [InlineData("$#,##0.00_);[Red]($#,##0.00)", 8)]
    [InlineData("m/d/yy", 14)]
    [InlineData("[h]:mm:ss", 46)]
    [InlineData("#,##0.0 \"kg\"", null)]
    public void ResolveBuiltInNumberFormatIdForCode_MapsKnownPresetCodes(string formatCode, int? expected)
    {
        PivotValueFieldPlanner.ResolveBuiltInNumberFormatIdForCode(formatCode).Should().Be(expected);
    }

    [Theory]
    [InlineData(PivotShowValuesAs.None, false)]
    [InlineData(PivotShowValuesAs.PercentOfGrandTotal, false)]
    [InlineData(PivotShowValuesAs.RunningTotalIn, true)]
    [InlineData(PivotShowValuesAs.DifferenceFrom, true)]
    [InlineData(PivotShowValuesAs.PercentOfParentTotal, true)]
    public void ShowValuesAsRequiresBaseField_MatchesExcel(PivotShowValuesAs showValuesAs, bool expected) =>
        PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(showValuesAs).Should().Be(expected);

    [Fact]
    public void TryValidateShowValuesAs_RequiresBaseField()
    {
        PivotValueFieldPlanner.ValidateShowValuesAs(PivotShowValuesAs.RunningTotalIn, null, null)
            .Should()
            .Be(PivotShowValuesAsValidationError.MissingBaseField);
        PivotValueFieldPlanner.TryValidateShowValuesAs(PivotShowValuesAs.RunningTotalIn, null, null, out var error)
            .Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();

        PivotValueFieldPlanner.ValidateShowValuesAs(PivotShowValuesAs.RunningTotalIn, 1, null)
            .Should()
            .Be(PivotShowValuesAsValidationError.None);
        PivotValueFieldPlanner.TryValidateShowValuesAs(PivotShowValuesAs.RunningTotalIn, 1, null, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void TryValidateShowValuesAs_DifferenceFrom_RequiresBaseItem()
    {
        PivotValueFieldPlanner.ValidateShowValuesAs(PivotShowValuesAs.DifferenceFrom, 1, null)
            .Should()
            .Be(PivotShowValuesAsValidationError.MissingBaseItem);
        PivotValueFieldPlanner.TryValidateShowValuesAs(PivotShowValuesAs.DifferenceFrom, 1, null, out _)
            .Should().BeFalse();
        PivotValueFieldPlanner.TryValidateShowValuesAs(PivotShowValuesAs.DifferenceFrom, 1, "Q1", out _)
            .Should().BeTrue();
    }

    [Fact]
    public void ResolveBaseFieldIndexAndItem_ClearedWhenNotRequired()
    {
        PivotValueFieldPlanner.ResolveBaseFieldIndex(PivotShowValuesAs.None, 3).Should().BeNull();
        PivotValueFieldPlanner.ResolveBaseItem(PivotShowValuesAs.None, "x").Should().BeNull();

        PivotValueFieldPlanner.ResolveBaseFieldIndex(PivotShowValuesAs.RunningTotalIn, 3).Should().Be(2);
        PivotValueFieldPlanner.ResolveBaseItem(PivotShowValuesAs.DifferenceFrom, " Q1 ").Should().Be("Q1");
    }

    [Fact]
    public void CreateResult_KeepsCustomName_AndAppliesSummaryAndShowValuesAs()
    {
        var field = new PivotDataFieldModel(2, "Sum of Amount", "sum");

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            Headers,
            customName: "Total Sales",
            summaryFunctionIndex: PivotValueFieldPlanner.FindSummaryFunctionIndex("average"),
            showValuesAsIndex: PivotValueFieldPlanner.FindShowValuesAsIndex(PivotShowValuesAs.PercentOfGrandTotal),
            baseFieldSelectedIndex: 0,
            baseItemText: null);

        result.Name.Should().Be("Total Sales");
        result.SummaryFunction.Should().Be("average");
        result.ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfGrandTotal);
        result.SourceFieldIndex.Should().Be(2);
    }

    [Fact]
    public void CreateResult_RegeneratesDefaultCaption_WhenNameLeftAsAutoGenerated()
    {
        // "Sum of Amount" is the auto-generated caption; switching to Count regenerates "Count of Amount".
        var field = new PivotDataFieldModel(2, "Sum of Amount", "sum");

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            Headers,
            customName: "Sum of Amount",
            summaryFunctionIndex: PivotValueFieldPlanner.FindSummaryFunctionIndex("count"),
            showValuesAsIndex: 0,
            baseFieldSelectedIndex: 0,
            baseItemText: null);

        result.Name.Should().Be("Count of Amount");
    }

    [Fact]
    public void CreateResult_BlankName_FallsBackToDefaultCaption()
    {
        var field = new PivotDataFieldModel(2, "Total Sales", "sum");

        var result = PivotValueFieldPlanner.CreateResult(
            field, Headers, customName: "   ", summaryFunctionIndex: 0, showValuesAsIndex: 0,
            baseFieldSelectedIndex: 0, baseItemText: null);

        result.Name.Should().Be("Sum of Amount");
    }
}
