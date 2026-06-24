using System.IO;
using System.Reflection;

namespace FreeW.App.Host;

/// <summary>
/// Catalog of bundled content-icons for Insert &gt; Icons.
/// Icons are clean monochrome SVGs shipped as Content files under
/// <c>Resources/ContentIconsSvg/{category}/{name}.svg</c>.
/// At runtime the catalog resolves each entry to an absolute file path
/// relative to the host assembly's directory — the same mechanism the
/// ribbon's SvgCommandIconLoader uses for ribbon glyphs.
/// </summary>
internal static class ContentIconCatalog
{
    /// <summary>A single entry in the icon catalog.</summary>
    internal sealed record IconEntry(
        /// <summary>Display name (title-cased, spaces from filename).</summary>
        string Name,
        /// <summary>Category label (People, Arrows, Technology, Business, Shapes, Symbols).</summary>
        string Category,
        /// <summary>Search keywords (lower-case, space-separated, derived from name + category).</summary>
        string Keywords,
        /// <summary>Absolute path to the SVG file at runtime.</summary>
        string Path);

    // ── Catalogue ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>All entries, lazily built once.</summary>
    public static IReadOnlyList<IconEntry> All => _all.Value;

    private static readonly Lazy<IReadOnlyList<IconEntry>> _all =
        new(BuildCatalog, isThreadSafe: true);

    private static IReadOnlyList<IconEntry> BuildCatalog()
    {
        // Resolve the content-icon folder relative to the host DLL; at runtime the Content items
        // are copied next to the executable, so this matches the output structure.
        var asm = Assembly.GetExecutingAssembly().Location;
        var asmDir = System.IO.Path.GetDirectoryName(asm) ?? AppContext.BaseDirectory;
        var iconRoot = System.IO.Path.Combine(asmDir, "Resources", "ContentIconsSvg");

        var entries = new List<IconEntry>();

        if (!Directory.Exists(iconRoot))
            return entries; // test runner not copied yet — empty is handled gracefully

        foreach (var categoryDir in Directory.EnumerateDirectories(iconRoot).OrderBy(d => d))
        {
            var category = TitleCase(System.IO.Path.GetFileName(categoryDir));
            foreach (var file in Directory.EnumerateFiles(categoryDir, "*.svg").OrderBy(f => f))
            {
                var stem = System.IO.Path.GetFileNameWithoutExtension(file);
                var name = TitleCase(stem.Replace('-', ' '));
                // keywords: name words + category (lower-case, space-joined)
                var keywords = string.Join(' ', name.ToLowerInvariant(), category.ToLowerInvariant());
                entries.Add(new IconEntry(name, category, keywords, file));
            }
        }

        return entries;
    }

    /// <summary>
    /// Filter the catalog by category and/or a search term.
    /// Both filters are optional; passing null/empty returns all entries.
    /// Matching is case-insensitive, substring on name + keywords.
    /// </summary>
    public static IEnumerable<IconEntry> Filter(string? category, string? search)
    {
        var result = (IEnumerable<IconEntry>)All;

        if (!string.IsNullOrWhiteSpace(category) && category != AllCategoriesLabel)
            result = result.Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            result = result.Where(e => e.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    /// <summary>Distinct sorted category labels, suitable for a filter drop-down.</summary>
    public static IReadOnlyList<string> Categories =>
        All.Select(e => e.Category).Distinct().OrderBy(c => c).ToList();

    /// <summary>Sentinel value meaning "all categories".</summary>
    public const string AllCategoriesLabel = "(All)";

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────
    private static string TitleCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w =>
            w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
