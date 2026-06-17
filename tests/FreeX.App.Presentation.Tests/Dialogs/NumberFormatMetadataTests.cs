using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class NumberFormatMetadataTests
{
    [Fact]
    public void General_ComposesGeneralCode()
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.General);

        meta.ToFormatCode().Should().Be("General");
        meta.BuildPreview().Should().Be("1234.56");
    }

    [Fact]
    public void Text_ComposesAtCode_AndPreviewsSampleText()
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Text);

        meta.ToFormatCode().Should().Be("@");
        meta.BuildPreview().Should().Be("Sample");
    }

    [Theory]
    [InlineData(0, true, "#,##0")]
    [InlineData(2, true, "#,##0.00")]
    [InlineData(0, false, "0")]
    [InlineData(3, false, "0.000")]
    public void Number_ComposesWithDecimalsAndSeparator(int decimals, bool separator, string expected)
    {
        var meta = new NumberFormatMetadata(
            NumberFormatCategory.Number,
            decimals,
            UseThousandsSeparator: separator);

        meta.ToFormatCode().Should().Be(expected);
    }

    [Theory]
    [InlineData(NegativeNumberStyle.Minus, "#,##0.00")]
    [InlineData(NegativeNumberStyle.RedMinus, "#,##0.00;[Red]-#,##0.00")]
    [InlineData(NegativeNumberStyle.Parentheses, "#,##0.00;(#,##0.00)")]
    [InlineData(NegativeNumberStyle.RedParentheses, "#,##0.00;[Red](#,##0.00)")]
    public void Number_AppliesNegativeStyle(NegativeNumberStyle style, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Number, 2, NegativeStyle: style);

        meta.ToFormatCode().Should().Be(expected);
    }

    [Fact]
    public void Currency_ComposesWithSymbolAndPreviews()
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Currency, 2, "$");

        meta.ToFormatCode().Should().Be("$#,##0.00");
        meta.BuildPreview().Should().Be("$1,234.56");
    }

    [Fact]
    public void Currency_AppliesNegativeStyle()
    {
        var meta = new NumberFormatMetadata(
            NumberFormatCategory.Currency,
            2,
            "$",
            NegativeNumberStyle.RedParentheses);

        meta.ToFormatCode().Should().Be("$#,##0.00;[Red]($#,##0.00)");
    }

    [Fact]
    public void Accounting_ComposesExcelStyleSections()
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Accounting, 2, "$");

        meta.ToFormatCode().Should().Be("_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)");
        meta.BuildPreview().Should().Contain("1,234.56");
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(2, "0.00%")]
    public void Percentage_Composes(int decimals, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Percentage, decimals);

        meta.ToFormatCode().Should().Be(expected);
    }

    [Fact]
    public void Percentage_Previews()
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Percentage, 2);

        meta.BuildPreview().Should().Be("123456.00%");
    }

    [Theory]
    [InlineData(0, "0E+00")]
    [InlineData(2, "0.00E+00")]
    public void Scientific_Composes(int decimals, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Scientific, decimals);

        meta.ToFormatCode().Should().Be(expected);
    }

    [Fact]
    public void DecimalPlaces_AreClampedToBounds()
    {
        new NumberFormatMetadata(NumberFormatCategory.Number, -5).ToFormatCode().Should().Be("#,##0");
        new NumberFormatMetadata(NumberFormatCategory.Number, 99).ToFormatCode()
            .Should().Be("#,##0." + new string('0', NumberFormatMetadata.MaxDecimalPlaces));
    }

    // ---- Decompose ----

    [Theory]
    [InlineData(null, NumberFormatCategory.General)]
    [InlineData("", NumberFormatCategory.General)]
    [InlineData("General", NumberFormatCategory.General)]
    [InlineData("@", NumberFormatCategory.Text)]
    [InlineData("0", NumberFormatCategory.Number)]
    [InlineData("#,##0.00", NumberFormatCategory.Number)]
    [InlineData("$#,##0.00", NumberFormatCategory.Currency)]
    [InlineData("0.00%", NumberFormatCategory.Percentage)]
    [InlineData("0.00E+00", NumberFormatCategory.Scientific)]
    [InlineData("_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", NumberFormatCategory.Accounting)]
    public void FromFormatCode_DetectsCategory(string? code, NumberFormatCategory expected)
    {
        NumberFormatMetadata.FromFormatCode(code).Category.Should().Be(expected);
    }

    [Theory]
    [InlineData("#,##0", 0)]
    [InlineData("#,##0.00", 2)]
    [InlineData("0.000", 3)]
    [InlineData("0.00%", 2)]
    [InlineData("0.00E+00", 2)]
    public void FromFormatCode_DetectsDecimalPlaces(string code, int expected)
    {
        NumberFormatMetadata.FromFormatCode(code).DecimalPlaces.Should().Be(expected);
    }

    [Fact]
    public void FromFormatCode_DetectsCurrencySymbol()
    {
        NumberFormatMetadata.FromFormatCode("$#,##0.00").CurrencySymbol.Should().Be("$");
    }

    [Fact]
    public void FromFormatCode_DetectsAccountingSymbol()
    {
        var meta = NumberFormatMetadata.FromFormatCode(
            "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)");

        meta.Category.Should().Be(NumberFormatCategory.Accounting);
        meta.CurrencySymbol.Should().Be("$");
    }

    [Theory]
    [InlineData("#,##0.00", NegativeNumberStyle.Minus)]
    [InlineData("#,##0.00;[Red]-#,##0.00", NegativeNumberStyle.RedMinus)]
    [InlineData("#,##0.00;(#,##0.00)", NegativeNumberStyle.Parentheses)]
    [InlineData("#,##0.00;[Red](#,##0.00)", NegativeNumberStyle.RedParentheses)]
    public void FromFormatCode_DetectsNegativeStyle(string code, NegativeNumberStyle expected)
    {
        NumberFormatMetadata.FromFormatCode(code).NegativeStyle.Should().Be(expected);
    }

    [Theory]
    [InlineData("#,##0.00", true)]
    [InlineData("0.00", false)]
    public void FromFormatCode_DetectsThousandsSeparator(string code, bool expected)
    {
        NumberFormatMetadata.FromFormatCode(code).UseThousandsSeparator.Should().Be(expected);
    }

    [Theory]
    [InlineData(NumberFormatCategory.Number, 2, null, NegativeNumberStyle.Parentheses, true)]
    [InlineData(NumberFormatCategory.Number, 0, null, NegativeNumberStyle.Minus, false)]
    [InlineData(NumberFormatCategory.Currency, 2, "$", NegativeNumberStyle.RedParentheses, true)]
    [InlineData(NumberFormatCategory.Percentage, 3, null, NegativeNumberStyle.Minus, true)]
    [InlineData(NumberFormatCategory.Scientific, 2, null, NegativeNumberStyle.Minus, true)]
    [InlineData(NumberFormatCategory.Accounting, 2, "$", NegativeNumberStyle.Minus, true)]
    public void Compose_ThenDecompose_RoundTrips(
        NumberFormatCategory category,
        int decimals,
        string? symbol,
        NegativeNumberStyle style,
        bool separator)
    {
        var original = new NumberFormatMetadata(category, decimals, symbol, style, separator);

        var roundTripped = NumberFormatMetadata.FromFormatCode(original.ToFormatCode());

        roundTripped.Category.Should().Be(category);
        roundTripped.DecimalPlaces.Should().Be(decimals);
        if (original.UsesNegativeStyle)
            roundTripped.NegativeStyle.Should().Be(style);
        if (original.UsesCurrencySymbol)
            roundTripped.CurrencySymbol.Should().Be(symbol);
    }

    [Fact]
    public void FieldApplicability_FlagsMatchCategory()
    {
        var currency = new NumberFormatMetadata(NumberFormatCategory.Currency);
        currency.UsesCurrencySymbol.Should().BeTrue();
        currency.UsesDecimalPlaces.Should().BeTrue();
        currency.UsesNegativeStyle.Should().BeTrue();

        var text = new NumberFormatMetadata(NumberFormatCategory.Text);
        text.UsesCurrencySymbol.Should().BeFalse();
        text.UsesDecimalPlaces.Should().BeFalse();
        text.UsesNegativeStyle.Should().BeFalse();
        text.UsesThousandsSeparator.Should().BeFalse();

        var accounting = new NumberFormatMetadata(NumberFormatCategory.Accounting);
        accounting.UsesNegativeStyle.Should().BeFalse();
        accounting.UsesThousandsSeparator.Should().BeFalse();
    }

    [Fact]
    public void UnrecognizedCustomCode_FallsBackToCustom()
    {
        // A genuinely custom code (not one of the canonical Special layouts) still resolves to Custom.
        NumberFormatMetadata.FromFormatCode("[>1000]0.0,\"K\";0")
            .Category.Should().Be(NumberFormatCategory.Custom);
    }

    // ---- Fraction ----

    [Theory]
    [InlineData(FractionType.UpToOneDigit, "# ?/?")]
    [InlineData(FractionType.UpToTwoDigits, "# ??/??")]
    [InlineData(FractionType.UpToThreeDigits, "# ???/???")]
    [InlineData(FractionType.Halves, "# ?/2")]
    [InlineData(FractionType.Quarters, "# ?/4")]
    [InlineData(FractionType.Eighths, "# ?/8")]
    [InlineData(FractionType.Sixteenths, "# ??/16")]
    [InlineData(FractionType.Tenths, "# ?/10")]
    [InlineData(FractionType.Hundredths, "# ??/100")]
    public void Fraction_ComposesStandardCodes(FractionType type, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Fraction, Fraction: type);

        meta.ToFormatCode().Should().Be(expected);
        NumberFormatMetadata.FractionFormatCode(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(FractionType.UpToOneDigit, "1234 5/9")]
    [InlineData(FractionType.UpToTwoDigits, "1234 14/25")]
    [InlineData(FractionType.Halves, "1234 1/2")]
    [InlineData(FractionType.Quarters, "1234 2/4")]
    [InlineData(FractionType.Eighths, "1234 4/8")]
    public void Fraction_PreviewsViaFormatter(FractionType type, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Fraction, Fraction: type);

        meta.BuildPreview().Should().Be(expected);
    }

    [Theory]
    [InlineData(FractionType.UpToOneDigit)]
    [InlineData(FractionType.UpToTwoDigits)]
    [InlineData(FractionType.UpToThreeDigits)]
    [InlineData(FractionType.Halves)]
    [InlineData(FractionType.Quarters)]
    [InlineData(FractionType.Eighths)]
    [InlineData(FractionType.Sixteenths)]
    [InlineData(FractionType.Tenths)]
    [InlineData(FractionType.Hundredths)]
    public void Fraction_RoundTrips(FractionType type)
    {
        var original = new NumberFormatMetadata(NumberFormatCategory.Fraction, Fraction: type);

        var roundTripped = NumberFormatMetadata.FromFormatCode(original.ToFormatCode());

        roundTripped.Category.Should().Be(NumberFormatCategory.Fraction);
        roundTripped.Fraction.Should().Be(type);
    }

    [Fact]
    public void Fraction_NonStandardShape_DetectsCategoryWithVariableWidth()
    {
        // A fraction-shaped code outside the standard set still resolves to the Fraction category,
        // mapping to the variable-width option matching the denominator placeholder count.
        var meta = NumberFormatMetadata.FromFormatCode("?/??");

        meta.Category.Should().Be(NumberFormatCategory.Fraction);
        meta.Fraction.Should().Be(FractionType.UpToTwoDigits);
    }

    [Fact]
    public void Fraction_UsesFractionTypeFlag()
    {
        new NumberFormatMetadata(NumberFormatCategory.Fraction).UsesFractionType.Should().BeTrue();
        new NumberFormatMetadata(NumberFormatCategory.Fraction).UsesDecimalPlaces.Should().BeFalse();
        new NumberFormatMetadata(NumberFormatCategory.Number).UsesFractionType.Should().BeFalse();
    }

    // ---- Scientific ----

    [Theory]
    [InlineData(0, "1E+03")]
    [InlineData(2, "1.23E+03")]
    public void Scientific_PreviewsViaFormatter(int decimals, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Scientific, decimals);

        meta.BuildPreview().Should().Be(expected);
    }

    [Theory]
    [InlineData("0E+00", 0)]
    [InlineData("0.00E+00", 2)]
    [InlineData("0.000E+00", 3)]
    public void Scientific_RoundTrips(string code, int decimals)
    {
        var meta = NumberFormatMetadata.FromFormatCode(code);

        meta.Category.Should().Be(NumberFormatCategory.Scientific);
        meta.DecimalPlaces.Should().Be(decimals);
        meta.ToFormatCode().Should().Be(code);
    }

    // ---- Special ----

    [Theory]
    [InlineData(SpecialType.ZipCode, "00000")]
    [InlineData(SpecialType.ZipCodePlus4, "00000-0000")]
    [InlineData(SpecialType.PhoneNumber, "[<=9999999]###-####;(###) ###-####")]
    [InlineData(SpecialType.SocialSecurityNumber, "000-00-0000")]
    public void Special_ComposesCodes(SpecialType type, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Special, Special: type);

        meta.ToFormatCode().Should().Be(expected);
        NumberFormatMetadata.SpecialFormatCode(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(SpecialType.ZipCode, "01235")]
    [InlineData(SpecialType.ZipCodePlus4, "01234-5600")]
    [InlineData(SpecialType.PhoneNumber, "(123) 456-7890")]
    [InlineData(SpecialType.SocialSecurityNumber, "123-45-6789")]
    public void Special_PreviewsRepresentativeSample(SpecialType type, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Special, Special: type);

        meta.BuildPreview().Should().Be(expected);
    }

    [Theory]
    [InlineData(SpecialType.ZipCode)]
    [InlineData(SpecialType.ZipCodePlus4)]
    [InlineData(SpecialType.PhoneNumber)]
    [InlineData(SpecialType.SocialSecurityNumber)]
    public void Special_RoundTrips(SpecialType type)
    {
        var original = new NumberFormatMetadata(NumberFormatCategory.Special, Special: type);

        var roundTripped = NumberFormatMetadata.FromFormatCode(original.ToFormatCode());

        roundTripped.Category.Should().Be(NumberFormatCategory.Special);
        roundTripped.Special.Should().Be(type);
    }

    [Fact]
    public void Special_UsesSpecialTypeFlag()
    {
        new NumberFormatMetadata(NumberFormatCategory.Special).UsesSpecialType.Should().BeTrue();
        new NumberFormatMetadata(NumberFormatCategory.Special).UsesDecimalPlaces.Should().BeFalse();
        new NumberFormatMetadata(NumberFormatCategory.Number).UsesSpecialType.Should().BeFalse();
    }

    // ---- Currency-symbol catalog ----

    [Fact]
    public void CurrencySymbols_CatalogIsPopulatedAndLabeled()
    {
        NumberFormatMetadata.CurrencySymbols.Should().NotBeEmpty();
        NumberFormatMetadata.CurrencySymbols.Should().Contain(e => e.Symbol == "$");
        NumberFormatMetadata.CurrencySymbols.Should().Contain(e => e.Symbol == "€");
        NumberFormatMetadata.CurrencySymbols.Should().OnlyContain(e =>
            !string.IsNullOrWhiteSpace(e.Symbol) && !string.IsNullOrWhiteSpace(e.Label));
    }

    [Theory]
    [InlineData("$", 2, "$#,##0.00")]
    [InlineData("€", 0, "€#,##0")]
    [InlineData("CHF", 2, "CHF#,##0.00")]
    public void CurrencySymbol_ComposesWithDecimals(string symbol, int decimals, string expected)
    {
        var meta = new NumberFormatMetadata(NumberFormatCategory.Currency, decimals, symbol);

        meta.ToFormatCode().Should().Be(expected);
    }

    [Fact]
    public void CurrencySymbol_ComposesWithNegativeStyle()
    {
        var meta = new NumberFormatMetadata(
            NumberFormatCategory.Currency,
            2,
            "€",
            NegativeNumberStyle.RedParentheses);

        meta.ToFormatCode().Should().Be("€#,##0.00;[Red](€#,##0.00)");
    }

    [Fact]
    public void CurrencySymbol_FromCatalog_ComposesInAccounting()
    {
        var entry = NumberFormatMetadata.CurrencySymbols.First(e => e.Symbol == "¥");
        var meta = new NumberFormatMetadata(NumberFormatCategory.Accounting, 2, entry.Symbol);

        meta.ToFormatCode().Should().Be(
            "_(¥* #,##0.00_);_(¥* (#,##0.00);_(¥* \"-\"??_);_(@_)");
        meta.BuildPreview().Should().Contain("1,234.56");
    }
}
