using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// Regression coverage for R39-commands-text-to-columns-1: the wizard's default "General" column
/// format must coerce a date-like string to a real date value (matching real Excel and FreeX's own
/// typed-cell-entry path, CellEntryParser.ParseScalarValue), instead of leaving it as plain text.
/// </summary>
public sealed class R39_TextToColumnsGeneralDateCoercionTests
{
    [Fact]
    public void ConvertValue_GeneralColumn_CoercesDateLikeTextToDateUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3/15/2024", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(2024, 3, 15).ToOADate()));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_CoercesIsoDateLikeTextToDateUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("2024-03-15", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(2024, 3, 15).ToOADate()));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_HonorsCurrentCultureDayFirstOrder()
    {
        // en-GB reads "15/03/2024" as day-first (15 March 2024), not month-first.
        using var cultureScope = TestCultureScope.CurrentCulture("en-GB");

        var result = TextToColumnsValueConverter.ConvertValue("15/03/2024", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(2024, 3, 15).ToOADate()));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_UsesExcelTwoDigitYearWindow()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("6/15/45", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(1945, 6, 15).ToOADate()));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_RejectsPre1900Date()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("12/31/1899", TextToColumnsColumnFormat.General);

        result.Should().Be(new TextValue("12/31/1899"));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_DoesNotOptIntoTimeOnlyParsing()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("15:30", TextToColumnsColumnFormat.General);

        result.Should().Be(new TextValue("15:30"));
    }

    [Fact]
    public void ConvertValue_TextColumn_KeepsDateLikeStringAsLiteralText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3/15/2024", TextToColumnsColumnFormat.Text);

        result.Should().Be(new TextValue("3/15/2024"));
    }

    [Fact]
    public void ConvertValue_TextColumn_KeepsLeadingZerosAsLiteralText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("00123", TextToColumnsColumnFormat.Text);

        result.Should().Be(new TextValue("00123"));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_StillParsesPlainNumericStringAsNumber()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("1234.56", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_StillParsesGroupedThousandsAsNumberNotDate()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        // Numeric parsing is tried before date detection, so a validly-grouped number must never
        // be misread as a date even though it contains multiple digit groups.
        var result = TextToColumnsValueConverter.ConvertValue("1,234.56", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_NonDateNonNumberStaysText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("Product", TextToColumnsColumnFormat.General);

        result.Should().Be(new TextValue("Product"));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_StillParsesBooleanText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("TRUE", TextToColumnsColumnFormat.General);

        result.Should().Be(new BoolValue(true));
    }
}
