using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// Regression coverage for E-t2c-splitter/K5 and K29.
///
/// K5: <see cref="TextToColumnsSplitter.SplitDelimited"/> must only toggle qualifier mode at the START
/// of a field, exactly like CSV parsing (see FreeX.Core.IO.DelimitedTextWorkbookReader's `atFieldStart`
/// gate). A stray/mid-field qualifier character (e.g. an inch mark `"`) is literal text, not a quote-open,
/// and must not swallow the rest of the line (including delimiters) into a single field.
///
/// K29: <see cref="TextToColumnsValueConverter"/>'s advanced Decimal/Thousands separator parsing must
/// reject configurations where both separators are identical, since Excel's Text Import Wizard forbids
/// this (stripping the thousands separator first would also erase the decimal marker, silently
/// truncating a value like "1,234" from 1.234 down to 1234 -- a 1000x data corruption with no warning).
/// </summary>
public sealed class TextToColumnsSplitterFieldStartQualifierTests
{
    [Fact]
    public void SplitDelimited_MidFieldQualifier_IsLiteralAndStillSplitsOnDelimiter()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("3\" pipe, Model X", ",", '"');

        fields.Should().Equal("3\" pipe", " Model X");
    }

    [Fact]
    public void SplitDelimited_MidFieldQualifier_DoesNotDropTheQualifierCharacter()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a1\"b,c", ",", '"');

        fields.Should().Equal("a1\"b", "c");
    }

    [Fact]
    public void SplitDelimited_QualifierAtFieldStart_StillOpensQualifiedSpan()
    {
        // Sanity check that the field-start fix does not regress genuine leading-qualifier fields.
        var fields = TextToColumnsSplitter.SplitDelimited("\"a,b\",c", ",", '"');

        fields.Should().Equal("a,b", "c");
    }

    [Fact]
    public void SplitDelimited_QualifierAtStartOfSecondField_OpensQualifiedSpanForThatField()
    {
        var fields = TextToColumnsSplitter.SplitDelimited("a,\"b,c\",d", ",", '"');

        fields.Should().Equal("a", "b,c", "d");
    }

    [Fact]
    public void SplitDelimited_TrailingMidFieldQualifier_IsLiteralWithNoUnterminatedSpan()
    {
        // A stray qualifier as the very last character of a field (not at field start) must remain
        // literal, not silently open an unterminated qualified span that swallows nothing further.
        var fields = TextToColumnsSplitter.SplitDelimited("a,b\"", ",", '"');

        fields.Should().Equal("a", "b\"");
    }

    [Fact]
    public void ConvertValue_IdenticalDecimalAndThousandsSeparators_DoesNotTruncateNumber()
    {
        var advancedOptions = new TextToColumnsAdvancedOptions(DecimalSeparator: ",", ThousandsSeparator: ",");

        var result = TextToColumnsValueConverter.ConvertValue(
            "1,234",
            TextToColumnsColumnFormat.General,
            advancedOptions);

        // Must not silently collapse to 1234 (a 1000x truncation). Since the separator configuration is
        // invalid (Excel forbids identical decimal/thousands separators), the value is left as text
        // rather than risk corrupting it.
        result.Should().Be(new TextValue("1,234"));
    }

    [Fact]
    public void ConvertValue_DifferentDecimalAndThousandsSeparators_StillParsesCorrectly()
    {
        var advancedOptions = new TextToColumnsAdvancedOptions(DecimalSeparator: ",", ThousandsSeparator: ".");

        var result = TextToColumnsValueConverter.ConvertValue(
            "1.234,56",
            TextToColumnsColumnFormat.General,
            advancedOptions);

        result.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void ConvertValue_DefaultAdvancedOptions_StillParsesGroupedNumber()
    {
        var advancedOptions = new TextToColumnsAdvancedOptions();

        var result = TextToColumnsValueConverter.ConvertValue(
            "1,234.56",
            TextToColumnsColumnFormat.General,
            advancedOptions);

        result.Should().Be(new NumberValue(1234.56));
    }
}
