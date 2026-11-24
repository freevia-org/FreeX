using System.Text;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    internal sealed record RunDisplayToken(
        Run SourceRun,
        int SourceRunIndex,
        int DisplayStart,
        string Text,
        RunFormatting Formatting,
        EquationVisualElement? EquationElement = null)
    {
        public int DisplayLength => Text.Length;
    }

    internal sealed record RunDisplayProjection(
        Run SourceRun,
        int SourceRunIndex,
        RunFormatting Formatting,
        string DisplayText,
        bool IsAtomic,
        bool IsHidden,
        IReadOnlyList<int>? DisplayToModelBoundaries,
        IReadOnlyList<RunDisplayToken> Tokens,
        EquationVisualPlan? EquationPlan = null);

    /// <summary>
    /// Resolves one source run without changing caches or capturing render diagnostics. Display offsets
    /// are run-local UTF-16 boundaries; only literal runs expose a map back to source text. The caller
    /// supplies the original source index even when projecting a single-run paragraph adapter.
    /// </summary>
    internal RunDisplayProjection ResolveRunDisplayProjection(
        Paragraph paragraph,
        Run source,
        int sourceRunIndex,
        int blockIndex,
        bool isTableCell)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRunIndex);

        var formatting = ResolveRunFmt(source, paragraph);
        var atomic = IsDisplaySelectionAtomicRun(source);
        var tokens = new List<RunDisplayToken>();
        var displayLength = 0;
        EquationVisualPlan? equationPlan = null;
        IReadOnlyList<int>? boundaries = null;

        void AddToken(string text, RunFormatting tokenFormatting, EquationVisualElement? equationElement = null,
            bool normalizeCellBreaks = true)
        {
            // Cell layout treats a Windows newline as one hard-break stop. Body offsets retain both
            // UTF-16 code units until body layout and all of its consumers adopt a different policy.
            if (isTableCell && normalizeCellBreaks)
            {
                if (tokens.Count > 0 && tokens[^1].Text.EndsWith('\r') && text.StartsWith('\n'))
                {
                    var previous = tokens[^1];
                    if (previous.Text.Length == 1)
                        tokens.RemoveAt(tokens.Count - 1);
                    else
                        tokens[^1] = previous with { Text = previous.Text[..^1] };
                    displayLength--;
                }
                text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            if (text.Length == 0)
                return;
            tokens.Add(new(source, sourceRunIndex, displayLength, text, tokenFormatting, equationElement));
            displayLength += text.Length;
        }

        RunDisplayProjection Result() => new(source, sourceRunIndex, formatting,
            string.Concat(tokens.Select(token => token.Text)), atomic, IsTextHiddenInCurrentView(formatting),
            boundaries, Array.AsReadOnly(tokens.ToArray()), equationPlan);

        // Embedded objects occupy layout space but do not contribute editable text offsets. Floating
        // body drawings likewise belong to the overlay; unsupported cell drawing fallbacks retain
        // their current text until the cell renderer owns their geometry.
        if (source.EmbeddedObject is not null || !isTableCell && IsFloatingDrawingRun(source))
            return Result();

        if (source.Equation is { } equation)
        {
            equationPlan = EquationVisualPlanner.Build(equation);
            foreach (var element in equationPlan.Elements)
            {
                if (element.Kind != EquationVisualElementKind.Segments)
                {
                    // A genuinely two-dimensional element is one display/caret stop. Its geometry is
                    // carried explicitly, never substituted with cached or flattened model text.
                    AddToken("\uFFFC", formatting, element);
                    continue;
                }
                foreach (var segment in element.Segments)
                    AddToken(segment.Text, ApplyEquationVisualStyle(formatting, segment.Style));
            }
            return Result();
        }

        var content = source.Text;
        if (source.ComplexField is not null)
        {
            var field = BuildBodyComplexFieldDisplayPlan(blockIndex, source);
            content = field.Text;
            if (field.IsFieldCode)
                formatting = formatting with { ColorHex = ComplexFieldDisplayPlanner.FieldCodeColorHex };
        }
        else if (source.FieldKind != RunFieldKind.None)
        {
            content = ResolveSimpleFieldDisplayText(source);
            if (source.FieldCodeVisible)
                formatting = formatting with { ColorHex = ComplexFieldDisplayPlanner.FieldCodeColorHex };
        }
        else if (source.FootnoteId is { } footnoteId)
        {
            content = ResolveNoteBodyMarkDisplayNumber(_doc, footnoteId, isFootnote: true);
            formatting = formatting with { VerticalAlign = VerticalAlign.Superscript };
        }
        else if (source.EndnoteId is { } endnoteId)
        {
            content = ResolveNoteBodyMarkDisplayNumber(_doc, endnoteId, isFootnote: false);
            formatting = formatting with { VerticalAlign = VerticalAlign.Superscript };
        }

        var display = new StringBuilder();
        var mapped = atomic ? null : new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            if (isTableCell && content[index] == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                continue;
            display.Append(DisplayGlyph(new Cell(content[index], formatting, Control: source.Control)));
            mapped?.Add(index + 1);
        }
        if (mapped is not null)
            boundaries = Array.AsReadOnly(mapped.ToArray());
        AddToken(display.ToString(), formatting, normalizeCellBreaks: false);
        return Result();
    }
    // The table renderer and AutoFit share the same resolved tokens and glyph advances. Embedded
    // objects reserve geometry without inventing an editable text stop; equations carry one stop.
    private List<CellWrappedItem> BuildCellDisplayItems(int blockIndex, Paragraph paragraph,
        double maxInner = double.PositiveInfinity, bool forceBold = false, bool captureDiagnostics = false)
    {
        var items = new List<CellWrappedItem>();
        var displayOffset = 0;
        for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            var projection = ResolveRunDisplayProjection(paragraph, run, runIndex, blockIndex, isTableCell: true);
            if (captureDiagnostics && projection.EquationPlan is { } equationPlan)
                CaptureEquationVisualPlan(equationPlan);
            if (run.EmbeddedObject is { } embeddedObject)
            {
                var plan = EmbeddedObjectVisualPlanner.Build(embeddedObject);
                var width = plan.WidthPt * PxPerPoint;
                var height = plan.HeightPt * PxPerPoint;
                if (width > maxInner)
                {
                    height *= maxInner / width;
                    width = maxInner;
                }
                items.Add(new CellWrappedItem('\0', width, height, false, plan, runIndex,
                    projection.Formatting, displayOffset));
            }
            foreach (var token in projection.Tokens)
            {
                var formatting = forceBold ? token.Formatting with { Bold = true } : token.Formatting;
                var hidden = IsTextHiddenInCurrentView(formatting);
                foreach (var character in token.Text)
                {
                    var isHardBreak = character is '\r' or '\n';
                    var metrics = MeasureBodyTextCell(character, formatting);
                    var width = metrics.Width;
                    var height = metrics.Height;
                    if (token.EquationElement is { } equationElement)
                    {
                        var size = MeasureEquationVisualElement(equationElement, formatting);
                        width = size.Width;
                        height = size.Height;
                    }
                    items.Add(new CellWrappedItem(character, hidden || isHardBreak ? 0 : width,
                        height, hidden, null, runIndex, formatting, displayOffset++,
                        token.EquationElement, run.Control, isHardBreak));
                }
            }
        }
        ApplyTableCharacterSpacing(paragraph, items);
        return items;
    }

}
