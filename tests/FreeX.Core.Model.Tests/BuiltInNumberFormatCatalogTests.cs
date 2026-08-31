using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class BuiltInNumberFormatCatalogTests
{
    [Theory]
    [InlineData(null, "General")]
    [InlineData(0, "General")]
    [InlineData(5, "$#,##0_);($#,##0)")]
    [InlineData(7, "$#,##0.00_);($#,##0.00)")]
    [InlineData(14, "m/d/yyyy")]
    [InlineData(44, "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)")]
    public void TryResolveFormatCode_MapsBuiltInIds(int? numberFormatId, string expected)
    {
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            .Should().BeTrue();

        formatCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("General", null)]
    [InlineData("$#,##0.00_);($#,##0.00)", 7)]
    [InlineData("m/d/yyyy", 14)]
    [InlineData("#,##0.0 \"kg\"", null)]
    public void ResolveNumberFormatIdForCode_MapsKnownBuiltInCodes(string formatCode, int? expected)
    {
        BuiltInNumberFormatCatalog.ResolveNumberFormatIdForCode(formatCode).Should().Be(expected);
    }

    [Theory]
    [InlineData(37, "#,##0_);(#,##0)")]
    [InlineData(38, "#,##0_);[Red](#,##0)")]
    [InlineData(39, "#,##0.00_);(#,##0.00)")]
    [InlineData(40, "#,##0.00_);[Red](#,##0.00)")]
    public void TryResolveFormatCode_BuiltInCommaStyles_IncludeSkipWidthDirective(int numberFormatId, string expected)
    {
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            .Should().BeTrue();

        formatCode.Should().Be(expected);
        formatCode.Should().Contain("_)", "Excel's real built-in table pads the positive section to align with the parenthesized negative section");
    }

    [Theory]
    [InlineData(5, "$#,##0_);($#,##0)")]
    [InlineData(6, "$#,##0_);[Red]($#,##0)")]
    [InlineData(7, "$#,##0.00_);($#,##0.00)")]
    [InlineData(8, "$#,##0.00_);[Red]($#,##0.00)")]
    public void TryResolveFormatCode_BuiltInCurrencyStyles_UnchangedByCommaStyleFix(int numberFormatId, string expected)
    {
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            .Should().BeTrue();

        formatCode.Should().Be(expected);
    }

    [Fact]
    public void CatalogLookups_UseStaticDictionariesInsteadOfLinearScans()
    {
        var source = ModelSourceTestSupport.ReadModelSource("BuiltInNumberFormatCatalog.cs");

        source.Should().Contain("FormatCodesById.TryGetValue");
        source.Should().Contain("NumberFormatIdsByCode.TryGetValue");
        source.Should().NotContain("FirstOrDefault");
    }

}
