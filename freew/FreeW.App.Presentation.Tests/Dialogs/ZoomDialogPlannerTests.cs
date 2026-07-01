using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ZoomDialogPlannerTests
{
    [Fact]
    public void Presets_ExposeWordZoomDialogChoicesInDisplayOrder()
    {
        ZoomDialogPlanner.Presets.Should().Equal(200, 100, 75);
    }

    [Fact]
    public void Build_SelectsMatchingPresetAndFormatsCurrentPercent()
    {
        var plan = ZoomDialogPlanner.Build(1.0);

        plan.CurrentPercent.Should().Be(100);
        plan.CustomPercentText.Should().Be(100.ToString(CultureInfo.CurrentCulture));
        plan.InitialChoice.Should().Be(ZoomDialogInitialChoice.Preset);
        plan.Presets.Should().ContainSingle(preset => preset.Percent == 100 && preset.IsSelected);
        plan.Presets.Where(preset => preset.Percent != 100).Should().OnlyContain(preset => !preset.IsSelected);
    }

    [Fact]
    public void Build_SelectsCustomWhenCurrentPercentDoesNotMatchPreset()
    {
        var plan = ZoomDialogPlanner.Build(1.25);

        plan.CurrentPercent.Should().Be(125);
        plan.InitialChoice.Should().Be(ZoomDialogInitialChoice.Custom);
        plan.Presets.Should().OnlyContain(preset => !preset.IsSelected);
    }

    [Theory]
    [InlineData(ZoomDialogFitOption.PageWidth, 1.31)]
    [InlineData(ZoomDialogFitOption.TextWidth, 1.42)]
    [InlineData(ZoomDialogFitOption.WholePage, 0.68)]
    public void TryCreateResult_ReturnsHostSuppliedFitFactor(ZoomDialogFitOption fitOption, double expected)
    {
        var request = new ZoomDialogSelectionRequest(fitOption, PresetPercent: null, CustomPercentText: "not parsed");
        var fits = new ZoomDialogFitFactors(PageWidthFactor: 1.31, TextWidthFactor: 1.42, WholePageFactor: 0.68);

        ZoomDialogPlanner.TryCreateResult(request, fits, out var result, out var error).Should().BeTrue();

        result.Should().Be(expected);
        error.Should().BeNull();
    }

    [Fact]
    public void TryCreateResult_ReturnsSelectedPresetViaZoomLevels()
    {
        var request = new ZoomDialogSelectionRequest(FitOption: null, PresetPercent: 75, CustomPercentText: "not parsed");

        ZoomDialogPlanner
            .TryCreateResult(request, new ZoomDialogFitFactors(1.1, 1.2, 0.7), out var result, out var error)
            .Should()
            .BeTrue();

        result.Should().Be(0.75);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("125", 1.25)]
    [InlineData("125%", 1.25)]
    [InlineData("25", ZoomLevels.Min)]
    [InlineData("250", ZoomLevels.Max)]
    public void TryCreateResult_ParsesWholeCustomPercentAndClampsThroughZoomLevels(string input, double expected)
    {
        var request = new ZoomDialogSelectionRequest(FitOption: null, PresetPercent: null, CustomPercentText: input);

        ZoomDialogPlanner
            .TryCreateResult(request, new ZoomDialogFitFactors(1.1, 1.2, 0.7), out var result, out var error)
            .Should()
            .BeTrue();

        result.Should().Be(expected);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("125.5")]
    [InlineData("abc")]
    public void TryCreateResult_RejectsNonIntegerCustomPercent(string input)
    {
        var request = new ZoomDialogSelectionRequest(FitOption: null, PresetPercent: null, CustomPercentText: input);

        ZoomDialogPlanner
            .TryCreateResult(request, new ZoomDialogFitFactors(1.1, 1.2, 0.7), out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(ZoomDialogValidationError.WholePercentRequired);
    }

    [Fact]
    public void ValidationMessageFor_ReturnsWordZoomCustomPercentMessage()
    {
        ZoomDialogPlanner.ValidationMessageFor(ZoomDialogValidationError.WholePercentRequired)
            .Should()
            .Be("Enter a whole zoom percentage.");
    }
}
