using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record BibliographyRegionPlan(
    IReadOnlyList<int> DeleteIndicesDescending,
    int InsertIndex,
    IReadOnlyList<Paragraph> Paragraphs);

public static class BibliographyRegionPlanner
{
    public static BibliographyRegionPlan BuildInsertPlan(
        TextDocument document,
        int insertAt,
        CitationStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedStyle = style ?? document.BibliographyStyle;
        Citations.EnsureStyles(document);

        return new BibliographyRegionPlan(
            DeleteIndicesDescending: [],
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: Citations.BuildBibliography(document, resolvedStyle));
    }

    public static BibliographyRegionPlan BuildRefreshPlan(
        TextDocument document,
        CitationStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedStyle = style ?? document.BibliographyStyle;
        Citations.EnsureStyles(document);

        var existingIndices = new List<int>();
        for (var i = 0; i < document.Blocks.Count; i++)
            if (Citations.IsBibliographyParagraph(document.Blocks[i]))
                existingIndices.Add(i);

        var insertAt = existingIndices.Count > 0
            ? existingIndices[0]
            : document.Blocks.Count;

        existingIndices.Reverse();

        return new BibliographyRegionPlan(
            DeleteIndicesDescending: existingIndices,
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: Citations.BuildBibliography(document, resolvedStyle));
    }
}
