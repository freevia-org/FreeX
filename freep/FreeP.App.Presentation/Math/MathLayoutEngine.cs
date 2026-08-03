using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeP.App.Compositor.MathLayout;

// ── Math layout engine (Theme 27) ────────────────────────────────────────────
//
// Converts a MathNode tree into a MathBox tree of positioned primitives.
// All sizing is approximated from the base font size; no font-metric calls are
// made so the engine is framework-free.
//
// Standard math typesetting approximations used:
//   • em = fontSizePt * (96/72)  (DIP)
//   • Math axis (center of operators/fractions) at 0.45 em above baseline
//   • Fraction bar on the math axis; num/den stacked x0.1 em gap from bar
//   • Script size = 0.70 x parent size, shift-up (sup) = 0.40 em, shift-down (sub) = 0.25 em
//   • Radical sign width x 0.65 em; overline clearance = 0.10 em
//   • N-ary operator enlarged to 1.5 x base size; limits at 0.70 x base
//   • Delimiter brackets scale to content height * 1.10

public static class MathLayoutEngine
{
    private const string MatrixPlaceholderGlyph = "\u25A1";
    private const double TwipsPerDip = 15.0;
    private readonly record struct LayoutOptions(bool SmallFraction);

    // ── Public entry ──────────────────────────────────────────────────────

    private static string ApplyMathAlphabet(string text, MathNode.MathAlphabet alphabet, bool isItalic, bool isBold)
    {
        if (string.IsNullOrEmpty(text) || alphabet is MathNode.MathAlphabet.Default or MathNode.MathAlphabet.Roman)
            return text;

        var mapped = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
            mapped.Append(MapMathAlphabetChar(ch, alphabet, isItalic, isBold));
        return mapped.ToString();
    }

    private static string MapMathAlphabetChar(char ch, MathNode.MathAlphabet alphabet, bool isItalic, bool isBold) =>
        alphabet switch
        {
            MathNode.MathAlphabet.Script when isBold => MapConsecutiveAlphabet(ch, upperStart: 0x1D4D0, lowerStart: 0x1D4EA),
            MathNode.MathAlphabet.Script => MapScript(ch),
            MathNode.MathAlphabet.Fraktur when isBold => MapConsecutiveAlphabet(ch, upperStart: 0x1D56C, lowerStart: 0x1D586),
            MathNode.MathAlphabet.Fraktur => MapFraktur(ch),
            MathNode.MathAlphabet.DoubleStruck => MapDoubleStruck(ch),
            MathNode.MathAlphabet.SansSerif when isBold && isItalic => MapConsecutiveAlphabet(ch, upperStart: 0x1D63C, lowerStart: 0x1D656),
            MathNode.MathAlphabet.SansSerif when isBold => MapConsecutiveAlphabet(ch, upperStart: 0x1D5D4, lowerStart: 0x1D5EE, digitStart: 0x1D7EC),
            MathNode.MathAlphabet.SansSerif when isItalic => MapConsecutiveAlphabet(ch, upperStart: 0x1D608, lowerStart: 0x1D622),
            MathNode.MathAlphabet.SansSerif => MapConsecutiveAlphabet(ch, upperStart: 0x1D5A0, lowerStart: 0x1D5BA, digitStart: 0x1D7E2),
            MathNode.MathAlphabet.Monospace => MapConsecutiveAlphabet(ch, upperStart: 0x1D670, lowerStart: 0x1D68A, digitStart: 0x1D7F6),
            _ => ch.ToString()
        };

    private static string MapScript(char ch) =>
        ch switch
        {
            'A' => FromCodePoint(0x1D49C),
            'B' => "\u212C",
            'C' => FromCodePoint(0x1D49E),
            'D' => FromCodePoint(0x1D49F),
            'E' => "\u2130",
            'F' => "\u2131",
            'G' => FromCodePoint(0x1D4A2),
            'H' => "\u210B",
            'I' => "\u2110",
            'J' => FromCodePoint(0x1D4A5),
            'K' => FromCodePoint(0x1D4A6),
            'L' => "\u2112",
            'M' => "\u2133",
            >= 'N' and <= 'Q' => FromCodePoint(0x1D4A9 + (ch - 'N')),
            'R' => "\u211B",
            >= 'S' and <= 'Z' => FromCodePoint(0x1D4AE + (ch - 'S')),
            >= 'a' and <= 'd' => FromCodePoint(0x1D4B6 + (ch - 'a')),
            'e' => "\u212F",
            'f' => FromCodePoint(0x1D4BB),
            'g' => "\u210A",
            >= 'h' and <= 'n' => FromCodePoint(0x1D4BD + (ch - 'h')),
            'o' => "\u2134",
            >= 'p' and <= 'z' => FromCodePoint(0x1D4C5 + (ch - 'p')),
            _ => ch.ToString()
        };

    private static string MapFraktur(char ch) =>
        ch switch
        {
            'A' => FromCodePoint(0x1D504),
            'B' => FromCodePoint(0x1D505),
            'C' => "\u212D",
            >= 'D' and <= 'G' => FromCodePoint(0x1D507 + (ch - 'D')),
            'H' => "\u210C",
            'I' => "\u2111",
            >= 'J' and <= 'Q' => FromCodePoint(0x1D50D + (ch - 'J')),
            'R' => "\u211C",
            >= 'S' and <= 'Y' => FromCodePoint(0x1D516 + (ch - 'S')),
            'Z' => "\u2128",
            >= 'a' and <= 'z' => FromCodePoint(0x1D51E + (ch - 'a')),
            _ => ch.ToString()
        };

    private static string MapDoubleStruck(char ch) =>
        ch switch
        {
            'A' => FromCodePoint(0x1D538),
            'B' => FromCodePoint(0x1D539),
            'C' => "\u2102",
            >= 'D' and <= 'G' => FromCodePoint(0x1D53B + (ch - 'D')),
            'H' => "\u210D",
            >= 'I' and <= 'M' => FromCodePoint(0x1D540 + (ch - 'I')),
            'N' => "\u2115",
            'O' => FromCodePoint(0x1D546),
            'P' => "\u2119",
            'Q' => "\u211A",
            'R' => "\u211D",
            >= 'S' and <= 'Y' => FromCodePoint(0x1D54A + (ch - 'S')),
            'Z' => "\u2124",
            >= 'a' and <= 'z' => FromCodePoint(0x1D552 + (ch - 'a')),
            >= '0' and <= '9' => FromCodePoint(0x1D7D8 + (ch - '0')),
            _ => ch.ToString()
        };

    private static string MapConsecutiveAlphabet(char ch, int upperStart, int lowerStart, int? digitStart = null) =>
        ch switch
        {
            >= 'A' and <= 'Z' => FromCodePoint(upperStart + (ch - 'A')),
            >= 'a' and <= 'z' => FromCodePoint(lowerStart + (ch - 'a')),
            >= '0' and <= '9' when digitStart.HasValue => FromCodePoint(digitStart.Value + (ch - '0')),
            _ => ch.ToString()
        };

