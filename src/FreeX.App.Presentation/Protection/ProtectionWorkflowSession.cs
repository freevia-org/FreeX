using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Protection;

public enum ProtectionCommandIntent
{
    ProtectSheet,
    UnprotectSheet,
    ProtectWorkbook,
    UnprotectWorkbook
}

public enum ProtectionWorkflowIssue
{
    None,
    PasswordConfirmationMismatch,
    WorkbookStructureRequired
}

public enum WorkbookWindowsProtectionPolicy
{
    PreserveExisting,
    ApplyDialogSelection
}

public sealed record ProtectionChromePlan(
    string ButtonContentResourceKey,
    string TooltipTitleResourceKey,
    string TooltipDescriptionResourceKey);

public sealed record ProtectionCommandPlan(
    ProtectionCommandIntent CommandIntent,
    IWorkbookCommand? Command,
    string? NormalizedPassword,
    IReadOnlyList<SheetProtectionPermission> Permissions,
    string TitleResourceKey,
    string SuccessMessageResourceKey,
    string SuccessStatusResourceKey,
    string FailureMessageResourceKey,
    ProtectionWorkflowIssue Issue = ProtectionWorkflowIssue.None)
{
    public bool CanExecute => Command is not null && Issue == ProtectionWorkflowIssue.None;
}

public sealed record ProtectionCommandExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    bool IsNoOp = false);

public sealed record ProtectionWorkflowOutcome(
    bool Success,
    bool Executed,
    bool IsNoOp,
    ProtectionCommandIntent CommandIntent,
    string TitleResourceKey,
    string SuccessMessageResourceKey,
    string SuccessStatusResourceKey,
    string FailureMessageResourceKey,
    ProtectionWorkflowIssue Issue,
    string? ErrorMessage)
{
    public bool StateChanged => Success && !IsNoOp;

    public string? ErrorResourceKey => Success
        ? null
        : Issue switch
        {
            ProtectionWorkflowIssue.PasswordConfirmationMismatch => "ShellLoc_PasswordsDoNotMatch",
            ProtectionWorkflowIssue.WorkbookStructureRequired => "ShellLoc_SelectStructureOrWindows",
            _ => FailureMessageResourceKey,
        };
}

public delegate ProtectionCommandExecutionResult ProtectionCommandExecutor(
    IWorkbookCommand command,
    string titleResourceKey);

/// <summary>
/// Owns portable sheet/workbook protection projection, input normalization, command composition,
/// execution outcomes, and the lockWindows metadata transition shared by the WPF and Avalonia shells.
/// Renderers retain their native controls, prompts, focus, dialog lifetime, and resource rendering.
/// </summary>
public sealed class ProtectionWorkflowSession
{
    private readonly Workbook _workbook;
    private readonly ProtectionCommandExecutor _executeCommand;

