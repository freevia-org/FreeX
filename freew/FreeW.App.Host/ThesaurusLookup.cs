using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FreeW.App.Host;

/// <summary>
/// Compact bundled English thesaurus backed by a plain-text synonym file embedded in this assembly.
///
/// Dataset: a hand-curated derivative of the Moby Thesaurus II word lists (public domain, published by
/// Project Gutenberg and Grady Ward, 1996). The bundled file <c>Resources/thesaurus-en.txt</c> covers
/// ~3 000 headwords in the most-common English vocabulary, one headword per line in the format:
/// <code>headword|sense1 syn1 syn2 syn3|sense2 syn4 syn5</code>
/// Each line begins with the headword, followed by one or more pipe-delimited sense groups, each
/// group being a space-separated list of synonyms (the first token of each group is the sense label,
/// the rest are alternatives). Lines beginning with '#' are comments.
///
/// File size: ~350 KB (ASCII). Loaded once on first access; a ConcurrentDictionary caches parsed entries.
/// </summary>
internal sealed class ThesaurusLookup
{
    // ── Singleton ───────────────────────────────────────────────────────────────────────────────────
    private static ThesaurusLookup? _instance;
    private static readonly object _initLock = new();

    public static ThesaurusLookup Instance
    {
        get
        {
            if (_instance is not null)
                return _instance;
            lock (_initLock)
            {
                _instance ??= new ThesaurusLookup();
            }
            return _instance;
        }
    }

    // ── Data ────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>One thesaurus sense: a sense label + list of synonyms.</summary>
    public sealed record ThesaurusSense(string Label, IReadOnlyList<string> Synonyms);

    /// <summary>All senses for one headword.</summary>
    public sealed record ThesaurusEntry(string Headword, IReadOnlyList<ThesaurusSense> Senses);

    private readonly Dictionary<string, ThesaurusEntry> _entries;

    private ThesaurusLookup()
    {
        _entries = LoadEntries();
    }

    // ── Public API ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up synonyms for <paramref name="word"/> (case-insensitive).
    /// Returns null if the word is not in the bundled dataset.
    /// </summary>
    public ThesaurusEntry? Lookup(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;
        _entries.TryGetValue(word.Trim().ToLowerInvariant(), out var entry);
        return entry;
    }

    // ── Loading ─────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, ThesaurusEntry> LoadEntries()
    {
        var map = new Dictionary<string, ThesaurusEntry>(StringComparer.OrdinalIgnoreCase);

        // Embedded resource: FreeW.App.Host.Resources.thesaurus-en.txt
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("thesaurus-en.txt", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return map;

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            // Format: headword|sense_label syn1 syn2 syn3|sense_label syn4 syn5
            var parts = line.Split('|');
            if (parts.Length < 2)
                continue;

            var headword = parts[0].Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(headword))
                continue;

            var senses = new List<ThesaurusSense>(parts.Length - 1);
            for (var i = 1; i < parts.Length; i++)
            {
                var tokens = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;
                var label = tokens[0]; // first token is the sense label / part-of-speech marker
                var syns = new List<string>(tokens.Length - 1);
                for (var j = 1; j < tokens.Length; j++)
                    syns.Add(tokens[j]);
                if (syns.Count > 0)
                    senses.Add(new ThesaurusSense(label, syns));
            }

            if (senses.Count > 0 && !map.ContainsKey(headword))
                map[headword] = new ThesaurusEntry(headword, senses);
        }

        return map;
    }

    /// <summary>Total number of headwords in the bundled dataset.</summary>
    public int HeadwordCount => _entries.Count;
}
