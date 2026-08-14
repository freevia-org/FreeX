using FluentAssertions;
using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Covers the neutral owner of the sheet-tab width estimate that both renderers duplicated
/// (WPF MainWindow.SheetTabs.cs, Avalonia MainWindow.cs). The two hosts' formulas genuinely
/// differ -- they are calibrated against different tab templates -- so the arithmetic is shared
/// while the calibration stays per-host; these tests pin BOTH calibrations to the exact numbers
/// the old private copies produced.
/// </summary>
public sealed class SheetTabWidthEstimatorTests
{
    // Old WPF copy: Math.Max(86, 54 + (isProtected ? 16 : 0) + (name?.Length ?? 0) * 7.5)
    private static double LegacyWpf(string? name, bool isProtected) =>
        Math.Max(86, 54 + (isProtected ? 16.0 : 0.0) + (name?.Length ?? 0) * 7.5);

    // Old Avalonia copy: Math.Clamp(20 + Math.Max(1, name.Length) * 6.6, 60, 168)
    private static double LegacyAvalonia(string name) =>
        Math.Clamp(20 + Math.Max(1, name.Length) * 6.6, 60, 168);

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Sheet1")]
    [InlineData("Quarterly Revenue Rollup")]
    [InlineData("An extremely long worksheet name that nobody should ever type but somebody will")]
    public void Wpf_MatchesTheLegacyPrivateCopy(string name)
    {
        SheetTabWidthEstimator.Estimate(name, isProtected: false, SheetTabWidthEstimator.Wpf)
            .Should().Be(LegacyWpf(name, isProtected: false));
        SheetTabWidthEstimator.Estimate(name, isProtected: true, SheetTabWidthEstimator.Wpf)
            .Should().Be(LegacyWpf(name, isProtected: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Sheet1")]
    [InlineData("Quarterly Revenue Rollup")]
    [InlineData("An extremely long worksheet name that nobody should ever type but somebody will")]
    public void Avalonia_MatchesTheLegacyPrivateCopy(string name)
    {
        SheetTabWidthEstimator.Estimate(name, SheetTabWidthEstimator.Avalonia)
            .Should().Be(LegacyAvalonia(name));
    }

    [Fact]
    public void Wpf_ShortNamesClampToTheMinimumWidth()
    {
        // 54 + 1 * 7.5 = 61.5, below the 86 floor.
        SheetTabWidthEstimator.Estimate("A", isProtected: false, SheetTabWidthEstimator.Wpf)
            .Should().Be(86);
        SheetTabWidthEstimator.Estimate("", isProtected: false, SheetTabWidthEstimator.Wpf)
            .Should().Be(86);
    }

    [Fact]
    public void Wpf_LongNamesGrowWithoutAnUpperClamp()
    {
        var name = new string('X', 200);

        SheetTabWidthEstimator.Estimate(name, isProtected: false, SheetTabWidthEstimator.Wpf)
            .Should().Be(54 + 200 * 7.5);
    }

    [Fact]
    public void Wpf_ProtectedIndicatorAddsWidthOnlyOnceTheMinimumIsCleared()
    {
        // Short name: both stay pinned at the 86 floor, so the indicator is invisible in the estimate.
        SheetTabWidthEstimator.Estimate("Sh", isProtected: true, SheetTabWidthEstimator.Wpf)
            .Should().Be(SheetTabWidthEstimator.Estimate("Sh", isProtected: false, SheetTabWidthEstimator.Wpf));

        // Long name: the indicator is a straight +16.
        var unprotected = SheetTabWidthEstimator.Estimate("Consolidated", isProtected: false, SheetTabWidthEstimator.Wpf);
        SheetTabWidthEstimator.Estimate("Consolidated", isProtected: true, SheetTabWidthEstimator.Wpf)
            .Should().Be(unprotected + 16);
    }

    [Fact]
    public void Avalonia_ShortNamesClampToSixtyAndLongNamesClampToOneSixtyEight()
    {
        SheetTabWidthEstimator.Estimate("", SheetTabWidthEstimator.Avalonia).Should().Be(60);
        SheetTabWidthEstimator.Estimate("A", SheetTabWidthEstimator.Avalonia).Should().Be(60);
        SheetTabWidthEstimator.Estimate(new string('X', 500), SheetTabWidthEstimator.Avalonia)
            .Should().Be(168);
    }

    [Fact]
    public void Avalonia_MidLengthNamesScaleLinearly()
    {
        // 20 + 10 * 6.6 = 86, inside the [60, 168] band.
        SheetTabWidthEstimator.Estimate("SheetTenXX", SheetTabWidthEstimator.Avalonia).Should().Be(86);
    }

    [Fact]
    public void Avalonia_HasNoProtectedIndicatorAllowance()
    {
        SheetTabWidthEstimator
            .Estimate("Consolidated", isProtected: true, SheetTabWidthEstimator.Avalonia)
            .Should()
            .Be(SheetTabWidthEstimator.Estimate("Consolidated", SheetTabWidthEstimator.Avalonia));
    }

    [Fact]
    public void NullNameIsTreatedAsEmpty()
    {
        SheetTabWidthEstimator.Estimate(null, isProtected: false, SheetTabWidthEstimator.Wpf)
            .Should().Be(86);
        SheetTabWidthEstimator.Estimate(null, SheetTabWidthEstimator.Avalonia)
            .Should().Be(60);
    }

    [Fact]
    public void InvertedMinimumAndMaximumMetricsDoNotThrow()
    {
        var metrics = new SheetTabWidthMetrics(
            BaseWidth: 10,
            CharacterWidth: 5,
            ProtectedIndicatorWidth: 0,
            MinimumWidth: 100,
            MaximumWidth: 40,
            TreatEmptyNameAsSingleCharacter: false);

        SheetTabWidthEstimator.Estimate("Sheet1", metrics).Should().Be(100);
    }
}
