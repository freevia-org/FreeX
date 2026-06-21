using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;
using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The File backstage panes for the Avalonia/macOS shell: Info, Export, and Account. These complete the
/// File menu the audit flagged as missing the three consolidated panes. Each is a lightweight dialog that
/// renders a PORTABLE, framework-neutral plan
/// (<see cref="WorkbookInfoPlanner"/> / <see cref="WorkbookExportScopePlanner"/> /
/// <see cref="LocalAccountInfoPlanner"/>) so macOS inherits the data shaping; this file only lays out and
/// localizes. Strings flow through <see cref="UiText"/> with the <c>Backstage_*</c> prefix. Export reuses
/// the existing <see cref="WorkbookExportPrintPlanner"/> + PDF path — it adds scope selection, not a new
/// export engine.
/// </summary>
public sealed partial class MainWindow
{
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

        var content = new StackPanel { Spacing = 14 };

        // File section
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Info_FileSectionHeader")));
        var fileGrid = CreateBackstageDetailGrid();
        foreach (var detail in FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.AvaloniaInfoDialog))
        {
            AddBackstageDetailRow(
                fileGrid,
                UiText.Get(detail.LabelKey),
                ResolveBackstageInfoDetailValue(detail.Id, display),
                detail.ValueAutomationId);
        }
        content.Children.Add(fileGrid);

        if (display.UnsavedChangesNote is { } unsavedChangesNote)
            content.Children.Add(CreateBackstageNote(unsavedChangesNote, "BackstageInfoUnsaved"));

        // Protection section
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Info_ProtectionSectionHeader")));
        content.Children.Add(CreateBackstageNote(display.WorkbookProtectionSummary, "BackstageInfoProtection"));
        content.Children.Add(CreateBackstageNote(display.ActiveSheetProtectionSummary, "BackstageInfoActiveSheetProtection"));

        var protectActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        foreach (var action in FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.AvaloniaInfoDialog))
        {
            protectActions.Children.Add(CreateBackstageActionButton(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageInfoAction(action.Id)));
        }
        content.Children.Add(protectActions);

        // Statistics section
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Info_StatisticsSectionHeader")));
        content.Children.Add(CreateBackstageNote(display.StatisticsSummary, "BackstageInfoStatistics"));

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
            Content = content,
        });

        dialog.Content = root;
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

        var content = new StackPanel { Spacing = 14 };

        if (!scopePlan.CanExport)
        {
            content.Children.Add(CreateBackstageNote(UiText.Get("Backstage_Export_Unavailable"), "BackstageExportUnavailable"));
        }

        // Scope radios
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Export_ScopeHeader")));
        var selectedScope = scopePlan.DefaultScope;
        foreach (var option in scopePlan.Scopes)
        {
            var radio = new RadioButton
            {
                GroupName = "BackstageExportScope",
                Content = UiText.Get(FreeXBackstagePaneCatalog.GetExportScopeLabelKey(option.Scope, option.IsAvailable)),
                IsEnabled = option.IsAvailable,
                IsChecked = option.IsDefault,
                Margin = new Thickness(0, 2),
            };
            AutomationProperties.SetAutomationId(radio, FreeXBackstagePaneCatalog.GetExportScopeAutomationId(option.Scope));
            var capturedScope = option.Scope;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    selectedScope = capturedScope;
            };
            content.Children.Add(radio);
        }

        // Format radios (PDF, and XPS only where the surface supports it)
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Export_FormatHeader")));
        var selectedFormat = scopePlan.DefaultOutputKind;
        foreach (var outputKind in scopePlan.SupportedOutputKinds)
        {
            var formatRadio = new RadioButton
            {
                GroupName = "BackstageExportFormat",
                Content = UiText.Get(FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(outputKind)),
                IsChecked = outputKind == scopePlan.DefaultOutputKind,
                Margin = new Thickness(0, 2),
            };
            AutomationProperties.SetAutomationId(formatRadio, FreeXBackstagePaneCatalog.GetExportOutputKindAutomationId(outputKind));
            var capturedKind = outputKind;
            formatRadio.IsCheckedChanged += (_, _) =>
            {
                if (formatRadio.IsChecked == true)
                    selectedFormat = capturedKind;
            };
            content.Children.Add(formatRadio);
        }

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

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
            Content = content,
        });

        dialog.Content = root;
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

        var content = new StackPanel { Spacing = 14 };

        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Account_ProductSectionHeader")));
        var grid = CreateBackstageDetailGrid();
        foreach (var detail in FreeXBackstagePaneCatalog.BuildAccountDetails())
        {
            AddBackstageDetailRow(
                grid,
                UiText.Get(detail.LabelKey),
                ResolveBackstageAccountDetailValue(detail.Id, plan),
                detail.ValueAutomationId);
        }
        content.Children.Add(grid);

        content.Children.Add(CreateBackstageNote(UiText.Get("Backstage_Account_LocalOnlyNote"), "BackstageAccountLocalOnlyNote"));

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        foreach (var action in FreeXBackstagePaneCatalog.BuildAccountActions(plan.OptionsAvailable))
        {
            actionRow.Children.Add(CreateBackstageActionButton(
                UiText.Get(action.LabelKey),
                action.AutomationId,
                dialog,
                ResolveBackstageAccountAction(action.Id)));
        }
        content.Children.Add(actionRow);

        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Account_NoticesSectionHeader")));
        foreach (var notice in FreeXBackstagePaneCatalog.BuildAccountNotices())
        {
            content.Children.Add(CreateBackstageNote(
                ResolveBackstageAccountNoticeValue(notice.Id, plan),
                notice.AutomationId));
        }

        var closeButton = new Button
        {
            Content = UiText.Get("Backstage_Account_CloseButton"),
            MinWidth = 96,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(closeButton, "BackstageAccountCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
            Content = content,
        });

        dialog.Content = root;
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

    private static string ResolveBackstageAccountDetailValue(
        FreeXBackstageAccountDetailId id,
        LocalAccountInfoPlan plan) =>
        id switch
        {
            FreeXBackstageAccountDetailId.Product => plan.ProductName,
            FreeXBackstageAccountDetailId.Version => plan.VersionText,
            FreeXBackstageAccountDetailId.Device => plan.DeviceName,
            FreeXBackstageAccountDetailId.User => string.IsNullOrWhiteSpace(plan.UserName)
                ? UiText.Get("Backstage_Account_UserLocalOnly")
                : plan.UserName,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };

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
    private static TextBlock CreateBackstageSectionHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Foreground = PrimaryInk,
        };

    private static TextBlock CreateBackstageNote(string text, string automationId)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryInk,
            LineHeight = 20,
        };
        AutomationProperties.SetAutomationId(block, automationId);
        return block;
    }

    private static AvaloniaGrid CreateBackstageDetailGrid() =>
        new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 2, 0, 0),
        };

    private static void AddBackstageDetailRow(AvaloniaGrid grid, string label, string value, string valueAutomationId)
    {
        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = SecondaryInk,
            Margin = new Thickness(0, 3, 12, 3),
        };
        AvaloniaGrid.SetColumn(labelBlock, 0);
        AvaloniaGrid.SetRow(labelBlock, rowIndex);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = PrimaryInk,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 3),
        };
        AutomationProperties.SetAutomationId(valueBlock, valueAutomationId);
        AvaloniaGrid.SetColumn(valueBlock, 1);
        AvaloniaGrid.SetRow(valueBlock, rowIndex);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    private static Button CreateBackstageActionButton(string text, string automationId, Window dialog, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            dialog.Close();
            action();
        };
        return button;
    }

    private static Button CreateBackstageCloseButton(string automationId, Window dialog)
    {
        var button = new Button
        {
            Content = UiText.Get("Backstage_Account_CloseButton"),
            MinWidth = 96,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetName(button, UiText.Get("Backstage_Account_CloseButton"));
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => dialog.Close();
        return button;
    }
}
