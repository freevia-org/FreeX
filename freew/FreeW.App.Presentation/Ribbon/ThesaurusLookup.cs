using System.Reflection;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Compact bundled English thesaurus backed by a plain-text synonym file embedded in this assembly.
/// </summary>
public sealed class ThesaurusLookup
{
    private static ThesaurusLookup? _instance;
    private static readonly object InitLock = new();

    private readonly Dictionary<string, ThesaurusEntry> _entries;

    private ThesaurusLookup()
    {
        _entries = LoadEntries();
    }

    public static ThesaurusLookup Instance
    {
        get
        {
            if (_instance is not null)
                return _instance;
            lock (InitLock)
            {
                _instance ??= new ThesaurusLookup();
            }

            return _instance;
        }
    }

    public int HeadwordCount => _entries.Count;

    public ThesaurusEntry? Lookup(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;
        _entries.TryGetValue(word.Trim().ToLowerInvariant(), out var entry);
        return entry;
    }

    private static Dictionary<string, ThesaurusEntry> LoadEntries()
    {
        var map = new Dictionary<string, ThesaurusEntry>(StringComparer.OrdinalIgnoreCase);
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("thesaurus-en.txt", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return map;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return map;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

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

                var label = tokens[0];
                var synonyms = tokens.Skip(1).ToList();
                if (synonyms.Count > 0)
                    senses.Add(new ThesaurusSense(label, synonyms));
            }

            if (senses.Count > 0 && !map.ContainsKey(headword))
                map[headword] = new ThesaurusEntry(headword, senses);
        }

        return map;
    }
}

public sealed record ThesaurusSense(string Label, IReadOnlyList<string> Synonyms);

public sealed record ThesaurusEntry(string Headword, IReadOnlyList<ThesaurusSense> Senses);
