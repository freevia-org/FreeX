using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host;

/// <summary>
/// FreeX's backstage (the full-window File screen) rebuilt on top of the shared, app-neutral
/// <see cref="BackstageFrame"/> — the de-brittling pilot for the unification program (P1). The hand-rolled
/// <c>StartScreenSidebar</c> rail is gone; this wrapper builds the frame, supplies the 12 rail entries with
/// their full FreeX metadata (key-tips, automation ids/names/help, rich tooltips, command-icon names) and
/// routes each entry back into FreeX's existing handlers.
///
/// The three content panes (<c>SsHomeView</c>/<c>SsInfoView</c>/<c>SsPrintView</c>) are kept exactly as they
/// were in XAML, parked in a hidden holder inside <c>StartScreenOverlay</c>; each pane entry's
/// <see cref="BackstageEntry.ContentFactory"/> first runs that pane's live-refresh logic (the same calls the
/// old <c>Show*View</c> methods made) and then reparents the pane element into the frame's content host.
/// </summary>
public partial class MainWindow
{
    private BackstageFrame? _backstageFrame;

    // Language-invariant pane identifiers (the frame's automation ids) so ShowInfoView()/ShowPrintView()
    // and Ctrl+P can land on a specific pane regardless of the current UI language.
    private const string BackstageHomePaneId = "BackstageHomeButton";
    private const string BackstageInfoPaneId = "BackstageInfoButton";
    private const string BackstagePrintPaneId = "BackstagePrintButton";

