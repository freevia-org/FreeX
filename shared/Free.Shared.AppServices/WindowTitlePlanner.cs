using System.IO;

namespace Free.Shared.AppServices;

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
    public static string Compose(
        string displayName,
        string applicationName,
        bool isDirty,
        string dirtyMarker,
        string separator,
        string windowSuffix = "",
        string groupSuffix = "")
    {
        var window = windowSuffix ?? "";
        var group = groupSuffix ?? "";
        var dirty = isDirty ? dirtyMarker : "";
        return $"{displayName}{window}{group}{dirty}{separator}{applicationName}";
    }

    /// <summary>
    /// Returns a document's display name from a file path: the file name without
    /// its extension.
    /// </summary>
    public static string DisplayNameFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path);
}
