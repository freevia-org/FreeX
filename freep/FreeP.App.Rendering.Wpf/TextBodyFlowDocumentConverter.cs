using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun       = System.Windows.Documents.Run;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;
using ModelTableCell = FreeP.Core.Model.TableCell;
using ModelTableRow  = FreeP.Core.Model.TableRow;
using WpfHyperlink  = System.Windows.Documents.Hyperlink;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Static helper for converting between a FreeP <see cref="TextBody"/> and a WPF
/// <see cref="FlowDocument"/> so that <see cref="InCanvasTextEditor"/> can use a
/// <see cref="System.Windows.Controls.RichTextBox"/> to preserve per-run formatting while editing.
///
/// Design: the conversion is entirely pure / framework-independent at the model level.
/// Only WPF types appear at the FlowDocument end — no live RichTextBox required — so
/// the round-trip can be exercised in unit tests with no STA constraint (the FlowDocument
/// itself is created on whatever thread it's needed; the RichTextBox wrapping it must be STA).
///
/// Wave 10A: initial implementation.
/// Deferred: IME behavior. List markers are display-only inline visuals; they never enter
/// model text or logical caret offsets.
/// </summary>
internal static class TextBodyFlowDocumentConverter
{
    // WPF uses DIPs; PowerPoint font size is in points. 1pt = 96/72 DIPs.
    private const double PtToDip = 96.0 / 72.0;
    private const double DipToPt = 72.0 / 96.0;
    private const double EmuPerDip = 9525.0;

    // ── TextBody → FlowDocument ───────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TextBody"/> to a WPF <see cref="FlowDocument"/>.
    /// Each model <see cref="ModelParagraph"/> becomes one WPF <see cref="WpfParagraph"/>;
    /// each model <see cref="ModelRun"/> becomes one WPF <see cref="WpfRun"/>.
    /// Paragraph alignment, font family/size, bold, italic, underline, strikethrough, color,
    /// and the sign of per-run baseline offsets are all mapped.
    ///
    /// If <paramref name="body"/> is null or empty an empty single-paragraph document is returned.
    /// </summary>
    public static FlowDocument ToFlowDocument(
        TextBody? body,
        double fallbackFontSizePt = InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt)
    {
        // 100000 DIPs (~1041 feet) is large enough that the FlowDocument never paginates
        // inside a RichTextBox, while staying within WPF's accepted finite range.
        const double VeryLargeWidth = 100_000.0;
        double flowWidth = body?.Wrap == false ? VeryLargeWidth : double.NaN;

        var doc = new FlowDocument
        {
            // Disable pagination — we render in a RichTextBox / scroll viewer.
            PageWidth   = flowWidth,
            ColumnWidth = flowWidth,
            FontFamily  = new FontFamily(InCanvasRichTextEditorDefaults.FallbackFontFamily),
            FontSize    = fallbackFontSizePt * PtToDip,
        };

        if (body is null || body.Paragraphs.Count == 0)
        {
            doc.Blocks.Add(new WpfParagraph());
            return doc;
        }

        var markerState = new PresentationListMarkerContinuationState();
        foreach (var mp in body.Paragraphs)
        {
            var inheritedStyle = body.LstStyle?.Resolve(mp.Level);
            var effectiveAlign = mp.Align
                ?? inheritedStyle?.Align
                ?? body.DefaultParaAlign;
            long? effectiveMarginLeftEmu = mp.MarginLeftEmu ?? inheritedStyle?.MarginLeftEmu;
            long? effectiveIndentEmu = mp.IndentEmu ?? inheritedStyle?.IndentEmu;
            var wp = new WpfParagraph
            {
                // Remove default paragraph margins so rendering stays tight.
                Margin = new Thickness(0),
                FlowDirection = ResolveFlowDirection(body, mp)
            };

            // Paragraph alignment.
            if (effectiveAlign.HasValue)
            {
                wp.TextAlignment = effectiveAlign.Value switch
                {
                    TextAlign.Left        => TextAlignment.Left,
                    TextAlign.Center      => TextAlignment.Center,
                    TextAlign.Right       => TextAlignment.Right,
                    TextAlign.Justify     => TextAlignment.Justify,
                    TextAlign.Distributed => TextAlignment.Justify,
                    _                     => TextAlignment.Left
                };
            }

            if (effectiveMarginLeftEmu.HasValue)
            {
                wp.Margin = new Thickness(effectiveMarginLeftEmu.Value / EmuPerDip, 0, 0, 0);
            }

            if (effectiveIndentEmu.HasValue)
                wp.TextIndent = effectiveIndentEmu.Value / EmuPerDip;

            ApplyInheritedRunStyle(wp, inheritedStyle);

            if (mp.SpaceBeforePt.HasValue || mp.SpaceAfterPt.HasValue)
            {
                wp.Margin = new Thickness(
                    wp.Margin.Left,
                    mp.SpaceBeforePt.HasValue ? mp.SpaceBeforePt.Value * PtToDip : 0,
                    0,
                    mp.SpaceAfterPt.HasValue  ? mp.SpaceAfterPt.Value  * PtToDip : 0);
            }

            if (mp.Runs.Count == 0)
            {
                // Preserve empty paragraph as a run with no text.
                if (CreateDisplayOnlyBullet(body, mp, markerState, fallbackFontSizePt) is { } emptyMarker)
                    wp.Inlines.Add(emptyMarker);
                wp.Inlines.Add(new WpfRun(string.Empty));
            }
            else
            {
                if (CreateDisplayOnlyBullet(body, mp, markerState, fallbackFontSizePt) is { } marker)
                    wp.Inlines.Add(marker);
                foreach (var mr in mp.Runs)
                    wp.Inlines.Add(ModelRunToWpfRun(mr, inheritedStyle));
            }

            doc.Blocks.Add(wp);
        }

        return doc;
    }

    // ── FlowDocument → TextBody ───────────────────────────────────────────────

