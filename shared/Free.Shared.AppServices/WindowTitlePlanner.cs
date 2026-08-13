using Free.Shared.IO;

namespace Free.Shared.AppServices;

public enum WindowTitleApplicationPlacement
{
    DocumentThenApplication,
    ApplicationThenDocument
}

public sealed record ApplicationWindowTitleSpec(
    string ApplicationName,
    string DefaultDocumentDisplayName,
    string DirtyMarker,
    string Separator,
    WindowTitleApplicationPlacement ApplicationPlacement = WindowTitleApplicationPlacement.DocumentThenApplication,
    bool CollapseCleanDefaultDocumentTitle = false);

/// <summary>App-neutral product and document-title policy shared by native window binders.</summary>
public static class ApplicationWindowTitlePolicy
{
    public static string Compose(
        ApplicationWindowTitleSpec spec,
        string? displayName,
        bool isDirty,
        string windowSuffix = "",
        string groupSuffix = "",
        bool isDefaultDocument = false)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var resolvedDisplayName = displayName ?? spec.DefaultDocumentDisplayName;
        if (spec.CollapseCleanDefaultDocumentTitle
            && isDefaultDocument
            && !isDirty
            && string.IsNullOrEmpty(windowSuffix)
            && string.IsNullOrEmpty(groupSuffix))
        {
            return spec.ApplicationName;
        }

        return WindowTitlePlanner.Compose(
            resolvedDisplayName,
            spec.ApplicationName,
            isDirty,
            spec.DirtyMarker,
            spec.Separator,
            windowSuffix,
            groupSuffix,
            spec.ApplicationPlacement);
    }
}

/// <summary>
/// Neutral window-title composition: assembles a document title from its
/// display name, an optional window/group suffix, a dirty marker, and the
/// owning application's name. Each app supplies its own conventions (app name,
/// dirty marker, separator) so the WPF hosts keep only rendering and call this
/// pure planner. No WPF / System.Windows dependencies.
/// </summary>
public static class WindowTitlePlanner
{
    /// <summary>
    /// Composes a window title as
    /// <c>{displayName}{windowSuffix}{groupSuffix}{dirtyMarker}{separator}{applicationName}</c>.
    /// </summary>
    /// <param name="displayName">The document's display name (e.g. the workbook name).</param>
    /// <param name="applicationName">The owning application's name (e.g. "FreeX", "FreeW").</param>
    /// <param name="isDirty">Whether the document has unsaved changes.</param>
    /// <param name="dirtyMarker">The marker appended when dirty (e.g. "*" or " *").</param>
    /// <param name="separator">
    /// The text placed between the document portion and the application name
    /// (e.g. " - " or " — ").
    /// </param>
    /// <param name="windowSuffix">
    /// Optional suffix appended immediately after the display name (e.g. " - 2"
    /// for a second window). Defaults to none.
    /// </param>
    /// <param name="groupSuffix">
    /// Optional suffix appended after the window suffix when applicable (e.g.
    /// " [Group]"). Defaults to none.
    /// </param>
    /// <param name="applicationPlacement">
    /// Whether the application name appears after the document portion or before it.
    /// Defaults to document-first, matching the WPF hosts.
    /// </param>
    public static string Compose(
        string displayName,
        string applicationName,
        bool isDirty,
        string dirtyMarker,
        string separator,
        string windowSuffix = "",
        string groupSuffix = "",
        WindowTitleApplicationPlacement applicationPlacement = WindowTitleApplicationPlacement.DocumentThenApplication)
    {
        var window = windowSuffix ?? "";
        var group = groupSuffix ?? "";
        var dirty = isDirty ? dirtyMarker : "";
        var documentTitle = $"{displayName}{window}{group}{dirty}";
        return applicationPlacement switch
        {
            WindowTitleApplicationPlacement.ApplicationThenDocument => $"{applicationName}{separator}{documentTitle}",
            _ => $"{documentTitle}{separator}{applicationName}",
        };
    }

    /// <summary>
    /// Returns a document's display name from a file path: the file name without
    /// its extension.
    /// </summary>
    public static string DisplayNameFromPath(string path) =>
        FilePathPolicy.FileNameWithoutExtensionOr(path, string.Empty);
}
