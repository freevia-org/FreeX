using Free.Shared.AppServices;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Backstage;

/// <summary>
/// The set of panes available in the FreeW backstage (File screen).
/// </summary>
internal enum BackstagePane
{
    Home,
    Open,
    SaveAs,
    Print,
    Share,
    Export,
    Info,
    Account,
}

/// <summary>
/// All the shell-level callbacks the backstage view needs to act on — collected into one record
/// so the view does not take a direct reference to <see cref="FreeW.App.Avalonia.MainWindow"/>.
/// The callbacks are set up by <c>MainWindow.cs</c> and are safe to call from the UI thread after
/// the backstage dialog closes.
/// </summary>
internal sealed record BackstageCallbacks(
    /// <summary>Human-readable document name (file name or "Untitled").</summary>
    string DisplayName,

    /// <summary>Current file path, or <c>null</c> if unsaved.</summary>
    string? CurrentPath,

    /// <summary>Snapshot of recent entries for this session.</summary>
    Func<IEnumerable<RecentFileEntry>> GetRecentEntries,

    /// <summary>All file formats the app can handle (used by Save As / Export planners).</summary>
    Func<IEnumerable<FileFormatDescriptor>> GetFileFormats,

    /// <summary>Current page settings for the Print pane.</summary>
    Func<PageSettings> GetPageSettings,

    /// <summary>Current persisted FreeW options snapshot.</summary>
    Func<FreeWOptions> GetCurrentOptions,

    /// <summary>Current app data folder path or a readable fallback label.</summary>
    Func<string> GetDataFolder,

    /// <summary>Current live document model for Info-pane safety summaries.</summary>
    Func<TextDocument> GetDocument,

    /// <summary>Whether the current document has unsaved changes.</summary>
    Func<bool> GetIsDirty,

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>Create a new empty document.</summary>
    Action NewDocument,

    /// <summary>Open the document at <paramref name="path"/> directly (no picker).</summary>
    Action<string> OpenRecent,

    /// <summary>Open a recent folder in the operating system shell.</summary>
    Action<string> OpenFolder,

    /// <summary>Open the file picker to browse for a document.</summary>
    Action Browse,

    /// <summary>Offer recovery from the latest autosave snapshot.</summary>
    Action RecoverUnsaved,

    /// <summary>Import text from a PDF through the host's existing import path.</summary>
    Action ImportPdfText,

    /// <summary>Save to the current path, falling back to Save As for an untitled document.</summary>
    Action Save,

    /// <summary>Trigger a Save-As dialog (format chosen by the user).</summary>
    Action SaveAs,

    /// <summary>Trigger a Save-As targeting a specific catalog save format (from the planner choice).</summary>
    Action<string, int> SaveAsFormat,

    /// <summary>Write a separate editable copy without changing the active path or dirty state.</summary>
    Action SaveCopy,

    /// <summary>Open the folder containing the current file.</summary>
    Action<string> OpenContainingFolder,

    /// <summary>Export the document as PDF via the existing PDF path.</summary>
    Action ExportPdf,

    /// <summary>Export XPS when the target has a real XPS writer; otherwise absent.</summary>
    Action? ExportXps,

    /// <summary>Edit the document's persisted core properties.</summary>
    Action EditProperties,

    /// <summary>Toggle Word-style Mark as Final advisory read-only state.</summary>
    Action MarkAsFinal,

    /// <summary>Open the document protection/restrict editing surface.</summary>
    Action RestrictEditing,

    /// <summary>Run the Document Inspector and optionally remove selected metadata.</summary>
    Action InspectDocument,

    /// <summary>Run the Accessibility Checker and show its report.</summary>
    Action CheckAccessibility,

    /// <summary>Open the FreeW options editor.</summary>
    Action OpenOptions,

    /// <summary>Close the document through the host dirty gate.</summary>
    Action CloseDocument,

    /// <summary>Host capability/status for direct native print.</summary>
    BackstageDirectPrintCapability? DirectPrintCapability = null,

    /// <summary>Open the host native print surface when the Avalonia target supplies one.</summary>
    Action? Print = null,

    /// <summary>Open the paginated print-preview surface. Direct native printing remains host-deferred.</summary>
    Action? PrintPreview = null);
