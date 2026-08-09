using System.Globalization;
using Free.Shared.Opc;

namespace FreeW.Core.Model;

/// <summary>
/// How serious an <see cref="AccessibilityIssue"/> is, mirroring Word's Accessibility Checker buckets.
/// <see cref="Error"/> is content that is effectively unusable for assistive technology (e.g. an image
/// with no alternative text); <see cref="Warning"/> is content that is usable but likely to be
/// confusing or hard to read (e.g. a low-contrast run or a table with no header row);
/// <see cref="Tip"/> is a best-practice nudge that does not block accessibility (e.g. a missing
/// document title).
/// </summary>
public enum AccessibilitySeverity
{
    Error,
    Warning,
    Tip
}

/// <summary>
/// The specific accessibility rule a single <see cref="AccessibilityIssue"/> reports. One value per
/// distinct check run by <see cref="AccessibilityChecker.Check(TextDocument)"/>, so a consumer can group
/// or filter issues by rule without parsing the human-readable message.
/// </summary>
public enum AccessibilityRule
{
    /// <summary>An inline image whose <see cref="InlineImage.AltText"/> is null/empty (an Error).</summary>
    MissingImageAltText,

    /// <summary>
    /// A hyperlink whose visible text is empty, the bare URL itself, or uninformative boilerplate such as
    /// "click here" (a Warning) — screen-reader users navigating by link text get no destination cue.
    /// </summary>
    UninformativeLinkText,

    /// <summary>
    /// The document's heading outline skips a level (e.g. Heading 1 straight to Heading 3) or the document
    /// has body text but no headings at all (a Warning), so the structure cannot be navigated by heading.
    /// </summary>
    HeadingOrderGap,

    /// <summary>A table whose first row is not marked/styled as a header row (a Warning).</summary>
    TableMissingHeaderRow,

    /// <summary>
    /// A text run whose colour contrast against its background falls below the WCAG AA ratio for normal
    /// text (4.5:1) (a Warning).
    /// </summary>
    LowContrastText,

    /// <summary>A table that contains one or more completely blank cells (a Tip).</summary>
    BlankTableCell,

    /// <summary>The document's core <see cref="DocumentProperties.Title"/> is empty (a Tip).</summary>
    MissingDocumentTitle
}

/// <summary>
/// A single accessibility finding produced by <see cref="AccessibilityChecker.Check(TextDocument)"/>: the
/// <see cref="Rule"/> that fired, its <see cref="Severity"/>, a human-readable <see cref="Message"/>, and a
/// locator describing <em>where</em> the issue lives. The locator is a <see cref="BlockIndex"/> into
/// <see cref="TextDocument.Blocks"/> (or -1 for a document-wide issue) plus an optional reference to the
/// offending <see cref="Paragraph"/>, <see cref="Run"/>, or <see cref="Table"/> so a UI can navigate to it.
/// Immutable record, mirroring <see cref="InspectionResult"/>.
/// </summary>
/// <param name="Rule">The rule that produced this finding.</param>
/// <param name="Severity">How serious the finding is (Error/Warning/Tip).</param>
/// <param name="Message">A human-readable description of the problem.</param>
/// <param name="BlockIndex">Index into <see cref="TextDocument.Blocks"/>, or -1 for a document-wide issue.</param>
/// <param name="Paragraph">The offending paragraph, when the issue is paragraph/run scoped; otherwise null.</param>
/// <param name="Run">The offending run, when the issue is run scoped; otherwise null.</param>
/// <param name="Table">The offending table, when the issue is table scoped; otherwise null.</param>
public sealed record AccessibilityIssue(
    AccessibilityRule Rule,
    AccessibilitySeverity Severity,
    string Message,
    int BlockIndex,
    Paragraph? Paragraph = null,
    Run? Run = null,
    Table? Table = null);

/// <summary>
/// The report produced by <see cref="AccessibilityChecker.Check(TextDocument)"/>: the ordered list of
/// <see cref="AccessibilityIssue"/>s plus convenience counts by severity. Issues are ordered by document
/// position (body block order; runs left-to-right within a paragraph), with the single document-wide
/// issues (missing title, "no headings at all") sorted last. Immutable record, mirroring
/// <see cref="InspectionResult"/>.
/// </summary>
public sealed record AccessibilityReport(IReadOnlyList<AccessibilityIssue> Issues)
{
    /// <summary>The number of <see cref="AccessibilitySeverity.Error"/> issues.</summary>
    public int ErrorCount => Issues.Count(i => i.Severity == AccessibilitySeverity.Error);

