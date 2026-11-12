using System.Collections;

namespace FreeW.Core.Model;

/// <summary>
/// Where an equation sits in a document: the list holding its paragraph (the body block list or a table
/// cell's paragraph list), the paragraph and the run that carries it.
/// </summary>
public sealed record EquationLocation(IList Container, int ParagraphIndex, Paragraph Paragraph, int RunIndex);

/// <summary>Finds equations in the body and in table cells (including nested tables).</summary>
public static class EquationLocator
{
    /// <summary>Locates the run carrying exactly this <paramref name="equation"/> instance, or null.</summary>
    public static EquationLocation? Find(TextDocument document, Equation equation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(equation);
        return FindIn(document.Blocks, equation, depth: 0);
    }

    private static EquationLocation? FindIn(IList container, Equation equation, int depth)
    {
        if (depth > 64)
            return null;

        for (var index = 0; index < container.Count; index++)
        {
            switch (container[index])
            {
                case Paragraph paragraph:
                    for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                    {
                        if (ReferenceEquals(paragraph.Runs[runIndex].Equation, equation))
                            return new EquationLocation(container, index, paragraph, runIndex);
                    }

                    break;

                case Table table:
                    if (FindIn(table, equation, depth) is { } inTable)
                        return inTable;
                    break;
            }
        }

        return null;
    }

    private static EquationLocation? FindIn(Table table, Equation equation, int depth)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                if (FindIn(cell.Paragraphs, equation, depth + 1) is { } inCell)
                    return inCell;

                foreach (var nested in cell.NestedTables)
                {
                    if (FindIn(nested, equation, depth + 1) is { } inNested)
                        return inNested;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Plans the paragraphs that replace an equation's paragraph when the equation is edited. The original
/// paragraph is never mutated: the plan builds new paragraphs around the same run objects, so the edit
/// can be undone by putting the original paragraph back.
/// <para>
/// A display equation (Word's "Change to Display") sits on a line of its own. When the edit turns an
/// equation that shares its paragraph with other content into a display equation, the paragraph is split
/// into the content before it, the equation, and the content after it; the equation's own paragraph
/// drops any explicit alignment so the equation's <see cref="Equation.Justification"/> decides.
/// </para>
/// </summary>
public static class EquationEditPlanner
{
    /// <summary>
    /// The paragraphs that should stand in for <paramref name="location"/>'s paragraph once its equation
    /// is <paramref name="replacement"/>. <paramref name="layoutChanged"/> says the display mode or
    /// justification is being changed (as opposed to only the mathematics).
    /// </summary>
    public static IReadOnlyList<Paragraph> Plan(EquationLocation location, Equation replacement, bool layoutChanged)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(replacement);

        var source = location.Paragraph;
        var runIndex = location.RunIndex;
        var equationRun = DocumentModelCloner.CloneRun(source.Runs[runIndex], RevisionClonePolicy.Preserve);
        equationRun.Equation = replacement;
        equationRun.Text = replacement.LinearText;

        var before = source.Runs.Take(runIndex).ToList();
        var after = source.Runs.Skip(runIndex + 1).ToList();
        var splitBefore = replacement.IsDisplayMath && before.Any(HasContent);
        var splitAfter = replacement.IsDisplayMath && after.Any(HasContent);

        if (!splitBefore && !splitAfter)
        {
            var single = Shell(source);
            single.Runs.AddRange(before);
            single.Runs.Add(equationRun);
            single.Runs.AddRange(after);
            single.BookmarkBoundaries.AddRange(source.BookmarkBoundaries);
            if (layoutChanged && replacement.IsDisplayMath && IsDisplayOnly(single))
                single.Formatting = single.Formatting with { Alignment = TextAlignment.Left };
            return [single];
        }

        var parts = new List<Paragraph>(3);
        var equationRuns = new List<Run>();
        var firstEquationRunIndex = 0;
        if (splitBefore)
        {
            var beforeParagraph = Shell(source);
            beforeParagraph.Runs.AddRange(before);
            beforeParagraph.BookmarkBoundaries.AddRange(
                source.BookmarkBoundaries.Where(boundary => boundary.RunIndex <= runIndex));
            parts.Add(beforeParagraph);
            firstEquationRunIndex = runIndex;
        }
        else
        {
            // Nothing visible before the equation: whatever is there stays in the equation's paragraph.
            equationRuns.AddRange(before);
        }

        equationRuns.Add(equationRun);
        if (!splitAfter)
            equationRuns.AddRange(after);

        var equationParagraph = Shell(source);
        equationParagraph.Runs.AddRange(equationRuns);
        equationParagraph.Formatting = equationParagraph.Formatting with { Alignment = TextAlignment.Left };
        var equationRunLimit = splitAfter ? runIndex + 1 : int.MaxValue;
        equationParagraph.BookmarkBoundaries.AddRange(source.BookmarkBoundaries
            .Where(boundary => (splitBefore ? boundary.RunIndex > runIndex : true) && boundary.RunIndex <= equationRunLimit)
            .Select(boundary => boundary with { RunIndex = boundary.RunIndex - firstEquationRunIndex }));
        parts.Add(equationParagraph);

        if (splitAfter)
        {
            var afterParagraph = Shell(source);
            afterParagraph.Runs.AddRange(after);
            afterParagraph.BookmarkBoundaries.AddRange(source.BookmarkBoundaries
                .Where(boundary => boundary.RunIndex > runIndex + 1)
                .Select(boundary => boundary with { RunIndex = boundary.RunIndex - (runIndex + 1) }));
            parts.Add(afterParagraph);
        }

        // Properties that belong to the start of the original paragraph stay on the first part; those
        // that belong to its end (its paragraph mark) stay on the last.
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (index > 0)
            {
                part.SpanningFieldStart = null;
                part.DropCap = null;
                part.BookmarkNames.Clear();
            }

            if (index < parts.Count - 1)
            {
                part.SectionBreak = null;
                part.EndsSpanningField = false;
                part.MarkRevision = RevisionKind.None;
                part.MarkRevisionAuthor = null;
                part.MarkRevisionDateXml = null;
            }
        }

        return parts;
    }

