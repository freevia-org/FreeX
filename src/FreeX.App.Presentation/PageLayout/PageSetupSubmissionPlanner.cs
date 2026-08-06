using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageSetupDialogAction
{
    Ok,
    Print,
    PrintPreview,
    Options
}

public enum PageSetupDialogFollowUpAction
{
    None,
    Print,
    PrintPreview,
    ShowPrinterOptions
}

public sealed record PageSetupValidationMessage(string? ResourceKey, string? FallbackText)
{
    public string Resolve(Func<string, string> textProvider, string fallbackResourceKey = PageSetupSubmissionPlanner.DefaultCaptionResourceKey)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        if (!string.IsNullOrWhiteSpace(ResourceKey))
            return textProvider(ResourceKey);

        return !string.IsNullOrWhiteSpace(FallbackText)
            ? FallbackText!
            : textProvider(fallbackResourceKey);
    }
}

public sealed record PageSetupSubmissionValidation(
    PageSetupValidationTarget? Target,
    PageSetupValidationRoute Route,
    PageSetupValidationMessage Message);

public sealed record PageSetupSubmissionPlan(
    PageSetupDialogFields Fields,
    PageSetupDialogAction RequestedAction,
    PageSetupCommandPlan CommandPlan)
{
    public PageSetupTargetCommandBuildResult TryBuildCompositeCommandForTarget(
        Sheet sourceSheet,
        SheetId targetSheetId,
        string label = PageSetupSubmissionPlanner.DefaultCommandLabel) =>
        PageSetupSubmissionPlanner.TryBuildCompositeCommandForTarget(sourceSheet, Fields, targetSheetId, label);

    public PageSetupTargetCommandBuildResult TryBuildCompositeCommandForTargets(
        Sheet sourceSheet,
        IEnumerable<SheetId> targetSheetIds,
        string label = PageSetupSubmissionPlanner.DefaultCommandLabel) =>
        PageSetupSubmissionPlanner.TryBuildCompositeCommandForTargets(sourceSheet, Fields, targetSheetIds, label);

    public PageSetupDialogFollowUpAction FollowUpAction =>
        PageSetupSubmissionPlanner.ResolveFollowUp(RequestedAction);
}

public sealed record PageSetupSubmissionBuildResult(
    PageSetupSubmissionPlan? Submission,
    PageSetupSubmissionValidation? Validation)
{
    public bool Success => Submission is not null;

    public static PageSetupSubmissionBuildResult Ok(PageSetupSubmissionPlan submission) =>
        new(submission, null);

    public static PageSetupSubmissionBuildResult Fail(PageSetupSubmissionValidation validation) =>
        new(null, validation);
}

public sealed record PageSetupTargetCommandBuildResult(
    IWorkbookCommand? Command,
    PageSetupSubmissionValidation? Validation)
{
    public bool Success => Command is not null;

    public static PageSetupTargetCommandBuildResult Ok(IWorkbookCommand command) =>
        new(command, null);

    public static PageSetupTargetCommandBuildResult Fail(PageSetupSubmissionValidation validation) =>
        new(null, validation);
}

public static class PageSetupSubmissionPlanner
{
    public const string DefaultCommandLabel = "Page Setup";
    public const string DefaultCaptionResourceKey = "PageSetup_PageSetup";

    public static PageSetupSubmissionBuildResult TryBuild(
        Sheet sourceSheet,
        PageSetupDialogFields fields,
        PageSetupDialogAction requestedAction = PageSetupDialogAction.Ok)
    {
        var build = PageSetupDialogModel.TryBuildCommandPlan(sourceSheet, fields);
        return build.Success
            ? PageSetupSubmissionBuildResult.Ok(new PageSetupSubmissionPlan(fields, requestedAction, build.Plan!))
            : PageSetupSubmissionBuildResult.Fail(BuildValidation(build.Target, build.Error));
    }

    public static PageSetupTargetCommandBuildResult TryBuildCompositeCommandForTarget(
        Sheet sourceSheet,
        PageSetupDialogFields fields,
        SheetId targetSheetId,
        string label = DefaultCommandLabel)
    {
        var build = PageSetupDialogModel.TryBuildCommandPlan(sourceSheet, fields, targetSheetId);
        return build.Success
            ? PageSetupTargetCommandBuildResult.Ok(build.Plan!.ToComposite(label))
            : PageSetupTargetCommandBuildResult.Fail(BuildValidation(build.Target, build.Error));
    }

    public static PageSetupTargetCommandBuildResult TryBuildCompositeCommandForTargets(
        Sheet sourceSheet,
        PageSetupDialogFields fields,
        IEnumerable<SheetId> targetSheetIds,
        string label = DefaultCommandLabel)
    {
        ArgumentNullException.ThrowIfNull(targetSheetIds);

        var commands = new List<IWorkbookCommand>();
        foreach (var targetSheetId in targetSheetIds)
        {
            var build = TryBuildCompositeCommandForTarget(sourceSheet, fields, targetSheetId, label);
            if (!build.Success)
                return build;

            commands.Add(build.Command!);
        }

        return commands.Count switch
        {
            0 => PageSetupTargetCommandBuildResult.Fail(BuildValidation(null, "No target sheets are selected.")),
            1 => PageSetupTargetCommandBuildResult.Ok(commands[0]),
            _ => PageSetupTargetCommandBuildResult.Ok(new CompositeWorkbookCommand(label, commands)),
        };
    }

    public static PageSetupDialogFollowUpAction ResolveFollowUp(PageSetupDialogAction action) =>
        action switch
        {
            PageSetupDialogAction.Options => PageSetupDialogFollowUpAction.ShowPrinterOptions,
            PageSetupDialogAction.Print => PageSetupDialogFollowUpAction.Print,
            PageSetupDialogAction.PrintPreview => PageSetupDialogFollowUpAction.PrintPreview,
            _ => PageSetupDialogFollowUpAction.None
        };

    public static PageSetupSubmissionValidation BuildValidation(PageSetupValidationTarget? target, string? fallbackText) =>
        new(
            target,
            PageSetupDialogModel.GetValidationRoute(target),
            BuildValidationMessage(target, fallbackText));

    private static PageSetupValidationMessage BuildValidationMessage(PageSetupValidationTarget? target, string? fallbackText) =>
        target switch
        {
            PageSetupValidationTarget.HeaderMargin or PageSetupValidationTarget.FooterMargin =>
                new("PageSetup_InvalidHeaderFooterMarginsMessage", fallbackText),
            PageSetupValidationTarget.Scaling => new("PageSetup_InvalidScalingMessage", fallbackText),
            PageSetupValidationTarget.FirstPageNumber => new("PageSetup_InvalidFirstPageNumberMessage", fallbackText),
            PageSetupValidationTarget.PrintQuality => new("PageSetup_InvalidPrintQualityMessage", fallbackText),
            PageSetupValidationTarget.PrintArea => new("PageSetup_InvalidPrintAreaMessage", fallbackText),
            PageSetupValidationTarget.RepeatRows or PageSetupValidationTarget.RepeatColumns =>
                new("PageSetup_InvalidPrintTitlesMessage", fallbackText),
            _ => new(null, fallbackText),
        };
}
