using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeW.App.Host;

/// <summary>
/// Plain single-format text for the print compositor's simplified rows (footnote/endnote bodies drawn at
/// the foot of a page). <see cref="FormattedText"/> cannot switch pair kerning off, so those rows measured
/// and wrapped with WPF's default kerning while the body text follows Word's rule -- kerning off unless a
/// run asks for it (see <c>RunKerningPlanner</c>). A <see cref="TextBlock"/> carries the same typography
/// switch the body flow uses, so both wrap alike.
/// </summary>
internal static class PrintPlainText
{
    /// <summary>Builds and measures the text; <paramref name="maxLines"/> &gt; 0 caps it with an ellipsis.</summary>
    internal static TextBlock Build(
        string text,
        FontFamily family,
        double fontSizeDip,
        Brush brush,
        double maxWidthDip,
        int maxLines = 0,
        FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontSize = fontSizeDip,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap
        };
        Typography.SetKerning(block, false);
        if (maxLines > 0)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.MaxHeight = Math.Ceiling(maxLines * family.LineSpacing * fontSizeDip) + 0.5;
        }

        block.Measure(new Size(Math.Max(1, maxWidthDip), double.PositiveInfinity));
        return block;
    }

    /// <summary>Arranges <paramref name="block"/> at the given page position and returns its height.</summary>
    internal static double Place(ContainerVisual parent, TextBlock block, double x, double y)
    {
        var size = block.DesiredSize;
        block.Arrange(new Rect(x, y, size.Width, size.Height));
        parent.Children.Add(block);
        return size.Height;
    }
}
