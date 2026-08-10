using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// R112 (family gap left open by R111): R111 fixed CSV/TXT import
/// (DelimitedTextWorkbookReader.LooksLikeCurrentCultureDateCandidate) so a bare, year-less "M/d" or
/// "M-d" token (e.g. "3/4", "1-2") coerces to a current-year date, matching real Excel.
/// Text-to-Columns' General conversion through ExcelDateEntryParser
/// carried the exact same bug -- converting a "3/4" column with the (default) General format
/// required either a letter or 3+ digit groups to even attempt a date parse, so a bare "3/4" fell
/// through to plain text instead of becoming a date. Both paths now route through the shared
/// FreeX.Core.IO.DateEntryShapeRecognizer.
/// </summary>
public sealed class R111_TextToColumnsYearlessMonthDayDateCoercionTests
{
    [Fact]
    public void ConvertValue_GeneralColumn_CoercesYearlessSlashMonthDayToCurrentYearDate()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3/4", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(DateTime.Now.Year, 3, 4).ToOADate()));
    }

    [Fact]
    public void ConvertValue_GeneralColumn_CoercesYearlessHyphenMonthDayToCurrentYearDate()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("1-2", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(DateTime.Now.Year, 1, 2).ToOADate()));
    }

    // No-regression: a full date literal with an explicit year must still parse the same as before.
    [Fact]
    public void ConvertValue_GeneralColumn_StillCoercesFullDateLiteralWithYear()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3/4/2024", TextToColumnsColumnFormat.General);

        result.Should().Be(new DateTimeValue(new DateTime(2024, 3, 4).ToOADate()));
    }

    // No-regression: a plain two-digit-group decimal sharing "." (which is not en-US's date
    // separator) must still parse as a number, not a date -- numeric parsing runs first for
    // General columns, so this also guards that ordering.
    [Fact]
    public void ConvertValue_GeneralColumn_StillCoercesDottedDecimalAsNumber()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3.14", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(3.14));
    }

    // No-regression: an ordinary non-date, non-numeric string must still stay text.
    [Fact]
    public void ConvertValue_GeneralColumn_StillCoercesNonDateTextAsText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("Product", TextToColumnsColumnFormat.General);

        result.Should().Be(new TextValue("Product"));
    }

    // No-regression: a Text-formatted column must keep the literal "3/4" verbatim, unaffected by
    // the General-column date coercion fix.
    [Fact]
    public void ConvertValue_TextColumn_KeepsYearlessMonthDayAsLiteralText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("3/4", TextToColumnsColumnFormat.Text);

        result.Should().Be(new TextValue("3/4"));
    }
}
