using FluentAssertions;
using FreeP.App.Compositor;
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
    [InlineData("Aptos", "Arial")]
    [InlineData("aptos", "Arial")]
    [InlineData("Aptos Display", "Aptos Display")]
    [InlineData("Calibri", "Calibri")]
    public void ResolvePowerPointFontFamily_UsesAvaloniaAptosFallback(
        string source,
        string expected)
    {
        SlideCanvas.ResolvePowerPointFontFamily(source).Should().Be(expected);
    }

    [Theory]
    [InlineData("Aptos", 0.95)]
    [InlineData("aptos", 0.95)]
    [InlineData("Aptos Display", 1.0)]
    [InlineData("Calibri", 1.0)]
    public void ResolvePowerPointFontScale_OnlyCalibratesAptosFallback(
        string source,
        double expected)
    {
        SlideCanvas.ResolvePowerPointFontScale(source)
            .Should().BeApproximately(expected, 0.000001);
    }

    [Fact]
    public void UsesImportedAptosBodyOrigin_RecognizesOnlyTheGuardedBulletBodySignature()
    {
        var paragraphs = Enumerable.Range(0, 6)
            .Select(_ => new ResolvedParagraph
            {
                Runs = new[]
                {
                    new ResolvedRun { Text = "Bullet", FontFamily = "Aptos", FontSizePt = 18.0 }
                },
                BulletKind = BulletKind.Char
            })
            .ToArray();

        SlideCanvas.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs
        }).Should().BeTrue();

        SlideCanvas.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs.Take(5).ToArray()
        }).Should().BeFalse();

        SlideCanvas.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.None,
            Paragraphs = paragraphs
        }).Should().BeFalse();

        SlideCanvas.UsesImportedAptosBodyOrigin(new ResolvedTextLayout
        {
            AutoFitKind = TextAutoFitKind.Shape,
            Paragraphs = paragraphs.Skip(1).Append(new ResolvedParagraph
            {
                Runs = new[]
                {
                    new ResolvedRun { Text = "Bullet", FontFamily = "Calibri", FontSizePt = 18.0 }
                },
                BulletKind = BulletKind.Char
            }).ToArray()
        }).Should().BeFalse();
    }
}