    /// <summary>The number of <see cref="AccessibilitySeverity.Warning"/> issues.</summary>
    public int WarningCount => Issues.Count(i => i.Severity == AccessibilitySeverity.Warning);

    /// <summary>The number of <see cref="AccessibilitySeverity.Tip"/> issues.</summary>
    public int TipCount => Issues.Count(i => i.Severity == AccessibilitySeverity.Tip);

    /// <summary>True when the document has no accessibility issues at all.</summary>
    public bool IsClean => Issues.Count == 0;
}

/// <summary>
/// Pure, WPF-free "Check Accessibility": analyses a <see cref="TextDocument"/> and reports the
/// accessibility problems a user would want to fix before sharing — missing image alt text, uninformative
/// link text, heading-order gaps, tables without a header row, low-contrast text, blank table cells, and a
/// missing document title. Lives in the model project so it is fully unit-testable without any UI and
/// touches no docx I/O; it never mutates the document. Mirrors the shape of <see cref="DocumentInspector"/>
/// (a single pure entry point returning an immutable result record).
/// </summary>
public static class AccessibilityChecker
{
    // WCAG 2.x AA minimum contrast ratio for normal-size text.
    private const double MinContrastRatio = 4.5;

    // Effective default text colour and page background when none is set anywhere in the resolution chain.
    // Word/FreeW render unset text as black on a white page, so that is the conservative assumption.
    private const string DefaultTextColorHex = "#000000";
    private const string DefaultBackgroundHex = "#FFFFFF";

    // Visible link texts that convey no destination — flagged as uninformative (compared case-insensitively
    // after trimming and stripping a trailing period, so "Click here." matches too).
    private static readonly string[] UninformativeLinkPhrases =
    [
        "click here", "here", "link", "this link", "read more", "more", "learn more", "click", "this"
    ];

    /// <summary>
    /// Check <paramref name="document"/> and return every accessibility issue found, ordered by document
    /// position. Pure: it never mutates the document. Returns an <see cref="AccessibilityReport"/> whose
    /// <see cref="AccessibilityReport.IsClean"/> is true when the document has no issues.
    /// </summary>
    public static AccessibilityReport Check(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<AccessibilityIssue>();

        // Walk the body in block order so per-block issues come out in document order.
        var blocks = document.Blocks;
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            switch (blocks[blockIndex])
            {
                case Paragraph paragraph:
                    CheckParagraph(document, paragraph, blockIndex, issues);
                    break;
                case Table table:
                    CheckTable(document, table, blockIndex, issues);
                    break;
            }
        }

        // Heading-order gaps between headings (skipped levels) are positional, so emit them inline above.
        CheckHeadingOrder(document, issues);

