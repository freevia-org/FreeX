using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Free.Shared.Pdf;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using ModelAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private readonly List<BodyTextLine> _bodyTextLines = [];
    private readonly List<TextRunCache> _bodyTextRunCaches = [];
    private sealed record BodyCaretAffinity(int Block, int Offset, int LineStart, int LineEnd);
    private BodyCaretAffinity? _bodyCaretAffinity;
    private BodyCaretAffinity? _pendingBodyHitAffinity;

    private void ValidateBodyCaretAffinity()
    {
        _pendingBodyHitAffinity = null;
        if (_bodyCaretAffinity is { } affinity && (_cellCaret is not null || _hfCaret is not null || _shapeCaret is not null
            || affinity.Block != _caret.Block || affinity.Offset != _caret.Offset))
            _bodyCaretAffinity = null;
    }

    private BodyTextLine? ShapedBodyLineForCaret(int block, int offset)
    {
        var lines = _bodyTextLines.Where(item => item.Block == block).ToArray();
        if (_bodyCaretAffinity is { } affinity && affinity.Block == block && affinity.Offset == offset)
        {
            var preferred = lines.FirstOrDefault(item => item.Line.FirstTextSourceIndex == affinity.LineStart
                && Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length) == affinity.LineEnd
                && offset >= item.Line.FirstTextSourceIndex && offset <= affinity.LineEnd);
            if (preferred is not null) return preferred;
            _bodyCaretAffinity = null; // Reflow changed this source line; do not reuse its affinity.
        }
        return lines.LastOrDefault(item => offset >= item.Line.FirstTextSourceIndex
            && offset <= Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length));
    }

    private BodyCaretAffinity? CaptureBodyCaretAffinity()
    {
        ValidateBodyCaretAffinity();
        if (_laidOutWidth >= 0) _ = ShapedBodyLineForCaret(_caret.Block, _caret.Offset);
        return _bodyCaretAffinity;
    }

    private void SetBodyCaretFromHit(DocPosition position)
    {
        _caret = position;
        _bodyCaretAffinity = _pendingBodyHitAffinity is { } affinity
            && affinity.Block == position.Block && affinity.Offset == position.Offset ? affinity : null;
        _pendingBodyHitAffinity = null;
    }

    private bool TryGetShapedBodyLineEdge(int block, int offset, bool toStart,
        out int edge, out BodyCaretAffinity? affinity)
    {
        edge = 0; affinity = null;
        var item = ShapedBodyLineForCaret(block, offset);
        if (item is null) return false;
        var end = Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length);
        edge = toStart ? item.Line.FirstTextSourceIndex : end;
        while (!toStart && edge > item.Line.FirstTextSourceIndex && IsBodySoftLineBreak(item.Text[edge - 1])) edge--;
        affinity = new(block, edge, item.Line.FirstTextSourceIndex, end);
        return true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearBodyTextLines();
        _laidOutWidth = -1;
        base.OnDetachedFromVisualTree(e);
    }
    private sealed record BodyTextPiece(Run Source, int Start, int End, RunFormatting Formatting, LinkInfo? Link, int? CommentId);
    private readonly record struct BodySoftMetadata(string? Url, string? Anchor, string? Tooltip, int? CommentId);
    private sealed record PreparedBodyLine(TextLine Line, double ExtraInset, double Width, double Height);
    private sealed record BodyParagraphPlan(string Text, int[] Stops, List<BodyTextPiece> Pieces, List<PreparedBodyLine> Lines, TextRunCache ShapeCache);
    private sealed record BodyTextLine(int Block, string Text, int[] Boundaries, TextLine Line,
        IReadOnlyList<BodyTextPiece> Pieces, int AnchorIndex, double AnchorDistance, double AdvanceHeight, double ColumnInset);

    private void ClearBodyTextLines()
    {
        foreach (var item in _bodyTextLines) item.Line.Dispose();
        _bodyTextLines.Clear();
        foreach (var cache in _bodyTextRunCaches) cache.Dispose();
        _bodyTextRunCaches.Clear();
    }

    // This slice replaces the scalar engine for ordinary LTR text that actually requires a
    // multi-unit Unicode text element. ASCII-only layout remains the compatibility baseline.
    // Fields/objects, revision views, RTL, float wrapping, tabs and mixed tracking retain
    // their separate layout paths; this is not a general complex-document/RTL shaping claim.
    internal bool BodyUsesShapedLiteralText(Paragraph paragraph)
    {
        var text = paragraph.PlainText;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (!boundaries.Where((start, index) => (index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length) - start > 1).Any()
            || !HeaderFooterTextServicesAvailable() || paragraph.DropCap is not null || _wrapExclusions.Count > 0)
            return false;
        var pf = ResolveParagraphFmt(paragraph);
        if (pf.Rtl || ContainsUnsupportedBodyDirection(text) || pf.Alignment == ModelAlignment.Justify || _doc.Page.AutoHyphenation && !pf.SuppressAutoHyphens || text.Any(character =>
                character == '\t' || char.IsControl(character) && !IsBodySoftLineBreak(character)))
            return false;
        var index = 0;
        var sourcePosition = 0;
        RunFormatting? previousVisibleFormatting = null;
        BodySoftMetadata? previousVisibleMetadata = null;
        double? uniformTracking = null;
        foreach (var run in paragraph.Runs)
        {
            if (!IsUnicodeLiteralRun(run) || run.Control is not null || run.TableFormula is not null
                || run.CrossReference is not null || run.Citation is not null || run.Ruby is not null
                || run.PreservedDrawing is not null || run.SubDocument is not null
                || run.IsPageBreak || run.IsColumnBreak || run.Revision != RevisionKind.None
                || run.FormatRevision is not null || run.NestedRevision is not null || run.MoveRevisionId is not null)
                return false;
            if (ResolveRunDisplayProjection(paragraph, run, index++, -1, isTableCell: false).DisplayText != run.Text)
                return false;
            if (run.IsCommentReference)
            {
                // Comment commands/imports retain an explicit, textless source marker. Its
                // character style has no glyph, but its raw position must be a legal stop.
                if (run.Text.Length != 0 || run.CommentId is null
                    || run.HyperlinkUrl is not null || run.HyperlinkAnchor is not null || run.HyperlinkTooltip is not null
                    || sourcePosition != text.Length && Array.BinarySearch(boundaries, sourcePosition) < 0)
                    return false;
                continue;
            }
            var formatting = ResolveRunFmt(run, paragraph);
            var metadata = new BodySoftMetadata(run.HyperlinkUrl, run.HyperlinkAnchor, run.HyperlinkTooltip, run.CommentId);
            if (IsTextHiddenInCurrentView(formatting) || formatting.Rtl || formatting.VerticalAlign != VerticalAlign.Baseline
                || RunBaselinePositionPlanner.ResolveOffsetDip(formatting, PxPerPoint) != 0
                || formatting.CharacterBorder is not null || formatting.DoubleStrikethrough)
                return false;
            var tracking = RunCharacterSpacingPlanner.ResolveAdvanceDip(formatting, PxPerPoint);
            if (run.Text.Length > 0)
            {
                if (uniformTracking is { } previousTracking && previousTracking != tracking) return false;
                uniformTracking = tracking;
                if (tracking != 0 && (formatting.Ligatures != LigatureMode.None || formatting.StylisticSet is not null)) return false;
            }
            if (run.Text.Length > 0)
            {
                if (sourcePosition > 0 && Array.BinarySearch(boundaries, sourcePosition) < 0
                    && (previousVisibleFormatting != formatting || previousVisibleMetadata != metadata))
                    return false; // An authored style/semantic change inside one grapheme needs its own policy.
                previousVisibleFormatting = formatting;
                previousVisibleMetadata = metadata;
                sourcePosition += run.Text.Length;
            }
        }
        // No individual source run is required to contain a complete scalar: equal rendering
        // styles are coalesced over the whole backing string, while pieces retain run identity.
        for (var offset = 0; offset < text.Length; offset++)
            if (char.IsHighSurrogate(text[offset]))
            {
                if (++offset >= text.Length || !char.IsLowSurrogate(text[offset])) return false;
            }
            else if (char.IsLowSurrogate(text[offset])) return false;
        if (uniformTracking.GetValueOrDefault() != 0)
        {
            var (_, spans) = BuildBodyTextPieces(paragraph);
            return TryCreateTrackedBodySource(text, spans, uniformTracking!.Value, out _);
        }
        return true;
    }

    private static bool ContainsUnsupportedBodyDirection(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var codepoint = Codepoint.ReadAt(text.AsSpan(), index, out var count);
            if (codepoint.BiDiClass is BidiClass.RightToLeft or BidiClass.ArabicLetter or BidiClass.ArabicNumber
                or BidiClass.FirstStrongIsolate or BidiClass.LeftToRightEmbedding or BidiClass.LeftToRightIsolate
                or BidiClass.LeftToRightOverride or BidiClass.PopDirectionalFormat or BidiClass.PopDirectionalIsolate
                or BidiClass.RightToLeftEmbedding or BidiClass.RightToLeftIsolate or BidiClass.RightToLeftOverride)
                return true;
            index += Math.Max(1, count);
        }
        return false;
    }

    private BodyParagraphPlan FormatShapedBodyParagraph(int block, Paragraph paragraph, double textWidth, double leftInset)
    {
        var text = paragraph.PlainText;
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).Distinct().ToArray();
        var (pieces, spans) = BuildBodyTextPieces(paragraph);
        ITextSource source = new HeaderFooterTextSource(text, spans);
        var tracking = RunCharacterSpacingPlanner.ResolveAdvanceDip(pieces.First(piece => piece.End > piece.Start).Formatting, PxPerPoint);
        if (tracking != 0 && !TryCreateTrackedBodySource(text, spans, tracking, out source))
            throw new InvalidOperationException("The tracked body paragraph changed its shaping capability during layout.");
        var defaults = spans[0].Properties;
        var pf = ResolveBodyParagraphLayoutFormatting(block, paragraph);
        var available = Math.Max(60, textWidth - leftInset - (pf.IndentLeftPt + pf.IndentRightPt) * PxPerPoint);
        var formattingProperties = new GenericTextParagraphProperties(defaults,
            textAlignment: pf.Alignment switch
            {
                ModelAlignment.Center => global::Avalonia.Media.TextAlignment.Center,
                ModelAlignment.Right => global::Avalonia.Media.TextAlignment.Right,
                _ => global::Avalonia.Media.TextAlignment.Left,
            }, textWrapping: TextWrapping.Wrap);
        return FormatBodyTextLines(text, stops, pieces, source, pf, available, formattingProperties);
    }

    private (List<BodyTextPiece> Pieces, List<(int Start, int End, TextRunProperties Properties)> Spans) BuildBodyTextPieces(Paragraph paragraph)
    {
        var pieces = new List<BodyTextPiece>();
        var spans = new List<(int Start, int End, TextRunProperties Properties)>();
        RunFormatting? previous = null;
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var formatting = ResolveRunFmt(run, paragraph);
            var link = BodyHyperlink(run);
            var displayFormatting = BodyDisplayFormatting(formatting, link);
            var end = position + run.Text.Length;
            pieces.Add(new(run, position, end, formatting, link, run.CommentId));
            if (end > position)
            {
                if (previous == displayFormatting && spans.Count > 0)
                    spans[^1] = (spans[^1].Start, end, spans[^1].Properties);
                else
                    spans.Add((position, end, BodyTextProperties(displayFormatting)));
                previous = displayFormatting;
            }
            position = end;
        }
        return (pieces, spans);
    }

    private static LinkInfo? BodyHyperlink(Run run) =>
        run.HyperlinkUrl is { Length: > 0 } || run.HyperlinkAnchor is { Length: > 0 }
            ? new LinkInfo(run.HyperlinkUrl, run.HyperlinkAnchor, run.HyperlinkTooltip) : null;

    // Keep the model/base formatting in placements for caret formatting and subsequent edits.
    // The existing scalar painter applies this same local hyperlink display rule.
    private static RunFormatting BodyDisplayFormatting(RunFormatting formatting, LinkInfo? link) =>
        link is { HasTarget: true } ? formatting with
        {
            ColorHex = string.IsNullOrWhiteSpace(formatting.ColorHex) ? HyperlinkColorHex : formatting.ColorHex,
            Underline = true,
        } : formatting;

    private static BodyTextPiece BodyPieceAtOffset(IReadOnlyList<BodyTextPiece> pieces, int offset) =>
        pieces.FirstOrDefault(piece => offset >= piece.Start && offset < piece.End)
        ?? pieces.LastOrDefault(piece => !piece.Source.IsCommentReference) ?? pieces[^1];

    private static IEnumerable<(int Start, int End, T Value)> BodyMetadataRanges<T>(
        IReadOnlyList<BodyTextPiece> pieces, Func<BodyTextPiece, T?> valueFor) where T : struct
    {
        (int Start, int End, T Value)? pending = null;
        foreach (var piece in pieces)
        {
            if (piece.End <= piece.Start) continue;
            var value = valueFor(piece);
            if (pending is { } previous && value is { } next
                && previous.End == piece.Start && EqualityComparer<T>.Default.Equals(previous.Value, next))
            {
                pending = (previous.Start, piece.End, previous.Value);
                continue;
            }
            if (pending is { } completed) yield return completed;
            pending = value is { } current ? (piece.Start, piece.End, current) : null;
        }
        if (pending is { } last) yield return last;
    }

    private static bool ClipBodyMetadataToLine(BodyTextLine item, ref int start, ref int end)
    {
        var lineStart = Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex);
        var visibleEnd = Math.Max(lineStart, Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length) - item.Line.NewLineLength);
        while (visibleEnd > lineStart && IsBodySoftLineBreak(item.Text[visibleEnd - 1])) visibleEnd--;
        start = Math.Max(start, lineStart);
        end = Math.Min(end, visibleEnd);
        return end > start;
    }

    private BodyParagraphPlan FormatBodyTextLines(string text, int[] stops, List<BodyTextPiece> pieces,
        ITextSource source, ParagraphFormatting pf, double available, TextParagraphProperties formattingProperties)
    {
        var prepared = new List<PreparedBodyLine>();
        // The formatter cache retains its own references to the pre-wrap shaped runs. Keep it
        // beside the line graph until invalidation; subsequent ruler/field formatting must not
        // reuse buffers that the body still paints or queries for caret geometry.
        var shapeCache = new TextRunCache();
        var scale = ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(_doc, pf)
            ? ImportedWordApplicationLineHeightScale : 1;
        try
        {
            var offset = 0;
            TextLine? previousLine = null;
            while (true)
            {
                var extra = prepared.Count == 0 ? Math.Max(0, pf.FirstLineIndentPt * PxPerPoint)
                    : Math.Max(0, -pf.FirstLineIndentPt * PxPerPoint);
                var width = Math.Max(20, available - extra);
                var line = TextFormatter.Current.FormatLine(source, offset, width, formattingProperties, previousLine?.TextLineBreak, shapeCache);
                if (line is null) break;
                if (line.Length <= 0)
                {
                    line.Dispose();
                    throw new InvalidOperationException("The body text formatter did not advance its UTF-16 source.");
                }
                var height = ApplyLineSpacing(Math.Max(DefaultFontSizePt * PxPerPoint * 1.3, line.Height) * scale, pf);
                prepared.Add(new(line, extra, width, height));
                offset += line.Length;
                previousLine = line;
                if (line.TextLineBreak?.TextEndOfLine is TextEndOfParagraph) break;
            }
            return new BodyParagraphPlan(text, stops, pieces, prepared, shapeCache);
        }
        catch
        {
            foreach (var item in prepared) item.Line.Dispose();
            shapeCache.Dispose();
            throw;
        }
    }

    private sealed record TrackedBodyRun(int Start, string Text, GlyphTypeface Typeface, double Size,
        sbyte BidiLevel, TextRunProperties Properties, GlyphInfo[] Glyphs);

    private sealed class TrackedBodyTextSource(ITextSource original, IReadOnlyList<TrackedBodyRun> runs) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            var run = runs.FirstOrDefault(item => textSourceIndex >= item.Start && textSourceIndex < item.Start + item.Text.Length);
            if (run is null) return original.GetTextRun(textSourceIndex);
            var start = textSourceIndex - run.Start;
            var glyphs = run.Glyphs.Where(glyph => glyph.GlyphCluster >= start).ToArray();
            if (glyphs.Length > 0 && glyphs[0].GlyphCluster != start)
                throw new InvalidOperationException("The formatter requested an internal tracked glyph-cluster boundary.");
            var buffer = new ShapedBuffer(run.Text.AsMemory(start), glyphs.Length, run.Typeface, run.Size, run.BidiLevel);
            for (var index = 0; index < glyphs.Length; index++)
                buffer[index] = new GlyphInfo(glyphs[index].GlyphIndex, glyphs[index].GlyphCluster - start,
                    glyphs[index].GlyphAdvance, glyphs[index].GlyphOffset);
            return new ShapedTextRun(buffer, run.Properties);
        }
    }

    // Shape whole spans first, including real font fallback. Tracking changes advance only after a
    // complete text element; it never asks the shaper to shape isolated characters/graphemes.
    private bool TryCreateTrackedBodySource(string text,
        List<(int Start, int End, TextRunProperties Properties)> spans, double tracking, out ITextSource source)
    {
        var original = new HeaderFooterTextSource(text, spans);
        source = original;
        using var untracked = new TextLayout(original, new GenericTextParagraphProperties(spans[0].Properties, textWrapping: TextWrapping.NoWrap));
        var runs = new List<TrackedBodyRun>();
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        foreach (var line in untracked.TextLines)
        {
            var position = line.FirstTextSourceIndex;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped)
                {
                    var glyphs = shaped.ShapedBuffer.ToArray();
                    if (glyphs.Any(glyph => glyph.GlyphAdvance < 0)) return false;
                    var origin = glyphs.Length > 0 ? glyphs[0].GlyphCluster : 0;
                    for (var index = 0; index < glyphs.Length; index++)
                        glyphs[index] = new GlyphInfo(glyphs[index].GlyphIndex, glyphs[index].GlyphCluster - origin,
                            glyphs[index].GlyphAdvance, glyphs[index].GlyphOffset);
                    var clusters = glyphs.Select(glyph => glyph.GlyphCluster).Distinct().Append(run.Length).ToArray();
                    for (var index = 0; index + 1 < clusters.Length; index++)
                        if (stops.Count(stop => stop >= position + clusters[index] && stop < position + clusters[index + 1]) > 1)
                            return false; // A multi-element ligature needs its own authored tracking/caret policy.
                    runs.Add(new(position, shaped.Text.ToString(), shaped.ShapedBuffer.GlyphTypeface,
                        shaped.ShapedBuffer.FontRenderingEmSize, shaped.BidiLevel, shaped.Properties, glyphs));
                }
                position += run.Length;
            }
        }
        for (var element = 0; element + 1 < stops.Length; element++)
        {
            var start = stops[element]; var end = stops[element + 1];
            if (!RunCharacterSpacingPlanner.IsSpacedElement(text[start..end])) continue;
            var glyphs = runs.SelectMany(run => run.Glyphs.Select((glyph, index) => (Run: run, Index: index, Source: run.Start + glyph.GlyphCluster)))
                .Where(glyph => glyph.Source >= start && glyph.Source < end).ToArray();
            if (glyphs.Length == 0) continue;
            var before = glyphs.Select(glyph => glyph.Run.Glyphs[glyph.Index]).ToArray();
            var advances = before.Select(glyph => glyph.GlyphAdvance).ToArray();
            if (tracking >= 0) advances[^1] += tracking;
            else
            {
                var remaining = Math.Min(-tracking, advances.Sum());
                for (var index = advances.Length - 1; index >= 0 && remaining > 0; index--)
                {
                    var removed = Math.Min(remaining, advances[index]);
                    advances[index] -= removed; remaining -= removed;
                }
            }
            double originalPosition = 0, trackedPosition = 0;
            for (var index = 0; index < glyphs.Length; index++)
            {
                var glyph = before[index];
                // Condensing a base must not drag a following zero-advance mark inside the
                // same grapheme. Compensate its offset for any removed preceding advance.
                var offset = new Vector(glyph.GlyphOffset.X + originalPosition - trackedPosition, glyph.GlyphOffset.Y);
                glyphs[index].Run.Glyphs[glyphs[index].Index] = new GlyphInfo(glyph.GlyphIndex, glyph.GlyphCluster, advances[index], offset);
                originalPosition += glyph.GlyphAdvance; trackedPosition += advances[index];
            }
        }
        source = new TrackedBodyTextSource(original, runs);
        return true;
    }

    private bool TryLayoutShapedLiteralBody(int block, Paragraph paragraph, double textWidth, double leftInset, string? marker)
    {
        if (!BodyUsesShapedLiteralText(paragraph)) return false;
        var plan = FormatShapedBodyParagraph(block, paragraph, textWidth, leftInset);
        _bodyTextRunCaches.Add(plan.ShapeCache);
        var text = plan.Text;
        var stops = plan.Stops;
        var pieces = plan.Pieces;
        var prepared = plan.Lines;
        var pf = ResolveBodyParagraphLayoutFormatting(block, paragraph);
        try
        {
            _layoutContentY += pf.SpaceBeforePt * PxPerPoint;
            var keep = DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(pf);
            if (keep || pf.KeepWithNext)
            {
                var follow = pf.KeepWithNext ? MeasureKeepWithNextFollowOnHeight(block, textWidth) : 0;
                ReserveCompleteParagraph(prepared.Sum(item => item.Height) + (pf.KeepWithNext ? pf.SpaceAfterPt * PxPerPoint + follow : 0));
            }
            var widowBreak = !keep && !pf.KeepWithNext && DocumentParagraphPaginationPlanner.IsWidowControlEffective(pf)
                ? PlanWidowOrphanBreak(prepared.Select(item => item.Height).ToArray()) : -1;
            for (var lineIndex = 0; lineIndex < prepared.Count; lineIndex++)
            {
                var item = prepared[lineIndex];
                BreakForWidowOrphanPlan(lineIndex, widowBreak);
                var contentY = ReserveContentY(item.Height);
                var y = ContentYToPageSpaceY(contentY);
                var x = ColumnLeftFor(contentY) + leftInset + pf.IndentLeftPt * PxPerPoint + item.ExtraInset;
                var line = item.Line;
                var start = Math.Min(text.Length, line.FirstTextSourceIndex);
                var end = Math.Min(text.Length, line.FirstTextSourceIndex + line.Length);
                var visibleEnd = Math.Max(start, end - line.NewLineLength);
                // Explicit source controls may be reported through TextLineBreak rather than
                // NewLineLength. They own raw offsets, but never a drawable glyph or extra width.
                while (visibleEnd > start && IsBodySoftLineBreak(text[visibleEnd - 1])) visibleEnd--;
                var anchorIndex = _placed.Count;
                var startDistance = BodyInsertionDistance(line, stops, start);
                var firstStop = Array.BinarySearch(stops, start);
                firstStop = firstStop >= 0 ? firstStop : ~firstStop;
                for (var stopIndex = firstStop; stopIndex < stops.Length && stops[stopIndex] <= visibleEnd; stopIndex++)
                {
                    var stop = stops[stopIndex];
                    var next = stopIndex + 1 < stops.Length ? stops[stopIndex + 1] : text.Length;
                    var length = Math.Max(0, Math.Min(visibleEnd, next) - stop);
                    var distance = BodyInsertionDistance(line, stops, stop);
                    var advance = length > 0 ? BodyInsertionDistance(line, stops, stop + length) - distance : 0;
                    var piece = BodyPieceAtOffset(pieces, stop);
                    _placed.Add(new PlacedChar(block, stop, x + distance, y, Math.Max(0, advance), item.Height,
                        piece.Formatting, length > 0 ? text[stop] : '\0', Sentinel: length == 0,
                        CommentId: length > 0 ? piece.CommentId : null, Link: length > 0 ? piece.Link : null,
                        ShapedText: text.Substring(stop, length)));
                }
                if (_placed.Count == anchorIndex)
                    _placed.Add(new PlacedChar(block, start, x + startDistance, y, 0, item.Height,
                        BodyPieceAtOffset(pieces, start).Formatting, '\0', Sentinel: true, ShapedText: ""));
                _bodyTextLines.Add(new(block, text, stops, line, pieces, anchorIndex, startDistance, item.Height,
                    leftInset + pf.IndentLeftPt * PxPerPoint + item.ExtraInset));
                if (!string.IsNullOrWhiteSpace(pf.ShadingColorHex) || pf.Border is not null)
                    _paragraphDecorations.Add((new Rect(x - 2, y, item.Width + 4, item.Height), pf.ShadingColorHex, pf.Border));
                if (lineIndex == 0 && marker is not null)
                {
                    var fmt = pieces.First(piece => !piece.Source.IsCommentReference).Formatting;
                    var markerWidth = Build(marker, fmt).WidthIncludingTrailingWhitespace;
                    var markerOffset = DocumentBodyParagraphLayoutPlanner.MarkerOffsetFromTextPt(
                        ResolveParagraphFmt(paragraph), markerWidth / PxPerPoint, 6 / PxPerPoint) * PxPerPoint;
                    _markers.Add((x + markerOffset, y, marker, fmt));
                }
                _layoutContentY = contentY + item.Height;
            }
            AdvanceAfterParagraphSpacing(pf.SpaceAfterPt * PxPerPoint);
            return true;
        }
        catch
        {
            // Do not leave the formatter's shaped buffers alive on a failed layout.
            foreach (var item in prepared)
                if (!_bodyTextLines.Any(owned => ReferenceEquals(owned.Line, item.Line))) item.Line.Dispose();
            throw;
        }
    }

    private bool TryMeasureShapedBodyFollowOn(int block, double textWidth, out double height)
    {
        height = 0;
        if (block + 1 >= _doc.Blocks.Count || _doc.Blocks[block + 1] is not Paragraph paragraph
            || !BodyUsesShapedLiteralText(paragraph)) return false;
        var plan = FormatShapedBodyParagraph(block + 1, paragraph, textWidth, 0);
        try
        {
            var pf = ResolveBodyParagraphLayoutFormatting(block + 1, paragraph);
            var count = DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(pf) ? plan.Lines.Count
                : DocumentParagraphPaginationPlanner.IsWidowControlEffective(pf)
                    ? plan.Lines.Count <= DocumentParagraphPaginationPlanner.WordWidowOrphanMinimumLines + 1
                        ? plan.Lines.Count : DocumentParagraphPaginationPlanner.WordWidowOrphanMinimumLines
                    : 1;
            height = pf.SpaceBeforePt * PxPerPoint + plan.Lines.Take(count).Sum(item => item.Height);
            return true;
        }
        finally
        {
            foreach (var item in plan.Lines) item.Line.Dispose();
            plan.ShapeCache.Dispose();
        }
    }

    private GenericTextRunProperties BodyTextProperties(RunFormatting formatting)
    {
        var original = HeaderFooterRunProperties(formatting);
        var decorations = new TextDecorationCollection();
        if (formatting.Underline) foreach (var decoration in TextDecorations.Underline) decorations.Add(decoration);
        if (formatting.Strikethrough) foreach (var decoration in TextDecorations.Strikethrough) decorations.Add(decoration);
        return new GenericTextRunProperties(original.Typeface, original.FontRenderingEmSize,
            textDecorations: decorations.Count > 0 ? decorations : null,
            foregroundBrush: original.ForegroundBrush, cultureInfo: original.CultureInfo, fontFeatures: original.FontFeatures);
    }

    private static double BodyInsertionDistance(TextLine line, int[] boundaries, int offset)
    {
        var hit = new CharacterHit(offset);
        if (offset > line.FirstTextSourceIndex)
        {
            var index = Array.BinarySearch(boundaries, offset - 1);
            index = index >= 0 ? index : ~index - 1;
            var start = boundaries[Math.Max(0, index)];
            hit = new CharacterHit(start, offset - start);
        }
        return line.GetDistanceFromCharacterHit(hit);
    }

    private Point BodyTextOrigin(BodyTextLine item)
    {
        // Existing pagination, vertical alignment and view-depth projection move the source
        // placements. Follow that exact anchor rather than maintaining a second page transform.
        var anchor = _placed[item.AnchorIndex];
        return new Point(anchor.X - item.AnchorDistance, anchor.Y);
    }

    private void DrawShapedBodyText(DrawingContext context)
    {
        var selection = NormalizedSelection();
        foreach (var item in _bodyTextLines)
        {
            var origin = BodyTextOrigin(item);
            foreach (var piece in item.Pieces)
            {
                var background = RunDecorationVisualPlanner.Build(piece.Formatting, PxPerPoint).BackgroundColorHex;
                var start = Math.Max(piece.Start, item.Line.FirstTextSourceIndex);
                var end = Math.Min(piece.End, Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length));
                if (background is null || end <= start) continue;
                foreach (var bounds in item.Line.GetTextBounds(start, end - start))
                    context.FillRectangle(BrushFor(background), bounds.Rectangle.Translate(new Vector(origin.X, origin.Y)));
            }
            if (selection is { } range && item.Block >= range.Start.Block && item.Block <= range.End.Block)
            {
                var start = Math.Max(item.Line.FirstTextSourceIndex, item.Block == range.Start.Block ? range.Start.Offset : 0);
                var end = Math.Min(Math.Min(item.Text.Length, item.Line.FirstTextSourceIndex + item.Line.Length),
                    item.Block == range.End.Block ? range.End.Offset : item.Text.Length);
                if (end > start)
                    foreach (var bounds in item.Line.GetTextBounds(start, end - start))
                        if (bounds.Rectangle.Width > 0) context.FillRectangle(SelectionBrush, bounds.Rectangle.Translate(new Vector(origin.X, origin.Y)));
            }
            if (CurrentReviewDisplayPolicy.ShouldHighlightComments)
                foreach (var commentRange in BodyMetadataRanges<int>(item.Pieces, static piece => piece.CommentId))
                {
                    var start = commentRange.Start; var end = commentRange.End;
                    if (!ClipBodyMetadataToLine(item, ref start, ref end)) continue;
                    var left = BodyInsertionDistance(item.Line, item.Boundaries, start);
                    var right = BodyInsertionDistance(item.Line, item.Boundaries, end);
                    if (right <= left) continue;
                    var tint = IsCommentResolved(commentRange.Value) ? ResolvedCommentTintBrush : CommentTintBrush;
                    context.FillRectangle(tint, new Rect(origin.X + left, origin.Y, right - left, item.AdvanceHeight));
                }
            item.Line.Draw(context, origin);
        }
        var proofing = BuildProofingOffsetSet();
        foreach (var placed in _placed)
        {
            if (placed.ShapedText is not { Length: > 0 } text) continue;
            for (var index = placed.Offset; index < placed.Offset + text.Length; index++)
                if (proofing.TryGetValue((placed.Block, index), out var kind))
                {
                    if (kind == ProofingDiagnosticKind.Grammar) DrawGrammarUnderline(context, placed);
                    else DrawProofingSquiggle(context, placed);
                    break;
                }
        }
    }

    private bool TryGetShapedBodyCaret(out Rect rect)
    {
        rect = default;
        if (_cellCaret is not null || _hfCaret is not null || _shapeCaret is not null) return false;
        var item = ShapedBodyLineForCaret(_caret.Block, _caret.Offset);
        if (item is null) return false;
        var origin = BodyTextOrigin(item);
        rect = new Rect(origin.X + BodyInsertionDistance(item.Line, item.Boundaries, _caret.Offset),
            origin.Y, 1.5, item.Line.Height);
        return true;
    }

    private bool TryGetShapedBodyVerticalPoint(int direction, Rect caret, out Point point)
    {
        point = default;
        if (_cellCaret is not null || _hfCaret is not null || _shapeCaret is not null) return false;
        var current = ShapedBodyLineForCaret(_caret.Block, _caret.Offset);
        if (current is null) return false;
        var currentOrigin = BodyTextOrigin(current);
        var index = _bodyTextLines.IndexOf(current) + Math.Sign(direction);
        if (index >= 0 && index < _bodyTextLines.Count && _bodyTextLines[index].Block == current.Block)
        {
            var adjacent = _bodyTextLines[index];
            var origin = BodyTextOrigin(adjacent);
            var columnDelta = origin.X - adjacent.ColumnInset - (currentOrigin.X - current.ColumnInset);
            point = new Point(caret.X + columnDelta, origin.Y + adjacent.AdvanceHeight / 2);
        }
        else
            point = new Point(caret.X, currentOrigin.Y + (direction > 0
                ? current.AdvanceHeight + caret.Height / 2 : -caret.Height / 2));
        return true;
    }

    private bool TryHitShapedBodyText(Point point, out DocPosition position)
    {
        position = _caret;
        BodyTextLine? best = null;
        var bestScore = double.PositiveInfinity;
        foreach (var item in _bodyTextLines)
        {
            var origin = BodyTextOrigin(item);
            var page = RenderedPageIndexForPoint(origin.X, origin.Y);
            if (_surfacePlan.UsesProjectedPageFlow && RenderedPageIndexForPoint(point.X, point.Y) != page) continue;
            if (ColumnIndexForRenderedX(point.X, page) != ColumnIndexForRenderedX(origin.X, page)) continue;
            var dy = Math.Abs(point.Y - origin.Y - item.AdvanceHeight / 2);
            var dx = point.X < origin.X ? origin.X - point.X : Math.Max(0, point.X - origin.X - item.Line.WidthIncludingTrailingWhitespace);
            var score = dy * 1000 + dx;
            if (score < bestScore) { best = item; bestScore = score; }
        }
        if (best is null) return false;
        // A nearby conventional paragraph/cell still owns its own pointer band.
        var bestOrigin = BodyTextOrigin(best);
        if (point.Y < bestOrigin.Y || point.Y > bestOrigin.Y + best.AdvanceHeight) return false;
        var hit = best.Line.GetCharacterHitFromDistance(point.X - bestOrigin.X);
        var offset = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, best.Text.Length);
        var boundary = Array.BinarySearch(best.Boundaries, offset);
        if (boundary < 0) offset = best.Boundaries[Math.Max(0, ~boundary - 1)];
        position = new DocPosition(best.Block, offset);
        _pendingBodyHitAffinity = new(best.Block, offset, best.Line.FirstTextSourceIndex,
            Math.Min(best.Text.Length, best.Line.FirstTextSourceIndex + best.Line.Length));
        _cellCaret = null;
        return true;
    }

    private sealed record BodyPdfSpan(int Start, string Text, double X, double Width, string? FontFamily);
    private IEnumerable<BodyPdfSpan> BodyPdfSpans(BodyTextLine item)
    {
        if (item.Pieces.Any(piece => piece.End > piece.Start && RunCharacterSpacingPlanner.ResolveAdvanceDip(piece.Formatting, PxPerPoint) != 0))
        {
            var first = item.Line.FirstTextSourceIndex;
            var end = Math.Min(item.Text.Length, first + item.Line.Length);
            for (var index = 0; index + 1 < item.Boundaries.Length; index++)
            {
                var start = item.Boundaries[index]; var next = item.Boundaries[index + 1];
                if (start < first || next > end) continue;
                var text = item.Text[start..next];
                if (text.All(IsBodySoftLineBreak)) continue;
                var x = BodyInsertionDistance(item.Line, item.Boundaries, start);
                var width = Math.Max(0, BodyInsertionDistance(item.Line, item.Boundaries, next) - x);
                var source = first;
                string? family = null;
                foreach (var run in item.Line.TextRuns)
                {
                    if (run is ShapedTextRun shaped && start >= source && start < source + run.Length)
                    { family = shaped.ShapedBuffer.GlyphTypeface.FamilyName; break; }
                    source += run.Length;
                }
                yield return new(start, text, x, width, family);
            }
            yield break;
        }
        var position = item.Line.FirstTextSourceIndex;
        var distance = item.Line.Start;
        foreach (var run in item.Line.TextRuns)
        {
            if (run is ShapedTextRun shaped)
            {
                var formatting = item.Pieces.FirstOrDefault(piece => position >= piece.Start && position < piece.End)?.Formatting ?? item.Pieces[^1].Formatting;
                yield return new(position, shaped.Text.ToString(), distance, shaped.Size.Width, formatting.FontFamily);
                distance += shaped.Size.Width;
            }
            position += run.Length;
        }
    }

    private void AppendShapedBodyPdfText(List<List<PdfDrawOp>> pages, List<List<PdfLinkOverlay>> pageLinkOverlays, double pageHeightPt)
    {
        foreach (var item in _bodyTextLines)
        {
            var origin = BodyTextOrigin(item);
            var page = PageIndexFromPageSpaceY(origin.Y + 0.01);
            if (page < 0) continue;
            while (pages.Count <= page) pages.Add([]);
            foreach (var span in BodyPdfSpans(item))
            {
                var x = origin.X + span.X;
                var sourceOffset = span.Start;
                var text = span.Text;
                var piece = BodyPieceAtOffset(item.Pieces, sourceOffset);
                var formatting = BodyDisplayFormatting(piece.Formatting, piece.Link);
                var size = formatting.FontSizePt ?? DefaultFontSizePt;
                var face = (formatting.Bold, formatting.Italic) switch
                {
                    (true, true) => PdfFontFace.BoldItalic, (true, false) => PdfFontFace.Bold,
                    (false, true) => PdfFontFace.Italic, _ => PdfFontFace.Regular,
                };
                // Preserve complete UTF-16 text and shaped placement in the adapter. The
                // shared PDF writer still owns font fallback/complex shaping as a separate slice.
                var xPt = Math.Max(0, (x - _contentLeft) / PxPerPoint + _doc.Page.MarginLeftPt);
                var yPt = pageHeightPt - (origin.Y - _surfacePlan.PageTopDip(page)) / PxPerPoint;
                var widthPt = span.Width / PxPerPoint;
                var decoration = RunDecorationVisualPlanner.Build(formatting, PxPerPoint);
                if (decoration.BackgroundColorHex is { } background)
                    pages[page].Add(new PdfFillRect(xPt, yPt - item.Line.Height / PxPerPoint,
                        widthPt, item.Line.Height / PxPerPoint, ParseColor(background)));
                pages[page].Add(new PdfText(xPt,
                    pageHeightPt - (origin.Y - _surfacePlan.PageTopDip(page)) / PxPerPoint - size,
                    size, face, ParseColor(formatting.ColorHex), text, span.FontFamily));
                if (formatting.Underline)
                    pages[page].Add(new PdfLine(xPt, yPt - item.AdvanceHeight * 0.82 / PxPerPoint,
                        xPt + widthPt, yPt - item.AdvanceHeight * 0.82 / PxPerPoint, ParseColor(formatting.ColorHex), Math.Max(0.5, size / 14)));
                if (formatting.Strikethrough)
                    pages[page].Add(new PdfLine(xPt, yPt - item.AdvanceHeight * 0.5 / PxPerPoint,
                        xPt + widthPt, yPt - item.AdvanceHeight * 0.5 / PxPerPoint, ParseColor(formatting.ColorHex), Math.Max(0.5, size / 14)));
            }
            // Shaping can coalesce adjacent links with identical display formatting. Their
            // semantic targets/ScreenTips remain independent of those painted font spans.
            foreach (var range in BodyMetadataRanges<LinkInfo>(item.Pieces, static piece => piece.Link))
            {
                var start = range.Start; var end = range.End;
                if (!ClipBodyMetadataToLine(item, ref start, ref end)) continue;
                var left = BodyInsertionDistance(item.Line, item.Boundaries, start);
                var right = BodyInsertionDistance(item.Line, item.Boundaries, end);
                if (right <= left) continue;
                while (pageLinkOverlays.Count <= page) pageLinkOverlays.Add([]);
                var link = range.Value;
                pageLinkOverlays[page].Add(new PdfLinkOverlay(
                    Math.Max(0, (origin.X + left - _contentLeft) / PxPerPoint + _doc.Page.MarginLeftPt),
                    Math.Max(0, (origin.Y - _surfacePlan.PageTopDip(page)) / PxPerPoint),
                    (right - left) / PxPerPoint, item.AdvanceHeight / PxPerPoint,
                    link.IsExternal ? link.Url : null, link.Tooltip,
                    link.IsInternal ? link.Anchor?.TrimStart('#').Trim() : null));
            }
        }
    }
}
