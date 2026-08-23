using System.Globalization;
using FluentAssertions;
using Free.Shared.IO;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

public sealed class LexicalNumericDeduplicationTests
{
    public static TheoryData<string, bool> PlainGroupingCases => new()
    {
        { "1234", true },
        { "1,234", true },
        { "12,345", true },
        { "123,456", true },
        { "1,234,567", true },
        { "+1,234", true },
        { "-1,234", true },
        { "1,234.56", true },
        { "1,234.5E+2", true },
        { "1234,567", false },
        { "1,23", false },
        { "1,2345", false },
        { ",123", false },
        { "1,,234", false },
        { "1,234,", false },
        { "1,23.56", false },
        { "$1,2", true },
        { " -1,2", true },
        { "1,23E+2", true },
        { "1.2,34", true },
    };

    public static TheoryData<string, NumberStyles, bool> StyledGroupingCases => new()
    {
        { "1,2", NumberStyles.Float, true },
        { "1,234", NumberStyles.Number, true },
        { "1,2", NumberStyles.Number, false },
        { "  -1,234  ", NumberStyles.Number, true },
        { "  -1,2  ", NumberStyles.Number, false },
        { "$1,234", NumberStyles.Currency, true },
        { "$1,2", NumberStyles.Currency, false },
        { "  $1,234", NumberStyles.Currency, true },
        { "  $1,2", NumberStyles.Currency, false },
        { "(1,234)", NumberStyles.Currency, true },
        { "(1,2)", NumberStyles.Currency, false },
        { "(1,2.5)", NumberStyles.Currency, true },
        { "$1,2", NumberStyles.Number, true },
        { "1,2$", NumberStyles.Currency, true },
    };

    [Theory]
    [MemberData(nameof(PlainGroupingCases))]
    public void NumericGroupingValidator_PreservesPlainCallerSemantics(string field, bool expected)
    {
        var format = CreateNumberFormat(",", ".");

        NumericTextGroupingValidator.HasValidGroupingShape(field, format).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(StyledGroupingCases))]
    public void NumericGroupingValidator_PreservesStyleAwareDelimitedSemantics(
        string field,
        NumberStyles styles,
        bool expected)
    {
        var format = CreateNumberFormat(",", ".", "$", [3]);

        NumericTextGroupingValidator.HasValidGroupingShape(field, styles, format).Should().Be(expected);
    }

    [Theory]
    [InlineData("1__234", true)]
    [InlineData("1__23", false)]
    [InlineData("12__345__678", true)]
    public void NumericGroupingValidator_SupportsMultiCharacterSeparators(string field, bool expected)
    {
        var format = CreateNumberFormat("__", ".");

        NumericTextGroupingValidator.HasValidGroupingShape(field, format).Should().Be(expected);
    }

    [Fact]
    public void NumericGroupingValidator_PreservesFixedThreeDigitPolicyAndEmptySeparatorBehavior()
    {
        var twoDigitCultureShape = CreateNumberFormat(",", ".", "$", [2]);
        NumericTextGroupingValidator.HasValidGroupingShape("1,23", twoDigitCultureShape)
            .Should().BeFalse();
        NumericTextGroupingValidator.HasValidGroupingShape("1,234", twoDigitCultureShape)
            .Should().BeTrue();

        var noSeparator = CreateNumberFormat("", ".");
        NumericTextGroupingValidator.HasValidGroupingShape("1,2", noSeparator)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("1", "1")]
    [InlineData(" 1 ", "1")]
    [InlineData("true", "1")]
    [InlineData("TRUE", "1")]
    [InlineData(" TrUe ", "1")]
    [InlineData("0", "0")]
    [InlineData(" 0 ", "0")]
    [InlineData("false", "0")]
    [InlineData("FALSE", "0")]
    [InlineData(" FaLsE ", "0")]
    [InlineData("2", null)]
    [InlineData("yes", null)]
    [InlineData("true false", null)]
    [InlineData("01", null)]
    [InlineData("+1", null)]
    public void NumericBooleanNormalizer_CanonicalizesOnlyOoxmlBooleanLexemes(
        string? value,
        string? expected)
    {
        XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric(value).Should().Be(expected);
    }

    [Fact]
    public void NumericBooleanNormalizer_RemainsDistinctFromLexicalPreservationHelper()
    {
        XlsxXmlNormalizationHelpers.NormalizeBoolean(" true ").Should().Be("true");
        XlsxXmlNormalizationHelpers.NormalizeBoolean("TRUE").Should().BeNull();
        XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric(" true ").Should().Be("1");
        XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric("TRUE").Should().Be("1");
    }

    [Fact]
    public void ProductionCallers_AdoptSharedGroupingAndNumericBooleanPolicies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var groupingCallers = new[]
        {
            ReadSource(root, "src", "FreeX.Core.IO", "DelimitedTextWorkbookReader.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "PdfTableReader.cs"),
            ReadSource(root, "src", "FreeX.Core.Model", "DataValidationNumericBoundText.cs"),
            ReadSource(root, "src", "FreeX.App.Services", "GoalSeekRequestParser.cs"),
        };
        groupingCallers.Should().OnlyContain(source =>
            source.Contains("NumericTextGroupingValidator.HasValidGroupingShape(", StringComparison.Ordinal) &&
            !source.Contains("private static bool HasValidGroupingShape", StringComparison.Ordinal));

        var numericBooleanCallers = new[]
        {
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxStylesheetSchemaNormalizer.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxWorksheetIgnoredErrorsNormalizer.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxWorksheetProtectionNormalizer.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxWorksheetScenarioNormalizer.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxWorksheetSheetFormatNormalizer.cs"),
            ReadSource(root, "src", "FreeX.Core.IO", "XlsxWorksheetSheetPropertiesNormalizer.cs"),
        };
        numericBooleanCallers.Should().OnlyContain(source =>
            source.Contains("XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric", StringComparison.Ordinal) &&
            !source.Contains("private static string? NormalizeBoolean(", StringComparison.Ordinal) &&
            !source.Contains("private static string? NormalizeBooleanOrNull(", StringComparison.Ordinal));

        var calculationProperties = ReadSource(
            root,
            "src",
            "FreeX.Core.IO",
            "XlsxWorksheetCalculationPropertyNormalizer.cs");
        calculationProperties.Should()
            .Contain("private static string? NormalizeBoolean(string? value)")
            .And.NotContain("NormalizeBooleanAsNumeric");
    }

    private static NumberFormatInfo CreateNumberFormat(
        string groupSeparator,
        string decimalSeparator,
        string currencySymbol = "$",
        int[]? groupSizes = null) =>
        new()
        {
            NumberGroupSeparator = groupSeparator,
            NumberDecimalSeparator = decimalSeparator,
            NumberGroupSizes = groupSizes ?? [3],
            CurrencyGroupSeparator = groupSeparator,
            CurrencyDecimalSeparator = decimalSeparator,
            CurrencyGroupSizes = groupSizes ?? [3],
            CurrencySymbol = currencySymbol,
        };

    private static string ReadSource(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
