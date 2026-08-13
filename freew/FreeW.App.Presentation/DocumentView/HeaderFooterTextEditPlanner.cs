using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record HeaderFooterTextDeletePlan(
    int FirstParagraphIndex,
    int RemoveCount,
    IReadOnlyList<Paragraph> ReplacementParagraphs,
    HeaderFooterTextPosition Caret);

/// <summary>
/// Builds renderer-neutral model edits for header/footer text. The plan replaces a contiguous paragraph
/// span, which lets either renderer route it through its own undo service without duplicating selection,
/// field-boundary, or paragraph-merge behavior.
/// </summary>
public static class HeaderFooterTextEditPlanner
{
    public static HeaderFooterTextDeletePlan? PlanDelete(
        HeaderFooter story,
        HeaderFooterTextRange selection)
    {
        ArgumentNullException.ThrowIfNull(story);
        var normalized = HeaderFooterTextSelectionPlanner.Normalize(story, selection.End, selection.Start);
        if (normalized is null || story.Paragraphs.Count == 0)
            return null;

        selection = normalized.Value;
        var startParagraph = story.Paragraphs[selection.Start.ParagraphIndex];
        var endParagraph = story.Paragraphs[selection.End.ParagraphIndex];
        var startAtoms = ToAtoms(startParagraph);
        var endAtoms = ReferenceEquals(startParagraph, endParagraph) ? startAtoms : ToAtoms(endParagraph);
        var startAtomIndex = AtomIndexForStart(startAtoms, selection.Start.Offset);
        var endAtomIndex = AtomIndexForEnd(endAtoms, selection.End.Offset);
        var caretOffset = startAtoms.Take(startAtomIndex).Sum(atom => atom.ModelLength);

        var replacement = (Paragraph)DocumentMerge.CloneBlock(startParagraph);
        var replacementAtoms = startAtoms.Take(startAtomIndex)
            .Concat(endAtoms.Skip(endAtomIndex))
            .ToList();
        SetAtoms(replacement, replacementAtoms);

        return new HeaderFooterTextDeletePlan(
            selection.Start.ParagraphIndex,
            selection.End.ParagraphIndex - selection.Start.ParagraphIndex + 1,
            [replacement],
            new HeaderFooterTextPosition(selection.Start.ParagraphIndex, caretOffset));
    }

    private static List<Atom> ToAtoms(Paragraph paragraph)
    {
        var atoms = new List<Atom>();
        foreach (var run in paragraph.Runs)
        {
            if (IsAtomic(run))
            {
                atoms.Add(new Atom(run, run.Text, Atomic: true));
                continue;
            }

            foreach (var character in run.Text)
                atoms.Add(new Atom(run, character.ToString(), Atomic: false));
        }
        return atoms;
    }

    private static bool IsAtomic(Run run) =>
        run.FieldKind != RunFieldKind.None
        || run.ComplexField is not null
        || run.Image is not null
        || run.Equation is not null
        || run.Shape is not null
        || run.WordArt is not null
        || run.Chart is not null
        || run.EmbeddedObject is not null
        || run.SmartArt is not null
        || run.PreservedDrawing is not null
        || run.DrawingGroup is not null;

    private static int AtomIndexForStart(IReadOnlyList<Atom> atoms, int modelOffset)
    {
        var position = 0;
        for (var index = 0; index < atoms.Count; index++)
        {
            if (modelOffset <= position)
                return index;
            var next = position + atoms[index].ModelLength;
            if (modelOffset < next)
                return index;
            position = next;
        }
        return atoms.Count;
    }

    private static int AtomIndexForEnd(IReadOnlyList<Atom> atoms, int modelOffset)
    {
        var position = 0;
        for (var index = 0; index < atoms.Count; index++)
        {
            if (modelOffset <= position)
                return index;
            var next = position + atoms[index].ModelLength;
            if (modelOffset < next)
                return atoms[index].Atomic ? index + 1 : index;
            position = next;
        }
        return atoms.Count;
    }

    private static void SetAtoms(Paragraph paragraph, IReadOnlyList<Atom> atoms)
    {
        paragraph.Runs.Clear();
        for (var index = 0; index < atoms.Count;)
        {
            var atom = atoms[index];
            if (atom.Atomic)
            {
                paragraph.Runs.Add(RevisionEditPlanner.CloneRunWithText(atom.Source, atom.Text));
                index++;
                continue;
            }

            var source = atom.Source;
            var start = index;
            while (index < atoms.Count
                   && !atoms[index].Atomic
                   && ReferenceEquals(atoms[index].Source, source))
            {
                index++;
            }

            paragraph.Runs.Add(RevisionEditPlanner.CloneRunWithText(
                source,
                string.Concat(atoms.Skip(start).Take(index - start).Select(item => item.Text))));
        }
    }

    private readonly record struct Atom(Run Source, string Text, bool Atomic)
    {
        public int ModelLength => Atomic ? Source.Text.Length : 1;
    }
}
