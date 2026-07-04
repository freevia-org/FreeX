using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// Regression coverage for G24: the no-advancedOptions default path of
/// <see cref="TextToColumnsValueConverter"/> must validate thousands-grouping shape
/// before allowing <c>NumberStyles.AllowThousands</c>, so a decimal-comma value like
/// "1234,56" is not silently corrupted into the grouped integer 123456 under an
/// en-US (dot-decimal) CurrentCulture.
/// </summary>
public sealed class TextToColumnsValueConverterCultureTests
{
    [Fact]
    public void ConvertValue_DoesNotCorruptDecimalCommaTextUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("1234,56", TextToColumnsColumnFormat.General);

        // "1234,56" is not a validly-grouped en-US thousands number (a real grouped value would be
        // "1,234.56"), so it must not be silently parsed as 123456. It also is not a valid plain
        // number under either en-US or invariant culture, so it should fall through to text.
        result.Should().Be(new TextValue("1234,56"));
    }

    [Fact]
    public void ConvertValue_StillParsesValidlyGroupedThousandsUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("1,234.56", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void ConvertValue_StillParsesPlainDecimalUnderEnUsCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var result = TextToColumnsValueConverter.ConvertValue("1234.56", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void ConvertValue_ParsesDecimalCommaAsDecimalUnderFrFrCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        var result = TextToColumnsValueConverter.ConvertValue("1234,56", TextToColumnsColumnFormat.General);

        result.Should().Be(new NumberValue(1234.56));
    }
}
