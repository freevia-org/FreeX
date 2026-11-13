using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>One file or folder in the Insert Hyperlink "Current Folder" list.</summary>
public sealed record HyperlinkFolderEntry(string Name, string FullPath, bool IsFolder);

/// <summary>One entry in the "Place in This Document" tree: a worksheet or a defined name.</summary>
public sealed record HyperlinkDocumentPlace(string Name, bool IsDefinedName);

/// <summary>
/// Excel's Insert Hyperlink dialog panes. The "Link to" bar switches the body between four panes,
/// each with its own fields: Existing File or Web Page (Look in + the folder's files + Address),
/// Place in This Document (cell reference + sheets and defined names), Create New Document (name +
/// full path) and E-mail Address (address + subject). Both renderers build their panes from this.
/// </summary>
public static class HyperlinkDialogPanePlanner
{
    /// <summary>Excel's "Link to" order.</summary>
    public static IReadOnlyList<HyperlinkTargetKind> LinkTypeOrder { get; } =
    [
        HyperlinkTargetKind.ExistingFileOrWebPage,
        HyperlinkTargetKind.PlaceInThisDocument,
        HyperlinkTargetKind.CreateNewDocument,
        HyperlinkTargetKind.EmailAddress,
    ];

    public const string DefaultCellReference = "A1";
    public const int MaxFolderEntries = 500;

    /// <summary>The "Look in" folder: the workbook's folder, else the user's Documents folder.</summary>
    public static string DefaultFolder(string? currentFilePath)
    {
        var directory = string.IsNullOrWhiteSpace(currentFilePath)
            ? null
            : Path.GetDirectoryName(currentFilePath);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>The folder's subfolders then files, alphabetically, hidden and system entries left out.</summary>
    public static IReadOnlyList<HyperlinkFolderEntry> ListFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        try
        {
            var directory = new DirectoryInfo(folder);
            static bool Visible(FileSystemInfo info) =>
                (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

            var folders = directory.EnumerateDirectories()
                .Where(Visible)
                .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(info => new HyperlinkFolderEntry(info.Name, info.FullName, IsFolder: true));
            var files = directory.EnumerateFiles()
                .Where(Visible)
                .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(info => new HyperlinkFolderEntry(info.Name, info.FullName, IsFolder: false));
            return folders.Concat(files).Take(MaxFolderEntries).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>Worksheets (in tab order) then workbook-level defined names, as Excel lists them.</summary>
    public static IReadOnlyList<HyperlinkDocumentPlace> DocumentPlaces(Workbook? workbook)
    {
        if (workbook is null)
            return [];

        var sheets = workbook.Sheets
            .Where(sheet => !sheet.IsChartsheet)
            .Select(sheet => new HyperlinkDocumentPlace(sheet.Name, IsDefinedName: false));
        var names = workbook.NamedRanges.Keys
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new HyperlinkDocumentPlace(name, IsDefinedName: true));
        return sheets.Concat(names).ToArray();
    }

    /// <summary>The link target for a place: <c>'Sheet name'!A1</c> for a sheet, the name itself for a defined name.</summary>
    public static string ComposeDocumentLocation(HyperlinkDocumentPlace place, string? cellReference)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (place.IsDefinedName)
            return place.Name;

        var reference = string.IsNullOrWhiteSpace(cellReference) ? DefaultCellReference : cellReference.Trim();
        return SheetNameFormatter.QuoteIfNeeded(place.Name) + "!" + reference;
    }

    /// <summary>Splits an existing in-document target back into its sheet (or name) and cell reference.</summary>
    public static (string Place, string CellReference) SplitDocumentLocation(string? target)
    {
        var text = (target ?? string.Empty).Trim().TrimStart('#');
        var bang = text.LastIndexOf('!');
        if (bang <= 0)
            return (text, DefaultCellReference);

        var sheet = text[..bang];
        if (sheet.Length >= 2 && sheet[0] == '\'' && sheet[^1] == '\'')
            sheet = sheet[1..^1].Replace("''", "'", StringComparison.Ordinal);

        var reference = text[(bang + 1)..];
        return (sheet, reference.Length == 0 ? DefaultCellReference : reference);
    }

    /// <summary>Excel's E-mail Address pane: <c>mailto:</c> address plus an optional encoded subject.</summary>
    public static string ComposeEmailTarget(string? address, string? subject)
    {
        var trimmed = (address ?? string.Empty).Trim();
        if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["mailto:".Length..];

        if (trimmed.Length == 0)
            return string.Empty;

        var target = "mailto:" + trimmed;
        return string.IsNullOrWhiteSpace(subject)
            ? target
            : target + "?subject=" + Uri.EscapeDataString(subject.Trim());
    }

    /// <summary>Splits a <c>mailto:</c> target back into the pane's address and subject.</summary>
    public static (string Address, string Subject) SplitEmailTarget(string? target)
    {
        var text = (target ?? string.Empty).Trim();
        if (text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            text = text["mailto:".Length..];

        var query = text.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
            return (text, string.Empty);

        var subject = string.Empty;
        foreach (var part in text[(query + 1)..].Split('&'))
        {
            if (part.StartsWith("subject=", StringComparison.OrdinalIgnoreCase))
                subject = Uri.UnescapeDataString(part["subject=".Length..]);
        }

        return (text[..query], subject);
    }

    /// <summary>Excel's Create New Document pane: the name joined to the chosen folder.</summary>
    public static string ComposeNewDocumentPath(string? folder, string? name)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
            return string.Empty;

        return Path.IsPathRooted(trimmedName) || string.IsNullOrWhiteSpace(folder)
            ? trimmedName
            : Path.Combine(folder.Trim(), trimmedName);
    }
}
