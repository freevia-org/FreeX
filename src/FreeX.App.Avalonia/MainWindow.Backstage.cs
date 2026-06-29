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

        var content = AvaloniaBackstageChrome.CreatePane(
            BuildBackstageInfoPaneSpec(display, dialog),
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
        long? sizeBytes = null;
        System.DateTime? modifiedUtc = null;
        System.DateTime? modifiedLocal = null;
        var path = _session.CurrentFilePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var info = new FileInfo(path);
                sizeBytes = info.Length;
                modifiedUtc = info.LastWriteTimeUtc;
                modifiedLocal = info.LastWriteTime;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return WorkbookInfoPlanner.Build(
            _session.Workbook,
            path,
            ResolveActiveSheetIndex(),
            sizeBytes,
            modifiedUtc,
            modifiedLocal,
            _session.IsDirty);
    }

    private static WorkbookInfoDisplayStrings CreateWorkbookInfoDisplayStrings() =>
        new(UiText.Get, (key, args) => UiText.Format(key, args));

    private AvaloniaBackstagePaneSpec BuildBackstageInfoPaneSpec(
        WorkbookInfoDisplayPlan display,
        Window dialog)
    {
        var elements = new List<AvaloniaBackstagePaneElementSpec>
        {
            new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Info_FileSectionHeader")),
            new AvaloniaBackstageDetailRowsElementSpec(BuildBackstageInfoDetailRows(display)),
        };

        if (display.UnsavedChangesNote is { } unsavedChangesNote)
            elements.Add(new AvaloniaBackstageNoteElementSpec(unsavedChangesNote, "BackstageInfoUnsaved"));

        elements.Add(new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Info_ProtectionSectionHeader")));
        elements.Add(new AvaloniaBackstageNoteElementSpec(display.WorkbookProtectionSummary, "BackstageInfoProtection"));
        elements.Add(new AvaloniaBackstageNoteElementSpec(display.ActiveSheetProtectionSummary, "BackstageInfoActiveSheetProtection"));
        elements.Add(new AvaloniaBackstageActionRowElementSpec(BuildBackstageInfoActionButtons(dialog)));
        elements.Add(new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Info_StatisticsSectionHeader")));
        elements.Add(new AvaloniaBackstageNoteElementSpec(display.StatisticsSummary, "BackstageInfoStatistics"));

        return new AvaloniaBackstagePaneSpec(elements);
    }

    private static IReadOnlyList<AvaloniaBackstageDetailRowSpec> BuildBackstageInfoDetailRows(
        WorkbookInfoDisplayPlan display)
    {
        var rows = new List<AvaloniaBackstageDetailRowSpec>();
        foreach (var detail in FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.AvaloniaInfoDialog))
        {
            rows.Add(new AvaloniaBackstageDetailRowSpec(
                UiText.Get(detail.LabelKey),
                ResolveBackstageInfoDetailValue(detail.Id, display),
                detail.ValueAutomationId));
        }

        return rows;
    }

    private IReadOnlyList<AvaloniaBackstageActionButtonSpec> BuildBackstageInfoActionButtons(Window dialog)
    {
        var actions = new List<AvaloniaBackstageActionButtonSpec>();
        foreach (var action in FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.AvaloniaInfoDialog))
        {
            actions.Add(CreateBackstageClosingActionButtonSpec(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageInfoAction(action.Id)));
        }

        return actions;
    }

    private static string ResolveBackstageInfoDetailValue(
        FreeXBackstageInfoDetailId id,
        WorkbookInfoDisplayPlan display) =>
        id switch
        {
            FreeXBackstageInfoDetailId.WorkbookName => display.WorkbookName,
            FreeXBackstageInfoDetailId.FilePath => display.FilePath,
            FreeXBackstageInfoDetailId.Format => display.Format,
            FreeXBackstageInfoDetailId.FileSize => display.FileSize,
            FreeXBackstageInfoDetailId.LastModified => display.LastModified,
            FreeXBackstageInfoDetailId.SheetCount => display.SheetCount,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

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

        var hasSelection =
            _session.SelectedRange.RowCount > 1 || _session.SelectedRange.ColCount > 1;
        var scopePlan = WorkbookExportScopePlanner.Build(
            _session.Workbook,
            hasSelection,
            WorkbookExportPrintSurface.MacOs);

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
            BuildBackstageExportPaneSpec(
                scopePlan,
                scope => selectedScope = scope,
                outputKind => selectedFormat = outputKind),
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

    private static AvaloniaBackstagePaneSpec BuildBackstageExportPaneSpec(
        WorkbookExportScopePlan scopePlan,
        Action<WorkbookExportPrintScope> selectScope,
        Action<WorkbookExportPrintOutputKind> selectOutputKind)
    {
        var elements = new List<AvaloniaBackstagePaneElementSpec>();
        if (!scopePlan.CanExport)
        {
            elements.Add(new AvaloniaBackstageNoteElementSpec(
                UiText.Get("Backstage_Export_Unavailable"),
                "BackstageExportUnavailable"));
        }

        elements.Add(new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Export_ScopeHeader")));
        elements.Add(new AvaloniaBackstageRadioGroupElementSpec(
            "BackstageExportScope",
            BuildBackstageExportScopeOptions(scopePlan, selectScope)));

        elements.Add(new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Export_FormatHeader")));
        elements.Add(new AvaloniaBackstageRadioGroupElementSpec(
            "BackstageExportFormat",
            BuildBackstageExportFormatOptions(scopePlan, selectOutputKind)));

        return new AvaloniaBackstagePaneSpec(elements);
    }

    private static IReadOnlyList<AvaloniaBackstageRadioOptionSpec> BuildBackstageExportScopeOptions(
        WorkbookExportScopePlan scopePlan,
        Action<WorkbookExportPrintScope> selectScope)
    {
        var options = new List<AvaloniaBackstageRadioOptionSpec>();
        foreach (var option in scopePlan.Scopes)
        {
            var backstageScope = ToBackstageExportScopeId(option.Scope);
            var capturedScope = option.Scope;
            options.Add(new AvaloniaBackstageRadioOptionSpec(
                UiText.Get(FreeXBackstagePaneCatalog.GetExportScopeLabelKey(backstageScope, option.IsAvailable)),
                FreeXBackstagePaneCatalog.GetExportScopeAutomationId(backstageScope),
                () => selectScope(capturedScope))
            {
                IsEnabled = option.IsAvailable,
                IsChecked = option.IsDefault,
            });
        }

        return options;
    }

    private static IReadOnlyList<AvaloniaBackstageRadioOptionSpec> BuildBackstageExportFormatOptions(
        WorkbookExportScopePlan scopePlan,
        Action<WorkbookExportPrintOutputKind> selectOutputKind)
    {
        var options = new List<AvaloniaBackstageRadioOptionSpec>();
        foreach (var outputKind in scopePlan.SupportedOutputKinds)
        {
            var backstageOutputKind = ToBackstageExportOutputKindId(outputKind);
            var capturedKind = outputKind;
            options.Add(new AvaloniaBackstageRadioOptionSpec(
                UiText.Get(FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(backstageOutputKind)),
                FreeXBackstagePaneCatalog.GetExportOutputKindAutomationId(backstageOutputKind),
                () => selectOutputKind(capturedKind))
            {
                IsChecked = outputKind == scopePlan.DefaultOutputKind,
            });
        }

        return options;
    }

    // ── File ▸ Account ────────────────────────────────────────────────────────────
    // Export dialog service-plan to Presentation-catalog adapters.
    private static FreeXBackstageExportScopeId ToBackstageExportScopeId(WorkbookExportPrintScope scope) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => FreeXBackstageExportScopeId.SelectedRange,
            WorkbookExportPrintScope.VisibleWorkbook => FreeXBackstageExportScopeId.VisibleWorkbook,
            WorkbookExportPrintScope.ActiveSheet => FreeXBackstageExportScopeId.ActiveSheet,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

    private static FreeXBackstageExportOutputKindId ToBackstageExportOutputKindId(
        WorkbookExportPrintOutputKind outputKind) =>
        outputKind switch
        {
            WorkbookExportPrintOutputKind.Xps => FreeXBackstageExportOutputKindId.Xps,
            WorkbookExportPrintOutputKind.Pdf => FreeXBackstageExportOutputKindId.Pdf,
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind), outputKind, null)
        };

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
            BuildBackstageAccountPaneSpec(plan, dialog),
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

    private AvaloniaBackstagePaneSpec BuildBackstageAccountPaneSpec(
        LocalAccountInfoPlan plan,
        Window dialog)
    {
        var elements = new List<AvaloniaBackstagePaneElementSpec>
        {
            new AvaloniaBackstageHeadingElementSpec(UiText.Get("Backstage_Account_Title")),
            new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Account_LocalInfoHeading")),
            new AvaloniaBackstageDetailRowsElementSpec(BuildBackstageAccountDetailRows(plan)),
            new AvaloniaBackstageActionRowElementSpec(BuildBackstageAccountActionButtons(plan, dialog)),
            new AvaloniaBackstageSectionHeaderElementSpec(UiText.Get("Backstage_Account_NoticesSectionHeader")),
        };

        foreach (var notice in FreeXBackstagePaneCatalog.BuildAccountNotices())
        {
            elements.Add(new AvaloniaBackstageNoteElementSpec(
                ResolveBackstageAccountNoticeValue(notice.Id, plan),
                notice.AutomationId));
        }

        return new AvaloniaBackstagePaneSpec(elements);
    }

    private IReadOnlyList<AvaloniaBackstageDetailRowSpec> BuildBackstageAccountDetailRows(
        LocalAccountInfoPlan plan)
    {
        var rows = new List<AvaloniaBackstageDetailRowSpec>();
        foreach (var detail in FreeXBackstagePaneCatalog.BuildAccountDetails())
        {
            rows.Add(new AvaloniaBackstageDetailRowSpec(
                UiText.Get(detail.LabelKey),
                ResolveBackstageAccountDetailValue(detail.Id, plan),
                detail.ValueAutomationId));
        }

        return rows;
    }

    private IReadOnlyList<AvaloniaBackstageActionButtonSpec> BuildBackstageAccountActionButtons(
        LocalAccountInfoPlan plan,
        Window dialog)
    {
        var actions = new List<AvaloniaBackstageActionButtonSpec>();
        foreach (var action in FreeXBackstagePaneCatalog.BuildAccountActions(plan.OptionsAvailable))
        {
            actions.Add(CreateBackstageClosingActionButtonSpec(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageAccountAction(action.Id)));
        }

        return actions;
    }

    private string ResolveBackstageAccountDetailValue(
        FreeXBackstageAccountDetailId id,
        LocalAccountInfoPlan plan) =>
        id switch
        {
            // No personalized FreeX user name override is configured, so it falls back to the OS account
            // — matching the Windows page, which shows the same identity for both rows by default.
            FreeXBackstageAccountDetailId.FreeXUserName => ResolveBackstageAccountUserName(plan),
            FreeXBackstageAccountDetailId.LocalOsAccount => ResolveBackstageAccountUserName(plan),
            FreeXBackstageAccountDetailId.Device => plan.DeviceName,
            FreeXBackstageAccountDetailId.AppVersion => plan.VersionText,
            FreeXBackstageAccountDetailId.OptionsFile => UiText.Get("Backstage_Account_OptionsFileLocalProfile"),
            FreeXBackstageAccountDetailId.CurrentWorkbook => ResolveBackstageAccountCurrentWorkbook(),
            FreeXBackstageAccountDetailId.Sharing => UiText.Get("Backstage_Account_SharingSaveAsRequired"),
            FreeXBackstageAccountDetailId.Export => UiText.Get("Backstage_Account_ExportReadyLocal"),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static string ResolveBackstageAccountUserName(LocalAccountInfoPlan plan) =>
        string.IsNullOrWhiteSpace(plan.UserName)
            ? UiText.Get("Backstage_Account_UserLocalOnly")
            : plan.UserName;

    private string ResolveBackstageAccountCurrentWorkbook()
    {
        var path = _session.CurrentFilePath;
        if (!string.IsNullOrWhiteSpace(path))
            return Path.GetFileName(path);

        var name = _session.Workbook.Name;
        return string.IsNullOrWhiteSpace(name)
            ? UiText.Get("Backstage_Account_CurrentWorkbookUnsaved")
            : name;
    }

    private Action ResolveBackstageAccountAction(FreeXBackstageAccountActionId id) =>
        id switch
        {
            FreeXBackstageAccountActionId.Options => ShowOptions,
            FreeXBackstageAccountActionId.LegalNotices => () => _ = ShowLegalNoticesDialogAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

    private static string ResolveBackstageAccountNoticeValue(
        FreeXBackstageAccountNoticeId id,
        LocalAccountInfoPlan plan) =>
        id switch
        {
            FreeXBackstageAccountNoticeId.Trademark => plan.TrademarkNotice,
            FreeXBackstageAccountNoticeId.License => plan.LicenseNotice,
            FreeXBackstageAccountNoticeId.Privacy => plan.PrivacyNotice,
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
