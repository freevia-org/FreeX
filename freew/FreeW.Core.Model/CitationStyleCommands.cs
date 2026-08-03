namespace FreeW.Core.Model;

/// <summary>
/// Applies a citation style to the document, refreshing existing native citation fields and an existing
/// generated bibliography as one undoable edit. A style change never inserts a bibliography when the
/// document did not already contain one.
/// </summary>
public sealed class ApplyCitationStyleCommand(CitationStyle style) : IDocumentCommand
{
    private CitationStyle _previousStyle;
    private List<(Run Run, string Text)>? _previousCitationResults;
    private List<(int Index, Block Block)>? _previousBibliography;
    private bool _initialized;
    private bool _applied;

    public string Label => "Apply Citation Style";

    public int EstimatedBytes => 512
        + ((_previousCitationResults?.Count ?? 0) * 64)
        + ((_previousBibliography?.Count ?? 0) * 256);

    public void Apply(IDocumentCommandContext context)
    {
        if (_applied)
            return;

        var document = context.Document;
        if (!_initialized)
        {
            _previousStyle = document.BibliographyStyle;
            _previousCitationResults = EnumerateRuns(document)
                .Where(run => run.ComplexField?.Keyword == "CITATION")
                .Select(run => (run, run.Text))
                .ToList();
            _previousBibliography = document.Blocks
                .Select((block, index) => (index, block))
                .Where(item => Citations.IsBibliographyParagraph(item.block))
                .Select(item => (item.index, item.block))
                .ToList();
            _initialized = true;
        }

        document.BibliographyStyle = style;
        foreach (var run in EnumerateRuns(document))
        {
            if (run.ComplexField is { Keyword: "CITATION" } field)
                run.Text = Citations.ResolveCitationField(document, field, run.Text);
        }

        RefreshExistingBibliography(document);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previousCitationResults is null || _previousBibliography is null)
            return;

        var document = context.Document;
        document.BibliographyStyle = _previousStyle;
        foreach (var (run, text) in _previousCitationResults)
            run.Text = text;

        RemoveBibliography(document);
        foreach (var (index, block) in _previousBibliography.OrderBy(item => item.Index))
            document.Blocks.Insert(Math.Clamp(index, 0, document.Blocks.Count), block);

        _applied = false;
    }

    private void RefreshExistingBibliography(TextDocument document)
    {
        if (_previousBibliography is not { Count: > 0 })
            return;

        var insertAt = document.Blocks
            .Select((block, index) => (block, index))
            .Where(item => Citations.IsBibliographyParagraph(item.block))
            .Select(item => item.index)
            .DefaultIfEmpty(document.Blocks.Count)
            .Min();

        RemoveBibliography(document);
        Citations.EnsureStyles(document);
        var control = BlockContentControl.BibliographyRegion();
        foreach (var paragraph in Citations.BuildBibliography(document, style))
        {
            paragraph.BlockContentControl = control;
            document.Blocks.Insert(Math.Clamp(insertAt++, 0, document.Blocks.Count), paragraph);
        }
    }

    private static void RemoveBibliography(TextDocument document)
    {
        for (var index = document.Blocks.Count - 1; index >= 0; index--)
            if (Citations.IsBibliographyParagraph(document.Blocks[index]))
                document.Blocks.RemoveAt(index);
    }

    private static IEnumerable<Run> EnumerateRuns(TextDocument document)
    {
        var seen = new HashSet<Run>(ReferenceEqualityComparer.Instance);
        foreach (var block in document.Blocks)
            foreach (var run in EnumerateBlockRuns(block))
                if (seen.Add(run))
                    yield return run;

        foreach (var run in EnumerateHeadersFootersRuns(document.FinalSectionHeadersFooters))
            if (seen.Add(run))
                yield return run;
    }

    private static IEnumerable<Run> EnumerateBlockRuns(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var run in EnumerateParagraphRuns(paragraph))
                    yield return run;
                if (paragraph.SectionBreak is { } section)
                    foreach (var run in EnumerateHeadersFootersRuns(section.HeadersFooters))
                        yield return run;
                break;
            case Table table:
                foreach (var paragraph in table.Rows
                             .SelectMany(row => row.Cells)
                             .SelectMany(cell => cell.Paragraphs))
                    foreach (var run in EnumerateParagraphRuns(paragraph))
                        yield return run;
                break;
        }
    }

    private static IEnumerable<Run> EnumerateParagraphRuns(Paragraph paragraph)
    {
        foreach (var run in paragraph.Runs)
        {
            yield return run;
            if (run.Shape is { } shape)
                foreach (var nested in shape.TextParagraphs.SelectMany(EnumerateParagraphRuns))
                    yield return nested;
            if (run.DrawingGroup is { } group)
                foreach (var nested in EnumerateDrawingGroupRuns(group))
                    yield return nested;
        }
    }

    private static IEnumerable<Run> EnumerateDrawingGroupRuns(DrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            if (child is Shape shape)
                foreach (var run in shape.TextParagraphs.SelectMany(EnumerateParagraphRuns))
                    yield return run;
            if (child is DrawingGroup nested)
                foreach (var run in EnumerateDrawingGroupRuns(nested))
                    yield return run;
        }
    }

    private static IEnumerable<Run> EnumerateHeadersFootersRuns(SectionHeadersFooters headersFooters)
    {
        foreach (var headerFooter in new[]
                 {
                     headersFooters.Header,
                     headersFooters.Footer,
                     headersFooters.EvenHeader,
                     headersFooters.EvenFooter,
                     headersFooters.FirstHeader,
                     headersFooters.FirstFooter
                 })
        {
            if (headerFooter is null)
                continue;
            foreach (var run in headerFooter.Paragraphs.SelectMany(EnumerateParagraphRuns))
                yield return run;
        }
    }
}
