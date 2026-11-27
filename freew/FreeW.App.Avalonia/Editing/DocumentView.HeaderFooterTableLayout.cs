using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using ModelAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private sealed record HfTableSurface(Rect Bounds, DocumentTableCellEffectiveFillPlan Fill,
        TableCellBorderVisualPlan Borders, int PageIndex);

    private readonly List<HfTableSurface> _headerFooterTableSurfaces = [];
    private readonly Dictionary<HfRenderItem, Rect> _headerFooterTableClips = [];
    private readonly Dictionary<HfRenderItem, HfTableParagraphLayout> _headerFooterTableParagraphs = [];

    /// <summary>A shaped source paragraph with authored, independently positioned line advances.</summary>
    private sealed class HfTableParagraphLayout(TextLayout layout, Paragraph? paragraph, string sourceText,
        IReadOnlyList<double> lineTops, IReadOnlyList<double> lineAdvances, double emptyAdvance, double emptyX, bool tableBold) : IDisposable
    {
        public TextLayout Layout { get; } = layout;
        public Paragraph? Paragraph { get; } = paragraph;
        public bool TableBold { get; } = tableBold;
        public string SourceText { get; } = sourceText;
        public IReadOnlyList<double> LineTops { get; } = lineTops;
        public IReadOnlyList<double> LineAdvances { get; } = lineAdvances;
        public double Height => LineAdvances.Count == 0 ? emptyAdvance : LineTops[^1] + LineAdvances[^1];

        public void Draw(DrawingContext context, Point origin)
        {
            for (var index = 0; index < Layout.TextLines.Count; index++)
                Layout.TextLines[index].Draw(context, new Point(origin.X, origin.Y + LineTops[index]));
        }

        public Rect InsertionBounds(int offset)
        {
            offset = HeaderFooterLayoutBoundary(SourceText, offset);
            var boundaries = StringInfo.ParseCombiningCharacters(SourceText);
            for (var index = 0; index < Layout.TextLines.Count; index++)
            {
                var line = Layout.TextLines[index];
                var end = Math.Min(SourceText.Length, line.FirstTextSourceIndex + line.Length);
                if (offset >= end && index + 1 < Layout.TextLines.Count)
                    continue;
                var hit = new CharacterHit(offset);
                if (offset > line.FirstTextSourceIndex)
                {
                    var previous = Array.BinarySearch(boundaries, offset - 1);
                    previous = previous >= 0 ? previous : ~previous - 1;
                    var start = boundaries[Math.Max(0, previous)];
                    hit = new CharacterHit(start, offset - start);
                }
                return new Rect(line.GetDistanceFromCharacterHit(hit), LineTops[index], 0, LineAdvances[index]);
            }
            return new Rect(emptyX, 0, 0, Height);
        }

        public int HitTest(Point point)
        {
            for (var index = 0; index < Layout.TextLines.Count; index++)
            {
                if (point.Y >= LineTops[index] + LineAdvances[index] && index + 1 < Layout.TextLines.Count)
                    continue;
                var hit = Layout.TextLines[index].GetCharacterHitFromDistance(point.X);
                return HeaderFooterLayoutBoundary(SourceText, hit.FirstCharacterIndex + hit.TrailingLength);
            }
            return 0;
        }

        public IEnumerable<Rect> SelectionBounds(int start, int end)
        {
            start = HeaderFooterLayoutBoundary(SourceText, start);
            end = HeaderFooterLayoutBoundary(SourceText, end);
            for (var index = 0; index < Layout.TextLines.Count; index++)
            {
                var line = Layout.TextLines[index];
                var overlapStart = Math.Max(start, line.FirstTextSourceIndex);
                var overlapEnd = Math.Min(end, Math.Min(SourceText.Length, line.FirstTextSourceIndex + line.Length));
                if (overlapEnd <= overlapStart)
                    continue;
                foreach (var bounds in line.GetTextBounds(overlapStart, overlapEnd - overlapStart))
                    yield return new Rect(bounds.Rectangle.X, LineTops[index], bounds.Rectangle.Width, LineAdvances[index]);
            }
        }

        public void Dispose() => Layout.Dispose();
    }

    private HfTableParagraphLayout BuildHeaderFooterTableParagraphLayout(Paragraph paragraph, double width, bool wrap, bool tableBold)
    {
        var spans = new List<(int Start, int End, TextRunProperties Properties)>();
        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            spans.Add((offset, offset + run.Text.Length, HeaderFooterRunProperties(ResolveHeaderFooterTableRunFmt(run, paragraph, tableBold))));
            offset += run.Text.Length;
        }
        var defaults = spans.Count > 0 ? spans[0].Properties
            : HeaderFooterRunProperties(ResolveRunFmt(RunFormatting.Default, paragraph, null));
        var formatting = ResolveParagraphFmt(paragraph);
        var properties = new GenericTextParagraphProperties(defaults,
            textAlignment: formatting.Alignment switch
            {
                ModelAlignment.Center => global::Avalonia.Media.TextAlignment.Center,
                ModelAlignment.Right => global::Avalonia.Media.TextAlignment.Right,
                _ => global::Avalonia.Media.TextAlignment.Left,
            }, textWrapping: wrap ? TextWrapping.Wrap : TextWrapping.NoWrap);
        var layout = new TextLayout(new HeaderFooterTextSource(paragraph.PlainText, spans), properties,
            maxWidth: Math.Max(1, width));
        var fallback = paragraph.Runs.Count > 0 ? ResolveHeaderFooterTableRunFmt(paragraph.Runs[0], paragraph, tableBold)
            : ResolveRunFmt(RunFormatting.Default, paragraph, null);
        return PositionHeaderFooterTableLines(layout, paragraph, paragraph.PlainText, formatting, fallback,
            AlignmentOffset(formatting.Alignment, width, 0, isLast: true), tableBold);
    }

    private HfTableParagraphLayout BuildHeaderFooterTableSegmentLayout(HfRenderItem item, ParagraphFormatting formatting)
    {
        var properties = HeaderFooterRunProperties(item.Fmt);
        var layout = new TextLayout(new HeaderFooterTextSource(item.Text, [(0, item.Text.Length, properties)]),
            new GenericTextParagraphProperties(properties, textWrapping: TextWrapping.NoWrap),
            maxWidth: double.PositiveInfinity);
        // Legacy segments already carry their tab/group X. A single right/center-aligned segment
        // retains the established block alignment while its source lines receive authored leading.
        var x = AlignmentOffset(item.Alignment, item.AvailableWidth, layout.WidthIncludingTrailingWhitespace, isLast: true);
        item.X += x;
        item.Alignment = ModelAlignment.Left;
        return PositionHeaderFooterTableLines(layout, null, item.Text, formatting, item.Fmt, 0, false);
    }

    private HfTableParagraphLayout PositionHeaderFooterTableLines(TextLayout layout, Paragraph? paragraph,
        string source, ParagraphFormatting formatting, RunFormatting fallback, double emptyX, bool tableBold)
    {
        var tops = new List<double>();
        var advances = new List<double>();
        var y = 0d;
        foreach (var line in layout.TextLines)
        {
            tops.Add(y);
            var advance = ApplyLineSpacing(line.Height, formatting);
            advances.Add(advance);
            y += advance;
        }
        return new HfTableParagraphLayout(layout, paragraph, source, tops, advances,
            ApplyLineSpacing(Build("Ag", fallback).Height, formatting), emptyX, tableBold);
    }

    private RunFormatting ResolveHeaderFooterTableRunFmt(Run run, Paragraph paragraph, bool tableBold)
    {
        var format = ResolveRunFmt(run, paragraph);
        return tableBold && (format.ExplicitlyOff & RunToggleOverrides.Bold) == 0
            ? format with { Bold = true } : format;
    }

    private void ClearHeaderFooterTableLayout()
    {
        foreach (var paragraph in _headerFooterTableParagraphs.Values)
            paragraph.Dispose();
        _headerFooterTableParagraphs.Clear();
        _headerFooterTableClips.Clear();
        _headerFooterTableSurfaces.Clear();
    }

    private double EmitHeaderFooterTableLayout(HeaderFooter story, Table table, double startY, double totalWidth,
        string pageNumberText, int pageCount, Func<int, HfTarget?>? targetFactory, HeaderFooterSlotKind slot,
        int pageNumber, int sectionOrdinal, int sectionRelativePageNumber, int sectionPageCount)
    {
        var savedLeft = _contentLeft;
        var preliminary = HeaderFooterTableLayoutPlanner.Build(table, totalWidth, _doc.UsesLegacyCompatibilityLayout);
        var measurements = new List<HeaderFooterTableCellMeasurement>();
        var ranges = new Dictionary<(int Row, int Cell), (int First, int End)>();
        try
        {
            foreach (var cell in preliminary.Cells)
            {
                _contentLeft = savedLeft + cell.ContentBounds.XDip;
                var first = _headerFooterItems.Count;
                var y = 0d;
                for (var paragraphIndex = 0; paragraphIndex < cell.SourceCell.Paragraphs.Count; paragraphIndex++)
                {
                    var paragraph = cell.SourceCell.Paragraphs[paragraphIndex];
                    var pf = ResolveParagraphFmt(paragraph);
                    y += Math.Max(0, pf.SpaceBeforePt) * PxPerPoint;
                    var flatIndex = HeaderFooterTableParagraphMap.ResolveParagraphIndex(story,
                        new HeaderFooterTableParagraphAddress(cell.RowIndex, cell.CellIndex, paragraphIndex));
                    var target = targetFactory is not null && flatIndex >= 0 ? targetFactory(flatIndex) : null;
                    if (HeaderFooterUsesSharedLiteralLayout(paragraph))
                    {
                        var layout = BuildHeaderFooterTableParagraphLayout(paragraph,
                            cell.ContentBounds.WidthDip, cell.SourceCell.WrapText, cell.Fill.EffectiveBold);
                        var formatting = paragraph.Runs.Count > 0 ? ResolveRunFmt(paragraph.Runs[0], paragraph)
                            : ResolveRunFmt(RunFormatting.Default, paragraph, null);
                        var item = new HfRenderItem
                        {
                            Text = paragraph.PlainText, Fmt = formatting, Slot = slot,
                            X = _contentLeft, Y = y, AvailableWidth = cell.ContentBounds.WidthDip,
                            Alignment = pf.Alignment, Target = target, OwnerTarget = target,
                            LineHeight = layout.Height, Height = layout.Height,
                            Width = layout.Layout.WidthIncludingTrailingWhitespace, ModelStartOffset = 0,
                        };
                        _headerFooterItems.Add(item);
                        _headerFooterTableParagraphs.Add(item, layout);
                        y += layout.Height;
                    }
                    else
                    {
                        // Keep fields, tabs, images and their atomic/source mapping on the established
                        // emitter. Only this cell's paragraph reservation uses actual font/line metrics.
                        var adapter = new HeaderFooter();
                        adapter.Paragraphs.Add(paragraph);
                        var before = _headerFooterItems.Count;
                        var end = EmitHfParagraphs(adapter, y, cell.ContentBounds.WidthDip, pageNumberText,
                            pageCount, _ => target, slot, pageNumber, sectionOrdinal,
                            sectionRelativePageNumber, sectionPageCount, cell.Fill.EffectiveBold);
                        var natural = 0d;
                        for (var index = before; index < _headerFooterItems.Count; index++)
                        {
                            var item = _headerFooterItems[index];
                            if (item.Image is not null || item.EmbeddedObject is not null)
                                natural = Math.Max(natural, item.Y - y + item.Height);
                            else if (HeaderFooterContainsSoftBreak(item.Text))
                            {
                                var textLayout = BuildHeaderFooterTableSegmentLayout(item, pf);
                                _headerFooterTableParagraphs.Add(item, textLayout);
                                natural = Math.Max(natural, item.Y - y + textLayout.Height);
                            }
                            else
                                natural = Math.Max(natural, item.Y - y + ApplyLineSpacing(Build("Ag", item.Fmt).Height, pf));
                        }
                        var advance = natural > 0 ? natural : Math.Max(0, end - y);
                        for (var index = before; index < _headerFooterItems.Count; index++)
                            _headerFooterItems[index].LineHeight = advance;
                        y += advance;
                    }
                    y += Math.Max(0, pf.SpaceAfterPt) * PxPerPoint;
                }
                ranges.Add((cell.RowIndex, cell.CellIndex), (first, _headerFooterItems.Count));
                measurements.Add(new(cell.RowIndex, cell.CellIndex, y));
            }

            var plan = HeaderFooterTableLayoutPlanner.Build(table, totalWidth, _doc.UsesLegacyCompatibilityLayout, measurements);
            foreach (var cell in plan.Cells)
            {
                var bounds = new Rect(savedLeft + cell.Bounds.XDip, startY + cell.Bounds.YDip,
                    cell.Bounds.WidthDip, cell.Bounds.HeightDip);
                _headerFooterTableSurfaces.Add(new(bounds, cell.Fill, cell.Borders, pageNumber - 1));
                var range = ranges[(cell.RowIndex, cell.CellIndex)];
                for (var index = range.First; index < range.End; index++)
                {
                    var item = _headerFooterItems[index];
                    item.Y += startY + cell.ContentBounds.YDip + cell.ContentOffsetDip;
                    _headerFooterTableClips.Add(item, bounds);
                }
            }
            return startY + plan.HeightDip;
        }
        finally
        {
            _contentLeft = savedLeft;
        }
    }

    private void ShiftHeaderFooterTableLayout(int firstSurface, int firstItem, double dy)
    {
        for (var index = firstSurface; index < _headerFooterTableSurfaces.Count; index++)
        {
            var item = _headerFooterTableSurfaces[index];
            _headerFooterTableSurfaces[index] = item with { Bounds = item.Bounds.Translate(new Vector(0, dy)) };
        }
        foreach (var item in _headerFooterItems.Skip(firstItem))
            if (_headerFooterTableClips.TryGetValue(item, out var clip))
                _headerFooterTableClips[item] = clip.Translate(new Vector(0, dy));
    }

    private void ProjectHeaderFooterTableLayout(Func<double, double, Point> move)
    {
        Rect Move(Rect rect)
        {
            var point = move(rect.X, rect.Y);
            return new Rect(point, rect.Size);
        }
        for (var index = 0; index < _headerFooterTableSurfaces.Count; index++)
        {
            var item = _headerFooterTableSurfaces[index];
            _headerFooterTableSurfaces[index] = item with { Bounds = Move(item.Bounds) };
        }
        foreach (var item in _headerFooterTableClips.Keys.ToArray())
            _headerFooterTableClips[item] = Move(_headerFooterTableClips[item]);
    }

    private void DrawHeaderFooterTableSurfaces(DrawingContext context)
    {
        foreach (var surface in _headerFooterTableSurfaces)
        {
            if (surface.Fill.EffectiveFillHex is { Length: > 0 } fill)
                context.FillRectangle(BrushFor(fill), surface.Bounds);
            DrawCellBorderEdges(context, surface.Bounds, surface.Borders);
        }
    }

    private IDisposable? PushHeaderFooterTableClip(DrawingContext context, HfRenderItem item) =>
        _headerFooterTableClips.TryGetValue(item, out var clip) ? context.PushClip(clip) : null;

    private bool DrawHeaderFooterTableParagraph(DrawingContext context, HfRenderItem item)
    {
        if (!_headerFooterTableParagraphs.TryGetValue(item, out var layout))
            return false;
        if (item.Control is not null)
            context.FillRectangle(ContentControlShadeBrush, new Rect(item.X, item.Y,
                Math.Max(1, layout.Layout.WidthIncludingTrailingWhitespace), Math.Max(1, layout.Height)));
        layout.Draw(context, new Point(item.X, item.Y + RunBaselinePositionPlanner.ResolveOffsetDip(item.Fmt, PxPerPoint)));
        return true;
    }
}
