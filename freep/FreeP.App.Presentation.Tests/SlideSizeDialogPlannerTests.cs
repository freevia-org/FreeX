using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideSizeDialogPlannerTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void InchesToEmu_OneInch_Returns914400()
    {
        SlideSizeDialogPlanner.InchesToEmu(1.0).Should().Be(914_400L);
    }

    [Fact]
    public void InchesToEmu_HalfInch_Returns457200()
    {
        SlideSizeDialogPlanner.InchesToEmu(0.5).Should().Be(457_200L);
    }

    [Fact]
    public void CmToEmu_OneCm_Returns360000()
    {
        SlideSizeDialogPlanner.CmToEmu(1.0).Should().Be(360_000L);
    }

    [Fact]
    public void CmToEmu_TenCm_Returns3600000()
    {
        SlideSizeDialogPlanner.CmToEmu(10.0).Should().Be(3_600_000L);
    }

    [Fact]
    public void EmuToInches_914400_ReturnsOneInch()
    {
        SlideSizeDialogPlanner.EmuToInches(914_400L).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void EmuToCm_360000_ReturnsOneCm()
    {
        SlideSizeDialogPlanner.EmuToCm(360_000L).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void InchesToEmu_RoundTrip_IsIdempotent()
    {
        long emu = SlideSizeDialogPlanner.InchesToEmu(13.333);
        SlideSizeDialogPlanner.EmuToInches(emu).Should().BeApproximately(13.333, 0.001);
    }

    [Fact]
    public void CmToEmu_RoundTrip_IsIdempotent()
    {
        double cm = 33.867;
        long emu = SlideSizeDialogPlanner.CmToEmu(cm);
        SlideSizeDialogPlanner.EmuToCm(emu).Should().BeApproximately(cm, 0.01);
    }

    [Fact]
    public void ClassifySize_Widescreen169Emu_ReturnsWidescreen()
    {
        var (cx, cy) = SlideSizeDialogPlanner.Widescreen169Emu;
        SlideSizeDialogPlanner.ClassifySize(cx, cy).Should().Be(SlideSizeDialogPreset.Widescreen169);
    }

    [Fact]
    public void ClassifySize_Standard43Emu_ReturnsStandard()
    {
        var (cx, cy) = SlideSizeDialogPlanner.Standard43Emu;
        SlideSizeDialogPlanner.ClassifySize(cx, cy).Should().Be(SlideSizeDialogPreset.Standard43);
    }

    [Fact]
    public void ClassifySize_CustomDimensions_ReturnsCustom()
    {
        SlideSizeDialogPlanner.ClassifySize(1_000_000L, 500_000L)
            .Should().Be(SlideSizeDialogPreset.Custom);
    }

    [Fact]
    public void Widescreen169Emu_MatchesExpectedValues()
    {
        var (cx, cy) = SlideSizeDialogPlanner.Widescreen169Emu;
        cx.Should().Be(12_192_000L);
        cy.Should().Be(6_858_000L);
    }

    [Fact]
    public void Standard43Emu_MatchesExpectedValues()
    {
        var (cx, cy) = SlideSizeDialogPlanner.Standard43Emu;
        cx.Should().Be(9_144_000L);
        cy.Should().Be(6_858_000L);
    }

    [Fact]
    public void Standard43_10x7p5Inches_MatchesPresetEmu()
    {
        var (cx, cy) = SlideSizeDialogPlanner.Standard43Emu;
        SlideSizeDialogPlanner.InchesToEmu(10.0).Should().Be(cx);
        SlideSizeDialogPlanner.InchesToEmu(7.5).Should().Be(cy);
    }

    [Fact]
    public void BuildInitialState_ClassifiesAndFormatsInches()
    {
        var state = SlideSizeDialogPlanner.BuildInitialState(
            12_192_000L,
            6_858_000L,
            SlideSizeDialogUnit.Inches,
            Invariant);

        state.Preset.Should().Be(SlideSizeDialogPreset.Widescreen169);
        state.Display.WidthText.Should().Be("13.333");
        state.Display.HeightText.Should().Be("7.500");
        state.Display.UnitLabel.Should().Be("in");
    }

    [Fact]
    public void BuildPresetSelectionDisplay_Custom_ReturnsNull()
    {
        SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
                SlideSizeDialogPreset.Custom,
                SlideSizeDialogUnit.Inches,
                Invariant)
            .Should().BeNull();
    }

    [Fact]
    public void BuildPresetSelectionDisplay_StandardInCentimeters_FormatsCentimeters()
    {
        var display = SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
            SlideSizeDialogPreset.Standard43,
            SlideSizeDialogUnit.Centimeters,
            Invariant);

        display.Should().NotBeNull();
        display!.WidthText.Should().Be("25.40");
        display.HeightText.Should().Be("19.05");
        display.UnitLabel.Should().Be("cm");
    }

    [Fact]
    public void BuildUnitChangeDisplay_ConvertsFromOldUnitAndFormatsNewUnit()
    {
        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            "13.333",
            "7.500",
            SlideSizeDialogUnit.Inches,
            SlideSizeDialogUnit.Centimeters,
            Invariant);

        display.WidthText.Should().Be("33.87");
        display.HeightText.Should().Be("19.05");
        display.UnitLabel.Should().Be("cm");
    }

    [Fact]
    public void BuildUnitChangeDisplay_InvalidText_NormalizesToZero()
    {
        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            "abc",
            "",
            SlideSizeDialogUnit.Inches,
            SlideSizeDialogUnit.Centimeters,
            Invariant);

        display.WidthText.Should().Be("0.00");
        display.HeightText.Should().Be("0.00");
        display.UnitLabel.Should().Be("cm");
    }

    [Fact]
    public void TryParsePositiveSize_ValidInches_ReturnsEmu()
    {
        var plan = SlideSizeDialogPlanner.TryParsePositiveSize(
            "12",
            "6.75",
            SlideSizeDialogUnit.Inches,
            Invariant);

        plan.IsValid.Should().BeTrue();
        plan.CxEmu.Should().Be(10_972_800L);
        plan.CyEmu.Should().Be(6_172_200L);
        plan.FocusField.Should().Be(SlideSizeDialogField.None);
    }

    [Fact]
    public void TryParsePositiveSize_InvalidWidth_ReturnsWidthFocus()
    {
        var plan = SlideSizeDialogPlanner.TryParsePositiveSize(
            "nope",
            "7.5",
            SlideSizeDialogUnit.Inches,
            Invariant);

        plan.IsValid.Should().BeFalse();
        plan.FocusField.Should().Be(SlideSizeDialogField.Width);
    }

    [Fact]
    public void TryParsePositiveSize_InvalidHeight_ReturnsHeightFocus()
    {
        var plan = SlideSizeDialogPlanner.TryParsePositiveSize(
            "10",
            "0",
            SlideSizeDialogUnit.Inches,
            Invariant);

        plan.IsValid.Should().BeFalse();
        plan.FocusField.Should().Be(SlideSizeDialogField.Height);
    }

    [Fact]
    public void BuildOkResult_ValidCentimeters_AcceptsResult()
    {
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "25.4",
            "19.05",
            SlideSizeDialogUnit.Centimeters,
            Invariant);

        result.ShouldApply.Should().BeTrue();
        result.CxEmu.Should().Be(9_144_000L);
        result.CyEmu.Should().Be(6_858_000L);
        result.Validation.Should().BeNull();
    }

    [Fact]
    public void BuildOkResult_UsesProvidedCultureForParsing()
    {
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "25,4",
            "19,05",
            SlideSizeDialogUnit.Centimeters,
            CultureInfo.GetCultureInfo("fr-FR"));

        result.ShouldApply.Should().BeTrue();
        result.CxEmu.Should().Be(9_144_000L);
        result.CyEmu.Should().Be(6_858_000L);
    }

    [Fact]
    public void BuildOkResult_InvalidPositiveNumbers_ReturnsMessageAndFocus()
    {
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "-1",
            "7.5",
            SlideSizeDialogUnit.Inches,
            Invariant);

        result.ShouldApply.Should().BeFalse();
        result.Validation.Should().NotBeNull();
        result.Validation!.Caption.Should().Be(SlideSizeDialogPlanner.InvalidSizeCaption);
        result.Validation.Message.Should().Be(SlideSizeDialogPlanner.InvalidPositiveNumbersMessage);
        result.Validation.FocusField.Should().Be(SlideSizeDialogField.Width);
    }

    [Fact]
    public void BuildOkResult_TooSmall_ReturnsMinimumMessageAndFocus()
    {
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "0.25",
            "7.5",
            SlideSizeDialogUnit.Inches,
            Invariant);

        result.ShouldApply.Should().BeFalse();
        result.Validation.Should().NotBeNull();
        result.Validation!.Caption.Should().Be(SlideSizeDialogPlanner.InvalidSizeCaption);
        result.Validation.Message.Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
        result.Validation.FocusField.Should().Be(SlideSizeDialogField.Width);
    }

    [Fact]
    public void TryApplyResult_ValidResult_UpdatesEditorSlideSize()
    {
        var pres = Presentation.CreateEmpty();
        var editor = new EditingSession(pres, new PresentationCommandBus(pres));
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "11",
            "6.25",
            SlideSizeDialogUnit.Inches,
            Invariant);

        SlideSizeDialogPlanner.TryApplyResult(editor, result).Should().BeTrue();

        pres.SlideSizeCxEmu.Should().Be(10_058_400L);
        pres.SlideSizeCyEmu.Should().Be(5_715_000L);
    }

    [Fact]
    public void TryApplyResult_InvalidResult_DoesNotUpdateEditorSlideSize()
    {
        var pres = Presentation.CreateEmpty();
        var editor = new EditingSession(pres, new PresentationCommandBus(pres));
        var originalCx = pres.SlideSizeCxEmu;
        var originalCy = pres.SlideSizeCyEmu;
        var result = SlideSizeDialogPlanner.BuildOkResult(
            "0.25",
            "6.25",
            SlideSizeDialogUnit.Inches,
            Invariant);

        SlideSizeDialogPlanner.TryApplyResult(editor, result).Should().BeFalse();

        pres.SlideSizeCxEmu.Should().Be(originalCx);
        pres.SlideSizeCyEmu.Should().Be(originalCy);
    }
}