    private static string FromCodePoint(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static int CountTextElements(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Lay out <paramref name="node"/> at the given base font size and return
    /// a <see cref="MathBox.Container"/> with all children positioned.
    /// The container's (X,Y) is always (0,0); the caller translates it to
    /// the desired slide position.
    /// </summary>
    public static MathBox.Container Layout(
        MathNode node,
        string fontFamily,
        double fontSizePt,
        double? paragraphWidthDip = null)
    {
        var box = node is MathNode.MathParagraph paragraph
            ? LayoutMathParagraph(
                paragraph,
                fontFamily,
                fontSizePt,
                paragraphWidthDip,
                new LayoutOptions(paragraph.SmallFraction == true))
            : LayoutNode(node, fontFamily, fontSizePt);
        var root = new MathBox.Container();
        root.Children.Add(box);
        box.X = 0; box.Y = 0;
        root.Metrics.Width  = box.Metrics.Width;
        root.Metrics.Height = box.Metrics.Height;
        root.Metrics.Ascent = box.Metrics.Ascent;
        return root;
    }

    // ── Node dispatcher ───────────────────────────────────────────────────

    private static MathBox LayoutNode(
        MathNode node,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options = default)
    {
        return node switch
        {
            MathNode.MathRoot root => LayoutNode(
                root.Content,
                string.IsNullOrWhiteSpace(root.Properties.MathFontFamily)
                    ? fontFamily
                    : root.Properties.MathFontFamily!,
                fontSizePt,
                options with { SmallFraction = root.Properties.SmallFraction ?? options.SmallFraction }),
            MathNode.Run     r  => LayoutRun(r, fontFamily, fontSizePt),
            MathNode.Frac    f  => LayoutFrac(f, fontFamily, fontSizePt, options),
            MathNode.Sup     s  => LayoutSup(s, fontFamily, fontSizePt, options),
            MathNode.Sub     s  => LayoutSub(s, fontFamily, fontSizePt, options),
            MathNode.SubSup  ss => LayoutSubSup(ss, fontFamily, fontSizePt, options),
            MathNode.PreSubSup ps => LayoutPreSubSup(ps, fontFamily, fontSizePt, options),
            MathNode.Rad     r  => LayoutRad(r, fontFamily, fontSizePt, options),
            MathNode.Nary    n  => LayoutNary(n, fontFamily, fontSizePt, options),
            MathNode.Limit   l  => LayoutLimit(l, fontFamily, fontSizePt, options),
            MathNode.Func    fn => LayoutFunc(fn, fontFamily, fontSizePt, options),
            MathNode.Delim   d  => LayoutDelim(d, fontFamily, fontSizePt, options),
            MathNode.Acc     a  => LayoutAcc(a, fontFamily, fontSizePt, options),
            MathNode.Bar     b  => LayoutBar(b, fontFamily, fontSizePt, options),
            MathNode.Box     bx => LayoutBox(bx, fontFamily, fontSizePt, options),
            MathNode.ArgSize arg => LayoutArgSize(arg, fontFamily, fontSizePt, options),
            MathNode.Phantom ph => LayoutPhantom(ph, fontFamily, fontSizePt, options),
            MathNode.BorderBox bb => LayoutBorderBox(bb, fontFamily, fontSizePt, options),
            MathNode.GroupChr g => LayoutGroupChr(g, fontFamily, fontSizePt, options),
            MathNode.Matrix  m  => LayoutMatrix(m, fontFamily, fontSizePt, options),
            MathNode.EqArray e  => LayoutEqArray(e, fontFamily, fontSizePt, options),
            MathNode.WrappedParagraph w => LayoutWrappedParagraph(
                w,
                double.PositiveInfinity,
                MathNode.MathParagraphJustification.Left,
                fontFamily,
                fontSizePt,
                options),
            MathNode.MathParagraph p => LayoutMathParagraph(p, fontFamily, fontSizePt, paragraphWidthDip: null, options),
            MathNode.Row     rw => LayoutRow(rw.Children, fontFamily, fontSizePt, options),
            MathNode.Unknown u  => LayoutFallback(u.FallbackText, fontFamily, fontSizePt),
            _                   => LayoutFallback("?", fontFamily, fontSizePt)
        };
    }

    private static MathBox LayoutMathParagraph(
        MathNode.MathParagraph paragraph,
        string fontFamily,
        double fontSizePt,
        double? paragraphWidthDip,
        LayoutOptions options)
    {
        var effectiveFontFamily = string.IsNullOrWhiteSpace(paragraph.MathFontFamily)
            ? fontFamily
            : paragraph.MathFontFamily!;
        var (leftMarginDip, rightMarginDip, contentWidthDip) =
            ResolveMathMargins(paragraph, paragraphWidthDip);
        var content = paragraphWidthDip is > 0
            ? WrapBinaryOperators(
                paragraph.Content,
                contentWidthDip,
                paragraph.BinaryBreak,
                paragraph.BinarySubtraction,
                effectiveFontFamily,
                fontSizePt,
                options,
                paragraph.WrapIndentTwips,
                paragraph.WrapRight)
            : paragraph.Content;
        var contentOptions = options with { SmallFraction = paragraph.SmallFraction ?? options.SmallFraction };
        var contentBox = content is MathNode.WrappedParagraph wrapped && paragraphWidthDip is > 0
            ? LayoutWrappedParagraph(
                wrapped,
                contentWidthDip,
                paragraph.Justification,
                effectiveFontFamily,
                fontSizePt,
                contentOptions)
            : LayoutNode(content, effectiveFontFamily, fontSizePt, contentOptions);
        var hasWrappedContinuationLines = content is MathNode.WrappedParagraph;
        var width = paragraphWidthDip is > 0
            ? Math.Max(paragraphWidthDip.Value, leftMarginDip + contentBox.Metrics.Width + rightMarginDip)
            : leftMarginDip + contentBox.Metrics.Width + rightMarginDip;

        var alignmentWidthDip = paragraphWidthDip is > 0
            ? contentWidthDip
            : contentBox.Metrics.Width;
        contentBox.X = hasWrappedContinuationLines
            ? leftMarginDip
            : paragraph.Justification switch
            {
                MathNode.MathParagraphJustification.Right => leftMarginDip +
                    Math.Max(0, alignmentWidthDip - contentBox.Metrics.Width),
                MathNode.MathParagraphJustification.Center or MathNode.MathParagraphJustification.CenterGroup =>
                    leftMarginDip + Math.Max(0, (alignmentWidthDip - contentBox.Metrics.Width) / 2.0),
                _ => leftMarginDip
            };
        contentBox.Y = 0;

        var container = new MathBox.Container();
        container.Children.Add(contentBox);
        container.Metrics.Width = width;
        container.Metrics.Height = contentBox.Metrics.Height;
        container.Metrics.Ascent = contentBox.Metrics.Ascent;
        return container;
    }

    private static MathBox LayoutWrappedParagraph(
        MathNode.WrappedParagraph paragraph,
        double availableWidthDip,
        MathNode.MathParagraphJustification firstLineJustification,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        if (paragraph.Rows.Count == 0)
            return MakeGlyph(string.Empty, fontFamily, fontSizePt, false);

        double em = Em(fontSizePt);
        double rowGap = em * 0.20;
        var rows = new List<MathBox>(paragraph.Rows.Count);
        double maxRowWidth = 0;
        double totalHeight = 0;
        var rowAscents = new double[paragraph.Rows.Count];
        var rowDescents = new double[paragraph.Rows.Count];

        foreach (var row in paragraph.Rows)
        {
            var rowBox = LayoutNode(row, fontFamily, fontSizePt, options);
            rows.Add(rowBox);
            maxRowWidth = Math.Max(maxRowWidth, rowBox.Metrics.Width);
            totalHeight += rowBox.Metrics.Height;
        }

        totalHeight += rowGap * Math.Max(0, rows.Count - 1);
        var width = double.IsFinite(availableWidthDip) && availableWidthDip > 0
            ? availableWidthDip
            : maxRowWidth;
        double continuationIndentDip = TwipsToDip(paragraph.WrapIndentTwips);
        double maxRight = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var rowBox = rows[i];
            rowAscents[i] = rowBox.Metrics.Ascent;
            rowDescents[i] = rowBox.Metrics.Descent;
            var x = i == 0
                ? firstLineJustification switch
                {
                    MathNode.MathParagraphJustification.Right => Math.Max(0, width - rowBox.Metrics.Width),
                    MathNode.MathParagraphJustification.Center or MathNode.MathParagraphJustification.CenterGroup =>
                        Math.Max(0, (width - rowBox.Metrics.Width) / 2.0),
                    _ => 0
                }
                : paragraph.WrapRight
                    ? Math.Max(0, width - rowBox.Metrics.Width)
                    : continuationIndentDip;
            rowBox.X = x;
            rowBox.Y = i == 0 ? 0 : rows[i - 1].Y + rows[i - 1].Metrics.Height + rowGap;
            maxRight = Math.Max(maxRight, x + rowBox.Metrics.Width);
        }

        var container = new MathBox.Container();
        container.Children.AddRange(rows);
        container.Metrics.Width = Math.Max(width, maxRight);
        container.Metrics.Height = totalHeight;
        container.Metrics.Ascent = ResolveStackedArrayAscent(
            MathArrayBaseJustification.Center,
            rowAscents,
            rowDescents,
            rowGap,
            totalHeight,
            em);
        return container;
    }

    private static (double LeftDip, double RightDip, double ContentWidthDip) ResolveMathMargins(
        MathNode.MathParagraph paragraph,
        double? paragraphWidthDip)
    {
        double leftDip = TwipsToDip(paragraph.LeftMarginTwips ?? 0);
        double rightDip = TwipsToDip(paragraph.RightMarginTwips ?? 0);

        if (paragraphWidthDip is not > 0)
            return (leftDip, rightDip, double.PositiveInfinity);

        var availableWidthDip = paragraphWidthDip.Value;
        // Word ignores the left margin when the two authored margins do not fit.
        if (leftDip + rightDip > availableWidthDip)
            leftDip = 0;

        // If the right margin alone exceeds the available width, Word uses the
        // documented 1440-twip fallback indent.
        if (rightDip > availableWidthDip)
            rightDip = TwipsToDip(1440);

        return (leftDip, rightDip, Math.Max(0, availableWidthDip - leftDip - rightDip));
    }

    private static double TwipsToDip(int twips) => twips / TwipsPerDip;

    private static MathNode WrapBinaryOperators(
        MathNode content,
        double availableWidth,
        MathNode.MathParagraphBinaryBreak binaryBreak,
        MathNode.MathParagraphBinarySubtraction binarySubtraction,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options,
        int wrapIndentTwips,
        bool wrapRight)
    {
        if (content is not MathNode.Row row || row.Children.Count < 2)
            return content;

        var naturalLayout = LayoutRow(row.Children, fontFamily, fontSizePt, options);
        if (naturalLayout.Metrics.Width <= availableWidth)
            return content;

        var rows = new List<MathNode>();
        var current = new List<MathNode>();
        foreach (var child in row.Children)
        {
            current.Add(child);
            var currentLayout = LayoutRow(current, fontFamily, fontSizePt, options);
            if (currentLayout.Metrics.Width <= availableWidth)
                continue;

            var operatorIndex = FindLastBinaryOperator(current);
            if (operatorIndex <= 0)
                continue;

            var left = new List<MathNode>();
            var right = new List<MathNode>();
            switch (binaryBreak)
            {
                case MathNode.MathParagraphBinaryBreak.Before:
                    AddRange(left, current, 0, operatorIndex);
                    AddRange(right, current, operatorIndex, current.Count - operatorIndex);
                    break;

                case MathNode.MathParagraphBinaryBreak.After:
                    AddRange(left, current, 0, operatorIndex + 1);
                    AddRange(right, current, operatorIndex + 1, current.Count - operatorIndex - 1);
                    break;

                case MathNode.MathParagraphBinaryBreak.Repeat:
                    AddRange(left, current, 0, operatorIndex);
                    left.Add(CreateRepeatedOperator(
                        current[operatorIndex],
                        binarySubtraction,
                        beforeBreak: true));
                    AddRange(right, current, operatorIndex + 1, current.Count - operatorIndex - 1);
                    right.Insert(0, CreateRepeatedOperator(
                        current[operatorIndex],
                        binarySubtraction,
                        beforeBreak: false));
                    break;
            }

            rows.Add(CreateParagraphRow(left));
            current = right;
        }

        if (rows.Count == 0)
            return content;

        rows.Add(CreateParagraphRow(current));
        return new MathNode.WrappedParagraph(rows, wrapIndentTwips, wrapRight);
    }

    private static int FindLastBinaryOperator(IReadOnlyList<MathNode> nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (TryGetBinaryOperatorText(nodes[i], out _))
                return i;
        }

        return -1;
    }

    private static bool TryGetBinaryOperatorText(MathNode node, out string text)
    {
        switch (node)
        {
            case MathNode.Run run when IsBinaryOperatorText(run.Text):
                text = run.Text.Trim();
                return true;
            case MathNode.Unknown unknown when IsBinaryOperatorText(unknown.FallbackText):
                text = unknown.FallbackText.Trim();
                return true;
            case MathNode.Box box when box.OperatorEmulator:
                return TryGetBinaryOperatorText(box.Base, out text);
            case MathNode.Phantom phantom when phantom.TransparentSpacing:
                return TryGetBinaryOperatorText(phantom.Base, out text);
            default:
                text = string.Empty;
                return false;
        }
    }

    private static bool IsBinaryOperatorText(string text) =>
        text.Trim() is "+" or "-" or "\u2212" or "\u00b1" or "\u00d7" or "\u00f7" or "*" or "/";

