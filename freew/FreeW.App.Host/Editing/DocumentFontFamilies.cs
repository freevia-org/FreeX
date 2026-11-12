using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Maps a document font name to the WPF <see cref="FontFamily"/> the renderer draws it with. An installed
/// face resolves by name as before. A face that is not installed but that Office has downloaded on demand
/// into its per-user cloud-font cache (%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts\&lt;name&gt;) — for
/// example Traditional Arabic, which Word fetches for Arabic documents — loads from that cache, because
/// Word on the same machine draws the document with exactly that file; substituting another face instead
/// changes advance widths and so every line break and page count. The cache is only read, never written.
/// </summary>
internal static class DocumentFontFamilies
{
    private static readonly Lazy<HashSet<string>> s_installed = new(() =>
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            names.Add(family.Source);
            foreach (var localized in family.FamilyNames.Values)
                names.Add(localized);
        }
        return names;
    });

    private static readonly ConcurrentDictionary<string, FontFamily?> s_cloudFamilies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> s_modelNameBySource = new(StringComparer.Ordinal);

    /// <summary>The Office cloud-font cache root; overridable so tests can point at a scratch folder.</summary>
    internal static string? CloudFontRoot { get; set; } = DefaultCloudFontRoot();

    public static bool IsInstalled(string name) => s_installed.Value.Contains(name);

    /// <summary>True when the face can be drawn as itself: installed, or present in the cloud-font cache.</summary>
    public static bool IsAvailable(string name) =>
        !string.IsNullOrWhiteSpace(name) && (IsInstalled(name) || TryGetCloudFamily(name) is not null);

    /// <summary>The WPF family for a document font name (installed by name, else the cached cloud file).</summary>
    public static FontFamily Create(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !IsInstalled(name) && TryGetCloudFamily(name) is { } cloud)
            return cloud;
        return new FontFamily(name);
    }

    /// <summary>
    /// The document font name for a WPF family read back off a live run: a cloud-cache family's
    /// <see cref="FontFamily.Source"/> is a file URI, which must not leak into the model on commit.
    /// </summary>
    public static string ModelName(FontFamily family) =>
        s_modelNameBySource.TryGetValue(family.Source, out var name) ? name : family.Source;

    internal static FontFamily? TryGetCloudFamily(string name) =>
        s_cloudFamilies.GetOrAdd(name, static n => LoadCloudFamily(CloudFontRoot, n));

    internal static void ResetCloudCacheForTests() => s_cloudFamilies.Clear();

    internal static FontFamily? LoadCloudFamily(string? root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        try
        {
            var directory = Path.Combine(root, name);
            if (!Directory.Exists(directory))
                return null;
            foreach (var family in Fonts.GetFontFamilies(new Uri(directory + Path.DirectorySeparatorChar)))
            {
                if (!family.FamilyNames.Values.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                    && !family.Source.EndsWith("#" + name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // An absolute file-URI source (not GetFontFamilies' "./#name" relative to a BaseUri) so the
                // family survives the XamlWriter clone the print/section-aware paginator makes of each block.
                var absolute = new FontFamily(
                    "file:///" + directory.Replace('\\', '/').TrimEnd('/') + "/#" + name);
                s_modelNameBySource[absolute.Source] = name;
                return absolute;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
        }
        return null;
    }

    private static string? DefaultCloudFontRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local)
            ? null
            : Path.Combine(local, "Microsoft", "FontCache", "4", "CloudFonts");
    }
}