    /// <summary>
    /// Replaces <paramref name="target"/> (found by instance) with <paramref name="replacement"/> directly
    /// in <paramref name="document"/>, applying the same paragraph plan as <see cref="ReplaceEquationCommand"/>.
    /// Passing the same instance for both gives a display equation its own line. Returns false when the
    /// target is not in the document. This is the non-undoable building block for hosts that record
    /// undo as whole-document snapshots; everything else should execute the command.
    /// </summary>
    public static bool TryReplace(TextDocument document, Equation target, Equation replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);

        if (EquationLocator.Find(document, target) is not { } location)
            return false;

        Swap(location, Plan(location, replacement, IsLayoutChange(target, replacement)));
        return true;
    }

    /// <summary>True when replacing <paramref name="target"/> changes how the equation is laid out.</summary>
    internal static bool IsLayoutChange(Equation target, Equation replacement) =>
        ReferenceEquals(target, replacement)
        || target.IsDisplayMath != replacement.IsDisplayMath
        || target.Justification != replacement.Justification;

    /// <summary>Puts the planned paragraphs where the located paragraph was.</summary>
    internal static void Swap(EquationLocation location, IReadOnlyList<Paragraph> planned)
    {
        location.Container.RemoveAt(location.ParagraphIndex);
        for (var offset = 0; offset < planned.Count; offset++)
            location.Container.Insert(location.ParagraphIndex + offset, planned[offset]);
    }

    /// <summary>True when the paragraph holds nothing but display equations (ignoring empty text runs).</summary>
    public static bool IsDisplayOnly(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var any = false;
        foreach (var run in paragraph.Runs)
        {
            if (run.Equation is { IsDisplayMath: true })
            {
                any = true;
                continue;
            }

            if (HasContent(run))
                return false;
        }

        return any;
    }

    private static bool HasContent(Run run) =>
        run.Text.Length > 0
        || run.Equation is not null
        || run.Image is not null
        || run.Shape is not null
        || run.Chart is not null
        || run.SmartArt is not null
        || run.WordArt is not null
        || run.EmbeddedObject is not null
        || run.DrawingGroup is not null;

    /// <summary>A copy of the paragraph's own properties with no runs and no bookmark boundaries.</summary>
    private static Paragraph Shell(Paragraph source)
    {
        var shell = DocumentModelCloner.CloneParagraph(source, RevisionClonePolicy.Preserve);
        shell.Runs.Clear();
        shell.BookmarkBoundaries.Clear();

        // The clone copies the section marker; the original instance must stay the one in the document
        // so section identity is not disturbed by an equation edit.
        shell.SectionBreak = source.SectionBreak;
        return shell;
    }
}

/// <summary>
/// Replaces one equation (identified by instance, wherever it currently is in the body or a table cell)
/// with another, as a single undoable edit. Everything undo needs is captured in <see cref="Apply"/>
/// from the document as it is at that moment; <see cref="Revert"/> puts the original paragraph object
/// back, so the document is exactly what it was.
/// </summary>
public sealed class ReplaceEquationCommand(Equation target, Equation replacement) : IDocumentCommand
{
    private IList? _container;
    private int _index;
    private Paragraph? _original;
    private IReadOnlyList<Paragraph>? _applied;

    public string Label => "Edit Equation";

    public bool HasEffect(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EquationLocator.Find(context.Document, target) is not null
            && (!ReferenceEquals(target, replacement) || LayoutNeedsWork(context));
    }

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_applied is not null || EquationLocator.Find(context.Document, target) is not { } location)
            return;

        var planned = EquationEditPlanner.Plan(
            location,
            replacement,
            EquationEditPlanner.IsLayoutChange(target, replacement));

        _container = location.Container;
        _index = location.ParagraphIndex;
        _original = location.Paragraph;
        _applied = planned;
        EquationEditPlanner.Swap(location, planned);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_applied is null || _container is null || _original is null)
            return;

        // Only unwind what is still there: if the paragraphs were moved or removed since, leave the
        // document alone rather than damage it.
        var intact = _index + _applied.Count <= _container.Count;
        for (var offset = 0; intact && offset < _applied.Count; offset++)
            intact = ReferenceEquals(_container[_index + offset], _applied[offset]);

        if (intact)
        {
            for (var offset = 0; offset < _applied.Count; offset++)
                _container.RemoveAt(_index);
            _container.Insert(_index, _original);
        }

        _container = null;
        _original = null;
        _applied = null;
    }

    /// <summary>
    /// Re-applying the same instance is how a freshly inserted display equation is given its own line; it
    /// only has an effect when that line does not exist yet.
    /// </summary>
    private bool LayoutNeedsWork(IDocumentCommandContext context) =>
        replacement.IsDisplayMath
        && EquationLocator.Find(context.Document, target) is { } location
        && !EquationEditPlanner.IsDisplayOnly(location.Paragraph);
}
