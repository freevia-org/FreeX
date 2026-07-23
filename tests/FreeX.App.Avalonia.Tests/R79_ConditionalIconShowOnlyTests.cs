using System.Linq;
using System.Reflection;

using Avalonia.Controls;

using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R79-render-cf-icon-showonly-5-2: an Icon Set rule with "Show Icon Only" checked
/// (<c>IconSetShowValue=false</c>, surfaced as <see cref="CfIconRenderInstruction.ShowValue"/> ==
/// false) correctly hides the cell text on WPF (<c>GridView.ConditionalIcons.cs</c> /
/// <c>GridView.Rendering.cs</c> skip the text draw entirely), but was a no-op on Avalonia: only the
/// icon's text GUTTER (spacing reservation) was skipped via <c>TextGutter == 0</c>, while the
/// <c>TextBlock</c> itself was still unconditionally added to the cell's <c>Grid</c>, so the value
/// text rendered on top of / crowding the icon glyph instead of being hidden. The fix conditions the
/// <c>TextBlock</c> addition in <c>MainWindow.CreateDefaultCellContent</c> on
/// <see cref="CfIconRenderInstruction.ShowValue"/>.
/// </summary>
public sealed class R79_ConditionalIconShowOnlyTests
{
    private static readonly MethodInfo CreateDefaultCellContentMethod = typeof(MainWindow).GetMethod(
        "CreateDefaultCellContent", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Grid Invoke(TextBlock textBlock, CfIconRenderInstruction icon)
    {
        // Parameters: textBlock, style, conditionalDataBar, conditionalIcon, zoomFactor,
        // scaledIndentPadding, sparklineLayer, patternBrush, borderNeighbors, isRightToLeft.
        var parameters = CreateDefaultCellContentMethod.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = textBlock;
        args[1] = null; // style
        args[2] = null; // conditionalDataBar
        args[3] = icon; // conditionalIcon
        args[4] = 1.0; // zoomFactor
        args[5] = 0.0; // scaledIndentPadding
        args[6] = null; // sparklineLayer
        args[7] = null; // patternBrush
        args[8] = default(CellBorderNeighborEdges); // borderNeighbors
        args[9] = false; // isRightToLeft

        return (Grid)CreateDefaultCellContentMethod.Invoke(null, args)!;
    }

    [Fact]
    public void CreateDefaultCellContent_IconSetShowValueFalse_DoesNotAddTheTextBlock()
    {
        var textBlock = new TextBlock { Text = "42" };
        var icon = new CfIconRenderInstruction(
            ConditionalIconGlyphKind.Arrow, IconIndex: 1, IconCount: 3, ColorHex: "#C00000",
            ShowValue: false, TextGutter: 0);

        var content = Invoke(textBlock, icon);

        content.Children.Contains(textBlock).Should().BeFalse(
            "\"Show Icon Only\" must hide the cell text entirely, matching WPF/Excel, instead of " +
            "just skipping the text's gutter margin while still drawing the value on top of the icon");
    }

    [Fact]
    public void CreateDefaultCellContent_IconSetShowValueTrue_NoRegression_StillAddsTheTextBlock()
    {
        var textBlock = new TextBlock { Text = "42" };
        var icon = new CfIconRenderInstruction(
            ConditionalIconGlyphKind.Arrow, IconIndex: 1, IconCount: 3, ColorHex: "#C00000",
            ShowValue: true, TextGutter: 20);

        var content = Invoke(textBlock, icon);

        content.Children.Contains(textBlock).Should().BeTrue(
            "when the rule shows the value alongside the icon, the text must still render as before");
    }
}
