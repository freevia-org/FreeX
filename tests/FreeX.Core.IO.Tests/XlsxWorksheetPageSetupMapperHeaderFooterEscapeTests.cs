using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that <see cref="XlsxWorksheetPageSetupMapper.FromHeaderFooterText"/> and
/// <see cref="XlsxWorksheetPageSetupMapper.ToHeaderFooterText"/> respect Excel's "&amp;&amp;"
/// escape sequence for a literal ampersand in header/footer text, instead of blindly matching
/// field-code substrings (e.g. the "D" following an escaped "&amp;&amp;" must not be read as the
/// "&amp;D" Date code).
/// </summary>
public sealed class XlsxWorksheetPageSetupMapperHeaderFooterEscapeTests
{
    [Fact]
    public void FromHeaderFooterText_DoesNotMisreadCodeAfterEscapedAmpersand()
    {
        // "R&&D Report" is Excel's raw representation of the literal text "R&D Report".
        // A blind Replace("&D", "&[Date]") would corrupt it into "R&&[Date] Report".
        var result = XlsxWorksheetPageSetupMapper.FromHeaderFooterText("R&&D Report");

        result.Should().Be("R&&D Report");
    }

    [Fact]
    public void ToHeaderFooterText_DoesNotMisreadCodeAfterEscapedAmpersand()
    {
        var result = XlsxWorksheetPageSetupMapper.ToHeaderFooterText("R&&D Report");

        result.Should().Be("R&&D Report");
    }

    [Fact]
    public void HeaderFooterText_RoundTripsLiteralAmpersandThroughBothConversions()
    {
        const string literal = "R&&D Report";

        var toBracket = XlsxWorksheetPageSetupMapper.FromHeaderFooterText(literal);
        var backToRaw = XlsxWorksheetPageSetupMapper.ToHeaderFooterText(toBracket);

        backToRaw.Should().Be(literal);
    }

    [Fact]
    public void FromHeaderFooterText_StillTranslatesUnescapedCodes()
    {
        var result = XlsxWorksheetPageSetupMapper.FromHeaderFooterText("Page &P of &N");

        result.Should().Be("Page &[Page] of &[Pages]");
    }

    [Fact]
    public void ToHeaderFooterText_StillTranslatesBracketTokens()
    {
        var result = XlsxWorksheetPageSetupMapper.ToHeaderFooterText("Page &[Page] of &[Pages]");

        result.Should().Be("Page &P of &N");
    }
}
