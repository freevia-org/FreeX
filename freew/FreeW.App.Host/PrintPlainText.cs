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

    /// <summary>
    /// Builds a note row the way Word prints it: the note's reference mark as a raised, reduced-size label
    /// directly followed by the note text (no "85. " plain-text prefix).
    /// </summary>
    internal static TextBlock BuildNote(
        string? label,
        string text,
        FontFamily family,
        double fontSizeDip,
        Brush brush,
        double maxWidthDip)
    {
        if (string.IsNullOrEmpty(label))
            return Build(text, family, fontSizeDip, brush, maxWidthDip);

        var block = new TextBlock
        {
            FontFamily = family,
            FontSize = fontSizeDip,
            FontWeight = FontWeights.Normal,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap
        };
        block.Inlines.Add(new Run(label)
        {
            BaselineAlignment = BaselineAlignment.Superscript,
            FontSize = fontSizeDip * 0.65
        });
        block.Inlines.Add(new Run(text));
        Typography.SetKerning(block, false);
        block.Measure(new Size(Math.Max(1, maxWidthDip), double.PositiveInfinity));
        return block;
    }

    /// <summary>
    /// Builds and places one footnote row using the note paragraph's own layout: Word's number + tab hanging
    /// layout (the mark hangs left of the text column, wrapped lines start at the text column), the paragraph's
    /// exact line pitch and its justification. A row with no indent and no exact pitch takes the plain path.
    /// Returns the height the row occupies.
    /// </summary>
    internal static double PlaceNoteRow(
        ContainerVisual parent,
        string? label,
        string text,
        FontFamily family,
        double fontSizeDip,
        Brush brush,
        double x,
        double y,
        double widthDip,
        double exactLineHeightDip,
        double indentDip,
        double hangingDip,
        bool justify)
    {
        if (exactLineHeightDip <= 0 && indentDip <= 0 && !justify)
            return Place(parent, BuildNote(label, text, family, fontSizeDip, brush, widthDip), x, y);

        var hanging = hangingDip > 0 && indentDip > 0;
        var bodyText = hanging ? text.TrimStart() : text;
        var bodyBlock = new TextBlock
        {
            Text = bodyText,
            FontFamily = family,
            FontSize = fontSizeDip,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = justify ? System.Windows.TextAlignment.Justify : System.Windows.TextAlignment.Left,
        };
        if (exactLineHeightDip > 0)
        {
            bodyBlock.LineHeight = exactLineHeightDip;
            bodyBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        Typography.SetKerning(bodyBlock, false);
        if (!hanging && !string.IsNullOrEmpty(label))
        {
            // A flush paragraph keeps its mark inline ahead of the text.
            bodyBlock.Inlines.InsertBefore(bodyBlock.Inlines.FirstInline, new Run(label)
            {
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = fontSizeDip * 0.65
            });
        }

        var textLeft = x + (hanging ? indentDip : 0);
        bodyBlock.Measure(new Size(Math.Max(1, widthDip - (hanging ? indentDip : 0)), double.PositiveInfinity));
        var height = Place(parent, bodyBlock, textLeft, y);
        if (hanging && !string.IsNullOrEmpty(label))
        {
            var labelBlock = new TextBlock
            {
                FontFamily = family,
                FontSize = fontSizeDip,
                Foreground = brush,
                TextWrapping = TextWrapping.NoWrap,
            };
            if (exactLineHeightDip > 0)
            {
                labelBlock.LineHeight = exactLineHeightDip;
                labelBlock.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }

            labelBlock.Inlines.Add(new Run(label)
            {
                BaselineAlignment = BaselineAlignment.Superscript,
                FontSize = fontSizeDip * 0.65
            });
            labelBlock.Measure(new Size(Math.Max(1, indentDip), double.PositiveInfinity));
            Place(parent, labelBlock, x + indentDip - hangingDip, y);
        }

        return height;
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
