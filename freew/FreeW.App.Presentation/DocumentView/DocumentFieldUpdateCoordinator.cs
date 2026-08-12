using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Identifies a projected complex field and the result text that must remain when the field is unlinked.
/// </summary>
public readonly record struct DocumentComplexFieldUnlinkTarget(
    ComplexField Field,
    string DisplayedResult);

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
                    resolved = ResolveComplexField(
                        storyParagraph,
                        run,
                        evaluationDocument,
                        fileName,
                        now,
                        culture,
                        pageNumberText,
                        pageCount,
                        crossReferencePageResolver,
                        crossReferencePageTextResolver,
                        canRecompute);
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

    /// <summary>
    /// Updates only the complex-field runs selected by a model-native renderer. Run identity, rather than
    /// value equality, keeps duplicate fields independent.
    /// </summary>
    public static int UpdateComplexFields(
        TextDocument targetDocument,
        TextDocument evaluationDocument,
        IReadOnlyCollection<Run> selectedRuns,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        Func<int, int?>? crossReferencePageResolver = null,
        Func<int, string?>? crossReferencePageTextResolver = null)
    {
        ArgumentNullException.ThrowIfNull(selectedRuns);
        var selected = new HashSet<Run>(selectedRuns, ReferenceEqualityComparer.Instance);
        return UpdateComplexFieldsCore(
            targetDocument,
            evaluationDocument,
            run => selected.Contains(run),
            fileName,
            now,
            culture,
            pageNumberText,
            pageCount,
            crossReferencePageResolver,
            crossReferencePageTextResolver);
    }

    /// <summary>
    /// Updates only the complex fields selected by a projected renderer. Field identity survives the
    /// renderer-to-model commit and lets the shared coordinator recover the owning model runs.
    /// </summary>
    public static int UpdateComplexFields(
        TextDocument targetDocument,
        TextDocument evaluationDocument,
        IReadOnlyCollection<ComplexField> selectedFields,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        Func<int, int?>? crossReferencePageResolver = null,
        Func<int, string?>? crossReferencePageTextResolver = null)
    {
        ArgumentNullException.ThrowIfNull(selectedFields);
        var selected = new HashSet<ComplexField>(selectedFields, ReferenceEqualityComparer.Instance);
        return UpdateComplexFieldsCore(
            targetDocument,
            evaluationDocument,
            run => run.ComplexField is { } field && selected.Contains(field),
            fileName,
            now,
            culture,
            pageNumberText,
            pageCount,
            crossReferencePageResolver,
            crossReferencePageTextResolver);
    }

    /// <summary>Changes field-code visibility for the selected model runs.</summary>
    public static int ToggleCode(IReadOnlyCollection<Run> selectedRuns)
    {
        ArgumentNullException.ThrowIfNull(selectedRuns);
        return MutateSelectedRuns(
            selectedRuns,
            field => field with { ShowCode = !field.ShowCode });
    }

    /// <summary>Changes field-code visibility for fields selected through a projected renderer.</summary>
    public static int ToggleCode(TextDocument document, IReadOnlyCollection<ComplexField> selectedFields)
        => MutateSelectedFields(
            document,
            selectedFields,
            field => field with { ShowCode = !field.ShowCode });

    /// <summary>Changes the update lock for the selected model runs.</summary>
    public static int SetLock(IReadOnlyCollection<Run> selectedRuns, bool isLocked)
    {
        ArgumentNullException.ThrowIfNull(selectedRuns);
        return MutateSelectedRuns(selectedRuns, field => field.WithLock(isLocked));
    }

    /// <summary>Changes the update lock for fields selected through a projected renderer.</summary>
    public static int SetLock(
        TextDocument document,
        IReadOnlyCollection<ComplexField> selectedFields,
        bool isLocked)
        => MutateSelectedFields(document, selectedFields, field => field.WithLock(isLocked));

    /// <summary>
    /// Replaces selected model-native fields with their existing cached results.
    /// </summary>
    public static int Unlink(IReadOnlyCollection<Run> selectedRuns)
    {
        ArgumentNullException.ThrowIfNull(selectedRuns);
        var selected = new HashSet<Run>(selectedRuns, ReferenceEqualityComparer.Instance);
        var unlinked = 0;
        foreach (var run in selected)
        {
            if (run.ComplexField is null)
                continue;
            run.ComplexField = null;
            unlinked++;
        }

        return unlinked;
    }

    /// <summary>
    /// Replaces fields selected through a projected renderer with the displayed results captured before
    /// that renderer committed its view back to the model.
    /// </summary>
    public static int Unlink(
        TextDocument document,
        IReadOnlyCollection<DocumentComplexFieldUnlinkTarget> selectedFields)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedFields);
        var results = new Dictionary<ComplexField, string>(ReferenceEqualityComparer.Instance);
        foreach (var selected in selectedFields)
            results[selected.Field] = selected.DisplayedResult;

        var unlinked = 0;
        foreach (var storyParagraph in DocumentFieldStories.Enumerate(document))
        {
            foreach (var run in storyParagraph.Paragraph.Runs)
            {
                if (run.ComplexField is not { } field
                    || !results.TryGetValue(field, out var displayedResult))
                {
                    continue;
                }

                run.Text = displayedResult;
                run.ComplexField = null;
                unlinked++;
            }
        }

        return unlinked;
    }

    /// <summary>
    /// Toggles all document-story fields to a single code-visibility state. Codes are shown unless a
    /// strict majority is already showing them, matching Word's document-wide Alt+F9 behavior.
    /// </summary>
    public static int ToggleAllCodes(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var fields = DocumentFieldStories.Enumerate(document)
            .SelectMany(story => story.Paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToList();
        if (fields.Count == 0)
            return 0;

        var show = fields.Count(run => run.ComplexField!.ShowCode) * 2 <= fields.Count;
        foreach (var run in fields)
            run.ComplexField = run.ComplexField! with { ShowCode = show };
        return fields.Count;
    }

    private static int UpdateComplexFieldsCore(
        TextDocument targetDocument,
        TextDocument evaluationDocument,
        Func<Run, bool> isSelected,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        Func<int, int?>? crossReferencePageResolver,
        Func<int, string?>? crossReferencePageTextResolver)
    {
        ArgumentNullException.ThrowIfNull(targetDocument);
        ArgumentNullException.ThrowIfNull(evaluationDocument);
        ArgumentNullException.ThrowIfNull(isSelected);
        ArgumentNullException.ThrowIfNull(culture);

        var updated = 0;
        foreach (var storyParagraph in DocumentFieldStories.Enumerate(targetDocument))
        {
            foreach (var run in storyParagraph.Paragraph.Runs)
            {
                if (!isSelected(run) || run.ComplexField is not { IsLocked: false } field)
                    continue;

                var canRecompute = DocumentFieldStories.CanRecomputeComplexField(
                    storyParagraph.StoryKind,
                    field);
                var resolved = ResolveComplexField(
                    storyParagraph,
                    run,
                    evaluationDocument,
                    fileName,
                    now,
                    culture,
                    pageNumberText,
                    pageCount,
                    crossReferencePageResolver,
                    crossReferencePageTextResolver,
                    canRecompute);
                if ((!canRecompute && resolved.Length == 0)
                    || string.Equals(run.Text, resolved, StringComparison.Ordinal))
                {
                    continue;
                }

                run.Text = resolved;
                updated++;
            }
        }

        return updated;
    }

    private static string ResolveComplexField(
        DocumentFieldStoryParagraph storyParagraph,
        Run run,
        TextDocument evaluationDocument,
        string? fileName,
        DateTime now,
        CultureInfo culture,
        string? pageNumberText,
        int? pageCount,
        Func<int, int?>? crossReferencePageResolver,
        Func<int, string?>? crossReferencePageTextResolver,
        bool canRecompute)
        => canRecompute
            ? ComplexFieldEngine.Recompute(
                evaluationDocument,
                storyParagraph.BodyBlockIndex,
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

    private static int MutateSelectedRuns(
        IReadOnlyCollection<Run> selectedRuns,
        Func<ComplexField, ComplexField> mutate)
    {
        var selected = new HashSet<Run>(selectedRuns, ReferenceEqualityComparer.Instance);
        var mutated = 0;
        foreach (var run in selected)
        {
            if (run.ComplexField is not { } field)
                continue;
            run.ComplexField = mutate(field);
            mutated++;
        }

        return mutated;
    }

    private static int MutateSelectedFields(
        TextDocument document,
        IReadOnlyCollection<ComplexField> selectedFields,
        Func<ComplexField, ComplexField> mutate)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedFields);
        var selected = new HashSet<ComplexField>(selectedFields, ReferenceEqualityComparer.Instance);
        var mutated = 0;
        foreach (var storyParagraph in DocumentFieldStories.Enumerate(document))
        {
            foreach (var run in storyParagraph.Paragraph.Runs)
            {
                if (run.ComplexField is not { } field || !selected.Contains(field))
                    continue;
                run.ComplexField = mutate(field);
                mutated++;
            }
        }

        return mutated;
    }
}
