using FluentAssertions;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class SlideCanvasLineSpacingTests
{
    [Theory]
    [InlineData(24.0, 28.32)]
    [InlineData(37.3333333333, 44.0533333333)]
    public void ResolvePowerPointLineHeight_UsesCalibratedDefaultLeading(
        double fontSizePx,
        double expected)
    {
        SlideCanvas.ResolvePowerPointLineHeight(fontSizePx)
            .Should().BeApproximately(expected, 0.000001);
    }

    [Theory]
    [InlineData(24.0, 28.8)]
    [InlineData(37.3333333333, 44.8)]
    public void ResolvePowerPointLineHeight_UsesFixedTextLeadingForNoAutofit(
        double fontSizePx,
        double expected)
    {
        SlideCanvas.ResolvePowerPointLineHeight(fontSizePx, TextAutoFitKind.None)
            .Should().BeApproximately(expected, 0.000001);
    }

    [Theory]
    [InlineData(TextAutoFitKind.Normal)]
    [InlineData(TextAutoFitKind.Shape)]
    public void ResolvePowerPointLineHeight_PreservesDefaultLeadingForAutofit(
        TextAutoFitKind autoFitKind)
    {
        SlideCanvas.ResolvePowerPointLineHeight(24.0, autoFitKind)
            .Should().BeApproximately(28.32, 0.000001);
    }

    [Theory]
    [InlineData("Aptos", "Cambria")]
    [InlineData("aptos", "Cambria")]
    [InlineData("Aptos Display", "Aptos Display")]
    [InlineData("Calibri", "Calibri")]
    public void ResolvePowerPointFontFamily_UsesAvaloniaAptosFallback(
        string source,
        string expected)
    {
        SlideCanvas.ResolvePowerPointFontFamily(source).Should().Be(expected);
    }
}
