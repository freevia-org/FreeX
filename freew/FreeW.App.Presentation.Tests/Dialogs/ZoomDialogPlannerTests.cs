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

    [Fact]
    public void BuildFitFactors_UsesLiveViewportAndPageGeometryForEveryFitChoice()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
        };

        var result = ZoomDialogPlanner.BuildFitFactors(page, viewportWidthDip: 816, viewportHeightDip: 528);

        result.PageWidthFactor.Should().BeApproximately(1.0, 1e-9);
        result.TextWidthFactor.Should().BeApproximately(1.3076923077, 1e-9);
        result.WholePageFactor.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void BuildFitFactors_DegenerateViewportFallsBackToDefaultZoom()
    {
        var result = ZoomDialogPlanner.BuildFitFactors(new PageSettings(), 0, 0);

        result.Should().Be(new ZoomDialogFitFactors(ZoomLevels.Default, ZoomLevels.Default, ZoomLevels.Default));
    }

    [Fact]
    public void BothHosts_DelegateFitPolicyAndAvaloniaDialogReceivesLiveFactors()
    {
        var repoRoot = FindRepositoryRoot();
        string Read(params string[] segments) =>
            File.ReadAllText(Path.Combine(new[] { repoRoot }.Concat(segments).ToArray()));

        var avaloniaMain = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var avaloniaDialog = Read("freew", "FreeW.App.Avalonia", "ZoomDialog.cs");
        var wpfMain = Read("freew", "FreeW.App.Host", "MainWindow.cs");

        avaloniaMain.Should().Contain("new ZoomDialog(_zoomScale, ComputeZoomFitFactors())");
        avaloniaMain.Should().Contain("ZoomDialogPlanner.BuildFitFactors(_editor.Document.Page");
        avaloniaDialog.Should().Contain("ZoomDialogFitFactors fitFactors");
        avaloniaDialog.Should().Contain("TryCreateResult(BuildSelectionRequest(), _fitFactors");
        avaloniaDialog.Should().NotContain("DefaultFitFactors");
        wpfMain.Should().Contain("ZoomDialogPlanner.BuildFitFactors(_editor.Model.Page");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not resolve repository root.");
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