    private static MathNode CreateRepeatedOperator(
        MathNode source,
        MathNode.MathParagraphBinarySubtraction binarySubtraction,
        bool beforeBreak)
    {
        if (!TryGetBinaryOperatorText(source, out var sourceText))
            return source;

        var text = sourceText;
        if (IsSubtractionText(sourceText))
        {
            text = binarySubtraction switch
            {
                MathNode.MathParagraphBinarySubtraction.PlusMinus when beforeBreak => "+",
                MathNode.MathParagraphBinarySubtraction.MinusPlus when !beforeBreak => "+",
                _ => sourceText
            };
        }

        if (source is MathNode.Run run)
            return new MathNode.Run(text, run.IsItalic, run.IsBold, run.Alphabet, run.IsLiteral);

        return new MathNode.Run(text, isItalic: false);
    }

    private static bool IsSubtractionText(string text) => text is "-" or "\u2212";

    private static MathNode CreateParagraphRow(IReadOnlyList<MathNode> nodes) =>
        nodes.Count == 1 ? nodes[0] : new MathNode.Row(nodes.ToArray());

    private static void AddRange(List<MathNode> destination, IReadOnlyList<MathNode> source, int start, int count)
    {
        for (var i = 0; i < count; i++)
            destination.Add(source[start + i]);
    }

    // ── Em conversion ────────────────────────────────────────────────────

    private static double Em(double fontSizePt) => fontSizePt * (96.0 / 72.0);

    // ── Run layout ────────────────────────────────────────────────────────

    private static MathBox LayoutRun(MathNode.Run run, string fontFamily, double fontSizePt)
    {
        var text = ApplyMathAlphabet(run.Text, run.Alphabet, run.IsItalic, run.IsBold);
        var alphabetOverridesStyle = run.Alphabet is not MathNode.MathAlphabet.Default and not MathNode.MathAlphabet.Roman;
        return MakeGlyph(
            text,
            fontFamily,
            fontSizePt,
            alphabetOverridesStyle ? false : run.IsItalic,
            alphabetOverridesStyle ? false : run.IsBold);
    }

    // ── Fallback (unknown) ────────────────────────────────────────────────

    private static MathBox LayoutFallback(string text, string fontFamily, double fontSizePt)
    {
        return MakeGlyph(text, fontFamily, fontSizePt, isItalic: false);
    }

    // ── Fraction layout ───────────────────────────────────────────────────

    private static MathBox LayoutFrac(
        MathNode.Frac frac,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        return frac.Type switch
        {
            MathNode.FracType.Linear => LayoutFracLinear(frac, fontFamily, fontSizePt, options),
            MathNode.FracType.Skewed => LayoutFracSkewed(frac, fontFamily, fontSizePt, options),
            MathNode.FracType.NoBar  => LayoutFracStacked(frac, fontFamily, fontSizePt, drawBar: false, options),
            _                        => LayoutFracStacked(frac, fontFamily, fontSizePt, drawBar: true, options),
        };
    }

    /// <summary>
    /// Stacked fraction layout used for both "bar" (default) and "noBar" (binomial)
    /// types: numerator over denominator, centered on the math axis. "bar" additionally
    /// draws the horizontal rule; "noBar" omits it but keeps identical spacing/centering.
    /// </summary>
    private static MathBox LayoutFracStacked(
        MathNode.Frac frac,
        string fontFamily,
        double fontSizePt,
        bool drawBar,
        LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double childSizePt = fontSizePt * (options.SmallFraction ? 0.70 : 0.85);

        var numBox = LayoutNode(frac.Numerator, fontFamily, childSizePt, options);
        var denBox = LayoutNode(frac.Denominator, fontFamily, childSizePt, options);

        // Fraction bar is on the math axis = 0.45 em above baseline
        double mathAxisAboveBaseline = em * 0.45;
        double barThickness = em * 0.07;
        double gap = em * 0.10; // gap between bar (or bar's would-be position) and num/den

        // Position numerator so its bottom sits gap above the bar
        // Bar center = mathAxisAboveBaseline above baseline
        // We measure from the TOP of the whole expression.
        // ascent of the whole frac = num.Height + gap + barThickness/2 + mathAxisAboveBaseline
        // but we'll compute layout as:
        //   numY = 0
        //   barY = numBox.Metrics.Height + gap
        //   denY = barY + barThickness + gap
        //   total height = denY + denBox.Metrics.Height
        //   baseline position from top = barY + barThickness / 2 + mathAxisAboveBaseline
        //
        // "noBar" keeps this exact geometry (so num/den centering and spacing is
        // unchanged) — it only skips adding the HRule child.

        double barY  = numBox.Metrics.Height + gap;
        double denY  = barY + barThickness + gap;
        double totalH = denY + denBox.Metrics.Height;

        double totalW = Math.Max(numBox.Metrics.Width, denBox.Metrics.Width) + em * 0.10;

        // Center num and den horizontally
        double numX  = (totalW - numBox.Metrics.Width) / 2.0;
        double denX  = (totalW - denBox.Metrics.Width) / 2.0;

        // Baseline: the math axis of the fraction sits at the fraction bar center
        double ascent = barY + barThickness / 2.0 + mathAxisAboveBaseline;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        // Numerator
        numBox.X = numX; numBox.Y = 0;
        container.Children.Add(numBox);

        // Fraction bar (omitted for "noBar" — binomial-coefficient style)
        if (drawBar)
        {
            var bar = new MathBox.HRule
            {
                X = 0, Y = barY,
                LineWidth = totalW,
                Thickness = barThickness
            };
            bar.Metrics.Width  = totalW;
            bar.Metrics.Height = barThickness;
            bar.Metrics.Ascent = 0;
            container.Children.Add(bar);
        }

        // Denominator
        denBox.X = denX; denBox.Y = denY;
        container.Children.Add(denBox);

        return container;
    }

    /// <summary>
    /// Linear fraction layout ("lin", and the "skw" approximation): numerator,
    /// slash, denominator, all inline on the baseline — not stacked.
    /// </summary>
    private static MathBox LayoutFracLinear(
        MathNode.Frac frac,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double gap = em * 0.08;

        // m:smallFrac uses script-size numerator/denominator content. The
        // slash remains full-size so the inline operator stays legible.
        var childSizePt = fontSizePt * (options.SmallFraction ? 0.70 : 1.0);
        var numBox   = LayoutNode(frac.Numerator, fontFamily, childSizePt, options);
        var slashBox = MakeGlyph("/", fontFamily, fontSizePt, isItalic: false);
        var denBox   = LayoutNode(frac.Denominator, fontFamily, childSizePt, options);

        // Common baseline = max ascent across the three inline boxes
        double ascent = Math.Max(numBox.Metrics.Ascent, Math.Max(slashBox.Metrics.Ascent, denBox.Metrics.Ascent));

        double totalW = numBox.Metrics.Width + gap + slashBox.Metrics.Width + gap + denBox.Metrics.Width;
        double totalH = Math.Max(
            (ascent - numBox.Metrics.Ascent) + numBox.Metrics.Height,
            Math.Max(
                (ascent - slashBox.Metrics.Ascent) + slashBox.Metrics.Height,
                (ascent - denBox.Metrics.Ascent) + denBox.Metrics.Height));

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double x = 0;
        numBox.X = x; numBox.Y = ascent - numBox.Metrics.Ascent;
        container.Children.Add(numBox);
        x += numBox.Metrics.Width + gap;

        slashBox.X = x; slashBox.Y = ascent - slashBox.Metrics.Ascent;
        container.Children.Add(slashBox);
        x += slashBox.Metrics.Width + gap;

        denBox.X = x; denBox.Y = ascent - denBox.Metrics.Ascent;
        container.Children.Add(denBox);

        return container;
    }

    /// <summary>
    /// Skewed fraction layout ("skw"): numerator above-left, denominator
    /// below-right, with a renderer-neutral diagonal line between them.
    /// </summary>
    private static MathBox LayoutFracSkewed(
        MathNode.Frac frac,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double childSizePt = fontSizePt * (options.SmallFraction ? 0.70 : 0.85);
        double gapX = em * 0.10;
        double gapY = em * 0.06;
        double lineThickness = Math.Max(1.0, em * 0.06);

        var numBox = LayoutNode(frac.Numerator, fontFamily, childSizePt, options);
        var denBox = LayoutNode(frac.Denominator, fontFamily, childSizePt, options);

        double diagonalW = Math.Max(em * 0.42, Math.Min(em * 0.72, Math.Max(numBox.Metrics.Width, denBox.Metrics.Width) * 0.55));
        double denY = numBox.Metrics.Height * 0.62 + gapY;
        double denX = numBox.Metrics.Width + gapX + diagonalW + gapX;
        double totalW = denX + denBox.Metrics.Width;

        double totalH = Math.Max(numBox.Metrics.Height, denY + denBox.Metrics.Height);
        double lineTopY = Math.Max(lineThickness / 2.0, numBox.Metrics.Height * 0.18);
        double lineBottomY = Math.Min(totalH - lineThickness / 2.0, denY + denBox.Metrics.Height * 0.82);

        if (lineBottomY <= lineTopY + lineThickness)
            lineBottomY = lineTopY + Math.Max(em * 0.70, lineThickness);

        totalH = Math.Max(totalH, lineBottomY + lineThickness / 2.0);

        double lineX = numBox.Metrics.Width + gapX;
        double mathAxisAboveBaseline = em * 0.45;
        double lineCenterY = (lineTopY + lineBottomY) / 2.0;
        double ascent = lineCenterY + mathAxisAboveBaseline;
        totalH = Math.Max(totalH, ascent + em * 0.25);

        var container = new MathBox.Container();
        container.Metrics.Width = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        numBox.X = 0;
        numBox.Y = 0;
        container.Children.Add(numBox);

        var slash = new MathBox.Line
        {
            X = lineX,
            Y = lineBottomY,
            X2 = diagonalW,
            Y2 = lineTopY - lineBottomY,
            Thickness = lineThickness
        };
        slash.Metrics.Width = diagonalW;
        slash.Metrics.Height = Math.Abs(slash.Y2);
        slash.Metrics.Ascent = 0;
        container.Children.Add(slash);

        denBox.X = denX;
        denBox.Y = denY;
        container.Children.Add(denBox);

        return container;
    }

