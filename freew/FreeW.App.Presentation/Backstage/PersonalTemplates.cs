namespace FreeW.App.Presentation.Backstage;

/// <summary>A personal template file offered by File &gt; New.</summary>
public sealed record PersonalTemplateEntry(string Path, string DisplayName);

/// <summary>
/// Where personal templates live. Word keeps them in "Custom Office Templates" under the user's
/// Documents folder; FreeW uses the same folder by default so templates saved from either application
/// show up in both, and honours an explicit override from <c>FreeWOptions.PersonalTemplatesFolder</c>.
/// </summary>
public static class PersonalTemplatesFolder
{
    public const string DefaultFolderName = "Custom Office Templates";

    private static readonly string[] TemplateExtensions = [".dotx", ".dotm"];

    /// <summary>
    /// Resolves the personal templates folder: the configured path when one is set (environment
    /// variables expanded), otherwise <c>Documents\Custom Office Templates</c>. Never creates it.
    /// </summary>
    public static string Resolve(string? configuredFolder, string? documentsFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
            return Environment.ExpandEnvironmentVariables(configuredFolder.Trim());

        var documents = string.IsNullOrWhiteSpace(documentsFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsFolder;
        return System.IO.Path.Combine(documents, DefaultFolderName);
    }

    /// <summary>True for the Word template formats a personal template can be (.dotx/.dotm).</summary>
    public static bool IsTemplateExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return TemplateExtensions.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The folder a Save As dialog should start in for <paramref name="preferredExtension"/>: the
    /// personal templates folder when saving a Word template (created on demand, as Word does), and
    /// null -- "leave the dialog's own default" -- for every other format or when it cannot be created.
    /// </summary>
    public static string? ResolveSaveDirectory(
        string? preferredExtension,
        string? configuredFolder,
        string? documentsFolder = null)
    {
        if (!IsTemplateExtension(preferredExtension))
            return null;

        try
        {
            var folder = Resolve(configuredFolder, documentsFolder);
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Lists the template files in a personal templates folder.</summary>
public static class PersonalTemplateCatalog
{
    /// <summary>
    /// The .dotx/.dotm files directly inside <paramref name="folder"/>, by name. Office lock files
    /// (<c>~$name.dotx</c>) and hidden files are skipped. A missing or unreadable folder is simply
    /// empty -- listing templates must never fail the New pane.
    /// </summary>
    public static IReadOnlyList<PersonalTemplateEntry> List(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return [];

        try
        {
            if (!Directory.Exists(folder))
                return [];

            return new DirectoryInfo(folder)
                .EnumerateFiles()
                .Where(file => PersonalTemplatesFolder.IsTemplateExtension(file.Extension)
                    && !file.Name.StartsWith("~$", StringComparison.Ordinal)
                    && (file.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(file => new PersonalTemplateEntry(
                    file.FullName,
                    System.IO.Path.GetFileNameWithoutExtension(file.Name)))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
