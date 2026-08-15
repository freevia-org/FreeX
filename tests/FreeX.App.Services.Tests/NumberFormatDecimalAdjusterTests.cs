using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class NumberFormatDecimalAdjusterTests
{
    [Theory]
    [InlineData(null, "0.0")]
    [InlineData("", "0.0")]
    [InlineData("General", "0.0")]
    [InlineData("0", "0.0")]
    [InlineData("#,##0", "#,##0.0")]
    [InlineData("#,##0.00", "#,##0.000")]
    [InlineData("$#,##0.00", "$#,##0.000")]
    [InlineData("_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)", "_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)")]
    [InlineData("_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)", "_(* #,##0.0_);_(* (#,##0.0);_(* \"-\"_);_(@_)")]
    public void AddDecimalPlace_AddsOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.AddDecimalPlace(format).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData("General", "0")]
    [InlineData("0", "0")]
    [InlineData("#,##0.0", "#,##0")]
    [InlineData("#,##0.00", "#,##0.0")]
    [InlineData("$#,##0.000", "$#,##0.00")]
    [InlineData("_(* #,##0.000_);_(* (#,##0.000);_(* \"-\"???_);_(@_)", "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)")]
    [InlineData("_(* #,##0.0_);_(* (#,##0.0);_(* \"-\"_);_(@_)", "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)")]
    public void RemoveDecimalPlace_RemovesOneDecimalSlot(string? format, string expected)
    {
        NumberFormatDecimalAdjuster.RemoveDecimalPlace(format).Should().Be(expected);
    }

    [Fact]
    public void DecimalAdjustmentRegexes_AreGeneratedAndCached()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("shared", "Free.Shared.AppServices", "NumberFormatDecimalAdjuster.cs"));

        source.Should().Contain("[GeneratedRegex");
        source.Should().NotMatchRegex(@"\bRegex\.Match\s*\(");
    }

    [Fact]
    public void AddDecimalPlace_LeavesBackslashEscapedLiteralPeriodAlone()
    {
        // "\." is a backslash-escaped literal period with no real decimal placeholder anywhere in
        // the code. It must be a no-op, exactly like the documented "no adjustable placeholder"
        // contract for date/text formats -- the escaped '.' must not be mistaken for the real
        // decimal separator and grown into "\.0".
        NumberFormatDecimalAdjuster.AddDecimalPlace(@"\.").Should().Be(@"\.");
    }

    [Fact]
    public void RemoveDecimalPlace_LeavesBackslashEscapedLiteralPeriodAlone()
    {
        // Mirrors AddDecimalPlace_LeavesBackslashEscapedLiteralPeriodAlone for the Decrease Decimal
        // path: the escaped '.' followed by a literal '0' must not be treated as a real decimal run
        // and stripped down to a dangling, unescaped backslash ("\").
        NumberFormatDecimalAdjuster.RemoveDecimalPlace(@"\.0").Should().Be(@"\.0");
    }

    [Fact]
    public void AddDecimalPlace_DoesNotSplitOnBackslashEscapedSemicolon()
    {
        // "0\;0" is a SINGLE section: the ';' is backslash-escaped literal text, not a
        // positive/negative/zero/text section separator. Splitting on it corrupts the format by
        // applying the decimal-place increase independently to each fake "section", inflating both
        // digit runs instead of only the first one.
        NumberFormatDecimalAdjuster.AddDecimalPlace(@"0\;0").Should().Be(@"0.0\;0");
    }

    [Fact]
    public void AddDecimalPlace_StillGrowsRealDecimalPastQuotedLiteralPeriod()
    {
        // Regression guard: a period that lives inside quoted literal text ("0.00\" in.\"") must
        // keep being ignored while the genuine leading decimal run keeps growing normally.
        NumberFormatDecimalAdjuster.AddDecimalPlace("0.00\" in.\"").Should().Be("0.000\" in.\"");
    }
}