    // ── Superscript layout ────────────────────────────────────────────────

    private static MathBox LayoutSup(MathNode.Sup sup, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox   = LayoutNode(sup.Base, fontFamily, fontSizePt, options);
        var scriptBox = LayoutNode(sup.Script, fontFamily, scriptSizePt, options);

        // Superscript raised: top of script = baseline - shiftUp
        // Shift up = 0.40 em
        double shiftUp = em * 0.40;
        double baselineFromTop = baseBox.Metrics.Ascent;
        // Script top sits at (baselineFromTop - shiftUp - scriptBox.Metrics.Ascent)
        // → script Y = baselineFromTop - shiftUp - scriptBox.Metrics.Ascent
        double scriptY = baselineFromTop - shiftUp - scriptBox.Metrics.Ascent;

        // HB3: for a normal-size base, scriptY is negative (the superscript
        // rises above the base's own top). Rather than clamping it to 0 —
        // which draws the script too low and never grows the container's
        // ascent to contain it — shift the WHOLE box down by the deficit so
        // the baseline stays put and the container grows upward.
        double deficit = scriptY < 0 ? -scriptY : 0;
        double baseY = deficit;
        scriptY += deficit;

        double totalH = Math.Max(baseY + baseBox.Metrics.Height, scriptY + scriptBox.Metrics.Height);
        double totalW = baseBox.Metrics.Width + scriptBox.Metrics.Width + em * 0.03;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseBox.Metrics.Ascent + deficit;

        baseBox.X = 0; baseBox.Y = baseY;
        scriptBox.X = baseBox.Metrics.Width + em * 0.03;
        scriptBox.Y = scriptY;

        container.Children.Add(baseBox);
        container.Children.Add(scriptBox);
        return container;
    }

    // ── Subscript layout ──────────────────────────────────────────────────

    private static MathBox LayoutSub(MathNode.Sub sub, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox   = LayoutNode(sub.Base, fontFamily, fontSizePt, options);
        var scriptBox = LayoutNode(sub.Script, fontFamily, scriptSizePt, options);

        // Subscript lowered: top of script at baseline + shiftDown
        double shiftDown = em * 0.25;
        double scriptY = baseBox.Metrics.Ascent + shiftDown;

        // HB3 (symmetric case): a deep subscript never needs to shift the
        // Ascent (the baseline never moves for a sub — only the descent
        // grows), so folding its bottom extent into totalH via Math.Max
        // already reports the correct (larger) Descent = totalH - Ascent.
        double totalH = Math.Max(baseBox.Metrics.Height, scriptY + scriptBox.Metrics.Height);
        double totalW = baseBox.Metrics.Width + scriptBox.Metrics.Width + em * 0.03;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseBox.Metrics.Ascent;

        baseBox.X = 0; baseBox.Y = 0;
        scriptBox.X = baseBox.Metrics.Width + em * 0.03;
        scriptBox.Y = scriptY;

        container.Children.Add(baseBox);
        container.Children.Add(scriptBox);
        return container;
    }

    // ── Sub+Sup layout ────────────────────────────────────────────────────

    private static MathBox LayoutSubSup(MathNode.SubSup ss, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox = LayoutNode(ss.Base, fontFamily, fontSizePt, options);
        var subBox  = LayoutNode(ss.Sub,  fontFamily, scriptSizePt, options);
        var supBox  = LayoutNode(ss.Sup,  fontFamily, scriptSizePt, options);

        double baseline = baseBox.Metrics.Ascent;

        // Sup: top at baseline - 0.40em - supBox.Ascent
        double supY = baseline - em * 0.40 - supBox.Metrics.Ascent;

        // Sub: top at baseline + 0.25em
        double subY = baseline + em * 0.25;

        // HB3: for a normal-size base, supY is negative (the superscript
        // rises above the base's own top). Shift the whole box down by the
        // deficit instead of clamping supY to 0, and grow the container's
        // Ascent by the same deficit so the baseline stays consistent.
        double deficit = supY < 0 ? -supY : 0;
        double baseY = deficit;
        supY += deficit;
        subY += deficit;

        double scriptX = baseBox.Metrics.Width + em * 0.03;
        double scriptW = Math.Max(subBox.Metrics.Width, supBox.Metrics.Width);

        double totalH = Math.Max(baseY + baseBox.Metrics.Height, Math.Max(
            supY + supBox.Metrics.Height,
            subY + subBox.Metrics.Height));
        double totalW = scriptX + scriptW;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseline + deficit;

        baseBox.X = 0; baseBox.Y = baseY;
        supBox.X = ResolveSubSupScriptX(scriptX, scriptW, supBox.Metrics.Width, ss.AlignScripts); supBox.Y = supY;
        subBox.X = ResolveSubSupScriptX(scriptX, scriptW, subBox.Metrics.Width, ss.AlignScripts); subBox.Y = subY;

        container.Children.Add(baseBox);
        container.Children.Add(supBox);
        container.Children.Add(subBox);
        return container;
    }

    // ── Radical layout ────────────────────────────────────────────────────

    private static double ResolveSubSupScriptX(double scriptX, double scriptW, double scriptBoxWidth, bool alignScripts) =>
        alignScripts
            ? scriptX + scriptW - scriptBoxWidth
            : scriptX;

    private static MathBox LayoutPreSubSup(MathNode.PreSubSup ps, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox = LayoutNode(ps.Base, fontFamily, fontSizePt, options);
        var subBox  = LayoutNode(ps.Sub,  fontFamily, scriptSizePt, options);
        var supBox  = LayoutNode(ps.Sup,  fontFamily, scriptSizePt, options);

        double baseline = baseBox.Metrics.Ascent;
        double supY = baseline - em * 0.40 - supBox.Metrics.Ascent;
        double subY = baseline + em * 0.25;

        double deficit = supY < 0 ? -supY : 0;
        double baseY = deficit;
        supY += deficit;
        subY += deficit;

        double scriptW = Math.Max(subBox.Metrics.Width, supBox.Metrics.Width);
        double gap = em * 0.03;
        double baseX = scriptW + gap;

        double totalH = Math.Max(baseY + baseBox.Metrics.Height, Math.Max(
            supY + supBox.Metrics.Height,
            subY + subBox.Metrics.Height));
        double totalW = baseX + baseBox.Metrics.Width;

        var container = new MathBox.Container();
        container.Metrics.Width = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseline + deficit;

        supBox.X = scriptW - supBox.Metrics.Width; supBox.Y = supY;
        subBox.X = scriptW - subBox.Metrics.Width; subBox.Y = subY;
        baseBox.X = baseX; baseBox.Y = baseY;

        container.Children.Add(supBox);
        container.Children.Add(subBox);
        container.Children.Add(baseBox);
        return container;
    }

    private static MathBox LayoutRad(MathNode.Rad rad, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);

        var radicand = LayoutNode(rad.Radicand, fontFamily, fontSizePt, options);

        double overlineClearance = em * 0.10;
        double overlineThick = em * 0.07;
        double signWidth = em * 0.65;

        // Degree index (optional)
        MathBox? degBox = null;
        if (rad.Degree is not null)
        {
            degBox = LayoutNode(rad.Degree, fontFamily, fontSizePt * 0.65, options);
        }

        // The radical sign height = radicand height + clearance + overline
        double radHeight = radicand.Metrics.Height + overlineClearance + overlineThick;

        // Degree position: above the radical sign's check-mark
        double degWidth = 0;
        if (degBox is not null)
        {
            degWidth = degBox.Metrics.Width + em * 0.03;
        }

        double radX = degWidth;
        double radicandX = radX + signWidth;
        double overlineWidth = radicand.Metrics.Width;

        // Radicand Y: below the overline (clearance space)
        double radicandY = overlineThick + overlineClearance;

        double totalW = radicandX + radicand.Metrics.Width;
        double totalH = radHeight;

        // baseline: the radicand's baseline shifted down by the overline area
        double ascent = radicandY + radicand.Metrics.Ascent;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        // Radical sign box
        var radSign = new MathBox.Radical
        {
            X = radX, Y = 0,
            OverlineWidth  = overlineWidth,
            OverlineThick  = overlineThick,
            SignWidth      = signWidth
        };
        radSign.Metrics.Width  = signWidth + overlineWidth;
        radSign.Metrics.Height = radHeight;
        radSign.Metrics.Ascent = overlineThick;
        container.Children.Add(radSign);

        // Overline rendered via the radical box (renderer draws the overline at Y=0)

        // Radicand
        radicand.X = radicandX; radicand.Y = radicandY;
        container.Children.Add(radicand);

        // Degree index
        if (degBox is not null)
        {
            // Position above the check-mark
            degBox.X = 0;
            degBox.Y = Math.Max(0, radHeight * 0.05);
            container.Children.Add(degBox);
        }