        // Document-wide tips come last (no single block to anchor them to).
        if (string.IsNullOrWhiteSpace(document.Properties.Title))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityRule.MissingDocumentTitle,
                AccessibilitySeverity.Tip,
                "The document has no title. Set a document title so assistive technology can announce it.",
                BlockIndex: -1));
        }

        return new AccessibilityReport(issues);
    }

    // --- Per-paragraph rules: missing alt text, uninformative link text, low-contrast text. ---
    private static void CheckParagraph(
        TextDocument document, Paragraph paragraph, int blockIndex, List<AccessibilityIssue> issues)
    {
        foreach (var run in paragraph.Runs)
        {
            // Missing image alt text (Error).
            if (run.Image is { } image && string.IsNullOrWhiteSpace(image.AltText))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityRule.MissingImageAltText,
                    AccessibilitySeverity.Error,
                    "An inline image has no alternative text. Add alt text describing the image.",
                    blockIndex, paragraph, run));
            }

            // Uninformative hyperlink text (Warning). Applies to both external and internal links.
            if (IsHyperlink(run) && IsUninformativeLinkText(run))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityRule.UninformativeLinkText,
                    AccessibilitySeverity.Warning,
                    "A hyperlink has empty or uninformative text (e.g. a bare URL or \"click here\"). " +
                    "Use descriptive link text naming the destination.",
                    blockIndex, paragraph, run));
            }

            // Low-contrast text (Warning). Only meaningful for runs that actually carry visible text.
            // A shape/text-box run (run.Shape != null) mirrors the shape's plain text into run.Text
            // (see Run.FromShape) but the *outer* run carries none of the shape's own formatting, so
            // grading it here would check default-black-on-default-white regardless of what the shape
            // actually looks like -- CheckShapeText below resolves against the shape's own inner runs
            // and its own fill instead.
            if (!string.IsNullOrWhiteSpace(run.Text) && run.Image is null && run.Shape is null)
            {
                var foreground = ResolveTextColor(document, paragraph, run);
                var background = ResolveBackgroundColor(paragraph, run);
                var ratio = ContrastRatio(foreground, background);
                if (ratio < MinContrastRatio)
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityRule.LowContrastText,
                        AccessibilitySeverity.Warning,
                        $"Text has low contrast ({ratio.ToString("0.0", CultureInfo.InvariantCulture)}:1, " +
                        $"below the {MinContrastRatio.ToString("0.0", CultureInfo.InvariantCulture)}:1 minimum). " +
                        "Increase the contrast between the text and its background.",
                        blockIndex, paragraph, run));
                }
            }
            else if (run.Shape is { HasText: true } shape)
            {
                CheckShapeText(document, shape, blockIndex, issues);
            }
        }
    }

    // --- Shape/text-box text: low-contrast text (Warning), resolved against the SHAPE's own text runs
    // and the SHAPE's own fill rather than the synthetic outer run/paragraph that carries it. ---
    private static void CheckShapeText(
        TextDocument document, Shape shape, int blockIndex, List<AccessibilityIssue> issues)
    {
        // Only a solid or gradient fill gives the shape a determinate on-screen backdrop; a no-fill or
        // pattern-fill shape has no single fixed background (the page or whatever sits behind the shape
        // shows through), so grading its text against a fabricated background would misfire -- the same
        // DO-NOT-WIDEN-PAST-THE-GUARD exemption FreeX's own low-contrast-shape-text rule uses for its
        // HasFill == false case.
        var hasFixedBackground = shape.ExtendedFill switch
        {
            { Kind: ShapeFillKind.NoFill } => false,
            { Kind: ShapeFillKind.Pattern } => false,
            { Kind: ShapeFillKind.Gradient } gradient => gradient.GradientStops.Count > 0,
            _ => !string.IsNullOrEmpty(shape.FillColorHex), // null ExtendedFill, or Solid (reuses FillColorHex)
        };
        if (!hasFixedBackground)
            return;

        foreach (var shapeParagraph in shape.TextParagraphs)
        {
            foreach (var shapeRun in shapeParagraph.Runs)
            {
                if (string.IsNullOrWhiteSpace(shapeRun.Text) || shapeRun.Image is not null)
                    continue;

                // Reuses the ordinary run/paragraph-style resolution chain so an explicit colour set on
                // the shape's own text run (or its paragraph style) is honoured, not overridden by a
                // synthetic default.
                var foreground = ResolveTextColor(document, shapeParagraph, shapeRun);
                var background = ResolveShapeFillColor(shape, foreground);
                var ratio = ContrastRatio(foreground, background);
                if (ratio < MinContrastRatio)
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityRule.LowContrastText,
                        AccessibilitySeverity.Warning,
                        $"Text has low contrast ({ratio.ToString("0.0", CultureInfo.InvariantCulture)}:1, " +
                        $"below the {MinContrastRatio.ToString("0.0", CultureInfo.InvariantCulture)}:1 minimum). " +
                        "Increase the contrast between the text and its background.",
                        blockIndex, shapeParagraph, shapeRun));
                }
            }
        }
    }

    // Resolve a shape's effective fill colour against a specific foreground. A gradient fill only stores
    // its stops (no computed midpoint), so grade against whichever stop has the worse (lower) contrast
    // with the text colour -- mirroring the analogous gradient-fill worst-stop rule in FreeX's
    // AccessibilityCheckerService.Contrast.cs. Callers only reach here when hasFixedBackground is true.
    private static string ResolveShapeFillColor(Shape shape, string foreground)
    {
        if (shape.ExtendedFill is { Kind: ShapeFillKind.Gradient } gradient && gradient.GradientStops.Count > 0)
        {
            return gradient.GradientStops
                .Select(stop => stop.ColorHex)
                .OrderBy(hex => ContrastRatio(foreground, hex))
                .First();
        }

        return shape.FillColorHex!;
    }

    // --- Per-table rules: missing header row (Warning) and blank cells (Tip). ---
    private static void CheckTable(
        TextDocument document, Table table, int blockIndex, List<AccessibilityIssue> issues)
    {
        if (table.RowCount == 0)
            return;

        // Header-row heuristic: the model's only explicit header signal is TableFormatting.HeaderRow
        // (w:tblLook firstRow). When that is off we fall back to a content heuristic — a first row is
        // "obviously a header" only if every cell is non-empty and bold-styled. If neither signal is
        // present we conservatively flag the table as lacking a marked header row.
        if (!HasHeaderRow(document, table))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityRule.TableMissingHeaderRow,
                AccessibilitySeverity.Warning,
                "A table has no header row. Mark its first row as a header so its structure is announced.",
                blockIndex, Paragraph: null, Run: null, Table: table));
        }

        // Blank cells (Tip): any cell with no visible text suggests missing data or a layout-only table.
        var hasBlankCell = table.Rows
            .SelectMany(r => r.Cells)
            .Any(c => string.IsNullOrWhiteSpace(c.PlainText));
        if (hasBlankCell)
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityRule.BlankTableCell,
                AccessibilitySeverity.Tip,
                "A table contains one or more blank cells. Fill in empty cells or merge them so the " +
                "table reads clearly.",
                blockIndex, Paragraph: null, Run: null, Table: table));
        }
    }

    // --- Heading-order rules: gaps between headings, or body text with no headings at all. ---
    private static void CheckHeadingOrder(TextDocument document, List<AccessibilityIssue> issues)
    {
        var outline = DocumentOutline.Of(document);

        if (outline.Count == 0)
        {
            // Document with body text but no headings at all → Tip (structure is harder to navigate).
            if (HasBodyText(document))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityRule.HeadingOrderGap,
                    AccessibilitySeverity.Tip,
                    "The document has body text but no headings. Add headings to give it a navigable " +
                    "structure.",
                    BlockIndex: -1));
            }
            return;
        }

        // Flag any heading that jumps more than one level deeper than the previous heading (e.g. Heading 1
        // → Heading 3). Title is level 0 and Heading N is level N; the first heading is the baseline.
        var previousLevel = 0;
        var seenFirst = false;
        foreach (var entry in outline)
        {
            if (seenFirst && entry.Level > previousLevel + 1)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityRule.HeadingOrderGap,
                    AccessibilitySeverity.Warning,
                    $"Heading level jumps from {previousLevel} to {entry.Level} (\"{entry.Text}\"), " +
                    "skipping a level. Use consecutive heading levels.",
                    entry.BlockIndex,
                    document.Blocks[entry.BlockIndex] as Paragraph));
            }
            previousLevel = entry.Level;
            seenFirst = true;
        }
    }

    // True when the document has at least one non-empty, non-heading body paragraph (i.e. real prose).
    private static bool HasBodyText(TextDocument document) =>
        document.Paragraphs.Any(p =>
            !DocumentOutline.TryGetLevel(p.StyleId, out _) &&
            !string.IsNullOrWhiteSpace(p.PlainText));

    // A run is a hyperlink when it carries either an external URL or an internal bookmark anchor.
    private static bool IsHyperlink(Run run) =>
        !string.IsNullOrEmpty(run.HyperlinkUrl) || !string.IsNullOrEmpty(run.HyperlinkAnchor);

    // A link's visible text is uninformative when it is blank, equals the bare URL/anchor it points at, or
    // is a known filler phrase ("click here", "read more", …).
    private static bool IsUninformativeLinkText(Run run)
    {
        var text = run.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return true;

        // A bare URL as the link text gives no human-readable destination cue.
        if (!string.IsNullOrEmpty(run.HyperlinkUrl) &&
            string.Equals(text, run.HyperlinkUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // Text that merely looks like a URL (starts with a scheme or www.) is equally unhelpful.
        if (LooksLikeUrl(text))
            return true;

        // Strip a single trailing period so "Click here." matches "click here".
        var normalized = text.TrimEnd('.').Trim();
        return UninformativeLinkPhrases.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUrl(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    // Does the table have a header row? True when the table style marks the first row as a header, or when
    // a content heuristic recognises the first row as an obvious header (every cell non-empty and bold).
    private static bool HasHeaderRow(TextDocument document, Table table)
    {
        if (table.Formatting.HeaderRow || table.Formatting.RepeatHeaderRow)
            return true;

        var firstRow = table.Rows[0];
        if (firstRow.Cells.Count == 0)
            return false;

        // Content fallback: a first row whose every cell has non-blank, fully bold text reads as a header.
        return firstRow.Cells.All(cell =>
            !string.IsNullOrWhiteSpace(cell.PlainText) &&
            cell.Paragraphs.SelectMany(p => p.Runs).Where(r => !string.IsNullOrEmpty(r.Text)).Any() &&
            cell.Paragraphs.SelectMany(p => p.Runs)
                .Where(r => !string.IsNullOrEmpty(r.Text))
                .All(r => IsBold(document, FindOwningParagraph(cell, r), r)));
    }

    private static Paragraph FindOwningParagraph(TableCell cell, Run run) =>
        cell.Paragraphs.First(p => p.Runs.Contains(run));

    // === Colour resolution ===
    // Resolve a run's effective text colour by walking run formatting → its paragraph style chain →
    // document default, then falling back to black. Mirrors how Word resolves rPr (run wins over style).

    private static string ResolveTextColor(TextDocument document, Paragraph paragraph, Run run)
    {
        if (!string.IsNullOrEmpty(run.Formatting.ColorHex))
            return run.Formatting.ColorHex!;

        var styleColor = ResolveStyleColor(document, paragraph.StyleId);
        if (!string.IsNullOrEmpty(styleColor))
            return styleColor!;

        if (!string.IsNullOrEmpty(document.DefaultRun.ColorHex))
            return document.DefaultRun.ColorHex!;

        return DefaultTextColorHex;
    }

    // Walk the BasedOn style chain looking for the nearest ancestor that sets a run colour.
    private static string? ResolveStyleColor(TextDocument document, string? styleId)
    {
        var guard = 0; // defend against a malformed cyclic BasedOn chain
        while (!string.IsNullOrEmpty(styleId) &&
               document.Styles.TryGetValue(styleId!, out var style) &&
               guard++ < 32)
        {
            if (!string.IsNullOrEmpty(style.Run.ColorHex))
                return style.Run.ColorHex;
            styleId = style.BasedOnStyleId;
        }
        return null;
    }

    // Resolve a run's effective background: run highlight wins, then paragraph shading, then the cell's
    // shading (if the paragraph lives in a table cell — handled by the caller passing cell shading via
    // paragraph), then a white page. Run/paragraph are the only backgrounds reachable from a run, so we
    // resolve highlight → paragraph shading → white. (Cell shading is folded in by CheckTable's paragraphs
    // inheriting nothing extra; a blank cell background is white, the conservative default.)
    private static string ResolveBackgroundColor(Paragraph paragraph, Run run)
    {
        if (!string.IsNullOrEmpty(run.Formatting.HighlightColorHex))
            return run.Formatting.HighlightColorHex!;

        if (!string.IsNullOrEmpty(paragraph.Formatting.ShadingColorHex))
            return paragraph.Formatting.ShadingColorHex!;

        return DefaultBackgroundHex;
    }

    private static bool IsBold(TextDocument document, Paragraph paragraph, Run run)
    {
        if (run.Formatting.Bold)
            return true;
        return ResolveStyleBold(document, paragraph.StyleId);
    }

    private static bool ResolveStyleBold(TextDocument document, string? styleId)
    {
        var guard = 0;
        while (!string.IsNullOrEmpty(styleId) &&
               document.Styles.TryGetValue(styleId!, out var style) &&
               guard++ < 32)
        {
            if (style.Run.Bold)
                return true;
            styleId = style.BasedOnStyleId;
        }
        return false;
    }

    // === WCAG contrast maths (self-contained) ===
    // The contrast ratio between two colours is (L1 + 0.05) / (L2 + 0.05), where L1 is the lighter and L2
    // the darker relative luminance, per WCAG 2.x. Relative luminance is computed from sRGB by linearising
    // each channel and weighting them 0.2126 R + 0.7152 G + 0.0722 B. Ratios range from 1:1 (identical) to
    // 21:1 (black on white).

    /// <summary>
    /// The WCAG contrast ratio (1.0–21.0) between two RRGGBB hex colours. Order-independent. Unparseable
    /// colours are treated as the relevant default (text→black, background→white) by the callers, so this
    /// helper assumes both inputs already parse.
    /// </summary>
    private static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        var l1 = RelativeLuminance(foregroundHex);
        var l2 = RelativeLuminance(backgroundHex);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The WCAG relative luminance (0.0–1.0) of an RRGGBB hex colour: each sRGB channel is normalised to
    /// 0..1, linearised via the sRGB transfer function, then combined with the luminance weights.
    /// </summary>
    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        var rl = LinearizeChannel(r / 255.0);
        var gl = LinearizeChannel(g / 255.0);
        var bl = LinearizeChannel(b / 255.0);
        return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
    }

    // The sRGB → linear transfer function applied per channel before the luminance weighting.
    private static double LinearizeChannel(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    // Parse an "#RRGGBB" or "RRGGBB" hex colour to its 0..255 channels. This WCAG helper intentionally
    // stays local: malformed values fall back to black so the checker remains conservative, unlike the
    // shared DrawingML/theme helpers that reject invalid text for serialization boundaries.
    private static (int R, int G, int B) ParseRgb(string hex)
    {
        var span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length == 6 &&
            int.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            int.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            int.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return (r, g, b);
        }

        return (0, 0, 0);
    }
}
