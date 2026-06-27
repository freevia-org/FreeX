namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstagePaneFocusTarget
{
    None,
    PrintNowButton
}

public sealed record FreeXBackstagePaneFlowPlan(
    FreeXBackstagePaneId Pane,
    bool RefreshGreeting,
    bool ResetRecentTab,
    bool RefreshRecentFiles,
    bool RefreshInfo,
    bool ResetPrintPreviewSettings,
    bool RefreshPrintOptions,
    bool RefreshPrintPreview,
    FreeXBackstagePaneFocusTarget FocusTarget);

public enum FreeXBackstageCommandWorkflowKind
{
    NewWorkbook,
    OpenWorkbook,
    ShareWorkbook,
    SaveWorkbook,
    SaveWorkbookAs,
    ExportWorkbook,
    CloseWorkbook,
    Account,
    Options
}

public sealed record FreeXBackstageCommandWorkflowPlan(
    FreeXBackstageCommandId Command,
    FreeXBackstageCommandWorkflowKind Workflow,
    bool UsesDirtyGate,
    bool UsesSaveResolution,
    bool ForcesSaveAsDialog,
    bool OpensNativeFileDialog);

/// <summary>
/// Renderer-neutral flow metadata for FreeX Backstage panes and file commands. Hosts still own the
/// concrete controls, dialogs, and I/O effects; this planner owns the workflow shape.
/// </summary>
public static class FreeXBackstageFlowPlanner
{
    public static FreeXBackstagePaneFlowPlan BuildPaneFlow(FreeXBackstagePaneId pane) =>
        pane switch
        {
            FreeXBackstagePaneId.Home => new(
                pane,
                RefreshGreeting: true,
                ResetRecentTab: true,
                RefreshRecentFiles: true,
                RefreshInfo: false,
                ResetPrintPreviewSettings: false,
                RefreshPrintOptions: false,
                RefreshPrintPreview: false,
                FocusTarget: FreeXBackstagePaneFocusTarget.None),

            FreeXBackstagePaneId.Info => new(
                pane,
                RefreshGreeting: false,
                ResetRecentTab: false,
                RefreshRecentFiles: false,
                RefreshInfo: true,
                ResetPrintPreviewSettings: false,
                RefreshPrintOptions: false,
                RefreshPrintPreview: false,
                FocusTarget: FreeXBackstagePaneFocusTarget.None),

            FreeXBackstagePaneId.Print => new(
                pane,
                RefreshGreeting: false,
                ResetRecentTab: false,
                RefreshRecentFiles: false,
                RefreshInfo: false,
                ResetPrintPreviewSettings: true,
                RefreshPrintOptions: true,
                RefreshPrintPreview: true,
                FocusTarget: FreeXBackstagePaneFocusTarget.PrintNowButton),

            _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null)
        };

    public static FreeXBackstageCommandWorkflowPlan BuildCommandWorkflow(
        FreeXBackstageCommandId command) =>
        command switch
        {
            FreeXBackstageCommandId.New => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.NewWorkbook,
                usesDirtyGate: true),

            FreeXBackstageCommandId.Open => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.OpenWorkbook,
                usesDirtyGate: true,
                opensNativeFileDialog: true),

            FreeXBackstageCommandId.Share => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.ShareWorkbook,
                usesSaveResolution: true),

            FreeXBackstageCommandId.Save => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.SaveWorkbook,
                usesSaveResolution: true),

            FreeXBackstageCommandId.SaveAs => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.SaveWorkbookAs,
                forcesSaveAsDialog: true,
                opensNativeFileDialog: true),

            FreeXBackstageCommandId.Export => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.ExportWorkbook,
                opensNativeFileDialog: true),

            FreeXBackstageCommandId.Close => FileWorkflow(
                command,
                FreeXBackstageCommandWorkflowKind.CloseWorkbook,
                usesDirtyGate: true),

            FreeXBackstageCommandId.Account => new(
                command,
                FreeXBackstageCommandWorkflowKind.Account,
                UsesDirtyGate: false,
                UsesSaveResolution: false,
                ForcesSaveAsDialog: false,
                OpensNativeFileDialog: false),

            FreeXBackstageCommandId.Options => new(
                command,
                FreeXBackstageCommandWorkflowKind.Options,
                UsesDirtyGate: false,
                UsesSaveResolution: false,
                ForcesSaveAsDialog: false,
                OpensNativeFileDialog: false),

            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

    private static FreeXBackstageCommandWorkflowPlan FileWorkflow(
        FreeXBackstageCommandId command,
        FreeXBackstageCommandWorkflowKind workflow,
        bool usesDirtyGate = false,
        bool usesSaveResolution = false,
        bool forcesSaveAsDialog = false,
        bool opensNativeFileDialog = false) =>
        new(
            command,
            workflow,
            usesDirtyGate,
            usesSaveResolution,
            forcesSaveAsDialog,
            opensNativeFileDialog);
}
