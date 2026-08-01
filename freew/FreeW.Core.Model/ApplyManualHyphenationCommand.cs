namespace FreeW.Core.Model;

/// <summary>One selected manual-hyphenation insertion in an existing run.</summary>
public sealed record ManualHyphenationEdit(Run Run, int CharacterOffset);

/// <summary>
/// Inserts a confirmed set of manual soft hyphens as one undoable body-text edit. Offsets refer to each
/// run's original text; applying them from right to left keeps every earlier position stable.
/// </summary>
public sealed class ApplyManualHyphenationCommand(IReadOnlyList<ManualHyphenationEdit> edits) : IDocumentCommand
{
    private Dictionary<Run, string>? _previousText;

    public string Label => "Manual Hyphenation";
    public int EstimatedBytes => Math.Max(256, edits.Count * 32);

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _previousText ??= edits.Select(edit => edit.Run)
            .Distinct()
            .ToDictionary(run => run, run => run.Text);

        foreach (var group in edits.GroupBy(edit => edit.Run))
        {
            var text = group.Key.Text;
            foreach (var offset in group.Select(edit => edit.CharacterOffset).Distinct().OrderByDescending(offset => offset))
            {
                if (offset <= 0 || offset > text.Length
                    || text[offset - 1] == Hyphenator.SoftHyphen
                    || (offset < text.Length && text[offset] == Hyphenator.SoftHyphen))
                {
                    continue;
                }
                text = text.Insert(offset, Hyphenator.SoftHyphen.ToString());
            }
            group.Key.Text = text;
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_previousText is null)
            return;
        foreach (var (run, text) in _previousText)
            run.Text = text;
        _previousText = null;
    }
}
