namespace FreeW.Core.Model;

/// <summary>Add one unique document-index term as an undoable edit.</summary>
public sealed class AddIndexEntryCommand(string term) : IDocumentCommand
{
    private IndexEntry? _added;

    public string Label => "Mark Index Entry";

    public void Apply(IDocumentCommandContext context)
    {
        var entry = new IndexEntry(term);
        if (entry.Term.Length == 0
            || context.Document.IndexEntries.Any(existing =>
                string.Equals(existing.Term, entry.Term, StringComparison.OrdinalIgnoreCase)))
            return;

        _added = entry;
        context.Document.IndexEntries.Add(entry);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_added is null)
            return;

        context.Document.IndexEntries.Remove(_added);
        _added = null;
    }
}