    private void InitializeBackstageFrame()
    {
        var frame = new BackstageFrame();
        // FreeX navy rail with darker hover/selection bands, matching the title bar.
        frame.SetAccent(
            sidebar: System.Windows.Media.Color.FromRgb(0x10, 0x25, 0x3A),
            hover: System.Windows.Media.Color.FromRgb(0x1C, 0x3A, 0x55),
            selected: System.Windows.Media.Color.FromRgb(0x24, 0x44, 0x5E),
            separator: System.Windows.Media.Color.FromRgb(0x24, 0x44, 0x5E));

        frame.ConfigureBackButton(
            automationId: "BackstageBackButton",
            automationName: UiText.Get("MainWindow_TooltipTitle_Back"),
            automationHelpText: UiText.Get("MainWindow_ToolTip_BackToWorkbook"),
            toolTip: UiText.Get("MainWindow_ToolTip_BackToWorkbook"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Back"),
            keyTip: "B");

        frame.SetEntries(BuildBackstageEntries());

        // The shared frame stamps the *shared* RibbonTooltip attached properties on each nav button, but
        // FreeX's Alt-keytip overlay (MainWindow.KeyTips.cs) reads FreeX's own RibbonTooltip attached
        // properties. Mirror key-tip/title/description onto the FreeX properties so the rail still lights up
        // under Alt and shows the Excel-style hover card, exactly as the hand-rolled rail did.
        frame.DecorateNavButtons((entry, button) =>
        {
            var keyTip = entry?.KeyTip ?? "B"; // null entry == back arrow
            var title = entry?.TooltipTitle ?? UiText.Get("MainWindow_TooltipTitle_Back");
            RibbonTooltip.SetKeyTip(button, keyTip);
            RibbonTooltip.SetTitle(button, title);
            if (entry?.TooltipDescription is { } description)
                RibbonTooltip.SetDescription(button, description);
        });

        // The frame closes itself (Esc / back arrow / an action entry). Funnel that through HideStartScreen
        // so the overlay collapses and worksheet focus is restored, exactly as the old rail did.
        frame.Closed += () =>
        {
            StartScreenOverlay.Visibility = Visibility.Collapsed;
            SheetGrid.Focus();
        };

        StartScreenFrameHost.Content = frame;
        _backstageFrame = frame;
    }

    private System.Collections.Generic.IEnumerable<BackstageEntry> BuildBackstageEntries()
    {
        // Pane entries swap the content host to the existing FreeX pane after running its refresh logic;
        // command entries fire an existing handler and the frame closes itself first (matching FreeW).
        // iconName routes each rail glyph to FreeX's Office SVG of that name (the same CommandName the old
        // XAML RibbonIcon used); the Kind is the geometry fallback.

        yield return BackstageEntry.Pane(
            UiText.Get("MainWindow_Text_Home"), RibbonCommandIconKind.Grid, BuildHomePane,
            keyTip: "H", automationId: BackstageHomePaneId,
            automationName: UiText.Get("MainWindow_Text_Home"),
            automationHelpText: UiText.Get("MainWindow_TooltipTitle_Home"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Home"),
            iconName: "Home");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_New"), RibbonCommandIconKind.Insert,
            async () => await RequestNewWorkbookAsync(),
            keyTip: "N", automationId: "BackstageNewButton",
            automationName: UiText.Get("MainWindow_Text_New"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_CreateANewWorkbook"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_New"),
            iconName: "New");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Open"), RibbonCommandIconKind.GetData,
            () => OpenButton_Click(this, new RoutedEventArgs()),
            keyTip: "O", automationId: "BackstageOpenButton",
            automationName: UiText.Get("MainWindow_Text_Open"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_OpenAnExistingWorkbook"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Open"),
            iconName: "Open");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Share"), RibbonCommandIconKind.Share,
            async () => await ShareWorkbookAsync(),
            keyTip: "SH", automationId: "BackstageShareButton",
            automationName: UiText.Get("MainWindow_Text_Share"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Share"),
            tooltipDescription: UiText.Get("MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile"),
            iconName: "Share");

        yield return BackstageEntry.Divider();

        yield return BackstageEntry.Pane(
            UiText.Get("MainWindow_Text_Info"), RibbonCommandIconKind.Info, BuildInfoPane,
            keyTip: "I", automationId: BackstageInfoPaneId,
            automationName: UiText.Get("MainWindow_Text_Info"),
            automationHelpText: UiText.Get("MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Info"),
            iconName: "Info");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Save"), RibbonCommandIconKind.Save,
            () => SaveButton_Click(this, new RoutedEventArgs()),
            keyTip: "S", automationId: "BackstageSaveButton",
            automationName: UiText.Get("MainWindow_AutomationName_Save"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_SaveTheWorkbook"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Save"),
            iconName: "Save");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_SaveAs"), RibbonCommandIconKind.Save,
            () => SaveAsButton_Click(this, new RoutedEventArgs()),
            keyTip: "A", automationId: "BackstageSaveAsButton",
            automationName: UiText.Get("MainWindow_TooltipTitle_SaveAs"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_SaveTheWorkbookWithANewNameOrFormat"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_SaveAs"),
            iconName: "Save As");

        yield return BackstageEntry.Pane(
            UiText.Get("MainWindow_Text_Print"), RibbonCommandIconKind.Print, BuildPrintPane,
            keyTip: "P", automationId: BackstagePrintPaneId,
            automationName: UiText.Get("MainWindow_AutomationName_Print"),
            automationHelpText: UiText.Get("MainWindow_AutomationHelpText_OpenPrintPreviewWithWorksheetSettingsAndNativePrintAccess"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Print"),
            tooltipDescription: UiText.Get("MainWindow_TooltipDescription_OpenThePrintPreviewAndNativePrintDialogForTheRenderedWorksheet"),
            iconName: "Print");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Export"), RibbonCommandIconKind.Share,
            () => ExportPdfButton_Click(this, new RoutedEventArgs()),
            keyTip: "E", automationId: "BackstageExportButton",
            automationName: UiText.Get("MainWindow_TooltipTitle_ExportPDFXPS"),
            automationHelpText: UiText.Get("MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_ExportPDFXPS"),
            tooltipDescription: UiText.Get("MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage"),
            iconName: "Export");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Close"), RibbonCommandIconKind.WindowClose,
            Close,
            keyTip: "C", automationId: "BackstageCloseButton",
            automationName: UiText.Get("MainWindow_AutomationName_Close"),
            automationHelpText: UiText.Get("MainWindow_TooltipTitle_Close"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Close"),
            iconName: "Close");

        yield return BackstageEntry.Divider(dockBottom: true);

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Account"), RibbonCommandIconKind.Info,
            () => SsAccountBtn_Click(this, new RoutedEventArgs()), dockBottom: true,
            keyTip: "AC", automationId: "BackstageAccountButton",
            automationName: UiText.Get("MainWindow_AutomationName_Account"),
            automationHelpText: UiText.Get("MainWindow_AutomationHelpText_ShowLocalAccountInformationForFreeX"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_LocalAccount"),
            tooltipDescription: UiText.Get("MainWindow_TooltipDescription_MicrosoftAccountIntegrationIsNotImplementedFreeXUsesLocalFilesAndLocalOp_EC989658"),
            iconName: "Account");

        yield return BackstageEntry.Command(
            UiText.Get("MainWindow_Text_Options"), RibbonCommandIconKind.View,
            () => SsOptionsBtn_Click(this, new RoutedEventArgs()), dockBottom: true,
            keyTip: "T", automationId: "BackstageOptionsButton",
            automationName: UiText.Get("MainWindow_AutomationName_Options"),
            automationHelpText: UiText.Get("MainWindow_AutomationHelpText_OpenFreeXSettingsAndFormulaErrorCheckingOptions"),
            tooltipTitle: UiText.Get("MainWindow_TooltipTitle_Options"),
            iconName: "Options");
    }

    // ── Pane content factories ──────────────────────────────────────────────────
    // Each runs the same live-refresh the old Show*View methods did, then hands the existing pane element to
    // the frame (after detaching it from its current parent — a WPF element has exactly one logical parent).

    private UIElement BuildHomePane()
    {
        UpdateSsGreeting();
        SwitchToRecentTab();
        UpdateSsRecentList();
        return ReparentForBackstage(SsHomeView);
    }

    private UIElement BuildInfoPane()
    {
        UpdateInfoView();
        return ReparentForBackstage(SsInfoView);
    }

    private UIElement BuildPrintPane()
    {
        var activeSheet = _workbook.GetSheet(_currentSheetId);
        _backstagePrintPreviewSettings = new PrintPreviewSettings();
        ConfigureBackstagePrintOptions(activeSheet);
        RefreshBackstagePrintPreview();
        var pane = ReparentForBackstage(SsPrintView);
        // The print pane lands focus on Print Now (Ctrl+P / the screenshot tour rely on this).
        Dispatcher.BeginInvoke(() =>
        {
            SsBackstagePrintNowButton.Focus();
            System.Windows.Input.Keyboard.Focus(SsBackstagePrintNowButton);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        return pane;
    }

    // Make a pane visible (the holder kept them collapsed) and detach it from whatever parent currently
    // owns it, so the frame's ContentControl can adopt it without a "already has a logical parent" error.
    private static UIElement ReparentForBackstage(FrameworkElement pane)
    {
        Detach(pane);
        pane.Visibility = Visibility.Visible;
        return pane;
    }

    // Remove a FrameworkElement from its current logical/visual parent, handling the parent shapes the
    // backstage panes can sit under (Panel / ContentControl / ContentPresenter / Decorator / ItemsControl).
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
            case ContentPresenter presenter when ReferenceEquals(presenter.Content, element):
                presenter.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ItemsControl itemsControl:
                itemsControl.Items.Remove(element);
                break;
        }
    }
}