    public ProtectionWorkflowSession(
        Workbook workbook,
        ProtectionCommandExecutor executeCommand)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
    }

    public SheetProtectionState ProjectSheet(Sheet sheet) => ProtectionStateProjector.ForSheet(sheet);

    public WorkbookProtectionState ProjectWorkbook() => ProtectionStateProjector.ForWorkbook(_workbook);

    public ProtectionWorkflowOutcome ExecuteSheet(Sheet sheet, ProtectSheetOptions options) =>
        Execute(CreateSheetCommandPlan(sheet, options));

    public ProtectionWorkflowOutcome ExecuteSheet(Sheet sheet, string? password) =>
        Execute(CreateSheetCommandPlan(sheet, password));

    public ProtectionWorkflowOutcome ExecuteWorkbook(
        ProtectWorkbookOptions options,
        WorkbookWindowsProtectionPolicy windowsPolicy = WorkbookWindowsProtectionPolicy.ApplyDialogSelection) =>
        Execute(CreateWorkbookCommandPlan(_workbook, options, windowsPolicy));

    public ProtectionWorkflowOutcome ExecuteWorkbook(string? password) =>
        Execute(CreateWorkbookCommandPlan(_workbook, password));

    public ProtectionWorkflowOutcome Execute(ProtectionCommandPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanExecute)
            return CreateOutcome(plan, success: false, executed: false, isNoOp: false, errorMessage: null);

        var result = _executeCommand(plan.Command!, plan.TitleResourceKey);
        return CreateOutcome(
            plan,
            result.Success,
            executed: true,
            result.IsNoOp,
            result.ErrorMessage);
    }

    public static ProtectionChromePlan CreateSheetChromePlan(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return CreateSheetChromePlan(sheet.IsProtected);
    }

    public static ProtectionChromePlan CreateSheetChromePlan(bool isProtected) =>
        isProtected
            ? new ProtectionChromePlan(
                "Protection_UnprotectSheetButton",
                "Protection_UnprotectSheetTitle",
                "Protection_UnprotectSheetDescription")
            : new ProtectionChromePlan(
                "MainWindow_Content_ProtectSheet",
                "MainWindow_TooltipTitle_ProtectSheet",
                "MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword");

    public static ProtectionChromePlan CreateWorkbookChromePlan(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return CreateWorkbookChromePlan(ProtectionStateProjector.ForWorkbook(workbook).IsStructureProtected);
    }

    public static ProtectionChromePlan CreateWorkbookChromePlan(bool isProtected) =>
        isProtected
            ? new ProtectionChromePlan(
                "Protection_UnprotectWorkbookButton",
                "Protection_UnprotectWorkbookTitle",
                "Protection_UnprotectWorkbookDescription")
            : new ProtectionChromePlan(
                "MainWindow_Content_ProtectWorkbook",
                "MainWindow_TooltipTitle_ProtectWorkbook",
                "MainWindow_TooltipDescription_PreventStructuralChangesToTheWorkbookSuchAsAddingDeletingOrRenamingSheet_47267D4F");

    public static ProtectionCommandPlan CreateSheetCommandPlan(Sheet sheet, string? password)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var options = ProtectSheetOptions.FromCorePermissions(
            SheetProtectionOptions.DefaultEnabledPermissions,
            password,
            password);
        return CreateSheetCommandPlan(sheet, options);
    }

    public static ProtectionCommandPlan CreateSheetCommandPlan(Sheet sheet, ProtectSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        var state = ProtectionStateProjector.ForSheet(sheet);
        var password = ProtectionPassword.Normalize(options.Password);
        if (state.IsProtected)
        {
            return CreatePlan(
                ProtectionCommandIntent.UnprotectSheet,
                new UnprotectSheetCommand(sheet.Id, password),
                password,
                [],
                "Protection_UnprotectSheetTitle",
                "Protection_SheetUnprotectedMessage",
                "ShellLoc_UnprotectedSheet",
                "ShellLoc_CouldNotUnprotectSheet");
        }

        var validation = options.ValidatePassword();
        var permissions = options.ToCorePermissions();
        if (!validation.IsValid)
        {
            return CreatePlan(
                ProtectionCommandIntent.ProtectSheet,
                command: null,
                normalizedPassword: null,
                permissions,
                "MainWindowMessage_ProtectSheetTitle",
                "Protection_SheetProtectedMessage",
                "ShellLoc_ProtectedSheet",
                "ShellLoc_CouldNotProtectSheet",
                ProtectionWorkflowIssue.PasswordConfirmationMismatch);
        }

        return CreatePlan(
            ProtectionCommandIntent.ProtectSheet,
            new ProtectSheetCommand(sheet.Id, password, permissions),
            password,
            permissions,
            "MainWindowMessage_ProtectSheetTitle",
            "Protection_SheetProtectedMessage",
            "ShellLoc_ProtectedSheet",
            "ShellLoc_CouldNotProtectSheet");
    }

    public static ProtectionCommandPlan CreateWorkbookCommandPlan(Workbook workbook, string? password)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var options = ProtectWorkbookOptions.Default with
        {
            Password = password,
            PasswordConfirmation = password,
        };
        return CreateWorkbookCommandPlan(
            workbook,
            options,
            WorkbookWindowsProtectionPolicy.PreserveExisting);
    }

    public static ProtectionCommandPlan CreateWorkbookCommandPlan(
        Workbook workbook,
        ProtectWorkbookOptions options,
        WorkbookWindowsProtectionPolicy windowsPolicy = WorkbookWindowsProtectionPolicy.ApplyDialogSelection)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(options);

        var state = ProtectionStateProjector.ForWorkbook(workbook);
        var password = ProtectionPassword.Normalize(options.Password);
        if (state.IsStructureProtected)
        {
            return CreatePlan(
                ProtectionCommandIntent.UnprotectWorkbook,
                new UnprotectWorkbookCommand(password),
                password,
                [],
                "Protection_UnprotectWorkbookTitle",
                "Protection_WorkbookUnprotectedMessage",
                "ShellLoc_UnprotectedWorkbook",
                "ShellLoc_CouldNotUnprotectWorkbook");
        }

        if (!options.ProtectStructure)
        {
            return CreatePlan(
                ProtectionCommandIntent.ProtectWorkbook,
                command: null,
                normalizedPassword: null,
                [],
                "MainWindowMessage_ProtectWorkbookTitle",
                "Protection_WorkbookProtectedMessage",
                "ShellLoc_ProtectedWorkbook",
                "ShellLoc_CouldNotProtectWorkbook",
                ProtectionWorkflowIssue.WorkbookStructureRequired);
        }

        if (!options.ValidatePassword().IsValid)
        {
            return CreatePlan(
                ProtectionCommandIntent.ProtectWorkbook,
                command: null,
                normalizedPassword: null,
                [],
                "MainWindowMessage_ProtectWorkbookTitle",
                "Protection_WorkbookProtectedMessage",
                "ShellLoc_ProtectedWorkbook",
                "ShellLoc_CouldNotProtectWorkbook",
                ProtectionWorkflowIssue.PasswordConfirmationMismatch);
        }

        IWorkbookCommand command = new ProtectWorkbookCommand(password, structureProtected: true);
        if (windowsPolicy == WorkbookWindowsProtectionPolicy.ApplyDialogSelection)
            command = new WorkbookProtectionMetadataTransitionCommand(command, options.ProtectWindows);

        return CreatePlan(
            ProtectionCommandIntent.ProtectWorkbook,
            command,
            password,
            [],
            "MainWindowMessage_ProtectWorkbookTitle",
            "Protection_WorkbookProtectedMessage",
            "ShellLoc_ProtectedWorkbook",
            "ShellLoc_CouldNotProtectWorkbook");
    }

    private static ProtectionCommandPlan CreatePlan(
        ProtectionCommandIntent intent,
        IWorkbookCommand? command,
        string? normalizedPassword,
        IReadOnlyList<SheetProtectionPermission> permissions,
        string titleResourceKey,
        string successMessageResourceKey,
        string successStatusResourceKey,
        string failureMessageResourceKey,
        ProtectionWorkflowIssue issue = ProtectionWorkflowIssue.None) =>
        new(
            intent,
            command,
            normalizedPassword,
            permissions,
            titleResourceKey,
            successMessageResourceKey,
            successStatusResourceKey,
            failureMessageResourceKey,
            issue);

    private static ProtectionWorkflowOutcome CreateOutcome(
        ProtectionCommandPlan plan,
        bool success,
        bool executed,
        bool isNoOp,
        string? errorMessage) =>
        new(
            success,
            executed,
            isNoOp,
            plan.CommandIntent,
            plan.TitleResourceKey,
            plan.SuccessMessageResourceKey,
            plan.SuccessStatusResourceKey,
            plan.FailureMessageResourceKey,
            plan.Issue,
            errorMessage);
}

file sealed class WorkbookProtectionMetadataTransitionCommand(
    IWorkbookCommand command,
    bool lockWindows) : IWorkbookCommand
{
    public string Label => command.Label;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var outcome = command.Apply(ctx);
        if (outcome.Success && !outcome.IsNoOp)
            ApplyLockWindows(ctx.Workbook, lockWindows);
        return outcome;
    }

    public void Revert(ICommandContext ctx) => command.Revert(ctx);

    private static void ApplyLockWindows(Workbook workbook, bool value)
    {
        var current = workbook.ProtectionMetadata;
        var raw = current?.Get("workbookProtection");

        XElement element;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                element = XElement.Parse(raw);
            }
            catch (XmlException)
            {
                element = new XElement("e");
            }
        }
        else
        {
            element = new XElement("e");
        }

        if (value)
            element.SetAttributeValue("lockWindows", "1");
        else
            element.Attribute("lockWindows")?.Remove();

        var clone = current?.Clone() ?? new NativeXmlPreserveBag();
        clone.Set(
            "workbookProtection",
            element.Attributes().Any() || element.HasElements
                ? element.ToString(SaveOptions.DisableFormatting)
                : null);
        workbook.ProtectionMetadata = clone;
    }
}
