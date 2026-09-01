using Free.Shared.Ribbon;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r192 (backlog item 34): the Home &gt; Font &gt; Size ribbon control is an EDITABLE combo, so a user
/// can type any number and press Enter. Nothing between that entry point and the writer bounded the
/// value, and PptxPackageWriter emitted <c>(int)Math.Round(FontSizePt * 100)</c> into DrawingML's
/// <c>a:rPr/@sz</c> -- ST_TextFontSize, an int in hundredths of a point bounded to [100, 400000]
/// (1pt to 4000pt). Anything outside that produced a schema-invalid file, and a large enough entry
/// overflowed the cast to a negative value that PowerPoint refuses to open.
/// </summary>
public sealed class R192_FontSizeBoundsTests
{
    private static bool TryGetSize(string typed, out double sizePt)
    {
        var context = RibbonCommandContext.ForSelectedValue(typed);
        return FreePRibbonCommandWorkflow.TryGetFontSize(context, out sizePt);
    }

    [Theory]
    [InlineData("99999999")]
    [InlineData("400001")]
    [InlineData("1e12")]
    public void TryGetFontSize_WithAnOversizeEntry_ClampsToTheLargestLegalSize(string typed)
    {
        TryGetSize(typed, out var sizePt).Should().BeTrue("an out-of-range size is a legible request");
        sizePt.Should().Be(FreePRibbonCommandWorkflow.MaxFontSizePt);

        // The value the writer would emit stays inside ST_TextFontSize.
        var hundredths = (int)System.Math.Round(sizePt * 100);
        hundredths.Should().BeInRange(100, 400_000);
    }

    [Fact]
    public void TryGetFontSize_WithATinyEntry_ClampsToTheSmallestLegalSize()
    {
        TryGetSize("0.001", out var sizePt).Should().BeTrue();
        sizePt.Should().Be(FreePRibbonCommandWorkflow.MinFontSizePt);
    }

    [Theory]
    [InlineData("18", 18.0)]
    [InlineData("10.5", 10.5)]
    [InlineData("4000", 4000.0)]
    [InlineData("1", 1.0)]
    public void TryGetFontSize_WithAnOrdinarySize_LeavesItAlone(string typed, double expected)
    {
        // The clamp must not disturb any size a user would really pick, nor the range boundaries.
        TryGetSize(typed, out var sizePt).Should().BeTrue();
        sizePt.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-12")]
    [InlineData("not a number")]
    [InlineData("")]
    public void TryGetFontSize_WithUnusableInput_StillRejects(string typed)
    {
        // Clamping must not turn junk into a value: rejection is still rejection.
        TryGetSize(typed, out _).Should().BeFalse();
    }
}
