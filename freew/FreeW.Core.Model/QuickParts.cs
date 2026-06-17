namespace FreeW.Core.Model;

/// <summary>
/// A reusable text snippet (AutoText / Quick Part): a <see cref="Name"/> plus the snippet body stored
/// as plain-text paragraph lines (<see cref="Lines"/>), one string per paragraph. Plain text keeps the
/// model trivially serializable (a JSON store round-trips it as-is) while still mapping cleanly onto the
/// document model: <see cref="ToParagraphs"/> turns the lines into <see cref="Paragraph"/>s for insertion,
/// and <see cref="FromParagraphs(string, System.Collections.Generic.IEnumerable{Paragraph})"/> captures a
/// selection's paragraphs back into a snippet. Names are matched case-insensitively (Word treats AutoText
/// entry names that way), and the name is normalised (trimmed) on construction.
/// </summary>
public sealed class QuickPart
{
    public QuickPart(string name, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(lines);
        Name = name.Trim();
        Lines = lines.ToList();
    }

    /// <summary>The snippet's entry name (trimmed). Quick Parts are looked up by this, case-insensitively.</summary>
    public string Name { get; }

    /// <summary>The snippet body, one plain-text string per paragraph. May be empty (an empty snippet).</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The snippet rendered as a single string, paragraphs joined by newlines.</summary>
    public string Text => string.Join("\n", Lines);

    /// <summary>Materialise the snippet's lines as fresh <see cref="Paragraph"/>s ready to insert.</summary>
    public IReadOnlyList<Paragraph> ToParagraphs() => Lines.Select(line => new Paragraph(line)).ToList();

    /// <summary>
    /// Capture a snippet from a sequence of paragraphs (e.g. the current selection), flattening each
    /// paragraph to its plain text. The <paramref name="name"/> is trimmed by the constructor.
    /// </summary>
    public static QuickPart FromParagraphs(string name, IEnumerable<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        return new QuickPart(name, paragraphs.Select(p => p.PlainText).ToList());
    }

    /// <summary>
    /// Capture a snippet from a block of text, splitting on newlines into one line per paragraph.
    /// CR/LF and lone-LF line endings are both honoured.
    /// </summary>
    public static QuickPart FromText(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new QuickPart(name, lines);
    }
}

/// <summary>
/// A pure, in-memory store of <see cref="QuickPart"/> snippets keyed by name (case-insensitive). This is
/// the testable core; the WPF/IO layer (see FreeW.App.Host's QuickPartLibrary) wraps it to persist the
/// snippets as JSON under FreeW's data folder. Adding a snippet whose name already exists overwrites the
/// previous one (matching Word, where saving an AutoText entry under an existing name replaces it), and
/// the most-recently-added wins. <see cref="Names"/> returns entries in case-insensitive name order so
/// the UI list is stable.
/// </summary>
public sealed class QuickPartStore
{
    private readonly Dictionary<string, QuickPart> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The number of stored snippets.</summary>
    public int Count => _byName.Count;

    /// <summary>The stored snippet names, in case-insensitive alphabetical order.</summary>
    public IReadOnlyList<string> Names =>
        _byName.Values.Select(p => p.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>All stored snippets, in case-insensitive name order.</summary>
    public IReadOnlyList<QuickPart> Snippets =>
        _byName.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Add (or overwrite) a snippet from its name and content lines. A blank name is rejected. Returns
    /// the stored <see cref="QuickPart"/>. If a snippet with the same name (case-insensitively) exists,
    /// it is replaced.
    /// </summary>
    public QuickPart Add(string name, IReadOnlyList<string> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Add(new QuickPart(name, content));
    }

    /// <summary>Add (or overwrite by name) an already-built snippet. A blank name is rejected.</summary>
    public QuickPart Add(QuickPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (string.IsNullOrWhiteSpace(part.Name))
            throw new ArgumentException("A Quick Part must have a non-empty name.", nameof(part));
        _byName[part.Name] = part;
        return part;
    }

    /// <summary>Look up a snippet by name (case-insensitive), or null when none is stored under it.</summary>
    public QuickPart? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return _byName.TryGetValue(name.Trim(), out var part) ? part : null;
    }

    /// <summary>True when a snippet with this name (case-insensitive) is stored.</summary>
    public bool Contains(string name) => Get(name) is not null;

    /// <summary>
    /// Remove the snippet with this name (case-insensitive). Returns true when one was removed, false
    /// when no snippet matched (a missing name is not an error).
    /// </summary>
    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return _byName.Remove(name.Trim());
    }

    /// <summary>Remove every stored snippet.</summary>
    public void Clear() => _byName.Clear();
}