        return container;
    }

    // ── N-ary (? ? ?) layout ─────────────────────────────────────────────

    private static MathBox LayoutNary(MathNode.Nary nary, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double limSizePt  = fontSizePt * 0.65;  // limit labels

        var operandBox = LayoutNode(nary.Operand, fontFamily, fontSizePt, options);
        double opSizePt = ResolveNaryOperatorSizePt(fontSizePt, operandBox, nary.GrowOperator);
        var opBox = MakeGlyph(nary.OperatorChar, fontFamily, opSizePt, isItalic: false);
        MathBox? subLimBox = nary.SubLimit is not null
            ? LayoutNode(nary.SubLimit, fontFamily, limSizePt, options) : null;
        MathBox? supLimBox = nary.SupLimit is not null
            ? LayoutNode(nary.SupLimit, fontFamily, limSizePt, options) : null;

        double opColW = opBox.Metrics.Width;
        if (subLimBox is not null) opColW = Math.Max(opColW, subLimBox.Metrics.Width);
        if (supLimBox is not null) opColW = Math.Max(opColW, supLimBox.Metrics.Width);
        opColW += em * 0.05;

        double limGap = em * 0.06;

        if (nary.LimitsAboveBelow)
        {
            // Stack: sup | op | sub, then operand to the right
            double supH = supLimBox is not null ? supLimBox.Metrics.Height + limGap : 0;
            double subH = subLimBox is not null ? subLimBox.Metrics.Height + limGap : 0;

            double opY  = supH;
            double subY = supH + opBox.Metrics.Height + limGap;

            // Math baseline: align operand's baseline with the op's baseline
            double opBaseline = opY + opBox.Metrics.Ascent;

            // Operand: vertically align its baseline with the operator's baseline
            double operandY = opBaseline - operandBox.Metrics.Ascent;
            double operandBottom = operandY + operandBox.Metrics.Height;
            double operandTop = Math.Min(0, operandY);

            // HB2: fold the operand's full extent (which may rise above the
            // top row or extend below the last limit row) into totalH so a
            // tall operand (e.g. a fraction after ? / ?) is never clipped.
            double stackH = supH + opBox.Metrics.Height + subH;
            double totalH = Math.Max(stackH, operandBottom) - operandTop;
            double totalW = opColW + em * 0.06 + operandBox.Metrics.Width;

            // opBaseline == opY + opBox.Metrics.Ascent == supH + opBox.Metrics.Ascent;
            // shift the ascent up by the same amount the operand rose above the top.
            double ascent = opBaseline - operandTop;

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = ascent;

            // If the operand rises above the stack's top (operandTop < 0),
            // shift every child down by -operandTop so all Y's stay >= 0
            // while the baseline (Ascent) already accounts for the shift.
            double shift = -operandTop;

            // Sup limit
            if (supLimBox is not null)
            {
                supLimBox.X = (opColW - supLimBox.Metrics.Width) / 2; supLimBox.Y = 0 + shift;
                c.Children.Add(supLimBox);
            }

            // Operator
            opBox.X = (opColW - opBox.Metrics.Width) / 2; opBox.Y = opY + shift;
            c.Children.Add(opBox);

            // Sub limit
            if (subLimBox is not null)
            {
                subLimBox.X = (opColW - subLimBox.Metrics.Width) / 2; subLimBox.Y = subY + shift;
                c.Children.Add(subLimBox);
            }

            operandBox.X = opColW + em * 0.06; operandBox.Y = operandY + shift;
            c.Children.Add(operandBox);

            return c;
        }
        else
        {
            // Integral style: sub/sup as scripts to the right of the operator
            // Same as SubSup but with the enlarged operator as base
            MathNode fakeBase    = new MathNode.Unknown(nary.OperatorChar);
            MathNode? fakeSub    = nary.SubLimit;
            MathNode? fakeSup    = nary.SupLimit;

            // Build manually using already-laid-out boxes
            double baseline = opBox.Metrics.Ascent;
            double supY = 0;
            double subY = opBox.Metrics.Height - (subLimBox?.Metrics.Height ?? 0);

            double scriptX = opBox.Metrics.Width + em * 0.02;
            double scriptW = Math.Max(subLimBox?.Metrics.Width ?? 0, supLimBox?.Metrics.Width ?? 0);

            double colH = opBox.Metrics.Height;
            double totalW = scriptX + scriptW + em * 0.06 + operandBox.Metrics.Width;

            // Operand: vertically align its baseline with the operator's baseline.
            double operandY = baseline - operandBox.Metrics.Ascent;
            double operandBottom = operandY + operandBox.Metrics.Height;
            double operandTop = Math.Min(0, operandY);

            // HB1: fold the operand's full extent into totalH (it was previously
            // computed from only the operator + limit-script boxes) so a tall
            // operand (e.g. ? of a fraction) is fully contained, not clipped.
            double scriptStackH = Math.Max(colH, Math.Max(
                supLimBox is not null ? supY + supLimBox.Metrics.Height : 0,
                subLimBox is not null ? subY + subLimBox.Metrics.Height : 0));
            double totalH = Math.Max(scriptStackH, operandBottom) - operandTop;

            double ascent = baseline - operandTop;

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = ascent;

            // Shift every child down by -operandTop when the operand rises
            // above the operator/limit column's top (operandTop < 0).
            double shift = -operandTop;

            opBox.X = 0; opBox.Y = 0 + shift;
            c.Children.Add(opBox);

            if (supLimBox is not null)
            {
                supLimBox.X = scriptX; supLimBox.Y = supY + shift;
                c.Children.Add(supLimBox);
            }
            if (subLimBox is not null)
            {
                subLimBox.X = scriptX; subLimBox.Y = subY + shift;
                c.Children.Add(subLimBox);
            }

            operandBox.X = scriptX + scriptW + em * 0.06; operandBox.Y = operandY + shift;
            c.Children.Add(operandBox);

            return c;
        }
    }

    // ── Function layout ───────────────────────────────────────────────────

    private static double ResolveNaryOperatorSizePt(double fontSizePt, MathBox operandBox, bool growOperator)
    {
        double defaultOperatorSizePt = fontSizePt * 1.50;
        if (!growOperator)
            return defaultOperatorSizePt;

        double operandHeightPt = operandBox.Metrics.Height * (72.0 / 96.0);
        return Math.Max(defaultOperatorSizePt, operandHeightPt);
    }

    private static MathBox LayoutLimit(MathNode.Limit limit, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double limitSizePt = fontSizePt * 0.70;
        double gap = em * 0.06;

        var baseBox = LayoutNode(limit.Base, fontFamily, fontSizePt, options);
        var limitBox = LayoutNode(limit.LimitValue, fontFamily, limitSizePt, options);

        double totalW = Math.Max(baseBox.Metrics.Width, limitBox.Metrics.Width);
        double totalH = baseBox.Metrics.Height + gap + limitBox.Metrics.Height;
        double ascent = limit.IsUpper
            ? limitBox.Metrics.Height + gap + baseBox.Metrics.Ascent
            : baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        baseBox.X = (totalW - baseBox.Metrics.Width) / 2.0;
        limitBox.X = (totalW - limitBox.Metrics.Width) / 2.0;

        if (limit.IsUpper)
        {
            limitBox.Y = 0;
            baseBox.Y = limitBox.Metrics.Height + gap;
            c.Children.Add(limitBox);
            c.Children.Add(baseBox);
        }
        else
        {
            baseBox.Y = 0;
            limitBox.Y = baseBox.Metrics.Height + gap;
            c.Children.Add(baseBox);
            c.Children.Add(limitBox);
        }

        return c;
    }

    private static MathBox LayoutFunc(MathNode.Func func, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        var nameBox = LayoutNode(func.FunctionName, fontFamily, fontSizePt, options);
        var argBox  = LayoutNode(func.Argument, fontFamily, fontSizePt, options);

        double gap = em * 0.08;
        double totalW = nameBox.Metrics.Width + gap + argBox.Metrics.Width;
        double totalH = Math.Max(nameBox.Metrics.Height, argBox.Metrics.Height);
        double ascent  = Math.Max(nameBox.Metrics.Ascent, argBox.Metrics.Ascent);

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        // Align baselines
        nameBox.X = 0;
        nameBox.Y = ascent - nameBox.Metrics.Ascent;
        argBox.X  = nameBox.Metrics.Width + gap;
        argBox.Y  = ascent - argBox.Metrics.Ascent;

        c.Children.Add(nameBox);
        c.Children.Add(argBox);
        return c;
    }

    // ── Delimiter layout ──────────────────────────────────────────────────

    private static MathBox LayoutDelim(MathNode.Delim delim, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);

        // Per ECMA-376 §22.1.2.20 (CT_DPr), m:sepChr is only meaningful when there
        // are two or more m:e children; a single element never gets a separator,
        // and an explicit empty sepChr suppresses the glyph (elements still abut
        // with the same gap, just no visible character between them).
        bool hasSeparator = delim.Elements.Count > 1 && !string.IsNullOrEmpty(delim.SepChar);
        MathBox? MakeSep() => hasSeparator ? MakeGlyph(delim.SepChar, fontFamily, fontSizePt, isItalic: false) : null;

        // Layout inner elements separated by a small gap (matching the separator glyph, if any)
        double sepGap = em * 0.15;
        var innerBoxes = new List<MathBox>();
        foreach (var el in delim.Elements)
            innerBoxes.Add(LayoutNode(el, fontFamily, fontSizePt, options));

        // Compute inner dimensions
        double innerW = 0, innerH = 0, innerAscent = 0;
        foreach (var b in innerBoxes)
        {
            innerW += b.Metrics.Width;
            innerH = Math.Max(innerH, b.Metrics.Height);
            innerAscent = Math.Max(innerAscent, b.Metrics.Ascent);
        }
        // Pre-measure separator glyphs (if any) so they can influence innerH/innerAscent
        // the same way the content boxes do (sized/baselined like the content).
        MathBox? sepSample = MakeSep();
        if (sepSample is not null)
        {
            innerH = Math.Max(innerH, sepSample.Metrics.Height);
            innerAscent = Math.Max(innerAscent, sepSample.Metrics.Ascent);
        }
        // Per-gap width between consecutive elements: a bare gap when there's no
        // separator glyph, or gap + glyph + gap when there is one (glyph flanked
        // symmetrically, matching the placement loop below exactly).
        double gapWidth = hasSeparator ? sepGap * 2 + sepSample!.Metrics.Width : sepGap;
        if (innerBoxes.Count > 1)
            innerW += gapWidth * (innerBoxes.Count - 1);

        // Brackets scale to inner height * 1.10 unless OMML m:grow is off or
        // m:dPr/m:shp requests centered, ordinary-height delimiter glyphs.
        bool shouldGrowBrackets = delim.Grow && delim.Shape == MathNode.Delim.DelimiterShape.Match;
        double bracketH = shouldGrowBrackets ? innerH * 1.10 : em;
        double bracketW = em * 0.35; // fixed bracket width
        double openBracketW = string.IsNullOrEmpty(delim.BegChar) ? 0 : bracketW;
        double closeBracketW = string.IsNullOrEmpty(delim.EndChar) ? 0 : bracketW;

        double totalW = openBracketW + innerW + closeBracketW;
        double totalH = Math.Max(innerH, bracketH);
        double ascent = innerAscent + (totalH - innerH) / 2.0;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        // Opening bracket
        if (!string.IsNullOrEmpty(delim.BegChar))
        {
            double bracketTop = (totalH - bracketH) / 2.0;
            var beg = new MathBox.Bracket(delim.BegChar) { X = 0, Y = bracketTop, ScaledHeight = bracketH };
            beg.Metrics.Width = bracketW; beg.Metrics.Height = bracketH; beg.Metrics.Ascent = ascent;
            c.Children.Add(beg);
        }

        // Inner elements
        double x = openBracketW;
        double innerTop = (totalH - innerH) / 2.0;
        for (int i = 0; i < innerBoxes.Count; i++)
        {
            var b = innerBoxes[i];
            b.X = x;
            b.Y = innerTop + (innerAscent - b.Metrics.Ascent);
            c.Children.Add(b);
            x += b.Metrics.Width;

            // Separator glyph between elements (m:sepChr; default ",", suppressed when
            // there's only one element or the value is explicitly empty).
            if (i < innerBoxes.Count - 1)
            {
                x += sepGap;
                if (hasSeparator)
                {
                    var sep = MakeGlyph(delim.SepChar, fontFamily, fontSizePt, isItalic: false);
                    sep.X = x; sep.Y = innerTop + (innerAscent - sep.Metrics.Ascent);
                    c.Children.Add(sep);
                    x += sep.Metrics.Width + sepGap;
                }
            }
        }

        // Closing bracket
        if (!string.IsNullOrEmpty(delim.EndChar))
        {
            double bracketTop = (totalH - bracketH) / 2.0;
            var end = new MathBox.Bracket(delim.EndChar) { X = x, Y = bracketTop, ScaledHeight = bracketH };
            end.Metrics.Width = bracketW; end.Metrics.Height = bracketH; end.Metrics.Ascent = ascent;
            c.Children.Add(end);
        }

        return c;
    }

    // ── Accent layout ─────────────────────────────────────────────────────

    private static MathBox LayoutAcc(MathNode.Acc acc, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(acc.Base, fontFamily, fontSizePt, options);

        // Accent placed above: use HRule-style overline for bar (̄) or a glyph for others
        double accentH = em * 0.25;
        double gap = em * 0.05;
        double totalH = accentH + gap + baseBox.Metrics.Height;
        double totalW = baseBox.Metrics.Width;
        double ascent = accentH + gap + baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        if (IsHorizontalRuleAccent(acc.AccentChar))
        {
            var thickness = em * 0.07;
            var hrule = new MathBox.HRule
            {
                X = 0,
                Y = (accentH - thickness) / 2.0,
                LineWidth = totalW,
                Thickness = thickness
            };
            hrule.Metrics.Width = totalW;
            hrule.Metrics.Height = thickness;
            hrule.Metrics.Ascent = 0;
            c.Children.Add(hrule);
        }
        else
        {
            // Draw the accent glyph above the base.
            var accentGlyph = MakeGlyph(acc.AccentChar, fontFamily, fontSizePt * 0.75, isItalic: false);
            accentGlyph.X = (totalW - accentGlyph.Metrics.Width) / 2;
            accentGlyph.Y = 0;
            c.Children.Add(accentGlyph);
        }

        baseBox.X = 0; baseBox.Y = accentH + gap;
        c.Children.Add(baseBox);

        return c;
    }

    private static bool IsHorizontalRuleAccent(string accentChar) =>
        accentChar is "\u0304" or "\u0305" or "\u00AF";

    // ── Bar layout ────────────────────────────────────────────────────────

    private static MathBox LayoutBar(MathNode.Bar bar, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(bar.Base, fontFamily, fontSizePt, options);

        double barThick = em * 0.07;
        double gap = em * 0.05;

        double totalW = baseBox.Metrics.Width;
        double totalH = bar.IsOver
            ? barThick + gap + baseBox.Metrics.Height
            : baseBox.Metrics.Height + gap + barThick;

        double ascent = bar.IsOver
            ? barThick + gap + baseBox.Metrics.Ascent
            : baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        if (bar.IsOver)
        {
            // Overline at top
            var hrule = new MathBox.HRule { X = 0, Y = 0, LineWidth = totalW, Thickness = barThick };
            hrule.Metrics.Width = totalW; hrule.Metrics.Height = barThick; hrule.Metrics.Ascent = 0;
            c.Children.Add(hrule);
            baseBox.X = 0; baseBox.Y = barThick + gap;
        }
        else
        {
            baseBox.X = 0; baseBox.Y = 0;
            var hrule = new MathBox.HRule { X = 0, Y = baseBox.Metrics.Height + gap, LineWidth = totalW, Thickness = barThick };
            hrule.Metrics.Width = totalW; hrule.Metrics.Height = barThick; hrule.Metrics.Ascent = 0;
            c.Children.Add(hrule);
        }
        c.Children.Add(baseBox);

        return c;
    }

    // ── GroupChr layout ───────────────────────────────────────────────────

    private static MathBox LayoutBox(MathNode.Box box, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        var baseBox = LayoutNode(box.Base, fontFamily, fontSizePt, options);

        var c = new MathBox.Container();
        c.Metrics.Width = baseBox.Metrics.Width;
        c.Metrics.Height = baseBox.Metrics.Height;
        c.Metrics.Ascent = baseBox.Metrics.Ascent;

        baseBox.X = 0;
        baseBox.Y = 0;
        c.Children.Add(baseBox);

        return c;
    }

    private static MathBox LayoutArgSize(MathNode.ArgSize argSize, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        var scale = Math.Pow(0.70, -argSize.Adjustment);
        return LayoutNode(argSize.Base, fontFamily, fontSizePt * scale, options);
    }

    private static MathBox LayoutPhantom(MathNode.Phantom phantom, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        var baseBox = LayoutNode(phantom.Base, fontFamily, fontSizePt, options);

        double naturalDescent = Math.Max(0, baseBox.Metrics.Height - baseBox.Metrics.Ascent);
        double reportedAscent = phantom.ZeroAscent ? 0 : baseBox.Metrics.Ascent;
        double reportedDescent = phantom.ZeroDescent ? 0 : naturalDescent;

        var c = new MathBox.Container();
        c.Metrics.Width = phantom.ZeroWidth ? 0 : baseBox.Metrics.Width;
        c.Metrics.Ascent = reportedAscent;
        c.Metrics.Height = Math.Max(0, reportedAscent + reportedDescent);

        if (phantom.Show)
        {
            baseBox.X = 0;
            baseBox.Y = 0;
            c.Children.Add(baseBox);
        }

        return c;
    }

    private static MathBox LayoutBorderBox(MathNode.BorderBox borderBox, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double thickness = Math.Max(1.0, em * 0.06);
        double padding = em * 0.18;
        double inset = thickness + padding;

        var baseBox = LayoutNode(borderBox.Base, fontFamily, fontSizePt, options);
        double totalW = baseBox.Metrics.Width + inset * 2.0;
        double totalH = baseBox.Metrics.Height + inset * 2.0;
        double left = thickness / 2.0;
        double top = thickness / 2.0;
        double right = totalW - thickness / 2.0;
        double bottom = totalH - thickness / 2.0;
        double centerX = totalW / 2.0;
        double centerY = totalH / 2.0;

        var c = new MathBox.Container();
        c.Metrics.Width = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = inset + baseBox.Metrics.Ascent;

        baseBox.X = inset;
        baseBox.Y = inset;
        c.Children.Add(baseBox);

        if (borderBox.ShowTop)
            AddLine(c, left, top, right, top, thickness);
        if (borderBox.ShowBottom)
            AddLine(c, left, bottom, right, bottom, thickness);
        if (borderBox.ShowLeft)
            AddLine(c, left, top, left, bottom, thickness);
        if (borderBox.ShowRight)
            AddLine(c, right, top, right, bottom, thickness);
        if (borderBox.StrikeHorizontal)
            AddLine(c, left, centerY, right, centerY, thickness);
        if (borderBox.StrikeVertical)
            AddLine(c, centerX, top, centerX, bottom, thickness);
        if (borderBox.StrikeBottomLeftToTopRight)
            AddLine(c, left, bottom, right, top, thickness);
        if (borderBox.StrikeTopLeftToBottomRight)
            AddLine(c, left, top, right, bottom, thickness);

        return c;
    }

    private static void AddLine(
        MathBox.Container container,
        double x1,
        double y1,
        double x2,
        double y2,
        double thickness)
    {
        var line = new MathBox.Line
        {
            X = x1,
            Y = y1,
            X2 = x2 - x1,
            Y2 = y2 - y1,
            Thickness = thickness
        };
        line.Metrics.Width = Math.Abs(x2 - x1);
        line.Metrics.Height = Math.Abs(y2 - y1);
        line.Metrics.Ascent = 0;
        container.Children.Add(line);
    }

    private static MathBox LayoutGroupChr(MathNode.GroupChr gc, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(gc.Base, fontFamily, fontSizePt, options);

        // Group char placed above or below. PowerPoint-authored braces grow
        // toward the grouped expression width; approximate that in the shared
        // layout before either renderer sees it.
        double gap  = em * 0.05;
        var grpGlyph = MakeGroupCharacterGlyph(gc.GrpChar, fontFamily, fontSizePt, baseBox.Metrics.Width);

        double totalW = Math.Max(baseBox.Metrics.Width, grpGlyph.Metrics.Width);
        double totalH = baseBox.Metrics.Height + gap + grpGlyph.Metrics.Height;
        double ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;

        if (gc.IsAbove)
        {
            ascent = grpGlyph.Metrics.Height + gap + baseBox.Metrics.Ascent;
            grpGlyph.X = (totalW - grpGlyph.Metrics.Width) / 2; grpGlyph.Y = 0;
            baseBox.X  = (totalW - baseBox.Metrics.Width)  / 2; baseBox.Y  = grpGlyph.Metrics.Height + gap;
        }
        else
        {
            ascent = baseBox.Metrics.Ascent;
            baseBox.X  = (totalW - baseBox.Metrics.Width)  / 2; baseBox.Y  = 0;
            grpGlyph.X = (totalW - grpGlyph.Metrics.Width) / 2; grpGlyph.Y = baseBox.Metrics.Height + gap;
        }

        c.Metrics.Ascent = ResolveGroupChrAscent(gc.VerticalJustification, ascent, totalH);
        c.Children.Add(grpGlyph);
        c.Children.Add(baseBox);
        return c;
    }

    private static double ResolveGroupChrAscent(
        MathNode.GroupChr.GroupChrVerticalJustification verticalJustification,
        double baselineAscent,
        double totalHeight) =>
        verticalJustification switch
        {
            MathNode.GroupChr.GroupChrVerticalJustification.Top => 0,
            MathNode.GroupChr.GroupChrVerticalJustification.Bottom => totalHeight,
            _ => baselineAscent
        };

    private static MathBox.Glyph MakeGroupCharacterGlyph(
        string groupChar,
        string fontFamily,
        double fontSizePt,
        double targetWidth)
    {
        const double groupCharBaseScale = 0.75;
        const double maxWidthScale = 4.0;

        var baseFontSizePt = fontSizePt * groupCharBaseScale;
        var glyph = MakeGlyph(groupChar, fontFamily, baseFontSizePt, isItalic: false);
        if (targetWidth <= glyph.Metrics.Width || glyph.Metrics.Width <= 0)
            return glyph;

        var widthScale = Math.Min(maxWidthScale, targetWidth / glyph.Metrics.Width);
        return MakeGlyph(groupChar, fontFamily, baseFontSizePt * widthScale, isItalic: false);
    }

    // ── Matrix layout ─────────────────────────────────────────────────────

    private static MathBox LayoutEqArray(MathNode.EqArray eqArray, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double rowGap = ResolveMathArrayGap(
            ToMathArraySpacingRule(eqArray.RowSpacingRule),
            eqArray.RowSpacing,
            em * 0.20);

        if (eqArray.Rows.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        var rows = new List<MathBox>(eqArray.Rows.Count);
        var alignmentOffsets = new List<double?>(eqArray.Rows.Count);
        var rowAsc = new double[eqArray.Rows.Count];
        var rowDsc = new double[eqArray.Rows.Count];
        double maxRowW = 0;
        double maxLeft = 0;
        double maxRight = 0;
        bool hasAlignmentPoint = false;
        double totalH = 0;

        for (int i = 0; i < eqArray.Rows.Count; i++)
        {
            var row = eqArray.Rows[i];
            var rowBox = LayoutNode(row, fontFamily, fontSizePt, options);
            var alignmentOffset = GetEqArrayAlignmentOffset(row, rowBox, eqArray.GetAlignmentPointIndex(i));
            rows.Add(rowBox);
            alignmentOffsets.Add(alignmentOffset);
            rowAsc[i] = rowBox.Metrics.Ascent;
            rowDsc[i] = rowBox.Metrics.Descent;
            maxRowW = Math.Max(maxRowW, rowBox.Metrics.Width);
            if (alignmentOffset.HasValue)
            {
                hasAlignmentPoint = true;
                maxLeft = Math.Max(maxLeft, alignmentOffset.Value);
                maxRight = Math.Max(maxRight, rowBox.Metrics.Width - alignmentOffset.Value);
            }

            totalH += rowBox.Metrics.Height;
        }

        totalH += rowGap * Math.Max(0, rows.Count - 1);
        double alignedWidth = hasAlignmentPoint ? maxLeft + maxRight : maxRowW;
        double totalW = Math.Max(maxRowW, alignedWidth);
        double alignmentOriginX = (totalW - alignedWidth) / 2.0;
        double sharedAlignmentX = alignmentOriginX + maxLeft;

        double ascent = ResolveStackedArrayAscent(
            ToMathArrayBaseJustification(eqArray.BaseJustification),
            rowAsc,
            rowDsc,
            rowGap,
            totalH,
            em);

        var container = new MathBox.Container();
        container.Metrics.Width = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double y = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var rowBox = rows[i];
            var alignmentOffset = alignmentOffsets[i];
            rowBox.X = alignmentOffset.HasValue
                ? sharedAlignmentX - alignmentOffset.Value
                : (totalW - rowBox.Metrics.Width) / 2.0;
            rowBox.Y = y;
            container.Children.Add(rowBox);
            y += rowBox.Metrics.Height + rowGap;
        }

        return container;
    }

    private static double? GetEqArrayAlignmentOffset(MathNode row, MathBox rowBox, int? alignmentPointIndex)
    {
        if (!alignmentPointIndex.HasValue)
            return null;

        int index = Math.Max(0, alignmentPointIndex.Value);
        if (index == 0)
            return 0;

        if (row is MathNode.Row && rowBox is MathBox.Container rowContainer)
        {
            if (index < rowContainer.Children.Count)
                return rowContainer.Children[index].X;

            return rowBox.Metrics.Width;
        }

        return index > 0 ? rowBox.Metrics.Width : 0;
    }

    private static MathBox LayoutMatrix(MathNode.Matrix matrix, string fontFamily, double fontSizePt, LayoutOptions options)
    {
        double em = Em(fontSizePt);
        double cellGapH = ResolveMathArrayGap(
            ToMathArraySpacingRule(matrix.ColumnGapRule),
            matrix.ColumnGap,
            em * 0.25);
        double cellGapV = ResolveMathArrayGap(
            ToMathArraySpacingRule(matrix.RowSpacingRule),
            matrix.RowSpacing,
            em * 0.20);

        if (matrix.Rows.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        int rowCount = matrix.Rows.Count;
        int colCount = 0;
        for (int r = 0; r < rowCount; r++)
            colCount = Math.Max(colCount, matrix.Rows[r].Count);

        // Layout all cells
        var cells = new MathBox[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            cells[r] = new MathBox[colCount];
            for (int c = 0; c < colCount && c < matrix.Rows[r].Count; c++)
                cells[r][c] = LayoutMatrixCell(matrix.Rows[r][c], matrix.HidePlaceholders, fontFamily, fontSizePt, options);
        }

        // Per-column width
        var colW = new double[colCount];
        for (int c = 0; c < colCount; c++)
        {
            colW[c] = Math.Max(colW[c], TwipsToDip(matrix.ColumnSpacingTwips));
            for (int r = 0; r < rowCount; r++)
                if (cells[r][c] is not null)
                    colW[c] = Math.Max(colW[c], cells[r][c].Metrics.Width);
        }

        // Per-row ascent and descent
        var rowAsc = new double[rowCount];
        var rowDsc = new double[rowCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                if (cells[r][c] is not null)
                {
                    rowAsc[r] = Math.Max(rowAsc[r], cells[r][c].Metrics.Ascent);
                    rowDsc[r] = Math.Max(rowDsc[r], cells[r][c].Metrics.Descent);
                }

        double totalW = 0;
        for (int c = 0; c < colCount; c++) totalW += colW[c];
        totalW += cellGapH * Math.Max(0, colCount - 1);

        double totalH = 0;
        for (int r = 0; r < rowCount; r++) totalH += rowAsc[r] + rowDsc[r];
        totalH += cellGapV * Math.Max(0, rowCount - 1);

        double ascent = ResolveStackedArrayAscent(
            ToMathArrayBaseJustification(matrix.BaseJustification),
            rowAsc,
            rowDsc,
            cellGapV,
            totalH,
            em);

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double rowY = 0;
        for (int r = 0; r < rowCount; r++)
        {
            double colX = 0;
            for (int c = 0; c < colCount; c++)
            {
                var cell = cells[r][c];
                if (cell is not null)
                {
                    cell.X = AlignMatrixCellX(
                        colX,
                        colW[c],
                        cell.Metrics.Width,
                        GetMatrixColumnAlignment(matrix, c));
                    cell.Y = rowY + rowAsc[r] - cell.Metrics.Ascent;
                    container.Children.Add(cell);
                }
                colX += colW[c] + cellGapH;
            }
            rowY += rowAsc[r] + rowDsc[r] + cellGapV;
        }

        return container;
    }

    private static double ResolveMathArrayGap(
        MathArraySpacingRule? rule,
        int? value,
        double defaultGap)
    {
        if (!rule.HasValue && !value.HasValue)
            return defaultGap;

        return (rule ?? MathArraySpacingRule.Single) switch
        {
            MathArraySpacingRule.OneAndHalf => defaultGap * 1.5,
            MathArraySpacingRule.Double => defaultGap * 2.0,
            MathArraySpacingRule.Exactly => PointsToDip(value ?? 0),
            MathArraySpacingRule.Multiple => defaultGap * Math.Max(0, value ?? 1),
            _ => defaultGap
        };
    }

    private static double ResolveStackedArrayAscent(
        MathArrayBaseJustification baseJustification,
        IReadOnlyList<double> rowAsc,
        IReadOnlyList<double> rowDsc,
        double rowGap,
        double totalHeight,
        double em)
    {
        if (rowAsc.Count == 0)
            return 0;

        double ascent = baseJustification switch
        {
            MathArrayBaseJustification.Top => rowAsc[0],
            MathArrayBaseJustification.Bottom => GetLastRowBaseline(rowAsc, rowDsc, rowGap),
            _ => totalHeight / 2.0 + em * 0.45
        };

        return Math.Clamp(ascent, 0, totalHeight);
    }

    private static MathArrayBaseJustification ToMathArrayBaseJustification(
        MathNode.Matrix.MatrixBaseJustification baseJustification) =>
        baseJustification switch
        {
            MathNode.Matrix.MatrixBaseJustification.Top => MathArrayBaseJustification.Top,
            MathNode.Matrix.MatrixBaseJustification.Bottom => MathArrayBaseJustification.Bottom,
            _ => MathArrayBaseJustification.Center
        };

    private static MathArrayBaseJustification ToMathArrayBaseJustification(
        MathNode.EqArray.EqArrayBaseJustification baseJustification) =>
        baseJustification switch
        {
            MathNode.EqArray.EqArrayBaseJustification.Top => MathArrayBaseJustification.Top,
            MathNode.EqArray.EqArrayBaseJustification.Bottom => MathArrayBaseJustification.Bottom,
            _ => MathArrayBaseJustification.Center
        };

    private static MathArraySpacingRule? ToMathArraySpacingRule(MathNode.Matrix.MatrixSpacingRule? rule) =>
        rule.HasValue ? (MathArraySpacingRule)(int)rule.Value : null;

    private static MathArraySpacingRule? ToMathArraySpacingRule(MathNode.EqArray.EqArraySpacingRule? rule) =>
        rule.HasValue ? (MathArraySpacingRule)(int)rule.Value : null;

    private enum MathArrayBaseJustification
    {
        Top,
        Center,
        Bottom
    }

    private enum MathArraySpacingRule
    {
        Single = 0,
        OneAndHalf = 1,
        Double = 2,
        Exactly = 3,
        Multiple = 4
    }

    private static double GetLastRowBaseline(
        IReadOnlyList<double> rowAsc,
        IReadOnlyList<double> rowDsc,
        double rowGap)
    {
        double y = 0;
        for (int row = 0; row < rowAsc.Count - 1; row++)
            y += rowAsc[row] + rowDsc[row] + rowGap;

        return y + rowAsc[^1];
    }

    private static double PointsToDip(int points) => points * (96.0 / 72.0);

    private static double TwipsToDip(int? twips) =>
        twips.HasValue ? PointsToDip(twips.Value) / 20.0 : 0;

    private static MathBox LayoutMatrixCell(
        MathNode cell,
        bool hidePlaceholders,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        if (!hidePlaceholders && IsEmptyMatrixCell(cell))
            return MakeGlyph(MatrixPlaceholderGlyph, fontFamily, fontSizePt * 0.85, isItalic: false);

        return LayoutNode(cell, fontFamily, fontSizePt, options);
    }

    private static bool IsEmptyMatrixCell(MathNode cell) =>
        cell switch
        {
            MathNode.Row row => row.Children.Count == 0,
            MathNode.Unknown unknown => string.IsNullOrEmpty(unknown.FallbackText),
            MathNode.Run run => string.IsNullOrEmpty(run.Text),
            _ => false
        };

    private static MathNode.Matrix.MatrixColumnAlignment GetMatrixColumnAlignment(MathNode.Matrix matrix, int column) =>
        column >= 0 && column < matrix.ColumnAlignments.Count
            ? matrix.ColumnAlignments[column]
            : MathNode.Matrix.MatrixColumnAlignment.Center;

    private static double AlignMatrixCellX(
        double colX,
        double colWidth,
        double cellWidth,
        MathNode.Matrix.MatrixColumnAlignment alignment) =>
        alignment switch
        {
            MathNode.Matrix.MatrixColumnAlignment.Left => colX,
            MathNode.Matrix.MatrixColumnAlignment.Right => colX + colWidth - cellWidth,
            _ => colX + (colWidth - cellWidth) / 2.0
        };

    // ── Row layout (horizontal sequence) ──────────────────────────────────

    private static MathBox LayoutRow(
        IReadOnlyList<MathNode> nodes,
        string fontFamily,
        double fontSizePt,
        LayoutOptions options)
    {
        double em = Em(fontSizePt);

        if (nodes.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        var boxes = new List<MathBox>(nodes.Count);
        foreach (var node in nodes)
            boxes.Add(LayoutNode(node, fontFamily, fontSizePt, options));

        // Align all on a common baseline (max ascent)
        double ascent = 0;
        foreach (var b in boxes) ascent = Math.Max(ascent, b.Metrics.Ascent);

        double totalW = 0, totalH = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            totalW += GetOperatorClassSpacingGapBefore(nodes, i, em);
            totalW += b.Metrics.Width;
            totalH = Math.Max(totalH, (ascent - b.Metrics.Ascent) + b.Metrics.Height);
            totalW += GetOperatorClassSpacingGapAfter(nodes, i, em);
        }

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        double x = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            x += GetOperatorClassSpacingGapBefore(nodes, i, em);
            b.X = x;
            b.Y = ascent - b.Metrics.Ascent;
            c.Children.Add(b);
            x += b.Metrics.Width;
            x += GetOperatorClassSpacingGapAfter(nodes, i, em);
        }

        return c;
    }

    private static double GetOperatorClassSpacingGapBefore(IReadOnlyList<MathNode> nodes, int index, double em)
    {
        if (index == 0)
            return 0;

        return GetOperatorClassSpacingGap(nodes[index], em, OperatorClassGapSide.Before);
    }

    private static double GetOperatorClassSpacingGapAfter(IReadOnlyList<MathNode> nodes, int index, double em)
    {
        if (index >= nodes.Count - 1)
            return 0;

        return GetOperatorClassSpacingGap(nodes[index], em, OperatorClassGapSide.After);
    }

    private static double GetOperatorClassSpacingGap(MathNode node, double em, OperatorClassGapSide side)
    {
        var spacingClass = GetOperatorClassSpacingClass(node);

        return spacingClass switch
        {
            TransparentPhantomSpacingClass.Relation => em * 0.18,
            TransparentPhantomSpacingClass.Binary => em * 0.14,
            TransparentPhantomSpacingClass.LargeOperator => em * 0.12,
            TransparentPhantomSpacingClass.Punctuation when side == OperatorClassGapSide.After => em * 0.10,
            _ => 0
        };
    }

    private static TransparentPhantomSpacingClass GetOperatorClassSpacingClass(MathNode node) =>
        node switch
        {
            MathNode.Phantom { TransparentSpacing: true } phantom =>
                GetTransparentPhantomSpacingClass(phantom.Base),
            MathNode.Box { OperatorEmulator: true } box =>
                GetOperatorEmulatorSpacingClass(box.Base),
            _ => TransparentPhantomSpacingClass.None
        };

    private static TransparentPhantomSpacingClass GetOperatorEmulatorSpacingClass(MathNode node)
    {
        if (node is MathNode.Row row && row.Children.Count == 1)
            return GetOperatorEmulatorSpacingClass(row.Children[0]);

        if (node is MathNode.Run run)
            return ClassifyOperatorEmulatorRun(run.Text);

        if (node is MathNode.Unknown unknown)
            return ClassifyOperatorEmulatorRun(unknown.FallbackText);

        return TransparentPhantomSpacingClass.None;
    }

    private static TransparentPhantomSpacingClass GetTransparentPhantomSpacingClass(MathNode node)
    {
        if (node is MathNode.Row row && row.Children.Count == 1)
            return GetTransparentPhantomSpacingClass(row.Children[0]);

        if (node is MathNode.Run run)
            return ClassifyTransparentPhantomSpacingRun(run.Text);

        if (node is MathNode.Unknown unknown)
            return ClassifyTransparentPhantomSpacingRun(unknown.FallbackText);

        return TransparentPhantomSpacingClass.None;
    }

    private static TransparentPhantomSpacingClass ClassifyTransparentPhantomSpacingRun(string text)
    {
        var trimmed = text.Trim();

        if (IsCommonMultiGlyphRelationOperator(trimmed))
            return TransparentPhantomSpacingClass.Relation;

        return ClassifySingleGlyphOperatorRun(trimmed);
    }

    private static TransparentPhantomSpacingClass ClassifyOperatorEmulatorRun(string text)
    {
        var trimmed = text.Trim();

        if (IsCommonMultiGlyphRelationOperator(trimmed))
            return TransparentPhantomSpacingClass.Relation;

        return ClassifySingleGlyphOperatorRun(trimmed);
    }

    private static bool IsCommonMultiGlyphRelationOperator(string text) =>
        text is "==" or "===" or ":=" or "<=" or ">=" or "!=" or "<>" or "=>"
            or "<=>" or "->" or "<-" or "<->";

    private static TransparentPhantomSpacingClass ClassifySingleGlyphOperatorRun(string text)
    {
        if (text.Length != 1)
            return TransparentPhantomSpacingClass.None;

        return text[0] switch
        {
            '=' or '<' or '>' or '\u2264' or '\u2265' or '\u2260' or '\u2248' or '\u2208' or '\u2209' or '\u2282'
                or '\u2283' or '\u2286' or '\u2287' or '\u2190' or '\u2192' or '\u2194' or '\u21d0' or '\u21d2'
                or '\u21d4' =>
                TransparentPhantomSpacingClass.Relation,
            '+' or '-' or '\u2212' or '\u00b1' or '\u00d7' or '\u00f7' or '*' or '/' =>
                TransparentPhantomSpacingClass.Binary,
            '\u2211' or '\u220f' or '\u222b' or '\u222e' or '\u22c0' or '\u22c1' or '\u22c2' or '\u22c3' =>
                TransparentPhantomSpacingClass.LargeOperator,
            ',' or ';' or ':' =>
                TransparentPhantomSpacingClass.Punctuation,
            _ => TransparentPhantomSpacingClass.None
        };
    }

    private enum OperatorClassGapSide
    {
        Before,
        After
    }

    private enum TransparentPhantomSpacingClass
    {
        None,
        Binary,
        Relation,
        LargeOperator,
        Punctuation
    }

    // ── Glyph factory ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MathBox.Glyph"/> with approximate metrics computed from the
    /// font size (no actual font metrics calls — keeps the engine framework-free).
    ///
    /// Approximations:
    ///   em   = fontSizePt * (96/72)
    ///   width x em * 0.60 * len  (proportional average; good for typical math chars)
    ///   ascent x em * 0.75
    ///   height x em * 1.0
    /// </summary>
    private static MathBox.Glyph MakeGlyph(string text, string fontFamily, double fontSizePt, bool isItalic, bool isBold = false)
    {
        double em = Em(fontSizePt);
        if (string.IsNullOrEmpty(text))
        {
            var empty = new MathBox.Glyph(string.Empty, fontFamily, fontSizePt, isItalic, isBold);
            empty.Metrics.Width = 0;
            empty.Metrics.Height = 0;
            empty.Metrics.Ascent = 0;
            return empty;
        }

        int len = System.Math.Max(1, CountTextElements(text));

        // Per-character width varies; use tighter estimate for single chars (operators)
        double charW = len == 1
            ? EstimateCharWidth(text[0], em)
            : em * 0.58 * len;

        double ascent  = em * 0.75;
        double descent = em * 0.25;
        double height  = ascent + descent;

        var g = new MathBox.Glyph(text, fontFamily, fontSizePt, isItalic, isBold);
        g.Metrics.Width  = charW;
        g.Metrics.Height = height;
        g.Metrics.Ascent = ascent;
        return g;
    }

    /// <summary>Estimate character width in DIP for a single character.</summary>
    private static double EstimateCharWidth(char ch, double em)
    {
        return ch switch
        {
            ',' or '.' or ':' or ';' or '!' or '\'' => em * 0.28,
            '|' or '|'                              => em * 0.22,
            '(' or ')' or '[' or ']' or '{' or '}'  => em * 0.35,
            '?' or '?' or '?'                       => em * 0.65,
            'v'                                     => em * 0.65,
            '±' or '×' or '÷' or '≤' or '≥' or '≠' or '≈' => em * 0.60,
            '+' or '-' or '-' or '='                => em * 0.60,
            'i' or 'j' or 'l' or 'I' or '1'         => em * 0.38,
            'm' or 'M' or 'W' or 'w'                => em * 0.82,
            ' '                                     => em * 0.28,
            _                                       => em * 0.58
        };
    }
}

