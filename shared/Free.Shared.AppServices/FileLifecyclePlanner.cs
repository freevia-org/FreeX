using Free.Shared.IO;

namespace Free.Shared.AppServices;

/// <summary>
/// The user's answer to the "save changes before doing X?" prompt that guards a destructive
/// action (New / Open / Close on a dirty document).
/// </summary>
public enum SaveChangesPrompt
{
    /// <summary>Save the changes, then proceed with the destructive action.</summary>
    Save,

    /// <summary>Discard the changes and proceed with the destructive action.</summary>
    DontSave,

    /// <summary>Abort the destructive action and keep the document open as-is.</summary>
    Cancel
}

/// <summary>
/// What the host must do <em>before</em> a destructive action (New / Open / Close) is allowed to
/// proceed, given the document's current dirty-state. Returned by
/// <see cref="FileLifecyclePlanner.PlanDirtyGate"/>.
/// </summary>
public enum DirtyGateIntent
{
    /// <summary>
    /// The document is clean — proceed with the destructive action immediately, no prompt.
    /// </summary>
    ProceedWithoutPrompt,

    /// <summary>
    /// The document is dirty — show the Save / Don't&#160;Save / Cancel prompt and feed the answer
    /// back through <see cref="FileLifecyclePlanner.ResolveDirtyGate"/> to learn what to do next.
    /// </summary>
    PromptSaveChanges
}

/// <summary>
/// The host action to take after the dirty-gate prompt has been answered. Returned by
/// <see cref="FileLifecyclePlanner.ResolveDirtyGate"/>.
/// </summary>
public enum DirtyGateAction
{
    /// <summary>Abort the destructive action; keep the document open.</summary>
    Cancel,

    /// <summary>
    /// Run a Save first (resolving Save-vs-SaveAs via <see cref="FileLifecyclePlanner.PlanSave"/>),
    /// and only proceed with the destructive action if that save succeeds.
    /// </summary>
    SaveThenProceed,

    /// <summary>Discard the unsaved changes and proceed with the destructive action.</summary>
    ProceedDiscardingChanges
}

/// <summary>
/// How a Save command should resolve: write to the existing file, or fall through to Save-As.
/// Returned by <see cref="FileLifecyclePlanner.PlanSave"/>.
/// </summary>
public enum FileSaveIntent
{
    /// <summary>
    /// The document already has a usable path — write to it directly (no dialog). Only produced when
    /// the document is dirty; a clean document with a path yields <see cref="NothingToDo"/>.
    /// </summary>
    UseExistingPath,

    /// <summary>
    /// No usable existing path — show the Save-As dialog to choose a target, then write to it.
    /// </summary>
    PromptSaveAs,

    /// <summary>
    /// The document is clean and already saved to its current path — the Save is a no-op.
    /// Hosts that always re-serialize on Save can treat this exactly like
    /// <see cref="UseExistingPath"/>; hosts that want to skip redundant writes can short-circuit.
    /// </summary>
    NothingToDo
}

/// <summary>
/// Whether a successfully opened/saved file should be registered in the recent-files (MRU) list.
/// Returned by <see cref="FileLifecyclePlanner.PlanRecentRegistration"/>.
/// </summary>
public enum RecentFileRegistration
{
    /// <summary>Register the path in the MRU list.</summary>
    Register,

    /// <summary>Do not register (e.g. recovery snapshots, template instantiations, transient paths).</summary>
    Skip
}

/// <summary>
/// A neutral request for the host's native file-open dialog. The host (any platform) maps
/// <see cref="Filter"/> / <see cref="DefaultExtension"/> onto its dialog and returns the chosen
/// path in a <see cref="FileDialogResult"/>.
/// </summary>
/// <param name="Filter">
///   The format-filter string in the platform-conventional form (e.g. on Windows
///   <c>"Word documents (*.docx)|*.docx|All files (*.*)|*.*"</c>). Built by
///   <see cref="FileDialogFilterBuilder"/>.
/// </param>
/// <param name="DefaultExtension">The default extension (with leading dot), e.g. <c>".docx"</c>.</param>
public sealed record FileOpenRequest(string Filter, string DefaultExtension);

/// <summary>
/// A neutral request for the host's native save-as dialog.
/// </summary>
/// <param name="Filter">The format-filter string (see <see cref="FileOpenRequest.Filter"/>).</param>
/// <param name="DefaultExtension">The default extension (with leading dot), e.g. <c>".docx"</c>.</param>
/// <param name="SuggestedFileName">
///   The initial file name to seed the dialog with (no directory), e.g. <c>"Document.docx"</c> for a
///   new document or the current file's name for a Save-As of an existing one.
/// </param>
public sealed record FileSaveAsRequest(string Filter, string DefaultExtension, string SuggestedFileName);

/// <summary>The outcome of a native file dialog: a chosen path, or cancellation.</summary>
/// <param name="Path">The chosen full path, or <c>null</c> when the user cancelled.</param>
public sealed record FileDialogResult(string? Path)
{
    /// <summary>The cancellation result (no path chosen).</summary>
    public static FileDialogResult Cancelled { get; } = new((string?)null);

    /// <summary>True when the user chose a path.</summary>
    public bool Chosen => !string.IsNullOrWhiteSpace(Path);
}

