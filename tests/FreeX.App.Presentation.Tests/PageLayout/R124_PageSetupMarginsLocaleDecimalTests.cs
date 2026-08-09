using System.Globalization;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Round-124 regression: Page Setup > Margins has four separate TextBoxes (Left/Right/Top/Bottom).
/// <see cref="PageSetupDialogPlanner.BuildMarginsText"/> joins their raw text with ',' into one string
/// that <see cref="PageMarginInputParser.TryParse"/> re-splits on ',' and parses with InvariantCulture
/// only. Under a comma-decimal CurrentCulture (de-DE, fr-FR, ...), typing a margin the way the OS
/// formats numbers -- e.g. "1,91" -- uses the very character the four fields are joined with, so the
/// token count inflates past 4 and the parse hard-fails with "Enter four comma-separated margins",
/// even though "1,91" is a perfectly valid margin in that locale. Real Excel accepts locale-formatted
/// decimal margin input. The fix normalizes each TextBox's text (CurrentCulture-then-InvariantCulture,
/// matching NumericInputParser's convention already used for this same dialog's Header/Footer margin
/// fields) to an invariant token *before* joining, so the join/split round-trip never sees a locale
/// decimal comma.
/// </summary>
public sealed class R124_PageSetupMarginsLocaleDecimalTests
{
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(string cultureName)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    private sealed class PageSetupTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void BuildMarginsText_CommaDecimalMarginsUnderCommaDecimalCulture_StillParseToFourValues()
    {
        using var _ = new CultureScope("de-DE");

        // Every field typed the way a German-locale user naturally types a fractional inch.
        var marginsText = PageSetupDialogPlanner.BuildMarginsText(
            new PageSetupMarginTextFields("1,91", "2,54", "0,98", "1,02"));

        var parsed = PageMarginInputParser.TryParse(marginsText, out var margins, out var error);

        parsed.Should().BeTrue(error);
        margins.Left.Should().Be(1.91);
        margins.Right.Should().Be(2.54);
        margins.Top.Should().Be(0.98);
        margins.Bottom.Should().Be(1.02);
    }

    /// <summary>
    /// The real product entry point: build the four separate-field dialog input under a comma-decimal
    /// CurrentCulture the way the WPF/Avalonia shells do (LeftMarginText/RightMarginText/...), run it
    /// through PageSetupDialogModel.TryBuildCommandPlan, execute the resulting SetPageSetupCommand, and
    /// confirm the sheet actually ends up with the locale-typed margins -- not a validation failure.
    /// </summary>
    [Fact]
    public void TryBuildCommandPlan_CommaDecimalMarginFields_AppliesCorrectMarginsToSheet()
    {
        using var _ = new CultureScope("de-DE");

        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);

        var initial = PageSetupDialogModel.FromSheet(sheet);
        var fields = PageSetupDialogPlanner.BuildFields(
            initial,
            new PageSetupDialogSurfaceInput
            {
                LeftMarginText = "1,91",
                RightMarginText = "2,54",
                TopMarginText = "0,98",
                BottomMarginText = "1,02",
                HeaderMarginText = initial.HeaderMarginText,
                FooterMarginText = initial.FooterMarginText,
                ScalingMode = initial.ScalingMode,
                ScalePercentText = initial.ScalePercentText,
                FitToWideText = initial.FitToWideText,
                FitToTallText = initial.FitToTallText,
            });

        var build = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        build.Success.Should().BeTrue(build.Error);
        build.Plan!.PageSetupCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.PageMargins.Left.Should().Be(1.91);
        sheet.PageMargins.Right.Should().Be(2.54);
        sheet.PageMargins.Top.Should().Be(0.98);
        sheet.PageMargins.Bottom.Should().Be(1.02);
    }

    /// <summary>
    /// No-regression sibling: plain '.'-decimal input (what every existing test and every en-US user
    /// types) must keep composing exactly as before, byte-for-byte -- including a trailing ".0" -- so the
    /// locale fix does not silently reformat ordinary invariant input.
    /// </summary>
    [Fact]
    public void BuildMarginsText_DotDecimalMargins_ComposeUnchangedUnderCommaDecimalCulture()
    {
        using var _ = new CultureScope("de-DE");

        var marginsText = PageSetupDialogPlanner.BuildMarginsText(
            new PageSetupMarginTextFields("0.7", "0.8", "0.9", "1.0"));

        marginsText.Should().Be("0.7,0.8,0.9,1.0");
    }

    /// <summary>
    /// No-regression sibling: a negative comma-decimal margin must still be rejected (not silently
    /// clamped or accepted) once it is correctly parsed as a single negative number rather than as an
    /// extra split token.
    /// </summary>
    [Fact]
    public void TryBuildCommandPlan_NegativeCommaDecimalMargin_StillFailsValidation()
    {
        using var _ = new CultureScope("de-DE");

        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        var initial = PageSetupDialogModel.FromSheet(sheet);
        var fields = PageSetupDialogPlanner.BuildFields(
            initial,
            new PageSetupDialogSurfaceInput
            {
                LeftMarginText = "-1,91",
                RightMarginText = "2,54",
                TopMarginText = "0,98",
                BottomMarginText = "1,02",
                HeaderMarginText = initial.HeaderMarginText,
                FooterMarginText = initial.FooterMarginText,
                ScalingMode = initial.ScalingMode,
                ScalePercentText = initial.ScalePercentText,
                FitToWideText = initial.FitToWideText,
                FitToTallText = initial.FitToTallText,
            });

        var build = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        build.Success.Should().BeFalse();
        build.Target.Should().Be(PageSetupValidationTarget.Margins);
    }
}
