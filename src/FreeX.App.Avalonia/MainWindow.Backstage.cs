using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.Core.Commands;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The File backstage panes for the Avalonia/macOS shell: Info, Export, and Account. These complete the
/// File menu the audit flagged as missing the three consolidated panes. Each is a lightweight dialog that
/// adapts a PORTABLE, framework-neutral plan into shared Avalonia pane specs
/// (<see cref="WorkbookInfoPlanner"/> / <see cref="WorkbookExportScopePlanner"/> /
/// <see cref="LocalAccountInfoPlanner"/>) so macOS inherits the data shaping; this file only wires and
/// localizes. Strings flow through <see cref="UiText"/> with the <c>Backstage_*</c> prefix. Export reuses
/// the existing <see cref="WorkbookExportPrintPlanner"/> + PDF path — it adds scope selection, not a new
/// export engine.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly AvaloniaBackstageChromeStyle BackstageChromeStyle = new(PrimaryInk!, SecondaryInk!)
    {
        SectionHeaderFontSize = 14,
        SectionHeaderMargin = default,
        DetailLabelMargin = new Thickness(0, 3, 12, 3),
        NoteLineHeight = 20,
    };

    // ── File ▸ Info ────────────────────────────────────────────────────────────
    private void ShowBackstageInfo() => _ = ShowBackstageInfoDialogAsync();

    private async Task ShowBackstageInfoDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        // R129-model-avalonia-info-formula-issues-1: mirrors the WPF host's UpdateInfoView comment
        // -- under Manual calculation a freshly-typed circular formula is never recalculated until
        // F9/save/an automatic-mode edit, so _session.CyclicCells would otherwise still be empty
        // here and File > Info would under-report circular references relative to Formulas > Error
        // Checking (CheckFormulaErrorsAsync, MainWindow.ErrorChecking.cs), which recalculates first.
        _session.RecalculateWorkbook();

        var plan = BuildWorkbookInfoPlan();
        var display = WorkbookInfoDisplayPlanner.Build(
            plan,
            WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog,
            CreateWorkbookInfoDisplayStrings(),
            CultureInfo.CurrentCulture);

        var dialog = new Window
        {
            Title = UiText.Get("Backstage_Info_Title"),
            Width = 460,
            Height = 560,
            MinWidth = 420,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "BackstageInfoDialog");

        var closeButton = CreateBackstageCloseButton("BackstageInfoCloseButton", dialog);

        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.AvaloniaInfoDialog,
            CreateBackstageInfoPaneRequest(display));
        var content = AvaloniaBackstageChrome.CreatePane(
            BuildBackstagePaneSpec(
                FreeXBackstagePaneProjectionPlanner.BuildInfoDialog(pane),
                dialog),
            BackstageChromeStyle);

        dialog.Content = AvaloniaBackstageChrome.CreateDialogLayout(
            new AvaloniaBackstageDialogLayoutSpec(content, closeButton));
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => closeButton.Focus();
        await dialog.ShowDialog(this);
    }

    private WorkbookInfoPlan BuildWorkbookInfoPlan()
    {
        return WorkbookInfoFileMetadataReader.BuildPlan(
            _session.Workbook,
            _session.CurrentFilePath,
            ResolveActiveSheetIndex(),
            hasUnsavedChanges: _session.IsDirty,
            // R129-model-avalonia-info-formula-issues-1: same cyclic-cell source the WPF host's
            // UpdateInfoView feeds BackstageInfoPlanner.Build (_recalcEngine.CyclicCells) -- without
            // this, a Linux/macOS user with a circular reference got no indication from File > Info
            // while a Windows user did.
            cyclicCells: _session.CyclicCells);
    }

    private static WorkbookInfoDisplayStrings CreateWorkbookInfoDisplayStrings() =>
        new(UiText.Get, (key, args) => UiText.Format(key, args));

    private static FreeXBackstageInfoPaneRequest CreateBackstageInfoPaneRequest(
        WorkbookInfoDisplayPlan display) =>
        new(
            display.WorkbookName,
            display.FilePath,
            display.SheetCount,
            display.Format,
            display.FileSize,
            display.LastModified,
            SharingStatus: string.Empty,
            ExportStatus: string.Empty,
            display.WorkbookProtectionSummary,
            display.ActiveSheetProtectionSummary,
            display.StatisticsSummary,
            AccessibilitySummary: string.Empty,
            display.FormulaErrorSummary,
            display.UnsavedChangesNote);

    private IReadOnlyList<AvaloniaBackstageActionButtonSpec> BuildBackstageInfoActionButtons(
        IReadOnlyList<FreeXBackstageInfoActionPlan> actions,
        Window dialog)
    {
        var buttons = new List<AvaloniaBackstageActionButtonSpec>();
        foreach (var action in actions)
        {
            buttons.Add(CreateBackstageClosingActionButtonSpec(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageInfoAction(action.Id)));
        }

        return buttons;
    }

    private Action ResolveBackstageInfoAction(FreeXBackstageInfoActionId id) =>
        id switch
        {
            FreeXBackstageInfoActionId.ProtectSheet => ProtectSheet,
            FreeXBackstageInfoActionId.ProtectWorkbook => ProtectWorkbook,
            FreeXBackstageInfoActionId.InspectWorkbook => () => _ = ShowReviewSummaryDialogAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    // ── File ▸ Export ────────────────────────────────────────────────────────────
    private void ShowBackstageExport() => _ = ShowBackstageExportDialogAsync();

    private async Task ShowBackstageExportDialogAsync()
    {
        if (_isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var hasSelection = HasPrintSelection(_session.SelectedRange);
        var scopePlan = WorkbookExportWorkflow.CreateScopePlan(
            _session.Workbook,
            hasSelection,
            WorkbookExportPrintSurface.MacOs);
        var exportPane = FreeXBackstageExportPanePlanner.Build(
            FreeXBackstageExportPanePlanner.CreateRequest(
                scopePlan.Scopes
                    .Select(scope => new FreeXBackstageExportScopeOptionSource<WorkbookExportPrintScope>(
                        scope.Scope,
                        scope.IsAvailable,
                        scope.IsDefault))
                    .ToArray(),
                scopePlan.SupportedOutputKinds,
                scopePlan.DefaultOutputKind,
                scopePlan.CanExport));

        var dialog = new Window
        {
            Title = UiText.Get("Backstage_Export_Title"),
            Width = 400,
            Height = 360,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "BackstageExportDialog");

        var selectedScope = scopePlan.DefaultScope;
        var selectedFormat = scopePlan.DefaultOutputKind;
        var content = AvaloniaBackstageChrome.CreatePane(
            BuildBackstagePaneSpec(
                FreeXBackstagePaneProjectionPlanner.BuildExportDialog(exportPane),
                dialog,
                scope => selectedScope = FreeXBackstageExportPanePlanner.ToExternalScope<WorkbookExportPrintScope>(scope),
                outputKind => selectedFormat = FreeXBackstageExportPanePlanner.ToExternalOutputKind<WorkbookExportPrintOutputKind>(outputKind)),
            BackstageChromeStyle);

        var exportButton = new Button
        {
            Content = UiText.Get("Backstage_Export_CreateButton"),
            MinWidth = 96,
            Padding = new Thickness(10, 4),
            IsEnabled = scopePlan.CanExport,
        };
        AutomationProperties.SetAutomationId(exportButton, "BackstageExportCreateButton");
        exportButton.Click += async (_, _) =>
        {
            dialog.Close();
            await ExportWorkbookPdfAsync(selectedScope, selectedFormat);
        };

        var cancelButton = new Button
        {
            Content = UiText.Get("Backstage_Export_CancelButton"),
            MinWidth = 96,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(cancelButton, "BackstageExportCancelButton");
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { cancelButton, exportButton },
        };

        dialog.Content = AvaloniaBackstageChrome.CreateDialogLayout(
            new AvaloniaBackstageDialogLayoutSpec(content, buttonRow));
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => exportButton.Focus();
        await dialog.ShowDialog(this);
    }

    private static IReadOnlyList<AvaloniaBackstageRadioOptionSpec> BuildBackstageExportScopeOptions(
        IReadOnlyList<FreeXBackstageExportRadioOptionProjection> scopeOptions,
        Action<FreeXBackstageExportScopeId> selectScope)
    {
        var options = new List<AvaloniaBackstageRadioOptionSpec>();
        foreach (var option in scopeOptions.OfType<FreeXBackstageExportScopeRadioOptionProjection>())
        {
            var capturedScope = option.Scope;
            options.Add(new AvaloniaBackstageRadioOptionSpec(
                UiText.Get(option.LabelKey),
                option.AutomationId,
                () => selectScope(capturedScope))
            {
                IsEnabled = option.IsEnabled,
                IsChecked = option.IsDefault,
            });
        }

        return options;
    }

    private static IReadOnlyList<AvaloniaBackstageRadioOptionSpec> BuildBackstageExportFormatOptions(
        IReadOnlyList<FreeXBackstageExportRadioOptionProjection> outputKindOptions,
        Action<FreeXBackstageExportOutputKindId> selectOutputKind)
    {
        var options = new List<AvaloniaBackstageRadioOptionSpec>();
        foreach (var option in outputKindOptions.OfType<FreeXBackstageExportOutputKindRadioOptionProjection>())
        {
            var capturedKind = option.OutputKind;
            options.Add(new AvaloniaBackstageRadioOptionSpec(
                UiText.Get(option.LabelKey),
                option.AutomationId,
                () => selectOutputKind(capturedKind))
            {
                IsChecked = option.IsDefault,
            });
        }

        return options;
    }

    // ── File ▸ Account ────────────────────────────────────────────────────────────
    private void ShowBackstageAccount() => _ = ShowBackstageAccountDialogAsync();

    private async Task ShowBackstageAccountDialogAsync()
    {
        var plan = LocalAccountInfoPlanner.Build(
            typeof(MainWindow).Assembly,
            deviceName: SafeEnvironment(() => Environment.MachineName),
            userName: SafeEnvironment(() => Environment.UserName),
            optionsAvailable: true);

        var dialog = new Window
        {
            Title = UiText.Get("Backstage_Account_Title"),
            Width = 520,
            Height = 540,
            MinWidth = 460,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "BackstageAccountDialog");

        var closeButton = CreateBackstageCloseButton("BackstageAccountCloseButton", dialog);
        var content = AvaloniaBackstageChrome.CreatePane(
            BuildBackstagePaneSpec(
                FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(
                    BuildBackstageAccountPanePlan(plan)),
                dialog),
            BackstageChromeStyle);

        dialog.Content = AvaloniaBackstageChrome.CreateDialogLayout(
            new AvaloniaBackstageDialogLayoutSpec(content, closeButton));
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => closeButton.Focus();
        await dialog.ShowDialog(this);
    }

    private FreeXBackstageAccountPanePlan BuildBackstageAccountPanePlan(
        LocalAccountInfoPlan plan) =>
        FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            plan.UserName,
            plan.DeviceName,
            plan.VersionText,
            plan.OptionsAvailable,
            _session.CurrentFilePath,
            _session.Workbook.Name,
            plan.TrademarkNotice,
            plan.LicenseNotice,
            plan.PrivacyNotice));

    private IReadOnlyList<AvaloniaBackstageActionButtonSpec> BuildBackstageAccountActionButtons(
        IReadOnlyList<FreeXBackstageAccountActionDefinition> actions,
        Window dialog)
    {
        var buttons = new List<AvaloniaBackstageActionButtonSpec>();
        foreach (var action in actions)
        {
            buttons.Add(CreateBackstageClosingActionButtonSpec(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageAccountAction(action.Id)));
        }

        return buttons;
    }

    private AvaloniaBackstagePaneSpec BuildBackstagePaneSpec(
        FreeXBackstagePaneProjectionPlan projection,
        Window dialog,
        Action<FreeXBackstageExportScopeId>? selectScope = null,
        Action<FreeXBackstageExportOutputKindId>? selectOutputKind = null)
    {
        var elements = new List<AvaloniaBackstagePaneElementSpec>();
        foreach (var element in projection.Elements)
        {
            elements.Add(element switch
            {
                FreeXBackstageHeadingProjectionElement heading =>
                    new AvaloniaBackstageHeadingElementSpec(UiText.Get(heading.TextKey)),
                FreeXBackstageSectionHeaderProjectionElement section =>
                    new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get(section.TextKey)),
                FreeXBackstageNoteProjectionElement note =>
                    new AvaloniaBackstageNoteElementSpec(
                        ResolveBackstageTextValue(note.Text),
                        note.AutomationId),
                FreeXBackstageDetailRowsProjectionElement details =>
                    new AvaloniaBackstageDetailRowsElementSpec(BuildBackstageDetailRows(details.Rows)),
                FreeXBackstageInfoActionRowProjectionElement actions =>
                    new AvaloniaBackstageActionRowElementSpec(BuildBackstageInfoActionButtons(actions.Actions, dialog)),
                FreeXBackstageAccountActionRowProjectionElement actions =>
                    new AvaloniaBackstageActionRowElementSpec(BuildBackstageAccountActionButtons(actions.Actions, dialog)),
                FreeXBackstageExportRadioGroupProjectionElement group =>
                    new AvaloniaBackstageRadioGroupElementSpec(
                        group.GroupAutomationId,
                        BuildBackstageExportOptions(group.Options, selectScope, selectOutputKind)),
                _ => throw new ArgumentOutOfRangeException(nameof(element), element, null),
            });
        }

        return new AvaloniaBackstagePaneSpec(elements);
    }

    private static IReadOnlyList<AvaloniaBackstageDetailRowSpec> BuildBackstageDetailRows(
        IReadOnlyList<FreeXBackstageDetailRowProjection> details)
    {
        var rows = new List<AvaloniaBackstageDetailRowSpec>();
        foreach (var detail in details)
        {
            rows.Add(new AvaloniaBackstageDetailRowSpec(
                UiText.Get(detail.LabelKey),
                ResolveBackstageTextValue(detail.Value),
                detail.ValueAutomationId));
        }

        return rows;
    }

    private static IReadOnlyList<AvaloniaBackstageRadioOptionSpec> BuildBackstageExportOptions(
        IReadOnlyList<FreeXBackstageExportRadioOptionProjection> options,
        Action<FreeXBackstageExportScopeId>? selectScope,
        Action<FreeXBackstageExportOutputKindId>? selectOutputKind) =>
        options.FirstOrDefault() switch
        {
            FreeXBackstageExportScopeRadioOptionProjection => BuildBackstageExportScopeOptions(
                options,
                selectScope ?? throw new ArgumentNullException(nameof(selectScope))),
            FreeXBackstageExportOutputKindRadioOptionProjection => BuildBackstageExportFormatOptions(
                options,
                selectOutputKind ?? throw new ArgumentNullException(nameof(selectOutputKind))),
            null => [],
            _ => throw new ArgumentOutOfRangeException(nameof(options), options, null)
        };

    private static string ResolveBackstageTextValue(FreeXBackstageTextValue value) =>
        value.TextKey is { } key
            ? UiText.Get(key)
            : value.Text ?? string.Empty;

    private Action ResolveBackstageAccountAction(FreeXBackstageAccountActionId id) =>
        id switch
        {
            FreeXBackstageAccountActionId.Options => ShowOptions,
            FreeXBackstageAccountActionId.LegalNotices => () => _ = ShowLegalNoticesDialogAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static string SafeEnvironment(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    // ── shared backstage chrome helpers ─────────────────────────────────────────
    private static AvaloniaBackstageActionButtonSpec CreateBackstageClosingActionButtonSpec(
        string text,
        string automationId,
        Window dialog,
        Action action) =>
        new(
            text,
            automationId,
            () =>
            {
                dialog.Close();
                action();
            });

    private static Button CreateBackstageCloseButton(string automationId, Window dialog)
    {
        var closeText = UiText.Get("Backstage_Account_CloseButton");
        return AvaloniaBackstageChrome.CreateActionButton(new AvaloniaBackstageActionButtonSpec(
            closeText,
            automationId,
            dialog.Close)
        {
            AutomationName = closeText,
            MinWidth = 96,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        });
    }
}
