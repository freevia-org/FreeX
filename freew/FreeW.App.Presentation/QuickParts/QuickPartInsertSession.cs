namespace FreeW.App.Presentation.QuickParts;

public sealed record QuickPartInsertState(
    IReadOnlyList<string> Names,
    int SelectedIndex)
{
    public bool IsEmpty => Names.Count == 0;
    public bool CanInsert => SelectedIndex >= 0 && SelectedIndex < Names.Count;
}

public sealed record QuickPartInsertAction(string Name, string Text);

/// <summary>
/// Owns saved Quick Part selection and acceptance for both renderers. Native dialogs only project
/// <see cref="Current"/> and report selection changes.
/// </summary>
public sealed class QuickPartInsertSession
{
    private readonly QuickPartLibrary _library;

    public QuickPartInsertSession(QuickPartLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        Current = BuildState(library.IsEmpty ? -1 : 0);
    }

    public QuickPartInsertState Current { get; private set; }

    public QuickPartInsertState SelectIndex(int selectedIndex)
    {
        Current = BuildState(selectedIndex);
        return Current;
    }

    public QuickPartInsertAction? AcceptSelection()
    {
        if (!Current.CanInsert)
            return null;

        var name = Current.Names[Current.SelectedIndex];
        var part = _library.Get(name);
        return part is null ? null : new QuickPartInsertAction(part.Name, part.Text);
    }

    private QuickPartInsertState BuildState(int selectedIndex)
    {
        var names = _library.Names.ToArray();
        var normalizedIndex = names.Length == 0
            ? -1
            : Math.Clamp(selectedIndex, 0, names.Length - 1);
        return new QuickPartInsertState(names, normalizedIndex);
    }
}
