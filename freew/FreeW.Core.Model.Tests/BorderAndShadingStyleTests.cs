namespace FreeW.Core.Model.Tests;

/// <summary>
/// Covers the Borders-and-Shading model: the <see cref="BorderLineStyle"/> / <see cref="ShadingPattern"/>
/// token mappings used by the docx writer/reader, the per-edge / line-style defaults on
/// <see cref="ParagraphBorder"/> (which must keep the existing quick-toggle box behaviour), and the
/// page-border line style default.
/// </summary>
public class BorderAndShadingStyleTests
{
    [Theory]
    [InlineData(BorderLineStyle.Single, "single")]
    [InlineData(BorderLineStyle.Dotted, "dotted")]
    [InlineData(BorderLineStyle.Dashed, "dashed")]
    [InlineData(BorderLineStyle.Double, "double")]
    [InlineData(BorderLineStyle.Thick, "thick")]
    [InlineData(BorderLineStyle.Wave, "wave")]
    public void BorderLineStyle_RoundTripsThroughToken(BorderLineStyle style, string token)
    {
        BorderLineStyles.ToToken(style).Should().Be(token);
        BorderLineStyles.FromToken(token).Should().Be(style);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nil")]
    [InlineData("somethingElse")]
    public void BorderLineStyle_FromUnknownToken_FallsBackToSingle(string? token)
    {
        BorderLineStyles.FromToken(token).Should().Be(BorderLineStyle.Single);
    }

    [Theory]
    [InlineData(ShadingPattern.Clear, "clear")]
    [InlineData(ShadingPattern.Solid, "solid")]
    [InlineData(ShadingPattern.Pct10, "pct10")]
    [InlineData(ShadingPattern.Pct25, "pct25")]
    [InlineData(ShadingPattern.Pct50, "pct50")]
    public void ShadingPattern_RoundTripsThroughToken(ShadingPattern pattern, string token)
    {
        ShadingPatterns.ToToken(pattern).Should().Be(token);
        ShadingPatterns.FromToken(token).Should().Be(pattern);
    }

    [Fact]
    public void ShadingPattern_FromUnknownToken_FallsBackToClear()
    {
        ShadingPatterns.FromToken("pct99").Should().Be(ShadingPattern.Clear);
    }

    [Fact]
    public void ParagraphBorder_DefaultsToFullSingleBox()
    {
        var border = new ParagraphBorder();

        border.LineStyle.Should().Be(BorderLineStyle.Single);
        border.Top.Should().BeTrue();
        border.Left.Should().BeTrue();
        border.Bottom.Should().BeTrue();
        border.Right.Should().BeTrue();
        border.BottomOnly.Should().BeFalse();
    }

    [Fact]
    public void ParagraphBorder_PerEdgeAndStyle_AreCarried()
    {
        var border = new ParagraphBorder("#123456", 1.25)
        {
            LineStyle = BorderLineStyle.Double,
            Top = false,
            Right = false,
        };

        border.ColorHex.Should().Be("#123456");
        border.WidthPt.Should().BeApproximately(1.25, 0.001);
        border.LineStyle.Should().Be(BorderLineStyle.Double);
        border.Top.Should().BeFalse();
        border.Left.Should().BeTrue();
        border.Bottom.Should().BeTrue();
        border.Right.Should().BeFalse();
    }

    [Fact]
    public void PageBorder_DefaultsToSingleLineStyle()
    {
        new PageBorder().LineStyle.Should().Be(BorderLineStyle.Single);
        (new PageBorder("#000000", 1.0) with { LineStyle = BorderLineStyle.Wave }).LineStyle
            .Should().Be(BorderLineStyle.Wave);
    }

    [Fact]
    public void ParagraphFormatting_ShadingPattern_DefaultsToClear()
    {
        ParagraphFormatting.Default.ShadingPattern.Should().Be(ShadingPattern.Clear);
    }
}