/// <summary>
/// Host seam for the native file dialogs. Implemented once per host/platform (WPF uses
/// <c>Microsoft.Win32.OpenFileDialog</c>/<c>SaveFileDialog</c>; Avalonia/macOS use their own pickers).
/// Kept here in the neutral tier so the planner can express the full lifecycle while every I/O
/// effect stays on the host side.
/// </summary>
public interface IFileDialogService
{
    /// <summary>Shows the open dialog; returns the chosen path or <see cref="FileDialogResult.Cancelled"/>.</summary>
    FileDialogResult ShowOpenDialog(FileOpenRequest request);

    /// <summary>Shows the save-as dialog; returns the chosen path or <see cref="FileDialogResult.Cancelled"/>.</summary>
    FileDialogResult ShowSaveAsDialog(FileSaveAsRequest request);
}

/// <summary>
/// Neutral, portable decision logic for the repeated file-lifecycle ceremony shared across the app
/// family — independent of document format.
///
/// <para>
/// It owns the <em>decisions</em>:
/// </para>
/// <list type="bullet">
///   <item><b>Dirty-gate</b> before a destructive action (New/Open/Close):
///     <see cref="PlanDirtyGate"/> + <see cref="ResolveDirtyGate"/>.</item>
///   <item><b>Save-vs-Save-As resolution</b>: <see cref="PlanSave"/>.</item>
///   <item><b>Recent-files registration</b> after a successful open/save:
///     <see cref="PlanRecentRegistration"/>.</item>
/// </list>
///
/// <para>
/// It does <b>not</b> perform any I/O effect — showing dialogs (<see cref="IFileDialogService"/>),
/// reading/writing the document bytes, prompting the user (<see cref="IUserMessageService"/>) and
/// touching the <see cref="RecentFilesStore"/> all stay on the host side. This mirrors the FreeX
/// <c>FileSavePlanner</c> / <c>WindowCloseDecisionPlanner</c> split, promoted to the shared tier so
/// FreeX (later) and FreeW (now) execute the same ceremony.
/// </para>
/// </summary>
public static class FileLifecyclePlanner
{
    /// <summary>
    /// Decides whether a destructive action (New / Open / Close) needs to prompt to save first.
    /// </summary>
    /// <param name="isDirty">Whether the document has unsaved changes.</param>
    /// <returns>
    ///   <see cref="DirtyGateIntent.ProceedWithoutPrompt"/> when clean, otherwise
    ///   <see cref="DirtyGateIntent.PromptSaveChanges"/>.
    /// </returns>
    public static DirtyGateIntent PlanDirtyGate(bool isDirty) =>
        isDirty ? DirtyGateIntent.PromptSaveChanges : DirtyGateIntent.ProceedWithoutPrompt;

    /// <summary>
    /// Maps the user's <see cref="SaveChangesPrompt"/> answer to the host action to take.
    /// </summary>
    public static DirtyGateAction ResolveDirtyGate(SaveChangesPrompt answer) => answer switch
    {
        SaveChangesPrompt.Save => DirtyGateAction.SaveThenProceed,
        SaveChangesPrompt.DontSave => DirtyGateAction.ProceedDiscardingChanges,
        SaveChangesPrompt.Cancel => DirtyGateAction.Cancel,
        _ => DirtyGateAction.Cancel
    };

    /// <summary>
    /// Resolves a Save command into <see cref="FileSaveIntent.UseExistingPath"/>,
    /// <see cref="FileSaveIntent.PromptSaveAs"/> or <see cref="FileSaveIntent.NothingToDo"/>.
    /// </summary>
    /// <param name="isDirty">Whether the document has unsaved changes.</param>
    /// <param name="currentFilePath">
    ///   The document's current path, or <c>null</c>/blank for a never-saved document.
    /// </param>
    /// <returns>
    ///   <list type="bullet">
    ///     <item>No usable path → <see cref="FileSaveIntent.PromptSaveAs"/> (regardless of dirtiness:
    ///       a never-saved document must always be given a target).</item>
    ///     <item>Has a path and dirty → <see cref="FileSaveIntent.UseExistingPath"/>.</item>
    ///     <item>Has a path and clean → <see cref="FileSaveIntent.NothingToDo"/>.</item>
    ///   </list>
    /// </returns>
    public static FileSaveIntent PlanSave(bool isDirty, string? currentFilePath)
    {
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return FileSaveIntent.PromptSaveAs;

        return isDirty ? FileSaveIntent.UseExistingPath : FileSaveIntent.NothingToDo;
    }

    /// <summary>
    /// Decides whether a successfully opened/saved file should be registered in the MRU list.
    /// </summary>
    /// <param name="path">The path that was opened or saved to.</param>
    /// <param name="suppressRecentFiles">
    ///   When <c>true</c> (recovery snapshots, transient template paths), the path is not registered.
    /// </param>
    public static RecentFileRegistration PlanRecentRegistration(string? path, bool suppressRecentFiles)
    {
        if (suppressRecentFiles || string.IsNullOrWhiteSpace(path))
            return RecentFileRegistration.Skip;

        return RecentFileRegistration.Register;
    }
}
