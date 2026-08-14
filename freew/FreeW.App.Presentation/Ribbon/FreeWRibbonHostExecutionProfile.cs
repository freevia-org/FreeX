using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FreeWRibbonHostExecutionCommands(
    IRibbonStatefulCommand ReviewingPane);

/// <summary>
/// Maps shell-owned operations to canonical FreeW actions. Editor-context and native control
/// commands remain renderer adapters and can replace these bindings before the final build.
/// </summary>
public static class FreeWRibbonHostExecutionProfile
{
    public static FreeWRibbonHostExecutionCommands Register(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonHostExecutionPorts ports,
        bool registerFileAdapterCommands)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        if (registerFileAdapterCommands)
        {
            bindings.Register("freew.backstage", new ActionRibbonCommand(ports.Backstage));
            bindings.Register("freew.new", new ActionRibbonCommand(ports.NewDocument));
            bindings.Register("freew.open", new ActionRibbonCommand(ports.Open));
            bindings.Register("freew.import-pdf-text", CommandOrUnavailable(ports.ImportPdfText));
            bindings.Register("freew.save", new ActionRibbonCommand(ports.Save));
        }

        bindings.BindAction(FreeWRibbonCommandAction.Cut, ports.Cut);
        bindings.BindAction(FreeWRibbonCommandAction.Copy, ports.Copy);
        bindings.BindAction(FreeWRibbonCommandAction.Paste, ports.Paste);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.PastePlain, ports.PastePlainText);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.PasteMerge, ports.PasteMergeFormatting);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.PasteSpecial, ports.OpenPasteSpecial);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.CharBorder, ports.OpenCharacterBorderDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.CharShading, ports.OpenCharacterShadingDialog);
        bindings.BindAction(FreeWRibbonCommandAction.FontDialog, ports.OpenFontDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.ChangeCase, ports.OpenChangeCaseDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.BordersShading, ports.OpenBordersAndShadingDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.TabsDialog, ports.OpenTabsDialog);
        bindings.BindAction(FreeWRibbonCommandAction.ParagraphDialog, ports.OpenParagraphDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.NewStyle, ports.OpenNewStyleDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.ManageStyles, ports.OpenManageStylesDialog);

        var find = bindings.BindAction(
            FreeWRibbonCommandAction.Find,
            ports.OpenFindReplaceDialog);
        bindings.Bind(FreeWRibbonCommandAction.Replace, find);
        bindings.Register("freew.find-replace-dialog", find);

        bindings.BindAction(FreeWRibbonCommandAction.Picture, ports.InsertPicture);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Symbol, ports.OpenSymbolPickerDialog);
        var screenClip = CommandOrUnavailable(ports.CaptureScreenClip);
        bindings.Bind(FreeWRibbonCommandAction.ScreenClipping, screenClip);
        bindings.Register("freew.screenshot", screenClip);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Field, ports.OpenFieldDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.DrawTable, ports.OpenDrawTableDialog);

        var pageSetup = new ActionRibbonCommand(ports.OpenPageSetupDialog);
        bindings.Bind(FreeWRibbonCommandAction.PageSetup, pageSetup);
        bindings.Register("freew.page-setup-dialog", pageSetup);
        bindings.BindAction(
            FreeWRibbonCommandAction.CustomMargins,
            ports.OpenCustomMarginsDialog ?? ports.OpenPageSetupDialog);
        bindings.BindAction(
            FreeWRibbonCommandAction.MorePaperSizes,
            ports.OpenMorePaperSizesDialog ?? ports.OpenPageSetupDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.LineNumbersOptions, ports.OpenLineNumberOptionsDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.HyphenationManual, ports.OpenManualHyphenationDialog);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.HyphenationOptions, ports.OpenHyphenationOptionsDialog);

        ReviewChangeRibbonWorkflow.Register(
            bindings,
            new ReviewChangeRibbonPorts(
                ports.PreviousChange,
                ports.NextChange,
                ports.AcceptThisChange,
                ports.RejectThisChange));
        var statistics = new ActionRibbonCommand(ports.OpenWordCountDialog);
        bindings.Bind(FreeWRibbonCommandAction.Statistics, statistics);
        bindings.Register("freew.word-count", statistics);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Thesaurus, ports.OpenThesaurus);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.CheckAccessibility, ports.CheckAccessibility);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.InspectDocument, ports.InspectDocument);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Compare, ports.CompareDocuments);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Combine, ports.CombineDocuments);
        var reviewingPane = bindings.BindToggle(
            FreeWRibbonCommandAction.ReviewingPane,
            ports.ToggleReviewingPane,
            ports.IsReviewingPaneVisible ?? (static () => false));
        bindings.Register("freew.reviewingpane", reviewingPane);
        BindOptionalToggle(
            bindings,
            FreeWRibbonCommandAction.ShowNotes,
            ports.ToggleNotesPane,
            ports.IsNotesPaneVisible);
        BindOptionalToggle(
            bindings,
            FreeWRibbonCommandAction.ShowMarkupBalloons,
            ports.ToggleReviewBalloons,
            ports.IsReviewBalloonsActive);
        RegisterSupportCommands(bindings, ports);
        return new FreeWRibbonHostExecutionCommands(reviewingPane);
    }

    public static void RegisterSupportCommands(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonHostExecutionPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        BindOrUnavailable(bindings, FreeWRibbonCommandAction.HelpOnline, ports.OpenHelpOnline);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.Feedback, ports.OpenFeedback);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.CopyDiagnostics, ports.CopyDiagnostics);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.CheckUpdates, ports.CheckForUpdates);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.About, ports.OpenAbout);
        BindOrUnavailable(bindings, FreeWRibbonCommandAction.LegalNotices, ports.OpenLegalNotices);
    }

    private static void BindOrUnavailable(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action,
        Action? callback) =>
        bindings.Bind(action, CommandOrUnavailable(callback));

    private static void BindOptionalToggle(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action,
        Action? toggle,
        Func<bool>? isChecked)
    {
        if (toggle is not null && isChecked is not null)
            bindings.BindToggle(action, toggle, isChecked);
    }

    private static IRibbonCommand CommandOrUnavailable(Action? callback) =>
        callback is null
            ? FreeWRibbonExecutionProfile.UnavailableCommand
            : new ActionRibbonCommand(callback);
}
