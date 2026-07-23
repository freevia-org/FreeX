using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class NumberFormatterRegexCachePerformanceTests
{
    [Theory]
    [InlineData("NumberFormatColorMapper.cs")]
    [InlineData("NumberFormatter.cs")]
    [InlineData("NumberFormatter.DateTime.cs")]
    [InlineData("NumberFormatter.Fractions.cs")]
    [InlineData("NumberFormatter.Sections.cs")]
    public void HotNumberFormatterParsers_UseCachedRegexInstances(string fileName)
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource(fileName);

        source.Should().Contain("private static readonly Regex");
        source.Should().NotMatchRegex(StaticRegexCallPattern);
    }

    [Fact]
    public void HotNumberFormatterSectionSelection_ParsesSectionsInSinglePass()
    {
        var numberSource = FormulaSourceTestSupport.ReadFormulaSource("NumberFormatter.cs");
        var dateTimeSource = FormulaSourceTestSupport.ReadFormulaSource("NumberFormatter.DateTime.cs");
        var sectionsSource = FormulaSourceTestSupport.ReadFormulaSource("NumberFormatter.Sections.cs");

        numberSource.Should().Contain("ParseSections(sections, indexedColors, theme, out var hasConditions)");
        dateTimeSource.Should().Contain("ParseSections(sections, indexedColors, theme, out var hasConditions)");
        sectionsSource.Should().Contain("private static ParsedSection[] ParseSections(");
        sectionsSource.Should().Contain("for (var i = 0; i < sections.Length; i++)");
        sectionsSource.Should().Contain("hasConditions |= parsedSection.Condition is not null;");
        numberSource.Should().NotContain("sections.Select(section => ParseSection");
        numberSource.Should().NotContain(".Any(section => section.Condition");
        dateTimeSource.Should().NotContain("sections.Select(section => ParseSection");
        dateTimeSource.Should().NotContain(".Any(section => section.Condition");
    }

    [Fact]
    public void HotNumberFormatColorMapping_AvoidsUppercaseNormalizedTokenAllocations()
    {
        var colorSource = FormulaSourceTestSupport.ReadFormulaSource("NumberFormatColorMapper.cs");

        colorSource.Should().Contain("TryConsumeIgnoringWhitespace(token, \"THEMEACCENT1\"");
        colorSource.Should().Contain("TokenStartsWithIgnoringWhitespace(token, \"THEME\")");
        colorSource.Should().Contain("StringComparison.OrdinalIgnoreCase");
        colorSource.Should().NotContain(
            "ToUpperInvariant() switch",
            "named color mapping should compare directly instead of allocating an uppercase token copy");
        colorSource.Should().NotContain(
            "ColorTokenWhitespaceRegex",
            "theme color directives should skip whitespace while comparing instead of regex-normalizing each token");
        colorSource.Should().NotContain(
            "NormalizeToken(",
            "theme color directives should avoid allocating normalized token strings on number-format hot paths");
    }

    [Fact]
    public void HotPlainNumericSingleSections_BypassDirectiveNormalizationPipeline()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("NumberFormatter.cs");
        var formatNumber = source[
            source.IndexOf("private static FormatResult FormatNumber", StringComparison.Ordinal)..
            source.IndexOf("private static string ApplyNumericFormat", StringComparison.Ordinal)];
        var plainNumericGuard = source[
            source.IndexOf("private static bool IsPlainNumericSection", StringComparison.Ordinal)..
            source.IndexOf("private static string ApplyNumericFormat", StringComparison.Ordinal)];

        formatNumber.Should().Contain("TryFormatPlainNumericSection(magnitude, effectiveFormat, out var plainNumericText)");
        formatNumber.IndexOf("TryFormatPlainNumericSection", StringComparison.Ordinal)
            .Should()
            .BeLessThan(formatNumber.IndexOf("ApplyNumericFormat", StringComparison.Ordinal));
        plainNumericGuard.Should().Contain("case '0':");
        plainNumericGuard.Should().Contain("case '#':");
        plainNumericGuard.Should().Contain("case '.':");
        plainNumericGuard.Should().Contain("case ',':");
        plainNumericGuard.Should().Contain("return hasPlaceholder && lastToken != ',';");
    }

    private const string StaticRegexCallPattern = @"\bRegex\.(?:Match|IsMatch|Replace)\s*\(";

}
