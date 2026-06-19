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
        AddBackstageDetailRow(fileGrid, UiText.Get("Backstage_Info_NameLabel"), plan.WorkbookName, "BackstageInfoName");
        AddBackstageDetailRow(
            fileGrid,
            UiText.Get("Backstage_Info_PathLabel"),
            plan.IsSaved ? plan.FilePath! : UiText.Get("Backstage_Info_NotSavedYet"),
            "BackstageInfoPath");
        AddBackstageDetailRow(fileGrid, UiText.Get("Backstage_Info_FormatLabel"), plan.FormatExtension, "BackstageInfoFormat");
        AddBackstageDetailRow(fileGrid, UiText.Get("Backstage_Info_SizeLabel"), FormatBackstageFileSize(plan), "BackstageInfoSize");
        AddBackstageDetailRow(fileGrid, UiText.Get("Backstage_Info_ModifiedLabel"), FormatBackstageLastModified(plan), "BackstageInfoModified");
        AddBackstageDetailRow(
            fileGrid,
            UiText.Get("Backstage_Info_SheetsLabel"),
            plan.SheetCount.ToString(CultureInfo.CurrentCulture),
            "BackstageInfoSheets");
        content.Children.Add(fileGrid);

        if (plan.HasUnsavedChanges)
            content.Children.Add(CreateBackstageNote(UiText.Get("Backstage_Info_UnsavedChanges"), "BackstageInfoUnsaved"));

        // Protection section
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Info_ProtectionSectionHeader")));
        content.Children.Add(CreateBackstageNote(FormatBackstageProtection(plan), "BackstageInfoProtection"));
        content.Children.Add(CreateBackstageNote(
            plan.ActiveSheetIsProtected
                ? UiText.Get("Backstage_Info_ActiveSheetProtected")
                : UiText.Get("Backstage_Info_ActiveSheetUnprotected"),
            "BackstageInfoActiveSheetProtection"));

        var protectActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        protectActions.Children.Add(CreateBackstageActionButton(
            UiText.Get("Backstage_Info_ProtectSheetAction"),
            "BackstageInfoProtectSheetButton",
            dialog,
            ProtectSheet));
        protectActions.Children.Add(CreateBackstageActionButton(
            UiText.Get("Backstage_Info_ProtectWorkbookAction"),
            "BackstageInfoProtectWorkbookButton",
            dialog,
            ProtectWorkbook));
        protectActions.Children.Add(CreateBackstageActionButton(
            UiText.Get("Backstage_Info_InspectAction"),
            "BackstageInfoInspectButton",
            dialog,
            () => _ = ShowReviewSummaryDialogAsync()));
        content.Children.Add(protectActions);

        // Statistics section
        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Info_StatisticsSectionHeader")));
        content.Children.Add(CreateBackstageNote(FormatBackstageStatistics(plan.Statistics), "BackstageInfoStatistics"));

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

    private static string FormatBackstageFileSize(WorkbookInfoPlan plan)
    {
        if (!plan.IsSaved)
            return UiText.Get("Backstage_Info_NotSavedYet");
        if (!plan.FileExistsOnDisk || plan.FileSizeBytes is not { } bytes)
            return UiText.Get("Backstage_Info_FileMissing");

        return FormatByteSize(bytes);
    }

    private static string FormatBackstageLastModified(WorkbookInfoPlan plan)
    {
        if (!plan.IsSaved)
            return UiText.Get("Backstage_Info_NotSavedYet");
        if (!plan.FileExistsOnDisk || plan.LastModifiedLocal is not { } modified)
            return UiText.Get("Backstage_Info_FileMissing");

        return modified.ToString("g", CultureInfo.CurrentCulture);
    }

    private static string FormatBackstageProtection(WorkbookInfoPlan plan) =>
        plan.ProtectionPosture switch
        {
            WorkbookProtectionPosture.StructureAndSheetsProtected => UiText.Format(
                "Backstage_Info_ProtectionStructureAndSheets",
                plan.ProtectedSheetCount.ToString(CultureInfo.CurrentCulture),
                plan.SheetCount.ToString(CultureInfo.CurrentCulture)),
            WorkbookProtectionPosture.StructureProtected => UiText.Get("Backstage_Info_ProtectionStructure"),
            WorkbookProtectionPosture.SheetsProtected => UiText.Format(
                "Backstage_Info_ProtectionSheets",
                plan.ProtectedSheetCount.ToString(CultureInfo.CurrentCulture),
                plan.SheetCount.ToString(CultureInfo.CurrentCulture)),
            _ => UiText.Get("Backstage_Info_ProtectionNone")
        };

    private static string FormatBackstageStatistics(WorkbookStatistics statistics) =>
        string.Join(Environment.NewLine,
            $"Cells with data: {statistics.CellCount}",
            $"Formulas: {statistics.FormulaCount}",
            $"Charts: {statistics.ChartCount}",
            $"Pictures: {statistics.PictureCount}",
            $"Named ranges: {statistics.NamedRangeCount}");

    private static string FormatByteSize(long bytes)
    {
        bytes = Math.Max(0, bytes);
        var culture = CultureInfo.CurrentCulture;
        if (bytes < 1024)
            return $"{bytes.ToString("N0", culture)} B";

        double value = bytes;
        var unitIndex = -1;
        string[] units = ["KB", "MB", "GB", "TB"];
        do
        {
            value /= 1024;
            unitIndex++;
        }
        while (value >= 1024 && unitIndex < units.Length - 1);

        var valueText = value >= 10
            ? value.ToString("N0", culture)
            : value.ToString("N1", culture);
        return $"{valueText} {units[unitIndex]}";
    }

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
                Content = FormatExportScopeLabel(option.Scope, option.IsAvailable),
                IsEnabled = option.IsAvailable,
                IsChecked = option.IsDefault,
                Margin = new Thickness(0, 2),
            };
            AutomationProperties.SetAutomationId(radio, "BackstageExportScope_" + option.Scope);
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
                Content = outputKind == WorkbookExportPrintOutputKind.Xps
                    ? UiText.Get("Backstage_Export_FormatXps")
                    : UiText.Get("Backstage_Export_FormatPdf"),
                IsChecked = outputKind == scopePlan.DefaultOutputKind,
                Margin = new Thickness(0, 2),
            };
            AutomationProperties.SetAutomationId(formatRadio, "BackstageExportFormat_" + outputKind);
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

    private static string FormatExportScopeLabel(WorkbookExportPrintScope scope, bool isAvailable) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => isAvailable
                ? UiText.Get("Backstage_Export_ScopeSelection")
                : UiText.Get("Backstage_Export_ScopeSelectionUnavailable"),
            WorkbookExportPrintScope.VisibleWorkbook => UiText.Get("Backstage_Export_ScopeWorkbook"),
            _ => UiText.Get("Backstage_Export_ScopeActiveSheet")
        };

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
        AddBackstageDetailRow(grid, UiText.Get("Backstage_Account_ProductLabel"), plan.ProductName, "BackstageAccountProduct");
        AddBackstageDetailRow(grid, UiText.Get("Backstage_Account_VersionLabel"), plan.VersionText, "BackstageAccountVersion");
        AddBackstageDetailRow(grid, UiText.Get("Backstage_Account_DeviceLabel"), plan.DeviceName, "BackstageAccountDevice");
        AddBackstageDetailRow(
            grid,
            UiText.Get("Backstage_Account_UserLabel"),
            string.IsNullOrWhiteSpace(plan.UserName)
                ? UiText.Get("Backstage_Account_UserLocalOnly")
                : plan.UserName,
            "BackstageAccountUser");
        content.Children.Add(grid);

        content.Children.Add(CreateBackstageNote(UiText.Get("Backstage_Account_LocalOnlyNote"), "BackstageAccountLocalOnlyNote"));

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        if (plan.OptionsAvailable)
        {
            actionRow.Children.Add(CreateBackstageActionButton(
                UiText.Get("Backstage_Account_OptionsButton"),
                "BackstageAccountOptionsButton",
                dialog,
                ShowOptions));
        }
        actionRow.Children.Add(CreateBackstageActionButton(
            UiText.Get("Backstage_Account_LegalNoticesButton"),
            "BackstageAccountLegalNoticesButton",
            dialog,
            () => _ = ShowLegalNoticesDialogAsync()));
        content.Children.Add(actionRow);

        content.Children.Add(CreateBackstageSectionHeader(UiText.Get("Backstage_Account_NoticesSectionHeader")));
        content.Children.Add(CreateBackstageNote(plan.TrademarkNotice, "BackstageAccountTrademark"));
        content.Children.Add(CreateBackstageNote(plan.LicenseNotice, "BackstageAccountLicense"));
        content.Children.Add(CreateBackstageNote(plan.PrivacyNotice, "BackstageAccountPrivacy"));

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