    /// <summary>
    /// Converts a WPF <see cref="FlowDocument"/> back to a <see cref="TextBody"/>.
    /// Walks every <see cref="Block"/> (expected to be <see cref="WpfParagraph"/>) and every
    /// inline within it (expected to be <see cref="WpfRun"/> or a nested <see cref="Span"/>).
    ///
    /// Contiguous WPF Runs that share identical properties within the same logical span are
    /// preserved as distinct model runs; merging is not performed (keeping round-trip lossless).
    ///
    /// The returned body preserves the original wrap policy; alignment, font, color, bold, italic,
    /// underline, and strikethrough are extracted. Color is stored as a resolved sRGB
    /// <see cref="ThemeAwareColor"/> (scheme ref not available during editing, by design).
    /// </summary>
    public static TextBody FromFlowDocument(FlowDocument doc, TextBody? originalBody = null)
    {
        var body = new TextBody
        {
            Wrap          = originalBody?.Wrap ?? true,
            Anchor        = originalBody?.Anchor,
            InsetLeftPt   = originalBody?.InsetLeftPt,
            InsetRightPt  = originalBody?.InsetRightPt,
            InsetTopPt    = originalBody?.InsetTopPt,
            InsetBottomPt = originalBody?.InsetBottomPt,
            DefaultParaRightToLeft = originalBody?.DefaultParaRightToLeft,
            LstStyle      = originalBody?.LstStyle,
        };

        var blocks = doc.Blocks.ToList();
        var editedParagraphTexts = blocks
            .Select(block => block is WpfParagraph paragraph
                ? ParagraphText(paragraph)
                : string.Empty)
            .ToArray();
        var sourceParagraphIndices = originalBody is null
            ? Array.Empty<int>()
            : InCanvasRichTextParagraphEditPlanner.ResolveSourceParagraphIndices(
                originalBody.Paragraphs,
                editedParagraphTexts);
        var consumedSourceParagraphs = new HashSet<int>();
        int modelParaIndex = 0;
        foreach (var block in blocks)
        {
            int sourceParaIndex = originalBody is null
                ? -1
                : sourceParagraphIndices[modelParaIndex];
            bool isSplitContinuation = sourceParaIndex >= 0
                && !consumedSourceParagraphs.Add(sourceParaIndex);
            var mp = sourceParaIndex >= 0
                ? InCanvasRichTextParagraphEditPlanner.CloneParagraphMetadata(
                    originalBody!.Paragraphs[sourceParaIndex],
                    clearAutoNumStartAtSpecified: isSplitContinuation)
                : new ModelParagraph();
            mp.Runs.Clear();

            // Restore paragraph alignment.
            if (block is WpfParagraph wpPara)
            {
                mp.Align = wpPara.TextAlignment switch
                {
                    TextAlignment.Center  => TextAlign.Center,
                    TextAlignment.Right   => TextAlign.Right,
                    TextAlignment.Justify => TextAlign.Justify,
                    _                     => TextAlign.Left
                };
                var sourceRightToLeft = sourceParaIndex >= 0
                    ? originalBody!.Paragraphs[sourceParaIndex].RightToLeft
                    : null;
                if (sourceRightToLeft.HasValue || sourceParaIndex < 0)
                {
                    mp.RightToLeft = sourceRightToLeft
                        ?? (wpPara.FlowDirection == FlowDirection.RightToLeft ? true : null);
                }
                else
                {
                    // A FlowDocument materializes inherited direction on every paragraph.
                    // Keep an omitted model attribute omitted when the editor left the
                    // inherited direction unchanged; author an override only on a change.
                    var inheritedDirection = ResolveFlowDirection(
                        originalBody!,
                        originalBody!.Paragraphs[sourceParaIndex]);
                    bool documentDirection = wpPara.FlowDirection == FlowDirection.RightToLeft;
                    mp.RightToLeft = inheritedDirection ==
                        (documentDirection ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)
                        ? null
                        : documentDirection;
                }
            }

            if (block is WpfParagraph wp2)
            {
                // Collect the original paragraph's runs (if available) so we can pass
                // each original run to WpfInlineToModelRun for Y2 scheme-color preservation.
                //
                // AA1 fix (3rd attempt) — FAIL-SAFE prefix/suffix pairing:
                //
                // Previous attempt (Z2) matched by CHARACTER OFFSET: the reconstructed
                // inline's start offset was looked up in the original run's [start,end) span.
                // That breaks on ANY delete before a run: deleting 3 chars from run A shifts
                // run B's reconstructed start offset left, so it falls inside A's original span
                // and gets A's scheme-color — visible corruption.
                //
                // INVARIANT: a reconstructed run must carry an original run's inherited
                // Color/font ONLY when that run is PROVABLY UNCHANGED.  On ANY doubt: null.
                //   • null (inherit) → the run re-inherits the placeholder/theme color — safe.
                //   • wrong color carried over → visible wrong color on screen — the bug.
                //
                // ALGORITHM — longest common PREFIX + longest common SUFFIX by TEXT equality:
                //   1. Materialise all leaf inlines into a list (we need indexed access).
                //   2. Find prefix length P: smallest i where leaf[i].Text ≠ origRun[i].Text
                //      (stop at min(leafCount, origCount)).
                //   3. Find suffix length S: walk from both ends while texts match, stopping
                //      before the already-matched prefix (so P+S ≤ min counts).
                //   4. Leaf index i < P        → origRuns[i]            (unchanged prefix)
                //      Leaf index i ≥ leafCount-S → origRuns[n - (leafCount-i)] (unchanged suffix)
                //      Leaf index otherwise    → null                   (disturbed middle)
                //
                // Guarantee: a run can NEVER be matched to a misaligned original run, because
                // matching requires TEXT equality at every step.  The wrong-color AA1 case is
                // structurally impossible: after a deletion the first mismatched text stops the
                // prefix, and B (now at a lower index) only enters the suffix match where its
                // text must equal the original's text at that suffix position.
                IReadOnlyList<ModelRun>? origRuns = null;
                if (sourceParaIndex >= 0)
                    origRuns = originalBody!.Paragraphs[sourceParaIndex].Runs;

                // Materialise leaf inlines so we can index them.
                var leafList = EnumerateEditableLeafInlines(wp2.Inlines).ToList();
                int m = leafList.Count;   // reconstructed count
                int n = origRuns?.Count ?? 0; // original count

                // Helper: text of a leaf inline (mirrors the model convention).
                static string LeafText(Inline leaf) => leaf switch
                {
                    WpfRun wr   => wr.Text ?? string.Empty,
                    LineBreak _ => "\n",
                    InlineUIContainer => "\uFFFC",
                    _           => string.Empty
                };

                // Helper: text of an original run.
                static string OrigText(ModelRun r) => r.Text ?? string.Empty;

                // Step 2: longest common prefix (by text equality).
                int prefixLen = 0;
                int maxPrefix = Math.Min(m, n);
                while (prefixLen < maxPrefix &&
                       LeafText(leafList[prefixLen]) == OrigText(origRuns![prefixLen]))
                {
                    prefixLen++;
                }

                // Step 3: longest common suffix (by text equality), not overlapping prefix.
                int suffixLen = 0;
                int maxSuffix = Math.Min(m, n) - prefixLen;
                while (suffixLen < maxSuffix &&
                       LeafText(leafList[m - 1 - suffixLen]) == OrigText(origRuns![n - 1 - suffixLen]))
                {
                    suffixLen++;
                }

                // Step 4: walk the materialised list and assign origRun per the prefix/suffix map.
                for (int li = 0; li < m; li++)
                {
                    ModelRun? origRun;
                    if (li < prefixLen)
                    {
                        // Unchanged prefix: provably same run.
                        origRun = origRuns![li];
                    }
                    else if (suffixLen > 0 && li >= m - suffixLen)
                    {
                        // Unchanged suffix: offset from the end.
                        int suffixOffset = li - (m - suffixLen); // 0-based within suffix
                        origRun = origRuns![n - suffixLen + suffixOffset];
                    }
                    else
                    {
                        // Disturbed middle — do NOT carry any original color/font.
                        origRun = null;
                    }

                    var mr = WpfInlineToModelRun(leafList[li], origRun);
                    mp.Runs.Add(mr);
                }
            }

            // Ensure at least one (empty) run so the paragraph is not lost.
            if (mp.Runs.Count == 0)
                mp.Runs.Add(new ModelRun { Text = string.Empty });

            body.Paragraphs.Add(mp);
            modelParaIndex++;
        }

        // Ensure at least one paragraph.
        if (body.Paragraphs.Count == 0)
        {
            var para = new ModelParagraph();
            para.Runs.Add(new ModelRun { Text = string.Empty });
            body.Paragraphs.Add(para);
        }

        return body;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static FlowDirection ResolveFlowDirection(TextBody body, ModelParagraph paragraph) =>
        paragraph.RightToLeft
            ?? body.LstStyle?.Resolve(paragraph.Level)?.RightToLeft
            ?? body.DefaultParaRightToLeft
            ?? false
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    private static void ApplyInheritedRunStyle(WpfParagraph paragraph, TextStyleLevel? style)
    {
        if (style is null)
            return;

        if (style.FontSizePt is > 0)
            paragraph.FontSize = style.FontSizePt.Value * PtToDip;
        if (style.Bold.HasValue)
            paragraph.FontWeight = style.Bold.Value ? FontWeights.Bold : FontWeights.Normal;
        if (style.Italic.HasValue)
            paragraph.FontStyle = style.Italic.Value ? FontStyles.Italic : FontStyles.Normal;
        if (!string.IsNullOrWhiteSpace(style.LatinFont)
            && !style.LatinFont.StartsWith("+", StringComparison.Ordinal))
        {
            paragraph.FontFamily = new FontFamily(style.LatinFont);
        }

        if (style.Color is { } color)
        {
            var resolved = color.Resolved;
            paragraph.Foreground = new SolidColorBrush(
                Color.FromArgb(color.Alpha, resolved.R, resolved.G, resolved.B));
        }
    }

    private static Inline ModelRunToWpfRun(ModelRun mr, TextStyleLevel? inheritedStyle = null)
    {
        if (mr.InlineTable is { } inlineTable)
        {
            return new InlineUIContainer(CreateInlineTableEditor(inlineTable))
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
        }

        if (mr.InlineOleObject is { } ole)
        {
            var label = string.IsNullOrWhiteSpace(ole.ClassName)
                ? "OLE object"
                : ole.ClassName;
            var border = new Border
            {
                Width = 42,
                Height = 20,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.Gainsboro,
                ToolTip = label,
                Child = new TextBlock
                {
                    Text = "OLE",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 9,
                    Foreground = Brushes.Black,
                },
            };
            border.MouseLeftButtonDown += (_, args) =>
            {
                if (args.ClickCount >= 2 && OleActivationService.TryActivate(ole))
                    args.Handled = true;
            };
            WpfOleInPlaceHost.AttachInline(border, ole, width: 42, height: 20);
            return new InlineUIContainer(border)
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
        }

        if (mr.InlineImage is { Bytes.Length: > 0 } image)
        {
            var control = new Image
            {
                Source = LoadBitmap(image.Bytes),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            if (mr.InlineImageWidthEmu is > 0)
                control.Width = mr.InlineImageWidthEmu.Value / 9525.0;
            if (mr.InlineImageHeightEmu is > 0)
                control.Height = mr.InlineImageHeightEmu.Value / 9525.0;

            return new InlineUIContainer(control)
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
        }

        // Y5: a run with Text=="\n" maps to a WPF LineBreak so soft breaks survive
        // repeated round-trips symmetrically (FromFlowDocument maps LineBreak → "\n").
        if (mr.Text == "\n")
            return new LineBreak();

        var wr = new WpfRun(mr.Text ?? string.Empty);

        // Y1: only set a LOCAL font-family / font-size on the inline when the model
        // run carries an explicit value.  Runs with null values must not set any local
        // value so that WpfInlineToModelRun sees UnsetValue and leaves them null (inherit).
        if (!string.IsNullOrEmpty(mr.FontFamily))
            wr.FontFamily = new FontFamily(mr.FontFamily);

        if (mr.FontSizePt.HasValue)
            wr.FontSize = mr.FontSizePt.Value * PtToDip;

        // WPF exposes the user-facing superscript/subscript behavior as a baseline
        // alignment. Preserve the authored numeric DrawingML value on read-back when
        // possible; new edits use canonical sign-only values in the model.
        wr.BaselineAlignment = mr.BaselineOffset switch
        {
            > 0 => BaselineAlignment.Superscript,
            < 0 => BaselineAlignment.Subscript,
            _   => BaselineAlignment.Baseline,
        };

        // Y4: only set Bold from an explicit model value or a programmatic true value. When a
        // paragraph carries an inherited style, leaving false unset lets the paragraph default
        // flow through and keeps inherited-bold runs from becoming local Normal values.
        // The key correction is the Y4 fix: map only FontWeights.Bold → mr.Bold; SemiBold/DemiBold
        // must NOT become Bold on the round-trip.
        if (mr.BoldSet || mr.Bold || inheritedStyle is null)
            wr.FontWeight = mr.Bold ? FontWeights.Bold : FontWeights.Normal;
        if (mr.ItalicSet || mr.Italic || inheritedStyle is null)
            wr.FontStyle = mr.Italic ? FontStyles.Italic : FontStyles.Normal;

        // Underline + Strikethrough as TextDecorations.
        if (mr.Underline || mr.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (mr.Underline)
                decorations.Add(TextDecorations.Underline[0].Clone());
            if (mr.Strikethrough)
                decorations.Add(TextDecorations.Strikethrough[0].Clone());
            wr.TextDecorations = decorations;
        }
        else
        {
            // Explicitly clear inherited decorations.
            wr.TextDecorations = new TextDecorationCollection();
        }

        // Y2: only set a LOCAL foreground when the run has an explicit color.
        // When mr.Color is null (inherit), leave Foreground unset so the read-back
        // via ReadLocalValue sees UnsetValue and leaves mr.Color null (preserving inherit).
        var color = ResolveModelColor(mr.Color);
        if (color.HasValue)
            wr.Foreground = new SolidColorBrush(color.Value);

        if (mr.Hyperlink is not { } link)
            return wr;

        var hyperlink = new WpfHyperlink(wr)
        {
            ToolTip = link.Tooltip,
        };
        if (link.IsExternal && Uri.TryCreate(link.Url, UriKind.Absolute, out var url))
            hyperlink.NavigateUri = url;
        else if (!string.IsNullOrWhiteSpace(link.TargetSlideId))
            hyperlink.NavigateUri = new Uri(
                "freep-slide:" + Uri.EscapeDataString(link.TargetSlideId),
                UriKind.Absolute);
        return hyperlink;
    }

    private static Grid CreateInlineTableEditor(InlineTableInfo info)
    {
        var table = info.Table;
        double spacingDip = Math.Max(0, table.RichTextCellSpacingPt.GetValueOrDefault()) * PtToDip;
        var columnWidths = Enumerable.Range(0, Math.Max(1, table.ColumnWidthsEmu.Count))
            .Select(column => column < table.ColumnWidthsEmu.Count
                ? Math.Max(24, table.ColumnWidthsEmu[column] / 9525.0)
                : 72)
            .ToArray();
        for (int column = 0; column + 1 < columnWidths.Length; column++)
            columnWidths[column] += spacingDip;
        var grid = new Grid
        {
            Tag = info.Clone(),
            Background = Brushes.Transparent,
            HorizontalAlignment = ToWpfHorizontalAlignment(
                table.Rows.FirstOrDefault()?.HorizontalAlignment),
            Margin = new Thickness(
                Math.Clamp(table.RichTextLeftIndentPt.GetValueOrDefault() * PtToDip, -1000, 1000),
                0,
                0,
                0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        int columnCount = columnWidths.Length;
        for (int column = 0; column < columnCount; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columnWidths[column]) });

        double tableWidth = columnWidths.Sum();
        var logicalGrid = InlineTableLogicalGridPlan.Create(table);
        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            double rowOffset = GetHorizontalOffset(row, columnWidths, tableWidth);
            double height = row.HeightEmu > 0 ? Math.Max(20, row.HeightEmu / 9525.0) : 24;
            var rowDefinition = new RowDefinition();
            if (row.HeightRule == TableRowHeightRule.AtLeast && row.HeightEmu > 0)
            {
                rowDefinition.Height = GridLength.Auto;
                rowDefinition.MinHeight = height;
            }
            else
            {
                rowDefinition.Height = new GridLength(height);
            }
            grid.RowDefinitions.Add(rowDefinition);
            foreach (var logicalCell in logicalGrid.Cells.Where(cell => cell.RowIndex == rowIndex))
            {
                var cell = logicalCell.Cell;
                int columnIndex = logicalCell.ColumnIndex;
                var textBox = new TextBox
                {
                    Text = cell.TextBody is null
                        ? string.Empty
                        : InCanvasTextEditPlanner.ExtractPlainText(cell.TextBody),
                    Tag = new InlineTableCellBinding(
                        logicalCell.RowIndex,
                        logicalCell.ColumnIndex,
                        logicalCell.SourceCellIndex,
                        cell),
                    AcceptsReturn = true,
                    BorderThickness = new Thickness(0.5),
                    BorderBrush = Brushes.Gray,
                    Margin = columnIndex + Math.Max(1, cell.GridSpan) < columnCount
                        ? new Thickness(0, 0, spacingDip, 0)
                        : new Thickness(0),
                    Padding = new Thickness(
                        cell.InsetLeftPt.GetValueOrDefault() * PtToDip,
                        cell.InsetTopPt.GetValueOrDefault() * PtToDip,
                        cell.InsetRightPt.GetValueOrDefault() * PtToDip,
                        cell.InsetBottomPt.GetValueOrDefault() * PtToDip),
                    VerticalContentAlignment = cell.Anchor switch
                    {
                        TableCellAnchor.Middle => VerticalAlignment.Center,
                        TableCellAnchor.Bottom => VerticalAlignment.Bottom,
                        _ => VerticalAlignment.Top,
                    },
                    RenderTransform = rowOffset > 0
                        ? new TranslateTransform(rowOffset, 0)
                        : null,
                };
                if (cell.Fill is ShapeFill.Solid solid)
                    textBox.Background = new SolidColorBrush(
                        Color.FromArgb(solid.Color.Alpha, solid.Color.Resolved.R,
                            solid.Color.Resolved.G, solid.Color.Resolved.B));
                Grid.SetRow(textBox, rowIndex);
                Grid.SetColumn(textBox, Math.Min(columnIndex, columnCount - 1));
                Grid.SetColumnSpan(textBox, Math.Min(Math.Max(1, cell.GridSpan), columnCount - columnIndex));
                Grid.SetRowSpan(textBox, Math.Min(Math.Max(1, cell.RowSpan), table.Rows.Count - rowIndex));
                textBox.PreviewKeyDown += (_, args) =>
                    OnInlineTableCellPreviewKeyDown(grid, info, textBox, args);
                grid.Children.Add(textBox);
            }
        }
        return grid;
    }

    private static void OnInlineTableCellPreviewKeyDown(
        Grid grid,
        InlineTableInfo info,
        TextBox current,
        KeyEventArgs args)
    {
        if (args.Key != Key.Tab)
            return;

        bool backwards = (args.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        var editorInfo = grid.Tag as InlineTableInfo ?? info;
        if (TryNavigateInlineTableCell(grid, editorInfo, current, backwards))
            args.Handled = true;
    }

    internal static bool TryNavigateInlineTableCell(
        Grid grid,
        InlineTableInfo info,
        TextBox current,
        bool backwards)
    {
        if (current.Tag is not InlineTableCellBinding binding)
            return false;

        var logicalGrid = InlineTableLogicalGridPlan.Create(info.Table);
        var currentCell = logicalGrid.ResolveCell(binding.RowIndex, binding.ColumnIndex);
        if (currentCell is null)
            return false;

        if (logicalGrid.TryGetAdjacent(currentCell, backwards, out var next))
        {
            grid.Children.OfType<TextBox>()
                .FirstOrDefault(child => child.Tag is InlineTableCellBinding nextBinding
                    && nextBinding.RowIndex == next.RowIndex
                    && nextBinding.ColumnIndex == next.ColumnIndex)
                ?.Focus();
            return true;
        }

        if (backwards)
        {
            // Keep Shift+Tab inside the inline table at its first cell.
            return true;
        }

        AppendInlineTableRow(grid, info);
        int newRowIndex = info.Table.Rows.Count - 1;
        grid.Children.OfType<TextBox>()
            .Where(child => child.Tag is InlineTableCellBinding binding
                && binding.RowIndex == newRowIndex
                && binding.ColumnIndex == 0)
            .FirstOrDefault()
            ?.Focus();
        return true;
    }

    private static void AppendInlineTableRow(Grid grid, InlineTableInfo info)
    {
        var table = info.Table;
        int rowIndex = table.Rows.Count;
        int columnCount = Math.Max(1, grid.ColumnDefinitions.Count);
        var row = InlineTableLogicalGridPlan.CreateAppendRow(table);
        table.Rows.Add(row);

        double spacingDip = Math.Max(0, table.RichTextCellSpacingPt.GetValueOrDefault()) * PtToDip;
        var widths = grid.ColumnDefinitions
            .Select(definition => definition.Width.IsAbsolute
                ? definition.Width.Value
                : 72)
            .ToArray();
        double rowOffset = GetHorizontalOffset(row, widths, widths.Sum());
        double height = row.HeightEmu > 0 ? Math.Max(20, row.HeightEmu / 9525.0) : 24;
        var rowDefinition = new RowDefinition();
        if (row.HeightRule == TableRowHeightRule.AtLeast && row.HeightEmu > 0)
        {
            rowDefinition.Height = GridLength.Auto;
            rowDefinition.MinHeight = height;
        }
        else
        {
            rowDefinition.Height = new GridLength(height);
        }
        grid.RowDefinitions.Add(rowDefinition);

        for (int column = 0; column < columnCount; column++)
        {
            var cell = row.Cells[column];
            var textBox = CreateInlineTableCellTextBox(
                cell,
                rowIndex,
                column,
                spacingDip,
                columnCount,
                rowOffset);
            Grid.SetRow(textBox, rowIndex);
            Grid.SetColumn(textBox, column);
            textBox.PreviewKeyDown += (_, args) =>
                OnInlineTableCellPreviewKeyDown(grid, info, textBox, args);
            grid.Children.Add(textBox);
        }
    }

    private static TextBox CreateInlineTableCellTextBox(
        ModelTableCell cell,
        int rowIndex,
        int columnIndex,
        double spacingDip,
        int columnCount,
        double rowOffset)
    {
        var textBox = new TextBox
        {
            Text = cell.TextBody is null
                ? string.Empty
                : InCanvasTextEditPlanner.ExtractPlainText(cell.TextBody),
            Tag = new InlineTableCellBinding(rowIndex, columnIndex, columnIndex, cell),
            AcceptsReturn = true,
            BorderThickness = new Thickness(0.5),
            BorderBrush = Brushes.Gray,
            Margin = columnIndex + Math.Max(1, cell.GridSpan) < columnCount
                ? new Thickness(0, 0, spacingDip, 0)
                : new Thickness(0),
            Padding = new Thickness(
                cell.InsetLeftPt.GetValueOrDefault() * PtToDip,
                cell.InsetTopPt.GetValueOrDefault() * PtToDip,
                cell.InsetRightPt.GetValueOrDefault() * PtToDip,
                cell.InsetBottomPt.GetValueOrDefault() * PtToDip),
            VerticalContentAlignment = cell.Anchor switch
            {
                TableCellAnchor.Middle => VerticalAlignment.Center,
                TableCellAnchor.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            },
            RenderTransform = rowOffset > 0
                ? new TranslateTransform(rowOffset, 0)
                : null,
        };
        if (cell.Fill is ShapeFill.Solid solid)
            textBox.Background = new SolidColorBrush(
                Color.FromArgb(solid.Color.Alpha, solid.Color.Resolved.R,
                    solid.Color.Resolved.G, solid.Color.Resolved.B));
        return textBox;
    }

    private static double GetHorizontalOffset(
        ModelTableRow row,
        IReadOnlyList<double> columnWidths,
        double tableWidth)
    {
        int columnIndex = 0;
        double rowWidth = 0;
        foreach (var cell in row.Cells)
        {
            int span = Math.Max(1, cell.GridSpan);
            for (int index = 0; index < span && columnIndex + index < columnWidths.Count; index++)
                rowWidth += columnWidths[columnIndex + index];
            columnIndex += span;
        }

        double extra = Math.Max(0, tableWidth - rowWidth);
        return row.HorizontalAlignment switch
        {
            TableRowHorizontalAlignment.Center => extra / 2,
            TableRowHorizontalAlignment.Right => extra,
            _ => 0,
        };
    }

    private static HorizontalAlignment ToWpfHorizontalAlignment(
        TableRowHorizontalAlignment? alignment) => alignment switch
    {
        TableRowHorizontalAlignment.Center => HorizontalAlignment.Center,
        TableRowHorizontalAlignment.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };

    private sealed record InlineTableCellBinding(
        int RowIndex,
        int ColumnIndex,
        int SourceCellIndex,
        ModelTableCell SourceCell);

    /// <summary>
    /// Recursively enumerates the leaf <see cref="Inline"/> elements of a paragraph,
    /// flattening nested <see cref="Span"/> containers that the RichTextBox editing engine
    /// may insert when a user applies formatting to a sub-range.
    /// </summary>
    internal static IEnumerable<Inline> EnumerateLeafInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in EnumerateLeafInlines(span.Inlines))
                    yield return child;
            }
            else
            {
                yield return inline;
            }
        }
    }

    internal static IEnumerable<Inline> EnumerateEditableLeafInlines(InlineCollection inlines) =>
        EnumerateLeafInlines(inlines).Where(inline => !IsDisplayOnlyMarker(inline));

    internal static int LogicalOffsetAt(FlowDocument document, TextPointer position)
    {
        int logicalOffset = 0;
        bool firstParagraph = true;
        foreach (var paragraph in document.Blocks.OfType<WpfParagraph>())
        {
            if (!firstParagraph)
            {
                if (position.CompareTo(paragraph.ContentStart) <= 0)
                    return logicalOffset;
                logicalOffset++;
            }
            firstParagraph = false;

            foreach (var inline in EnumerateEditableLeafInlines(paragraph.Inlines))
            {
                if (position.CompareTo(inline.ContentStart) <= 0)
                    return logicalOffset;

                if (position.CompareTo(inline.ContentEnd) <= 0)
                {
                    if (inline is WpfRun run)
                    {
                        string text = new TextRange(run.ContentStart, position).Text
                            .Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n');
                        return logicalOffset + text.Length;
                    }

                    return logicalOffset;
                }

                logicalOffset += inline switch
                {
                    WpfRun run => run.Text?.Length ?? 0,
                    LineBreak => 1,
                    _ => 0,
                };
            }
        }

        return logicalOffset;
    }

    private static readonly DependencyProperty DisplayOnlyMarkerProperty =
        DependencyProperty.RegisterAttached(
            "DisplayOnlyMarker",
            typeof(bool),
            typeof(TextBodyFlowDocumentConverter),
            new FrameworkPropertyMetadata(false));

    private static void SetDisplayOnlyMarker(Inline inline) =>
        inline.SetValue(DisplayOnlyMarkerProperty, true);

    internal static bool IsDisplayOnlyMarker(Inline inline) =>
        inline.GetValue(DisplayOnlyMarkerProperty) is true;

    private static string ParagraphText(WpfParagraph paragraph) =>
        string.Concat(EnumerateEditableLeafInlines(paragraph.Inlines).Select(inline => inline switch
        {
            WpfRun run => run.Text ?? string.Empty,
            LineBreak => "\n",
            InlineUIContainer => "\uFFFC",
            _ => string.Empty,
        }));

    private static Inline? CreateDisplayOnlyBullet(
        TextBody body,
        ModelParagraph paragraph,
        PresentationListMarkerContinuationState markerState,
        double fallbackFontSizePt)
    {
        if (paragraph.BulletSuppressed)
        {
            markerState.Break();
            return null;
        }

        var seedRun = paragraph.Runs.FirstOrDefault(run => !string.IsNullOrEmpty(run.Text))
            ?? paragraph.Runs.FirstOrDefault();
        var style = body.LstStyle?.Resolve(paragraph.Level);
        bool inheritsStyleBullet = paragraph.BulletKind == BulletKind.None
            && style?.BulletKind is { };
        BulletKind effectiveKind = inheritsStyleBullet
            ? style!.BulletKind!.Value
            : paragraph.BulletKind;
        string? effectiveChar = inheritsStyleBullet
            ? style!.BulletChar
            : paragraph.BulletChar;
        AutoNumType effectiveAutoNumType = inheritsStyleBullet
            ? style!.AutoNumType
            : paragraph.AutoNumType;
        ThemeAwareColor? effectiveColor = inheritsStyleBullet
            ? (style!.BulletColorFollowsText ? null : style.BulletColor)
            : (paragraph.BulletColorFollowsText ? null : paragraph.BulletColor);
        string? effectiveFont = inheritsStyleBullet
            ? (style!.BulletFontFollowsText ? null : style.BulletFontFamily)
            : (paragraph.BulletFontFollowsText ? null : paragraph.BulletFontFamily);
        double? effectiveSizePt = inheritsStyleBullet
            ? (style!.BulletSizeFollowsText ? null : style.BulletSizePt)
            : (paragraph.BulletSizeFollowsText ? null : paragraph.BulletSizePt);
        int? effectiveSizePct = inheritsStyleBullet
            ? (style!.BulletSizeFollowsText ? null : style.BulletSizePct)
            : (paragraph.BulletSizeFollowsText ? null : paragraph.BulletSizePct);
        double markerSizePt = effectiveSizePt
            ?? (effectiveSizePct is > 0 && seedRun?.FontSizePt is > 0
                ? seedRun.FontSizePt.Value * effectiveSizePct.Value / 100000.0
                : seedRun?.FontSizePt)
            ?? fallbackFontSizePt;
        string? markerText = null;
        if (effectiveKind == BulletKind.Char)
        {
            markerText = effectiveChar ?? "•";
            markerState.Break();
        }
        else if (effectiveKind == BulletKind.Auto)
        {
            int value = markerState.Next(
                paragraph.Level,
                effectiveAutoNumType,
                paragraph.AutoNumStartAt,
                paragraph.AutoNumStartAtSpecified);
            markerText = markerState.FormatTemplate(
                paragraph.Level,
                effectiveAutoNumType,
                value,
                paragraph.AutoNumTextTemplate);
        }
        else if (effectiveKind == BulletKind.Image)
        {
            markerState.Break();
            if (paragraph.BulletImage is not { Bytes.Length: > 0 } image
                || LoadBitmap(image.Bytes) is not { } bitmap)
                return null;

            return CreateDisplayOnlyMarker(
                new Image
                {
                    Source = bitmap,
                    Width = markerSizePt * PtToDip,
                    Height = markerSizePt * PtToDip,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false,
                });
        }
        else
        {
            markerState.Break();
            return null;
        }

        if (string.IsNullOrEmpty(markerText))
            return null;

        var marker = new TextBlock
        {
            Text = markerText + " ",
            FontFamily = new FontFamily(
                effectiveFont
                ?? seedRun?.FontFamily
                ?? InCanvasRichTextEditorDefaults.FallbackFontFamily),
            FontSize = markerSizePt * PtToDip,
            Foreground = ResolveModelColor(effectiveColor ?? seedRun?.Color) is { } effectiveBrushColor
                ? new SolidColorBrush(effectiveBrushColor)
                : Brushes.Black,
            IsHitTestVisible = false,
            Focusable = false,
            TextWrapping = TextWrapping.NoWrap,
        };
        return CreateDisplayOnlyMarker(marker);
    }

    private static Inline CreateDisplayOnlyMarker(FrameworkElement child)
    {
        var container = new InlineUIContainer(child)
        {
            BaselineAlignment = BaselineAlignment.Baseline,
        };
        SetDisplayOnlyMarker(container);
        return container;
    }

    /// <summary>
    /// Reads formatting properties from a WPF <see cref="Inline"/> into a model <see cref="ModelRun"/>.
    ///
    /// Y1/Y2/Y4: Properties are read via <see cref="DependencyObject.ReadLocalValue"/> so that
    /// ONLY values explicitly set on this inline are captured.  An inherited / unset value returns
    /// <see cref="DependencyProperty.UnsetValue"/> and is left null in the model (inherit).
    /// This prevents baking theme/placeholder defaults into every run on a no-op edit commit.
    ///
    /// Y2: the original run (looked up by position) is passed through for its Color when the
    /// inline's Foreground is locally unset — preserving the SchemeColor ref.
    /// </summary>
    internal static ModelRun WpfInlineToModelRun(Inline inline, ModelRun? originalRun = null)
    {
        var mr = new ModelRun();

        // Text — only WpfRun has text; LineBreaks become "\n".
        mr.Text = inline switch
        {
            WpfRun wr   => wr.Text ?? string.Empty,
            LineBreak _  => "\n",
            InlineUIContainer => "\uFFFC",
            _            => string.Empty
        };

        if (inline is InlineUIContainer { Child: Image image })
        {
            var source = image.Source as BitmapSource;
            if (source is not null)
            {
                mr.InlineImage = new ImagePart
                {
                    Bytes = BitmapSourceToPng(source),
                    ContentType = originalRun?.InlineImage?.ContentType ?? "image/png",
                };
            }

            mr.InlineImageWidthEmu = originalRun?.InlineImageWidthEmu
                ?? ToEmu(image.Width);
            mr.InlineImageHeightEmu = originalRun?.InlineImageHeightEmu
                ?? ToEmu(image.Height);
        }

        if (inline is InlineUIContainer
            && originalRun?.InlineOleObject is { } originalOle)
        {
            mr.InlineOleObject = new InlineOleObjectInfo
            {
                EmbeddedBytes = originalOle.EmbeddedBytes.ToArray(),
                FileName = originalOle.FileName,
                ClassName = originalOle.ClassName,
            };
        }

        if (inline is InlineUIContainer { Child: Grid grid }
            && grid.Tag is InlineTableInfo originalTable)
        {
            var table = originalTable.Clone();
            foreach (var textBox in grid.Children.OfType<TextBox>())
            {
                if (textBox.Tag is not InlineTableCellBinding binding)
                    continue;
                var row = table.Table.Rows.ElementAtOrDefault(binding.RowIndex);
                var cell = row?.Cells.ElementAtOrDefault(binding.SourceCellIndex);
                if (cell is null)
                    continue;

                // Keep the cloned rich cell body, including nested inline tables, when
                // the editable text was not changed. A plain TextBox is the host editor
                // for this bounded path; only a real text edit should flatten its body.
                var originalText = binding.SourceCell.TextBody is null
                    ? string.Empty
                    : InCanvasTextEditPlanner.ExtractPlainText(binding.SourceCell.TextBody);
                if (string.Equals(originalText, textBox.Text ?? string.Empty, StringComparison.Ordinal))
                    continue;

                cell.TextBody = new TextBody
                {
                    Paragraphs =
                    {
                        new ModelParagraph { Runs = { new ModelRun { Text = textBox.Text ?? string.Empty } } },
                    },
                };
            }
            mr.InlineTable = table;
        }

        // Y1: read FontFamily LOCAL value only (not resolved/inherited).
        var localFamily = inline.ReadLocalValue(TextElement.FontFamilyProperty);
        if (localFamily != DependencyProperty.UnsetValue && localFamily is FontFamily ff)
            mr.FontFamily = ff.Source;
        // else leave mr.FontFamily = null (inherit)

        // Y1: read FontSize LOCAL value only.
        var localSize = inline.ReadLocalValue(TextElement.FontSizeProperty);
        if (localSize != DependencyProperty.UnsetValue && localSize is double sizeDip
            && !double.IsNaN(sizeDip) && sizeDip > 0)
            mr.FontSizePt = Math.Round(sizeDip * DipToPt, 4);
        // else leave mr.FontSizePt = null (inherit)

        // Preserve the exact authored baseline token for an unchanged source run. WPF's
        // BaselineAlignment models the visible superscript/subscript choice, not DrawingML's
        // percentage magnitude, so a newly created run receives a stable sign-only fallback.
        var localBaseline = inline.ReadLocalValue(Inline.BaselineAlignmentProperty);
        if (localBaseline != DependencyProperty.UnsetValue && localBaseline is BaselineAlignment alignment)
        {
            mr.BaselineOffset = alignment switch
            {
                BaselineAlignment.Superscript => originalRun?.BaselineOffset ?? 10000,
                BaselineAlignment.Subscript   => originalRun?.BaselineOffset ?? -10000,
                _                             => null,
            };
        }
        else
        {
            mr.BaselineOffset = originalRun?.BaselineOffset;
        }

        // Y4: read FontWeight LOCAL value only, and map ONLY FontWeights.Bold to mr.Bold=true.
        // SemiBold/DemiBold must NOT be coerced to Bold.
        var localWeight = inline.ReadLocalValue(TextElement.FontWeightProperty);
        if (localWeight != DependencyProperty.UnsetValue && localWeight is FontWeight fw)
        {
            mr.Bold    = fw == FontWeights.Bold;
            mr.BoldSet = true; // explicit WPF local value → must win over inherited style (PP1)
        }
        // else leave mr.Bold = false, mr.BoldSet = false (inherit from style chain)

        // Italic — read LOCAL value only.
        var localStyle = inline.ReadLocalValue(TextElement.FontStyleProperty);
        if (localStyle != DependencyProperty.UnsetValue && localStyle is FontStyle fs)
        {
            mr.Italic    = fs == FontStyles.Italic || fs == FontStyles.Oblique;
            mr.ItalicSet = true; // explicit WPF local value → must win over inherited style (PP1)
        }

        // Underline / Strikethrough from TextDecorations — read LOCAL value.
        var localDecorations = inline.ReadLocalValue(Inline.TextDecorationsProperty);
        if (localDecorations != DependencyProperty.UnsetValue &&
            localDecorations is TextDecorationCollection decorations)
        {
            foreach (var d in decorations)
            {
                if (d.Location == TextDecorationLocation.Underline)
                    mr.Underline = true;
                else if (d.Location == TextDecorationLocation.Strikethrough)
                    mr.Strikethrough = true;
            }
        }

        // WPF exposes only the enabled/disabled decoration, so retain an authored
        // DrawingML variant when an unchanged source run still has the decoration.
        if (mr.Underline && originalRun?.UnderlineStyleToken is not null)
            mr.UnderlineStyleToken = originalRun.UnderlineStyleToken;
        if (mr.Strikethrough && originalRun?.StrikeStyleToken is not null)
            mr.StrikeStyleToken = originalRun.StrikeStyleToken;

        // Y2: read Foreground LOCAL value only.
        // When unset (inherited), carry the ORIGINAL run's Color (incl. SchemeColor ref) through
        // unchanged so theme-slot references survive a no-op edit session.
        // When set locally, compare the resolved sRGB against the original run's resolved color:
        //   • If they match, carry the original Color object (preserving any SchemeColor ref)
        //     because the user did NOT actually change this run's color.
        //   • If they differ, synthesize a new plain sRGB — the user explicitly picked a new color.
        var localForeground = inline.ReadLocalValue(TextElement.ForegroundProperty);
        if (localForeground != DependencyProperty.UnsetValue &&
            localForeground is SolidColorBrush brush)
        {
            var c           = brush.Color;
            var resolvedSrg = new SrgbColor(c.R, c.G, c.B);

            // If the original run had a Color whose Resolved sRGB equals what the inline has,
            // the user did not change this color — carry the original (which may have a SchemeColor).
            if (originalRun?.Color is not null &&
                originalRun.Color.Resolved == resolvedSrg)
            {
                mr.Color = originalRun.Color;
            }
            else
            {
                // Color actually changed (or there was no original) — synthesize new sRGB.
                mr.Color = new ThemeAwareColor(resolvedSrg);
            }
        }
        else
        {
            // Foreground is inherited — preserve the original run's Color (may be null or a
            // SchemeColor ref such as accent1) rather than synthesizing a new sRGB.
            mr.Color = originalRun?.Color;
        }

        // FlowDocument has no native representation for DrawingML run effects. When the
        // reconstructed inline is paired with an unchanged source run, carry the
        // renderer-neutral effect state through the WPF editing round-trip.
        if (originalRun is not null)
        {
            mr.TextFill = originalRun.TextFill;
            mr.TextOutline = originalRun.TextOutline;
            mr.TextShadow = originalRun.TextShadow;
            mr.TextReflection = originalRun.TextReflection;
            mr.TextGlow = originalRun.TextGlow;
            mr.TextSoftEdge = originalRun.TextSoftEdge;
        }

        for (DependencyObject? parent = inline.Parent;
             parent is not null;
             parent = (parent as FrameworkContentElement)?.Parent)
        {
            if (parent is not WpfHyperlink hyperlink)
                continue;

            var navigateUri = hyperlink.NavigateUri?.OriginalString;
            if (navigateUri?.StartsWith("freep-slide:", StringComparison.OrdinalIgnoreCase) == true)
            {
                mr.Hyperlink = new FreeP.Core.Model.Hyperlink
                {
                    TargetSlideId = Uri.UnescapeDataString(navigateUri["freep-slide:".Length..]),
                    Tooltip = hyperlink.ToolTip as string,
                };
            }
            else if (!string.IsNullOrWhiteSpace(navigateUri) || hyperlink.ToolTip is string)
            {
                mr.Hyperlink = new FreeP.Core.Model.Hyperlink
                {
                    Url = navigateUri,
                    Tooltip = hyperlink.ToolTip as string,
                };
            }
            break;
        }

        return mr;
    }

    private static BitmapImage? LoadBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static long? ToEmu(double value) =>
        double.IsFinite(value) && value > 0 ? (long)Math.Round(value * 9525.0) : null;

    /// <summary>
    /// Resolves a <see cref="ThemeAwareColor"/> to a WPF <see cref="Color"/>.
    /// Only the sRGB channel is used (scheme refs are not available in the editor context).
    /// Returns null if the color is null.
    /// </summary>
    internal static Color? ResolveModelColor(ThemeAwareColor? color)
    {
        if (color is null) return null;
        var s = color.Resolved;
        return Color.FromRgb(s.R, s.G, s.B);
    }
}
