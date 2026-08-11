using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Toolkit-neutral F9 field mutation pass. Renderers supply page mapping and repaint the result; this
/// coordinator owns story traversal, lock handling, cross-reference resolution and live-value fallback.
/// </summary>
public static class DocumentFieldUpdateCoordinator
{
    public static bool RequiresPageResolver(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DocumentFieldStories.Enumerate(document)
            .SelectMany(item => item.Paragraph.Runs)
            .Any(run => run.CrossReference?.Kind == CrossRefFieldKind.PageRef
                || run.ComplexField?.ContainsKeyword("PAGEREF") == true);
    }

    public static int Update(
        TextDocument targetDocument,
        TextDocument evaluationDocument,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        Func<int, int?>? crossReferencePageResolver = null,
        Func<int, string?>? crossReferencePageTextResolver = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocument);
        ArgumentNullException.ThrowIfNull(evaluationDocument);
        ArgumentNullException.ThrowIfNull(culture);

        var updated = 0;
        foreach (var storyParagraph in DocumentFieldStories.Enumerate(targetDocument))
        {
            var blockIndex = storyParagraph.BodyBlockIndex;
            var paragraph = storyParagraph.Paragraph;
            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var run = paragraph.Runs[runIndex];
                string? resolved = null;
                var applyEmptyResult = false;
                if (run.CrossReference is { } crossReference)
                {
                    resolved = CrossReferences.ResolveField(
                        targetDocument,
                        crossReference,
                        run.Text,
                        blockIndex,
                        crossReferencePageResolver,
                        crossReferencePageTextResolver,
                        sourceRunIndex: runIndex);
                }
                else if (run.ComplexField is { } complexField)
                {
                    if (complexField.IsLocked)
                        continue;

                    var canRecompute = DocumentFieldStories.CanRecomputeComplexField(
                        storyParagraph.StoryKind,
                        complexField);
                    resolved = canRecompute
                        ? ComplexFieldEngine.Recompute(
                            evaluationDocument,
                            blockIndex,
                            run,
                            crossReferencePageResolver,
                            crossReferencePageTextResolver)
                        : ComplexFieldDisplayPlanner.ResolveComplexFieldValue(
                            run,
                            evaluationDocument,
                            fileName,
                            now,
                            culture,
                            pageNumberText,
                            pageCount);
                    applyEmptyResult = canRecompute;
                }
                else if (run.FieldKind != RunFieldKind.None)
                {
                    resolved = ComplexFieldDisplayPlanner.ResolveLiveValue(
                        run.FieldKind,
                        run.Text,
                        evaluationDocument,
                        fileName,
                        now,
                        culture,
                        pageNumberText,
                        pageCount);
                }

                if (resolved is null || (!applyEmptyResult && resolved.Length == 0))
                    continue;
                if (string.Equals(run.Text, resolved, StringComparison.Ordinal))
                    continue;
                run.Text = resolved;
                updated++;
            }
        }

        return updated;
    }
}
