using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableOfAuthoritiesRegionPlan(
    IReadOnlyList<int> DeleteIndicesDescending,
    int InsertIndex,
    IReadOnlyList<Paragraph> Paragraphs);

public static class TableOfAuthoritiesRegionPlanner
{
    public static bool ContainsRegion(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var block in document.Blocks)
            if (TableOfAuthorities.IsTableOfAuthoritiesParagraph(block))
                return true;

        return false;
    }

    public static TableOfAuthoritiesRegionPlan BuildInsertPlan(
        TextDocument document,
        int insertAt,
        ToaOptions? options = null,
        ToaCitationPageResolver? pageResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedOptions = options ?? ToaOptions.Default;
        TableOfAuthorities.EnsureStyles(document);

        return new TableOfAuthoritiesRegionPlan(
            DeleteIndicesDescending: [],
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: TableOfAuthorities.Build(document, resolvedOptions, pageResolver));
    }

    public static TableOfAuthoritiesRegionPlan BuildInsertPlanWithTableAddresses(
        TextDocument document,
        int insertAt,
        ToaOptions? options = null,
        ToaCitationPageAddressResolver? pageResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedOptions = options ?? ToaOptions.Default;
        TableOfAuthorities.EnsureStyles(document);

        return new TableOfAuthoritiesRegionPlan(
            DeleteIndicesDescending: [],
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: TableOfAuthorities.BuildWithTableAddresses(document, resolvedOptions, pageResolver));
    }

    public static TableOfAuthoritiesRegionPlan BuildRefreshPlan(
        TextDocument document,
        ToaOptions? options = null,
        ToaCitationPageResolver? pageResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedOptions = options ?? TableOfAuthorities.ExistingOptions(document) ?? ToaOptions.Default;
        TableOfAuthorities.EnsureStyles(document);

        var existingIndices = new List<int>();
        for (var i = 0; i < document.Blocks.Count; i++)
            if (TableOfAuthorities.IsTableOfAuthoritiesParagraph(document.Blocks[i]))
                existingIndices.Add(i);

        var insertAt = existingIndices.Count > 0
            ? existingIndices[0]
            : document.Blocks.Count;

        existingIndices.Reverse();

        return new TableOfAuthoritiesRegionPlan(
            DeleteIndicesDescending: existingIndices,
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: TableOfAuthorities.Build(document, resolvedOptions, pageResolver));
    }

    public static TableOfAuthoritiesRegionPlan BuildRefreshPlanWithTableAddresses(
        TextDocument document,
        ToaOptions? options = null,
        ToaCitationPageAddressResolver? pageResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolvedOptions = options ?? TableOfAuthorities.ExistingOptions(document) ?? ToaOptions.Default;
        TableOfAuthorities.EnsureStyles(document);

        var existingIndices = new List<int>();
        for (var i = 0; i < document.Blocks.Count; i++)
            if (TableOfAuthorities.IsTableOfAuthoritiesParagraph(document.Blocks[i]))
                existingIndices.Add(i);

        var insertAt = existingIndices.Count > 0
            ? existingIndices[0]
            : document.Blocks.Count;

        existingIndices.Reverse();

        return new TableOfAuthoritiesRegionPlan(
            DeleteIndicesDescending: existingIndices,
            InsertIndex: Math.Clamp(insertAt, 0, document.Blocks.Count),
            Paragraphs: TableOfAuthorities.BuildWithTableAddresses(document, resolvedOptions, pageResolver));
    }
}
