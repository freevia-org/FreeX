using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsNumberFormatPlannerTests
{
    [Theory]
    [InlineData("General", false, true, false, false, false, false)]
    [InlineData("Number", true, false, true, false, true, true)]
    [InlineData("Currency", true, false, true, true, true, true)]
    [InlineData("Accounting", true, false, true, true, false, true)]
    [InlineData("Percentage", true, false, true, false, false, true)]
    [InlineData("Scientific", true, false, true, false, false, true)]
    [InlineData("Date", true, false, false, false, false, false)]
    [InlineData("Custom", true, false, false, false, false, false)]
    [InlineData(null, false, false, false, false, false, false)]
    public void ControlPlanner_MatchesExcelNumberCategoryControlAvailability(
        string? category,
        bool showsType,
        bool showsGeneralDescription,
        bool usesDecimals,
        bool usesSymbol,
        bool usesNegativeOptions,
        bool generatesFormat)
    {
        FormatCellsNumberControlPlanner.Plan(category)
            .Should()
            .Be(new FormatCellsNumberControlAvailability(
                showsType,
                showsGeneralDescription,
                usesDecimals,
                usesSymbol,
                usesNegativeOptions,
                generatesFormat));
    }

    [Theory]
    [InlineData("Zip Code", "00000")]
    [InlineData("Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)")]
    [InlineData("Long time ([$-F400])", "[$-F400]")]
    public void ResolveNumberFormat_MapsExcelLikeLabelsToCodes(string label, string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(label, 0)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Number", "#,##0.00", "0", "None", 0, "#,##0")]
    [InlineData("Currency", "$#,##0.00", "3", "EUR", 2, "EUR#,##0.000;(EUR#,##0.000)")]
    [InlineData("Accounting", "$#,##0.00", "2", "GBP", 0, "_(GBP* #,##0.00_);_(GBP* (#,##0.00);_(GBP* \"-\"??_);_(@_)")]
    [InlineData("Percentage", "0.00%", "1", "None", 0, "0.0%")]
    public void ResolveNumberFormat_ComposesGeneratedCategories(
        string category,
        string selectedFormat,
        string decimalPlaces,
        string symbol,
        int negativeIndex,
        string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveNumberFormat(selectedFormat, 0, category, decimalPlaces, symbol, negativeIndex)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("Number", "0", "None", 0, "#,##0")]
    [InlineData("Number", "2", "None", 3, "#,##0.00;[Red](#,##0.00)")]
    [InlineData("Currency", "2", "$", 1, "$#,##0.00;[Red]-$#,##0.00")]
    [InlineData("Scientific", "4", "None", 0, "0.0000E+00")]
    [InlineData("Percentage", "0", "None", 0, "0%")]
    public void ResolveSelectedNumberFormat_BuildsCodeFromControlsForGeneratedCategories(
        string category,
        string decimalPlaces,
        string symbol,
        int negativeIndex,
        string expected)
    {
        FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat(category, "", 0, decimalPlaces, symbol, negativeIndex)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ResolveSelectedNumberFormat_FallsBackToCatalogCodeForNonGeneratedCategory()
    {
        FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat("Date", "d-mmm-yy", 0, "2", "None", 0)
            .Should()
            .Be("d-mmm-yy");
    }

    [Theory]
    [InlineData("#,##0.0000", 4)]
    [InlineData("#,##0;[Red](#,##0)", 0)]
    [InlineData("0.000\"; units\";[Red]-0", 3)]
    [InlineData("0.0\\;kg;[Red]-0", 1)]
    [InlineData(null, 2)]
    public void DecimalPlacesForFormat_MatchesExcelDecimalControls(string? format, int expected)
    {
        FormatCellsNumberFormatPlanner.DecimalPlacesForFormat(format)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Categories_ExposesTheExcelNumberCategoryFamilies()
    {
        FormatCellsNumberFormatPlanner.Categories
            .Should()
            .ContainInOrder("General", "Number", "Currency", "Accounting", "Date", "Time", "Percentage")
            .And.Contain(new[] { "Fraction", "Scientific", "Text", "Special", "Custom" });
    }

    [Fact]
    public void LabelsForCategory_ReturnsDistinctTypeLabelsForTheCategory()
    {
        var labels = FormatCellsNumberFormatPlanner.LabelsForCategory("Currency");

        labels.Should().OnlyHaveUniqueItems();
        labels.Should().Contain("Currency ($#,##0.00)");
    }

    [Theory]
    [InlineData("0.00", "1234.56")]
    [InlineData("0%", "123456%")]
    [InlineData("m/d/yyyy", "5/21/2026")]
    public void PreviewForFormat_RendersTheSampleValueThroughTheFormatter(string format, string expected)
    {
        FormatCellsNumberFormatPlanner.PreviewForFormat(format)
            .Should()
            .Be(expected);
    }

    // ── F16: long-date / long-time LCID-only codes preview as date/time ──────────────────────

    [Fact]
    public void PreviewForFormat_LongDateCode_ProducesDateLikePreview()
    {
        // Regression for F16: [$-F800] must preview as a date string, not empty/garbage.
        // The preview value should contain a 4-digit year (2026) and look date-like.
        var preview = FormatCellsNumberFormatPlanner.PreviewForFormat("[$-F800]");

        preview.Should().NotBeNullOrWhiteSpace();
        preview.Should().NotBe("1234.56");   // was the broken "fell through to numeric" result
        preview.Should().Contain("2026");    // sample serial is 2026-05-21
    }

    [Fact]
    public void PreviewForFormat_LongDatePresetLabel_ProducesDateLikePreview()
    {
        // Regression for F16: looking up by label should also produce a date preview.
        var preview = FormatCellsNumberFormatPlanner.PreviewForFormat("Long date ([$-F800])");

        preview.Should().NotBeNullOrWhiteSpace();
        preview.Should().NotBe("1234.56");
        preview.Should().Contain("2026");
    }

    [Fact]
    public void PreviewForFormat_LongTimeCode_ProducesTimeLikePreview()
    {
        // Regression for F16: [$-F400] must preview as a time string, not empty/garbage.
        var preview = FormatCellsNumberFormatPlanner.PreviewForFormat("[$-F400]");

        preview.Should().NotBeNullOrWhiteSpace();
        preview.Should().NotBe("1234.56");
        // Sample serial is 2026-05-21 13:30 — time preview must contain digits with a colon.
        preview.Should().MatchRegex(@"\d+:\d+");
    }

    // ── F17: ResolveNumberFormat null-safety ──────────────────────────────────────────────────

    [Fact]
    public void ResolveNumberFormat_NullText_ReturnsNull()
    {
        // Regression for F17: null must not throw NullReferenceException.
        var result = FormatCellsNumberFormatPlanner.ResolveNumberFormat((string?)null, 0);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveNumberFormat_WhitespaceText_ReturnsNull()
    {
        // Regression for F17: whitespace-only must return null, same as sibling methods.
        var result = FormatCellsNumberFormatPlanner.ResolveNumberFormat("   ", 0);
        result.Should().BeNull();
    }

    // ── freex-custom-formats F1: preview must agree with the real applied format for a
    // ── quoted-literal custom code that contains a date-like letter (e.g. the 'y' in " yd"). ──

    [Theory]
    [InlineData("0\" yd\"")]
    [InlineData("0\" yrs\"")]
    public void PreviewForFormat_QuotedLiteralContainingDateLetter_AgreesWithRealAppliedFormat(string format)
    {
        // The dialog's live preview and the real cell-rendering path (NumberFormatter.Format
        // against the actual NumberValue, exactly as FormatWithColor dispatches on OK) must
        // produce the SAME text for the SAME format code and SAME sample value -- that is the
        // defect: previously the preview mis-classified this as a date format because the
        // quoted "yd"/"yrs" literal tripped a raw substring search for 'y'.
        var expectedFromRealFormatter = NumberFormatter.Format(new NumberValue(1234.56), format);

        var preview = FormatCellsNumberFormatPlanner.PreviewForFormat(format);

        preview.Should().Be(expectedFromRealFormatter);
        preview.Should().Be(format == "0\" yd\"" ? "1235 yd" : "1235 yrs");
    }

    [Theory]
    [InlineData("yyyy-mm-dd")]
    [InlineData("m/d/yyyy")]
    [InlineData("mmmm d, yyyy")]
    public void PreviewForFormat_RealDateFormat_StillAgreesWithRealAppliedFormat(string format)
    {
        // Sibling/no-regression check: an unquoted date token (real 'y'/'m'/'d' outside any
        // quoted literal) must still be classified as date/time, and the preview must render
        // through the sample DateTime the same way the dialog always has.
        var sampleDate = new DateTime(2026, 5, 21, 13, 30, 0).ToOADate();
        var expectedFromRealFormatter = NumberFormatter.Format(new DateTimeValue(sampleDate), format);

        FormatCellsNumberFormatPlanner.PreviewForFormat(format)
            .Should()
            .Be(expectedFromRealFormatter);
    }

    [Theory]
    [InlineData("0.00\" m\"")]
    [InlineData("#,##0\" mi\"")]
    public void PreviewForFormat_QuotedLiteralWithoutDateLetter_StillPreviewsNumerically(string format)
    {
        // Sibling/no-regression check: control formats without a misleading quoted letter must
        // continue to preview as numbers, unaffected by the quote-stripping change.
        var expectedFromRealFormatter = NumberFormatter.Format(new NumberValue(1234.56), format);

        FormatCellsNumberFormatPlanner.PreviewForFormat(format)
            .Should()
            .Be(expectedFromRealFormatter);
    }
}
