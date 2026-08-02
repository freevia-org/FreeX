using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.Accessibility;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Protection;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Presentation.SparklineUI;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;
using SubtotalColumnChoice = FreeX.App.Presentation.DataTools.SubtotalDialogColumnChoice;
using SharedRibbon = Free.Shared.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// Headless cross-platform visual-parity capture for the WPF host.
///
/// <para>
/// Invoked via <c>FreeX.App.Host.exe --parity-capture &lt;outDir&gt;</c>: renders each app SURFACE
/// (ribbon tabs, grid, dialogs, backstage panes) to a PNG using <see cref="RenderTargetBitmap"/> on the
/// (already-STA) WPF dispatcher thread — no visible foreground window is required; windows are laid out and
/// rendered offscreen. The output (one PNG per surface + a <c>manifest.json</c>) is byte-pairable with the
/// Avalonia/Linux capture so a runner can diff the two shells surface-by-surface.
/// </para>
///
/// <para>
/// Each surface is captured inside its own try/catch: a surface that cannot render offscreen is recorded as
/// <c>captured:false</c> with a diagnostic note, and never aborts the rest of the run.
/// </para>
/// </summary>
internal static class ParityCapture
{
    private const double SurfaceWidth = 1120;
    private const double SurfaceHeight = 720;

    /// <summary>The CLI switch that selects this mode.</summary>
    public const string Switch = "--parity-capture";

    private sealed record SurfaceResult(
        string Id,
        string Kind,
        string Png,
        bool Captured,
        string Note,
        int? Width = null,
        int? Height = null,
        string? EvidenceProvenance = null);

    /// <summary>
    /// Returns the output directory if <paramref name="args"/> requests parity capture, else null.
    /// </summary>
    public static string? TryGetOutputDirectory(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], Switch, StringComparison.Ordinal))
                continue;

            if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]))
                return args[i + 1];

            // Support --parity-capture=<dir> form too.
            return null;
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith(Switch + "=", StringComparison.Ordinal))
            {
                var value = arg[(Switch.Length + 1)..];
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Optional focused capture selector used by parity workers to refresh a single expensive dialog surface.
    /// </summary>
    public static string? TryGetTargetSurfaceId(IReadOnlyList<string> args)
    {
        const string TargetSwitch = "--parity-capture-target";

        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], TargetSwitch, StringComparison.Ordinal))
                continue;

            return i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1])
                ? args[i + 1]
                : null;
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith(TargetSwitch + "=", StringComparison.Ordinal))
            {
                var value = arg[(TargetSwitch.Length + 1)..];
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Renders every surface to <paramref name="outDir"/> and writes the manifest. Runs on the WPF dispatcher
    /// thread (already STA). Swallows per-surface failures; only a catastrophic I/O failure on the manifest
    /// propagates.
    /// </summary>
    public static void Run(string outDir, Func<MainWindow> mainWindowFactory, string? targetSurfaceId = null)
    {
        Directory.CreateDirectory(outDir);
        var results = new List<SurfaceResult>();

        // Closing the (only) main window must not tear down the Application before the dialog surfaces are
        // captured. Default ShutdownMode is OnLastWindowClose, which would shut us down mid-run; switch to
        // explicit so we control teardown via the caller's Shutdown().
        if (Application.Current is { } app)
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (string.IsNullOrWhiteSpace(targetSurfaceId) ||
            !targetSurfaceId.StartsWith("dialog.", StringComparison.Ordinal))
        {
            CaptureRibbonAndShell(outDir, mainWindowFactory, results, targetSurfaceId);
        }

        if (string.IsNullOrWhiteSpace(targetSurfaceId) ||
            targetSurfaceId.StartsWith("dialog.", StringComparison.Ordinal))
        {
            CaptureDialogs(outDir, results, targetSurfaceId);
        }

        WriteManifest(outDir, results);

        var captured = results.Count(r => r.Captured);
        Console.WriteLine(
            $"[parity-capture] wrote {captured}/{results.Count} surfaces + manifest.json to {outDir}");
    }

    // ----- Ribbon tabs + grid + backstage: driven from one live, offscreen MainWindow -----

    private static void CaptureRibbonAndShell(
        string outDir,
        Func<MainWindow> mainWindowFactory,
        List<SurfaceResult> results,
        string? targetSurfaceId = null)
    {
        MainWindow? window = null;
        try
        {
            window = mainWindowFactory();
            // Lay the window out offscreen at a fixed size; no foreground / taskbar presence.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = false;
            window.Left = -10000;
            window.Top = -10000;
            window.Width = SurfaceWidth;
            window.Height = SurfaceHeight;
            window.Show();
            PumpDispatcher();
            // Render the same fixed demo workbook as the Avalonia capture so grid + data-dependent surfaces
            // compare identical CONTENT (the live shell otherwise opens an empty Book1). See
            // ParityDemoWorkbookFactory — both shells build from the committed docs/parity/parity-demo.csv.
            window.AdoptWorkbookForParityCapture(ParityDemoWorkbookFactory.Create());
            EnsureFormulaBarVisibleForParityCapture(window);
            PumpDispatcher();
            window.UpdateLayout();
            PumpDispatcher();

            if (string.Equals(targetSurfaceId, "popup.nameBoxDropdown", StringComparison.Ordinal))
            {
                CaptureNameBoxDropdownSurface(outDir, window, results);
                return;
            }

            // Static ribbon tabs.
            foreach (var (surfaceId, catalogId) in RibbonTabSurfaces)
            {
                CaptureSurface(results, surfaceId, "static-tab", outDir, () =>
                {
                    if (!TrySelectRibbonTab(window!, catalogId))
                        throw new InvalidOperationException($"Ribbon tab '{catalogId}' not found.");
                    window!.UpdateLayout();
                    PumpDispatcher();
                    return RenderElement(window!, SurfaceWidth, SurfaceHeight);
                });
            }

            // Contextual ribbon tabs: temporarily reveal each hidden contextual tab so the capture report
            // shows how Windows and Linux render the same shared tab declaration.
            foreach (var (surfaceId, catalogId) in ContextualRibbonTabSurfaces)
            {
                CaptureSurface(results, surfaceId, "contextual-tab", outDir, () =>
                {
                    if (!TrySelectRibbonTab(window!, catalogId, forceVisible: true))
                        throw new InvalidOperationException($"Contextual ribbon tab '{catalogId}' not found.");
                    window!.UpdateLayout();
                    PumpDispatcher();
                    return RenderElement(window!, SurfaceWidth, SurfaceHeight);
                });
            }

            // Grid/demo screen: render the full app screen with the Home tab selected so it is comparable to
            // the Avalonia capture at the same pixel size.
            CaptureSurface(results, "grid.demo", "screen", outDir, () =>
            {
                // Land on the Home tab so the ribbon/grid composition matches the demo baseline.
                TrySelectRibbonTab(window!, "HomeTab");
                window!.UpdateLayout();
                PumpDispatcher();
                return RenderElement(window!, SurfaceWidth, SurfaceHeight);
            });
            CaptureSurface(results, "grid.sheetTabsOverflow", "screen", outDir, () =>
            {
                PrepareSheetTabsOverflowParityCapture(window!);
                TrySelectRibbonTab(window!, "HomeTab");
                window!.UpdateLayout();
                PumpDispatcher();
                return RenderElement(window!, SurfaceWidth, SurfaceHeight);
            });

            CaptureNameBoxDropdownSurface(outDir, window, results);

            // Backstage panes. WPF exposes Info as a true backstage pane; Export and Account are rail
            // *actions* (they open the Export-options dialog / show account info) rather than dedicated
            // panes, so capture the full backstage host with those action entries focused instead of
            // silently comparing them as Home.
            CaptureSurface(results, "backstage.Info", "backstage", outDir, () =>
                RenderBackstage(window!, "ShowInfoView"));
            CaptureSurface(results, "backstage.Export", "backstage", outDir,
                () => RenderBackstage(window!, "ShowHomeView", "BackstageExportButton"),
                note: "WPF Export is a backstage rail action (opens Export dialog); rendered the backstage rail host with Export focused.");
            CaptureSurface(results, "backstage.Account", "backstage", outDir,
                () => RenderBackstage(window!, "ShowHomeView", "BackstageAccountButton", CreateBackstageAccountPane()),
                note: "WPF Account is a backstage rail action; rendered a capture-only Account content pane with the Account entry focused.");
        }
        catch (Exception ex)
        {
            // The window itself failed to construct/show. Record every shell surface as not-captured.
            var note = Flatten(ex);
            foreach (var (surfaceId, _) in RibbonTabSurfaces)
                AddMissing(results, surfaceId, "static-tab", note);
            foreach (var (surfaceId, _) in ContextualRibbonTabSurfaces)
                AddMissing(results, surfaceId, "contextual-tab", note);
            AddMissing(results, "grid.demo", "screen", note);
            AddMissing(results, "grid.sheetTabsOverflow", "screen", note);
            AddMissing(results, "popup.nameBoxDropdown", "overlay", note);
            AddMissing(results, "backstage.Info", "backstage", note);
            AddMissing(results, "backstage.Export", "backstage", note);
            AddMissing(results, "backstage.Account", "backstage", note);
        }
        finally
        {
            try { window?.Hide(); } catch { /* best-effort teardown */ }
            try { window?.SuppressNextClosePrompt(); } catch { /* best-effort teardown */ }
            try { window?.Close(); } catch { /* best-effort teardown */ }
            PumpDispatcher();
        }
    }

    private static void PrepareSheetTabsOverflowParityCapture(MainWindow window)
    {
        while (GetWorkbookSheetCount(window) < 20)
            InvokePrivate(window, "InsertNewSheet");

        InvokePrivate(window, "RefreshSheetTabs");
        if (window.FindName("SheetTabsRowGrid") is FrameworkElement sheetTabsRow)
            sheetTabsRow.UpdateLayout();
        window.UpdateLayout();
        PumpDispatcher();
    }

    private static int GetWorkbookSheetCount(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_workbook",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MainWindow._workbook field not found.");
        return field.GetValue(window) is Workbook workbook ? workbook.Sheets.Count : 0;
    }

    private static BitmapSource RenderBackstage(
        MainWindow window,
        string showViewMethod,
        string? focusEntryId = null,
        UIElement? replacementContent = null)
    {
        InvokePrivate(window, "ShowStartScreen");
        window.UpdateLayout();
        PumpDispatcher();
        InvokePrivate(window, showViewMethod);
        if (replacementContent is not null)
            SetBackstageContent(window, replacementContent);
        if (!string.IsNullOrWhiteSpace(focusEntryId))
            FocusBackstageEntry(window, focusEntryId);
        // The start-screen Home view carries a "More templates (Excluded)" rail link that points at an
        // unavailable online-template service; it is excluded from the product, so collapse it for the
        // parity capture so the Home/Export surface matches the Linux backstage (which omits it). The
        // live control stays in the XAML — this only affects the offscreen capture pass.
        HideBackstageMoreTemplatesLink(window);
        window.UpdateLayout();
        PumpDispatcher();

        if (window.FindName("StartScreenOverlay") is not FrameworkElement overlay ||
            overlay.Visibility != Visibility.Visible)
        {
            // Fall back to the whole window if the overlay did not materialize.
            return RenderElement(window, SurfaceWidth, SurfaceHeight);
        }

        return RenderElement(window, SurfaceWidth, SurfaceHeight);
    }

    private static void HideBackstageMoreTemplatesLink(MainWindow window)
    {
        if (window.FindName("StartScreenOverlay") is not DependencyObject overlay)
            return;

        foreach (var button in FindVisualChildren<Button>(overlay))
        {
            if (string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "MoreTemplatesExcludedButton",
                    StringComparison.Ordinal))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static void SetBackstageContent(MainWindow window, UIElement content)
    {
        var frame = GetBackstageFrame(window);
        var field = frame?.GetType().GetField(
            "_content",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(frame) is ContentControl contentHost)
            contentHost.Content = content;
    }

    private static void FocusBackstageEntry(MainWindow window, string entryId)
    {
        var frame = GetBackstageFrame(window);
        var method = frame?.GetType().GetMethod("FocusEntry", [typeof(string)]);
        _ = method?.Invoke(frame, [entryId]);
        SelectBackstageEntryChrome(frame, entryId);
        PumpDispatcher();
    }

    private static void SelectBackstageEntryChrome(object? frame, string entryId)
    {
        if (frame is null)
            return;

        var frameType = frame.GetType();
        var findButton = frameType.GetMethod(
            "FindNavButton",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var setSelected = frameType.GetMethod(
            "SetSelected",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (findButton?.Invoke(frame, [entryId]) is Button button)
            _ = setSelected?.Invoke(frame, [button]);
    }

    private static object? GetBackstageFrame(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_backstageFrame",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(window);
    }

    private static UIElement CreateBackstageAccountPane()
    {
        var projection = FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(
            BuildParityCapturedBackstageAccountPanePlan());
        var heading = projection.Elements.OfType<FreeXBackstageHeadingProjectionElement>().Single();
        var sectionHeader = projection.Elements.OfType<FreeXBackstageSectionHeaderProjectionElement>().First();
        var detailRows = projection.Elements.OfType<FreeXBackstageDetailRowsProjectionElement>().Single();
        var root = new StackPanel
        {
            Margin = new Thickness(40, 34, 46, 0),
        };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(heading.TextKey),
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 18),
        });
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get(sectionHeader.TextKey),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < detailRows.Rows.Count; i++)
        {
            var detail = detailRows.Rows[i];
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddAccountDetail(
                details,
                i,
                UiText.Get(detail.LabelKey),
                ResolveBackstageAccountValue(detail.Value));
        }
        root.Children.Add(details);
        return root;
    }

    private static FreeXBackstageAccountPanePlan BuildParityCapturedBackstageAccountPanePlan()
    {
        var accountInfo = LocalAccountInfoPlanner.Build(
            typeof(MainWindow).Assembly,
            deviceName: Environment.MachineName,
            userName: Environment.UserName,
            optionsAvailable: true);

        return FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            accountInfo.UserName,
            accountInfo.DeviceName,
            accountInfo.VersionText,
            accountInfo.OptionsAvailable,
            null,
            "Parity Demo (not saved yet)",
            accountInfo.TrademarkNotice,
            accountInfo.LicenseNotice,
            accountInfo.PrivacyNotice));
    }

    private static string ResolveBackstageAccountValue(FreeXBackstageTextValue value) =>
        value.TextKey is { } key
            ? UiText.Get(key)
            : value.Text ?? string.Empty;

    private static void AddAccountDetail(Grid grid, int row, string label, string value)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            Margin = new Thickness(0, 0, 18, 10),
            TextAlignment = TextAlignment.Left,
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 560,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
        };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
    }

    private static void EnsureFormulaBarVisibleForParityCapture(MainWindow window)
    {
        if (window.FindName("FormulaBarBorder") is FrameworkElement formulaBarBorder)
            formulaBarBorder.Visibility = Visibility.Visible;

        if (window.FindName("FormulaBar") is TextBox formulaBar)
        {
            formulaBar.Height = 30;
            formulaBar.AcceptsReturn = false;
        }
    }

    // ----- Standalone dialogs -----

    private static void CaptureDialogs(string outDir, List<SurfaceResult> results, string? targetSurfaceId = null)
    {
        // Use the same seeded workbook as Avalonia so Page Setup Header/Footer captures compare
        // resolved preview text rather than two unrelated blank-sheet states.
        var workbook = ParityDemoWorkbookFactory.Create();
        var sheet = workbook.Sheets.Single();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 5));

        if (!string.IsNullOrWhiteSpace(targetSurfaceId))
        {
            if (string.Equals(targetSurfaceId, "dialog.FormatCells", StringComparison.Ordinal))
            {
                CaptureDialogTabs(results, "dialog.FormatCells", outDir,
                    () => new FormatCellsDialog(CellStyle.Default, FormatCellsDialogTab.Number),
                    ["Number", "Alignment", "Font", "Border", "Fill", "Protection"]);
            }
            else if (string.Equals(targetSurfaceId, "dialog.AccessibilityChecker", StringComparison.Ordinal))
            {
                CaptureAccessibilityCheckerDialog(results, outDir, AccessibilityCheckerParityFixture.CreateDialogIssues(sheet.Id));
            }
            else if (string.Equals(targetSurfaceId, "dialog.GoalSeek", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.GoalSeek", outDir, () => CreateGoalSeekParityDialog(sheet.Id));
            }
            else if (string.Equals(targetSurfaceId, "dialog.GoToSpecial", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.GoToSpecial", outDir, () => new GoToSpecialDialog());
            }
            else if (string.Equals(targetSurfaceId, "dialog.Sparkline", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.Sparkline", outDir, () =>
                    new SparklineDialog("Sheet1!$D$2:$D$5", "Sheet1!$H$2:$H$5", SparklineKind.Line, sheetId: sheet.Id));
            }
            else if (string.Equals(targetSurfaceId, "dialog.ExportOptions", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.ExportOptions", outDir, () =>
                    new ExportOptionsDialog(hasSelection: true, initialPdfLanguage: ExportPlanner.DefaultPdfLanguage, ExportFormat.Pdf));
            }
            else if (string.Equals(targetSurfaceId, "dialog.ProtectWorkbook", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.ProtectWorkbook", outDir, () =>
                    new PasswordProtectionDialog(UiText.Get("Protection_ProtectWorkbookTitle"), UiText.Get("Protection_PasswordToUnprotectWorkbook")));
            }
            else if (string.Equals(targetSurfaceId, "dialog.FindReplace", StringComparison.Ordinal) ||
                targetSurfaceId.StartsWith("dialog.FindReplace.", StringComparison.Ordinal))
            {
                CaptureDialogTabs(results, "dialog.FindReplace", outDir,
                    () => new FindReplaceDialog(
                        getWorkbook: () => workbook,
                        commandBus: new CommandBus(_ => new WorkbookCommandContext(workbook)),
                        navigateTo: _ => { },
                        replaceMode: false),
                    ["Find", "Replace"]);
            }
            else if (string.Equals(targetSurfaceId, "dialog.PivotTableOptions", StringComparison.Ordinal) ||
                targetSurfaceId.StartsWith("dialog.PivotTableOptions.", StringComparison.Ordinal))
            {
                CaptureDialogTabs(results, "dialog.PivotTableOptions", outDir, () =>
                {
                    var (pivot, cache, _) = CreatePivotModels(sheet.Id);
                    return new PivotTableOptionsDialog(pivot, cache);
                },
                    ["LayoutAndFormat", "TotalsAndFilters", "Display", "Printing", "Data", "AltText"]);
            }
            else if (string.Equals(targetSurfaceId, "dialog.PageSetup", StringComparison.Ordinal) ||
                targetSurfaceId.StartsWith("dialog.PageSetup.", StringComparison.Ordinal))
            {
                CaptureDialogTabs(results, "dialog.PageSetup", outDir,
                    () => new PageSetupDialog(sheet),
                    ["Page", "Margins", "HeaderFooter", "Sheet"]);
            }
            else if (string.Equals(targetSurfaceId, "dialog.HeaderFooterDialog", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.HeaderFooterDialog", outDir,
                    () => new HeaderFooterDialog(sheet));
            }
            else if (string.Equals(targetSurfaceId, "dialog.Consolidate", StringComparison.Ordinal))
            {
                CaptureConsolidateDialogDirect(results, outDir, sheet.Id, ResolveSheetId(workbook));
            }
            else if (string.Equals(targetSurfaceId, "dialog.ErrorChecking", StringComparison.Ordinal))
            {
                CaptureDialog(results, "dialog.ErrorChecking", outDir, () =>
                    new ErrorCheckingDialog(ErrorCheckingDialogPlanner.CreateParityIssues(sheet.Id), _ => { }, _ => true, _ => { }));
            }
            else if (string.Equals(targetSurfaceId, "dialog.ScenarioManager", StringComparison.Ordinal))
            {
                ScenarioManagerParityFixture.Seed(workbook, sheet.Id);
                CaptureDialog(results, "dialog.ScenarioManager", outDir, () =>
                    new ScenarioManagerDialog(workbook, sheet.Id, ResolveSheetId(workbook)));
            }
            else if (string.Equals(targetSurfaceId, "dialog.Options.Save", StringComparison.Ordinal) ||
                string.Equals(targetSurfaceId, "dialog.Options.Language", StringComparison.Ordinal) ||
                string.Equals(targetSurfaceId, "dialog.Options.EaseOfAccess", StringComparison.Ordinal) ||
                string.Equals(targetSurfaceId, "dialog.Options.CustomizeRibbon", StringComparison.Ordinal) ||
                string.Equals(targetSurfaceId, "dialog.Options.TrustCenter", StringComparison.Ordinal))
            {
                CaptureDialogTabs(results, "dialog.Options", outDir,
                    () => new OptionsDialog(FreeXOptions.FromAppOptions(OptionsDialogParityFixture.Create())),
                    ["General", "Formulas", "Proofing", "Save", "Language", "EaseOfAccess",
                        "Advanced", "CustomizeRibbon", "QuickAccessToolbar", "AddIns", "TrustCenter", "View"],
                    captureSizeResolver: surfaceId =>
                        surfaceId.Equals("dialog.Options.Formulas", StringComparison.Ordinal)
                            ? (OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.FormulasCaptureHeight)
                            : (OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.CaptureHeight),
                    captureOnlySurfaceId: targetSurfaceId);
            }
            else
            {
                AddMissing(results, targetSurfaceId, "dialog", "Targeted WPF parity capture only supports dialog.FormatCells, dialog.AccessibilityChecker, dialog.GoalSeek, dialog.GoToSpecial, dialog.Sparkline, dialog.ExportOptions, dialog.ProtectWorkbook, dialog.PivotTableOptions, dialog.PageSetup, dialog.HeaderFooterDialog, dialog.Consolidate, dialog.ErrorChecking, dialog.ScenarioManager, and the targeted Options tabs.");
            }

            return;
        }

        CaptureDialogTabs(results, "dialog.FormatCells", outDir,
            () => new FormatCellsDialog(CellStyle.Default, FormatCellsDialogTab.Number),
            ["Number", "Alignment", "Font", "Border", "Fill", "Protection"]);

        CaptureDialogTabs(results, "dialog.FindReplace", outDir,
            () => new FindReplaceDialog(
                getWorkbook: () => workbook,
                commandBus: new CommandBus(_ => new WorkbookCommandContext(workbook)),
                navigateTo: _ => { },
                replaceMode: false),
            ["Find", "Replace"]);

        CaptureDialog(results, "dialog.GoTo", outDir, () =>
            new GoToDialog(sheet.Id));

        CaptureDialog(results, "dialog.GoToSpecial", outDir, () =>
            new GoToSpecialDialog());

        CaptureDialog(results, "dialog.CreateTable", outDir, () =>
            new CreateTableDialog(sheet.Id, "Sheet1!$A$1:$D$5", "TableStyleMedium2"));

        CaptureDialog(results, "dialog.RecommendedPivotTables", outDir, () =>
            new RecommendedPivotTablesDialog());

        CaptureDialog(results, "dialog.Sort", outDir, () =>
            new SortDialog());

        CaptureDialog(results, "dialog.SortOptions", outDir, () =>
            new SortOptionsDialog(new SortDialogOptions()));

        CaptureDialog(results, "dialog.AutoFilter", outDir, () =>
            CreateAutoFilterDialog(workbook, sheet));

        CaptureDialog(results, "dialog.TextToColumns", outDir, () =>
            new TextToColumnsDialog(
                ["North,Widget,120", "South,Gadget,85", "East,Sprocket,200"],
                new CellAddress(sheet.Id, 2, 6)));

        CaptureDialog(results, "dialog.AdvancedFilter", outDir, () =>
            new AdvancedFilterDialog(sheet.Id, "Sheet1!$A$1:$D$5", ResolveSheetId(workbook)));

        CaptureConsolidateDialogDirect(results, outDir, sheet.Id, ResolveSheetId(workbook));

        CaptureDialog(results, "dialog.RemoveDuplicates", outDir, () =>
            new RemoveDuplicatesDialog(CreateColumnChoices("Region", "Product", "Revenue", "Units")));

        CaptureDialog(results, "dialog.GoalSeek", outDir, () =>
            CreateGoalSeekParityDialog(sheet.Id));

        CaptureDialog(results, "dialog.GoalSeekStatus", outDir, () =>
            new GoalSeekStatusDialog(new GoalSeekResult(true, 125d, 5000d, 7), 5000d));

        CaptureDialog(results, "dialog.DataTable", outDir, () =>
            new DataTableDialog(sheet.Id, range));

        ScenarioManagerParityFixture.Seed(workbook, sheet.Id);
        CaptureDialog(results, "dialog.ScenarioManager", outDir, () =>
            new ScenarioManagerDialog(workbook, sheet.Id, ResolveSheetId(workbook)));

        CaptureDialog(results, "dialog.ForecastSheet", outDir, () =>
            new ForecastSheetDialog());

        var subtotalWorkbook = ParityDemoWorkbookFactory.Create();
        var subtotalSheet = subtotalWorkbook.Sheets.Single();
        SubtotalParityFixture.ApplySheetState(subtotalSheet);
        var subtotalFixture = SubtotalParityFixture.CreateState(subtotalSheet);
        CaptureDialog(results, "dialog.Subtotal", outDir, () =>
            new SubtotalDialog(
                subtotalFixture.Columns,
                subtotalFixture.SummaryBelowData,
                subtotalFixture.CreatePlan()));

        CaptureDialog(results, "dialog.Sparkline", outDir, () =>
            new SparklineDialog("Sheet1!$D$2:$D$5", "Sheet1!$H$2:$H$5", SparklineKind.Line, sheetId: sheet.Id));

        CaptureDialog(results, "dialog.InsertHyperlink", outDir, () =>
            new HyperlinkDialog("https://freex.local/report", "Quarterly report"));

        CaptureDialog(results, "dialog.SymbolPicker", outDir, () =>
            new SymbolPickerDialog());

        CaptureDialog(results, "dialog.EvaluateFormula", outDir, () =>
            new EvaluateFormulaDialog(CreateFormulaEvaluationSummary(sheet.Id)));

        CaptureDialog(results, "dialog.ErrorChecking", outDir, () =>
            new ErrorCheckingDialog(CreateErrorCheckingIssues(sheet.Id), _ => { }, _ => true, _ => { }));

        CaptureDialog(results, "dialog.WatchWindow", outDir, () =>
            new WatchWindowDialog(
                () => CreateWatchEntries(sheet.Id),
                addWatch: null,
                getSelectionText: () => "Sheet1!$D$2",
                navigateTo: _ => { },
                removeWatch: _ => { }));

        CaptureDialog(results, "dialog.AddWatch", outDir, () =>
            new AddWatchDialog("Sheet1!$B$2"));

        CaptureDialog(results, "dialog.WorkbookStatistics", outDir, () =>
            new WorkbookStatisticsDialog(WorkbookStatisticsService.GetStatistics(workbook)));

        CaptureDialog(results, "dialog.RenameSheet", outDir, () =>
            new SheetNameDialog(sheet.Name));

        CaptureDialog(results, "dialog.UnhideSheet", outDir, () =>
            new UnhideSheetDialog(["Archive"]));

        CaptureDialog(results, "dialog.About", outDir, () =>
            new AboutDialog());

        CaptureDialog(results, "dialog.LegalNotices", outDir, () =>
            new LegalNoticesDialog());

        CaptureDialog(results, "dialog.SelectDataSource", outDir, () =>
            new SelectDataSourceDialog("A1:C6", firstColumnIsCategories: true, sheetId: sheet.Id, resolveSheetId: ResolveSheetId(workbook)));

        CaptureDialog(results, "dialog.ChangeChartType", outDir, () =>
            new ChangeChartTypeDialog(ChartType.Column));

        CaptureDialog(results, "dialog.FormatChartArea", outDir, () =>
            new ChartAreaLegendDialog(CreateChart(sheet.Id)));

        CaptureDialog(results, "dialog.ShapeEffects", outDir, () =>
            new ShapeEffectsDialog(DrawingShapeEffectPreset.Shadow));

        CaptureDialog(results, "dialog.ShapeGradient", outDir, () =>
            new ShapeGradientDialog());

        CaptureDialog(results, "dialog.Zoom", outDir, () =>
            new ZoomDialog(100));

        CaptureDialog(results, "dialog.CustomViews", outDir, () =>
        {
            // Seed a couple of named views so the dialog has meaningful rows to compare
            // (mirrors the Avalonia parity wrapper, which seeds the same view names).
            workbook.CustomViews.Clear();
            workbook.CustomViews.Add(new WorkbookCustomView("Summary View", []));
            workbook.CustomViews.Add(new WorkbookCustomView("Detailed View", []));
            return new CustomViewsDialog(workbook, new CommandBus(_ => new WorkbookCommandContext(workbook)));
        });

        CaptureDialog(results, "dialog.PrintPreview", outDir, () =>
            new PrintPreviewDialog(
                workbook.Name,
                CreatePrintPreviewDocument(),
                new PrintSettingsPlan([UiText.Get("PrintPreview_DefaultScopeActiveSheet")]),
                fixturePrinterName: PrintPreviewSurfacePlanner.ParityPrinterName));

        CaptureWorkbookFileDialogSurface(results, "dialog.OpenWorkbook", outDir, CreateOpenWorkbookDialogSurfacePlan);

        CaptureWorkbookFileDialogSurface(results, "dialog.SaveAsWorkbook", outDir, () =>
            CreateSaveAsWorkbookDialogSurfacePlan(workbook.Name));

        CaptureDialog(results, "dialog.ExportOptions", outDir, () =>
            new ExportOptionsDialog(hasSelection: true, initialPdfLanguage: ExportPlanner.DefaultPdfLanguage, ExportFormat.Pdf));

        CaptureDialog(results, "dialog.SelectionPane", outDir, () =>
            new SelectionPaneDialog(CreateSelectionPaneItems()));

        CaptureDialogTabs(results, "dialog.PivotTableOptions", outDir, () =>
        {
            var (pivot, cache, _) = CreatePivotModels(sheet.Id);
            return new PivotTableOptionsDialog(pivot, cache);
        },
            ["LayoutAndFormat", "TotalsAndFilters", "Display", "Printing", "Data", "AltText"]);

        CaptureDialogTabs(results, "dialog.PivotFieldFilter", outDir,
            () => new PivotFieldFilterDialog(["North", "South", "East", "West"], selectedItems: ["North", "South"]),
            ["SelectItems", "LabelFilters", "ValueFilters"]);

        CaptureDialogTabs(results, "dialog.PivotValueFieldSettings", outDir,
            () => new PivotValueFieldSettingsDialog(new PivotDataFieldModel(4, "Sum of Revenue", "sum"), CreatePivotHeaders()),
            ["SummarizeValuesBy", "ShowValuesAs", "NumberFormat"]);

        CaptureDialog(results, "dialog.InsertSlicer", outDir, () =>
            new InsertSlicerDialog(CreatePivotHeaders(), selectedField: "Region"));

        CaptureDialog(results, "dialog.InsertTimeline", outDir, () =>
            new InsertTimelineDialog(CreatePivotHeaders(), selectedField: "Date"));

        CaptureDialog(results, "dialog.AllowEditRanges", outDir, () =>
            new AllowEditRangeDialog(sheet.Id, "Sheet1!$B$2:$D$5", [range]));

        CaptureDialog(results, "dialog.ProtectSheet", outDir, () =>
            new PasswordProtectionDialog(UiText.Get("Protection_ProtectSheetTitle"), UiText.Get("Protection_PasswordToUnprotectSheet")));

        CaptureDialog(results, "dialog.ProtectWorkbook", outDir, () =>
            new PasswordProtectionDialog(UiText.Get("Protection_ProtectWorkbookTitle"), UiText.Get("Protection_PasswordToUnprotectWorkbook")));

        CaptureAccessibilityCheckerDialog(results, outDir, AccessibilityCheckerParityFixture.CreateDialogIssues(sheet.Id));

        CaptureDialog(results, "dialog.DataValidation", outDir, () =>
            new DataValidationDialog());

        CaptureDialog(results, "dialog.ConditionalFormatNewRule", outDir, () =>
            new NewConditionalFormatRuleDialog("Greater Than", range));

        CaptureDialog(results, "dialog.ConditionalFormatManage", outDir, () =>
        {
            // Seed a few example rules over the dialog's range so its rules list shows rows (mirrors the
            // Avalonia parity wrapper, which seeds DataBar / ColorScale / Greater-Than rules).
            SeedConditionalFormatRules(sheet, range);
            return new ManageConditionalFormatsDialog(sheet, range);
        });

        // Page Setup: both shells have the same 4 tabs in the same order (Page/Margins/Header-Footer/Sheet).
        CaptureDialogTabs(results, "dialog.PageSetup", outDir,
            () => new PageSetupDialog(sheet),
            ["Page", "Margins", "HeaderFooter", "Sheet"]);

        CaptureDialog(results, "dialog.HeaderFooterDialog", outDir,
            () => new HeaderFooterDialog(sheet));

        CaptureDialogTabs(results, "dialog.Options", outDir,
            () => new OptionsDialog(FreeXOptions.FromAppOptions(OptionsDialogParityFixture.Create())),
            [
                "General", "Formulas", "Proofing", "Save", "Language", "EaseOfAccess",
                "Advanced", "CustomizeRibbon", "QuickAccessToolbar", "AddIns", "TrustCenter", "View",
            ],
            captureSizeResolver: surfaceId => surfaceId.Equals("dialog.Options.Formulas", StringComparison.Ordinal)
                ? (OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.FormulasCaptureHeight)
                : (OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.CaptureHeight));
    }

    private static Func<string, SheetId?> ResolveSheetId(Workbook workbook) =>
        name => workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;

    private static void CaptureConsolidateDialogDirect(
        List<SurfaceResult> results,
        string outDir,
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId)
    {
        CaptureDialog(
            results,
            "dialog.Consolidate",
            outDir,
            () => new ConsolidateDialog(
                sheetId,
                ConsolidateParityFixture.SourceReference,
                ConsolidateParityFixture.DestinationReference,
                resolveSheetId: resolveSheetId),
            requireForeground: true,
            note: "Direct foreground WPF ConsolidateDialog capture from the production window; seeded by ConsolidateParityFixture A1:C4/H2.");
    }


    private static AutoFilterDialog CreateAutoFilterDialog(Workbook workbook, Sheet sheet)
    {
        var fixture = AutoFilterParityFixturePlanner.CreateFixturePlan(
            workbook,
            sheet,
            AutoFilterMenuResources.TextProvider,
            AutoFilterMenuResources.BlankDisplayText);
        return new AutoFilterDialog(fixture.MenuPlan);
    }

    private static GoalSeekDialog CreateGoalSeekParityDialog(SheetId sheetId)
    {
        var setCell = new CellAddress(sheetId, 2, 3);
        var changingCell = new CellAddress(sheetId, 2, 5);
        var dialog = new GoalSeekDialog(sheetId, setCell);
        dialog.ApplyInputValues(setCell, "5000", changingCell);
        return dialog;
    }

    private static WorkbookFileDialogSurfacePlan CreateOpenWorkbookDialogSurfacePlan()
    {
        var formats = WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanOpen)
            .ToArray();
        return WorkbookFileDialogSurfacePlanner.CreateOpenPlan(WorkbookFilePickerPlanner.BuildOpenPickerPlan(formats));
    }

    private static WorkbookFileDialogSurfacePlan CreateSaveAsWorkbookDialogSurfacePlan(string workbookName)
    {
        var formats = WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(format => format.CanSave)
            .ToArray();
        var pickerPlan = WorkbookFilePickerPlanner.BuildSavePickerPlan(
            formats,
            workbookName,
            fallbackDisplayName: "Book1",
            preferredExtension: AppOptions.FreeXWorkbookDefaultFormat);
        return WorkbookFileDialogSurfacePlanner.CreateSaveAsPlan(pickerPlan);
    }

    private static Window CreateWorkbookFileDialogSurface(WorkbookFileDialogSurfacePlan plan)
    {
        var dialog = new Window
        {
            Title = plan.Title,
            Width = WorkbookFileDialogSurfacePlanner.Width,
            Height = WorkbookFileDialogSurfacePlanner.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        AutomationProperties.SetAutomationId(dialog, plan.DialogAutomationId);

        dialog.Content = CreateWorkbookFileDialogSurfaceContent(plan);
        return dialog;
    }

    private static FrameworkElement CreateWorkbookFileDialogSurfaceContent(WorkbookFileDialogSurfacePlan plan)
    {
        var places = new StackPanel
        {
            Width = 128,
            Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF5, 0xF8)),
        };
        foreach (var place in new[] { "Recent", "Desktop", "Documents", "This PC" })
            places.Children.Add(new TextBlock { Text = place, Margin = new Thickness(12, 10, 8, 2) });

        var fileList = new ListBox
        {
            MinHeight = 220,
            ItemsSource = new[]
            {
                "Budget.xlsx",
                "Quarterly Report.fxl",
                "Sales.csv",
                "Forecast.xlsx"
            },
        };

        var fileNameBox = new TextBox
        {
            Text = plan.FileName,
            Width = 300,
        };
        AutomationProperties.SetAutomationId(fileNameBox, WorkbookFileDialogSurfacePlanner.FileNameBoxAutomationId);

        var fileTypeBox = new ComboBox
        {
            Width = 300,
            ItemsSource = plan.FileTypes.Select(type => $"{type.DisplayName} ({string.Join("; ", type.Patterns)})").ToArray(),
            SelectedIndex = 0,
        };
        AutomationProperties.SetAutomationId(fileTypeBox, WorkbookFileDialogSurfacePlanner.FileTypeBoxAutomationId);

        var form = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddWorkbookFileDialogField(form, 0, plan.FileNameLabel, fileNameBox);
        AddWorkbookFileDialogField(form, 1, plan.FileTypeLabel, fileTypeBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(new Button { Content = plan.PrimaryCommandText, Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) });
        buttons.Children.Add(new Button { Content = UiText.Cancel, Width = 82, IsCancel = true });

        var right = new DockPanel();
        DockPanel.SetDock(form, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);
        right.Children.Add(form);
        right.Children.Add(fileList);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(places, Dock.Left);
        root.Children.Add(places);
        root.Children.Add(right);

        return root;
    }

    private static void AddWorkbookFileDialogField(Grid form, int row, string label, Control control)
    {
        var labelControl = new Label
        {
            Content = label,
            Target = control,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4),
        };
        Grid.SetRow(labelControl, row);
        Grid.SetColumn(labelControl, 0);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.Margin = new Thickness(0, 0, 0, 4);
        form.Children.Add(labelControl);
        form.Children.Add(control);
    }

    private static FixedDocument CreatePrintPreviewDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(
            PrintPreviewParityFixture.DocumentWidth,
            PrintPreviewParityFixture.DocumentHeight);
        foreach (var page in PrintPreviewParityFixture.Pages)
            document.Pages.Add(CreatePrintPreviewPage(page));
        return document;
    }

    private static PageContent CreatePrintPreviewPage(PrintPreviewParityPage fixturePage)
    {
        var page = new FixedPage
        {
            Width = PrintPreviewParityFixture.PageWidth,
            Height = PrintPreviewParityFixture.PageHeight,
            Background = Brushes.White,
        };

        foreach (var run in fixturePage.TextRuns)
            AddFixedText(page, run);

        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(page);
        return pageContent;
    }

    private static void AddFixedText(FixedPage page, PrintPreviewParityTextRun run)
    {
        var block = new TextBlock
        {
            Text = run.Text,
            FontSize = run.FontSize,
            FontWeight = run.Bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(run.Color.R, run.Color.G, run.Color.B)),
        };
        FixedPage.SetLeft(block, run.Left);
        FixedPage.SetTop(block, run.Top);
        page.Children.Add(block);
    }

    private static IReadOnlyList<RemoveDuplicateColumnChoice> CreateColumnChoices(params string[] headers) =>
        headers.Select((header, index) => new RemoveDuplicateColumnChoice((uint)index, header, true)).ToArray();

    private static FormulaEvaluationSummary CreateFormulaEvaluationSummary(SheetId sheetId)
    {
        var address = new CellAddress(sheetId, 6, 4);
        return new FormulaEvaluationSummary(
            sheetId,
            "Sheet1",
            address,
            "=SUM(D2:D5)",
            "469",
            [
                new FormulaEvaluationStep("SUM(D2:D5)", "469"),
                new FormulaEvaluationStep("D2:D5", "{120;85;200;64}"),
                new FormulaEvaluationStep("=SUM(D2:D5)", "469"),
            ]);
    }

    private static IReadOnlyList<FormulaErrorIssue> CreateErrorCheckingIssues(SheetId sheetId) =>
        ErrorCheckingDialogPlanner.CreateParityIssues(sheetId);

    private static IReadOnlyList<WatchWindowEntry> CreateWatchEntries(SheetId sheetId) =>
    [
        new WatchWindowEntry(sheetId, "Demo", new CellAddress(sheetId, 2, 3), "120", null),
        new WatchWindowEntry(sheetId, "Demo", new CellAddress(sheetId, 3, 3), "85", null),
    ];

    private static ChartModel CreateChart(SheetId sheetId) =>
        new()
        {
            Name = "Revenue Chart",
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4)),
            Title = "Revenue by region",
            XAxisTitle = "Region",
            YAxisTitle = "Revenue",
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right,
            ChartAreaFillColor = new CellColor(255, 255, 255),
            PlotAreaFillColor = new CellColor(248, 250, 252),
        };

    private static IReadOnlyList<SelectionPaneItem> CreateSelectionPaneItems() =>
        SelectionPaneParityFixture.CreateDialogItems(Guid.NewGuid(), Guid.NewGuid());

    private static string[] CreatePivotHeaders() =>
        ["Region", "Product", "Date", "Units", "Revenue"];

    private static (PivotTableModel Pivot, PivotCacheModel Cache, IReadOnlyList<string> Headers) CreatePivotModels(SheetId sheetId)
    {
        var headers = CreatePivotHeaders();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 5));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = "$A$1:$E$5",
            RecordCount = 4,
        };
        foreach (var header in headers)
            cache.Fields.Add(new PivotCacheFieldModel(header, ContainsString: true));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = cache.CacheId,
            SourceRange = sourceRange,
            TargetRange = new GridRange(new CellAddress(sheetId, 8, 1), new CellAddress(sheetId, 12, 4)),
            StyleName = "PivotStyleLight16",
            ShowSubtotals = true,
            ShowRowStripes = true,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Revenue", "sum"));
        return (pivot, cache, headers);
    }

    /// <summary>
    /// Seeds three example conditional-format rules (DataBar, three-color ColorScale, Greater-Than) over
    /// <paramref name="range"/> so the Manage Conditional Formats dialog lists rows. Idempotent: clears any
    /// existing rules first so a re-run keeps the same three.
    /// </summary>
    private static void SeedConditionalFormatRules(Sheet sheet, GridRange range)
    {
        sheet.ConditionalFormats.Clear();
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.DataBar,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 3,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
        });
    }

    private static void CaptureDialog(
        List<SurfaceResult> results,
        string surfaceId,
        string outDir,
        Func<Window> factory,
        bool requireForeground = false,
        string note = "")
    {
        CaptureSurface(results, surfaceId, "dialog", outDir, () =>
        {
            Window? dialog = null;
            try
            {
                dialog = factory();
                dialog.WindowStartupLocation = requireForeground
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.Manual;
                dialog.ShowInTaskbar = false;
                dialog.ShowActivated = requireForeground;
                if (!requireForeground)
                {
                    dialog.Left = -10000;
                    dialog.Top = -10000;
                }
                dialog.Show();
                PumpDispatcher();
                if (requireForeground)
                {
                    dialog.Activate();
                    dialog.Focus();
                    PumpDispatcher();
                }
                dialog.UpdateLayout();
                PumpDispatcher();

                var hasClientCaptureSize = TryGetFixedDialogClientCaptureSize(surfaceId, out var clientWidth, out var clientHeight);
                var hasOuterCaptureSize = TryGetFixedDialogCaptureSize(surfaceId, out var fixedWidth, out var fixedHeight);
                if (hasClientCaptureSize)
                {
                    ApplyDialogClientCaptureSize(dialog, (clientWidth, clientHeight));
                }
                else if (hasOuterCaptureSize)
                {
                    ApplyOuterDialogCaptureSize(dialog, (fixedWidth, fixedHeight));
                }

                var width = hasClientCaptureSize
                    ? clientWidth
                    : fixedWidth > 0 ? fixedWidth : dialog.ActualWidth > 0 ? dialog.ActualWidth : dialog.Width;
                var height = hasClientCaptureSize
                    ? clientHeight
                    : fixedHeight > 0 ? fixedHeight : dialog.ActualHeight > 0 ? dialog.ActualHeight : dialog.Height;
                if (double.IsNaN(width) || width <= 0) width = 480;
                if (double.IsNaN(height) || height <= 0) height = 360;
                return RenderDialog(dialog, width, height);
            }
            finally
            {
                try { dialog?.Close(); } catch { /* best-effort teardown */ }
                PumpDispatcher();
            }
        }, note);
    }

    private static bool TryGetFixedDialogCaptureSize(string surfaceId, out double width, out double height)
    {
        (width, height) = surfaceId switch
        {
            "dialog.ExportOptions" => (ExportOptionsDialogSurfacePlanner.CaptureWidth, ExportOptionsDialogSurfacePlanner.CaptureHeight),
            "dialog.ProtectWorkbook" => (ProtectionDialogPlanner.ProtectWorkbookCaptureWidth, ProtectionDialogPlanner.ProtectWorkbookCaptureHeight),
            "dialog.Sparkline" => (SparklinePlanner.InsertDialogCaptureWidth, SparklinePlanner.InsertDialogCaptureHeight),
            "dialog.Consolidate" => (ConsolidateDialogPlanner.CaptureWidth, ConsolidateDialogPlanner.CaptureHeight),
            _ => (0, 0)
        };

        return width > 0 && height > 0;
    }

    private static bool TryGetFixedDialogClientCaptureSize(string surfaceId, out double width, out double height)
    {
        (width, height) = surfaceId switch
        {
            "dialog.ScenarioManager" =>
                (ScenarioManagerDialogLayout.DialogWidth, ScenarioManagerDialogLayout.DialogHeight),
            _ => (0, 0)
        };

        return width > 0 && height > 0;
    }

    private static void CaptureWorkbookFileDialogSurface(
        List<SurfaceResult> results,
        string surfaceId,
        string outDir,
        Func<WorkbookFileDialogSurfacePlan> planFactory)
    {
        CaptureSurface(
            results,
            surfaceId,
            "dialog",
            outDir,
            () => RenderWorkbookFileDialogSurface(planFactory()),
            "Captured from FreeX.App.Host --parity-capture WorkbookFileDialogSurfacePlanner direct surface at 640x420");
    }

    internal static BitmapSource RenderWorkbookFileDialogSurfaceForTest(WorkbookFileDialogSurfacePlan plan) =>
        RenderWorkbookFileDialogSurface(plan);

    internal static BitmapSource RenderAccessibilityCheckerDialogForTest(IReadOnlyList<AccessibilityIssue> issues) =>
        RenderAccessibilityCheckerDialog(issues);

    private static void CaptureAccessibilityCheckerDialog(
        List<SurfaceResult> results,
        string outDir,
        IReadOnlyList<AccessibilityIssue> issues)
    {
        CaptureSurface(
            results,
            "dialog.AccessibilityChecker",
            "dialog",
            outDir,
            () => RenderAccessibilityCheckerDialog(issues),
            "Captured from FreeX.App.Host --parity-capture-target dialog.AccessibilityChecker planner-backed direct surface at 360x520");
    }

    private static BitmapSource RenderAccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        var width = (int)AccessibilityCheckerDialogMetrics.Width;
        var height = (int)AccessibilityCheckerDialogMetrics.Height;
        var plan = AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get);
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetResolution(96, 96);

        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        using (var titleFont = new System.Drawing.Font("Segoe UI", (float)AccessibilityCheckerDialogMetrics.TitleFontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
        using (var headerFont = new System.Drawing.Font("Segoe UI", (float)AccessibilityCheckerDialogMetrics.BodyFontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
        using (var bodyFont = new System.Drawing.Font("Segoe UI", (float)AccessibilityCheckerDialogMetrics.BodyFontSize, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel))
        using (var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
        using (var mutedBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x4B, 0x55, 0x63)))
        using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xAB, 0xAB, 0xAB)))
        using (var lightBorderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xD0, 0xD7, 0xDE)))
        using (var selectionBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xE6, 0xF0, 0xFA)))
        using (var buttonFill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF8, 0xF9, 0xFA)))
        using (var defaultButtonPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x2B, 0x57, 0x91), 2))
        using (var buttonPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0xAD, 0xB5, 0xBD)))
        using (var stringFormat = new System.Drawing.StringFormat())
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            stringFormat.Trimming = System.Drawing.StringTrimming.EllipsisCharacter;

            graphics.DrawString(plan.Title, titleFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, (float)AccessibilityCheckerDialogMetrics.ContentMargin);
            graphics.DrawString(plan.InspectionResultsHeader, headerFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, 50);

            var treeTop = (float)AccessibilityCheckerDialogMetrics.ResultsTreeTop;
            var treeHeight = (float)AccessibilityCheckerDialogMetrics.ResultsTreeHeight;
            graphics.DrawRectangle(
                borderPen,
                (float)AccessibilityCheckerDialogMetrics.ContentMargin,
                treeTop,
                (float)AccessibilityCheckerDialogMetrics.ResultsTreeWidth,
                treeHeight);

            var y = treeTop + 7;
            foreach (var section in plan.Sections)
            {
                graphics.DrawString($"v {section.Header} ({section.IssueCount})", headerFont, textBrush, 24, y);
                y += 22;

                foreach (var group in section.Groups)
                {
                    graphics.DrawString($"v {group.Label} ({group.Items.Count})", bodyFont, textBrush, 38, y);
                    y += 20;

                    foreach (var item in group.Items)
                    {
                        if (y < treeTop + treeHeight - 24 && y < treeTop + 55)
                            graphics.FillRectangle(selectionBrush, 50, y - 2, 286, 20);

                        graphics.DrawString(item.ObjectLabel, bodyFont, textBrush, 58, y);
                        y += 20;
                    }
                }
            }

            var selectedGroup = plan.Sections.SelectMany(section => section.Groups).FirstOrDefault();
            var selectedItem = selectedGroup?.Items.FirstOrDefault();
            var selection = AccessibilityCheckerDialogPlanner.CreateSelection(selectedItem, null, plan);

            y = (float)AccessibilityCheckerDialogMetrics.AdditionalInformationTop;
            graphics.DrawString(plan.AdditionalInformationHeader, headerFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, y);
            y += 24;
            graphics.DrawString(plan.WhyFixHeader, headerFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, y);
            y += 18;
            y += DrawWrappedGdiText(graphics, selection.WhyFix, bodyFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, y, (float)AccessibilityCheckerDialogMetrics.ResultsTreeWidth, stringFormat) + 8;
            graphics.DrawString(plan.HowToFixHeader, headerFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, y);
            y += 18;
            _ = DrawWrappedGdiText(graphics, selection.HowToFix, bodyFont, textBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, y, (float)AccessibilityCheckerDialogMetrics.ResultsTreeWidth, stringFormat);

            _ = DrawWrappedGdiText(graphics, selection.StatusText, bodyFont, mutedBrush, (float)AccessibilityCheckerDialogMetrics.ContentMargin, (float)AccessibilityCheckerDialogMetrics.StatusTop, (float)AccessibilityCheckerDialogMetrics.ResultsTreeWidth, stringFormat);
            graphics.DrawLine(lightBorderPen, (float)AccessibilityCheckerDialogMetrics.ContentMargin, (float)AccessibilityCheckerDialogMetrics.ButtonDividerTop, width - (float)AccessibilityCheckerDialogMetrics.ContentMargin, (float)AccessibilityCheckerDialogMetrics.ButtonDividerTop);
            var closeX = width - (float)AccessibilityCheckerDialogMetrics.ContentMargin - (float)AccessibilityCheckerDialogMetrics.ActionButtonWidth;
            var goToX = closeX - (float)AccessibilityCheckerDialogMetrics.ActionButtonSpacing - (float)AccessibilityCheckerDialogMetrics.ActionButtonWidth;
            DrawGdiButton(graphics, new System.Drawing.RectangleF(goToX, (float)AccessibilityCheckerDialogMetrics.ActionButtonTop, (float)AccessibilityCheckerDialogMetrics.ActionButtonWidth, (float)AccessibilityCheckerDialogMetrics.ActionButtonHeight), plan.GoToAction.Text, bodyFont, textBrush, buttonFill, defaultButtonPen);
            DrawGdiButton(graphics, new System.Drawing.RectangleF(closeX, (float)AccessibilityCheckerDialogMetrics.ActionButtonTop, (float)AccessibilityCheckerDialogMetrics.ActionButtonWidth, (float)AccessibilityCheckerDialogMetrics.ActionButtonHeight), plan.CloseAction.Text.Replace("_", "", StringComparison.Ordinal), bodyFont, textBrush, buttonFill, buttonPen);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static float DrawWrappedGdiText(
        System.Drawing.Graphics graphics,
        string text,
        System.Drawing.Font font,
        System.Drawing.Brush brush,
        float x,
        float y,
        float maxWidth,
        System.Drawing.StringFormat format)
    {
        var size = graphics.MeasureString(text, font, (int)maxWidth, format);
        graphics.DrawString(text, font, brush, new System.Drawing.RectangleF(x, y, maxWidth, size.Height), format);
        return size.Height;
    }

    private static void DrawGdiButton(
        System.Drawing.Graphics graphics,
        System.Drawing.RectangleF bounds,
        string text,
        System.Drawing.Font font,
        System.Drawing.Brush textBrush,
        System.Drawing.Brush fillBrush,
        System.Drawing.Pen borderPen)
    {
        graphics.FillRectangle(fillBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

        var textSize = graphics.MeasureString(text, font);
        var x = bounds.X + Math.Max(0, (bounds.Width - textSize.Width) / 2);
        var y = bounds.Y + Math.Max(0, (bounds.Height - textSize.Height) / 2);
        graphics.DrawString(text, font, textBrush, x, y);
    }

    private static BitmapSource RenderWorkbookFileDialogSurface(WorkbookFileDialogSurfacePlan plan)
    {
        Window? dialog = null;
        try
        {
            dialog = CreateWorkbookFileDialogSurface(plan);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.ShowInTaskbar = false;
            dialog.ShowActivated = false;
            dialog.Left = -10000;
            dialog.Top = -10000;
            dialog.Show();
            PumpDispatcher();
            dialog.UpdateLayout();
            PumpDispatcher();

            return RenderDialog(
                dialog,
                WorkbookFileDialogSurfacePlanner.Width,
                WorkbookFileDialogSurfacePlanner.Height);
        }
        finally
        {
            try { dialog?.Close(); } catch { /* best-effort teardown */ }
            PumpDispatcher();
        }
    }

    private static BitmapSource RenderWorkbookFileDialogContent(FrameworkElement content)
    {
        var width = WorkbookFileDialogSurfacePlanner.Width;
        var height = WorkbookFileDialogSurfacePlanner.Height;
        var frame = new Border
        {
            Background = Brushes.White,
            Width = width,
            Height = height,
            Child = content,
        };

        return RenderElement(frame, width, height);
    }

    /// <summary>
    /// Multi-tab variant of <see cref="CaptureDialog"/>: opens the dialog once, renders its default surface
    /// (<c>&lt;surfaceId&gt;.png</c>), then for each tab index drives the dialog's tab/category selector
    /// (the first <see cref="TabControl"/>, else the first category <see cref="ListBox"/> — Options uses a
    /// left-rail ListBox rather than a TabControl) to that index and renders <c>&lt;surfaceId&gt;.&lt;tabName&gt;.png</c>.
    /// Mirrors the Avalonia <c>CaptureModalTabsAsync</c> so the comparison runner pairs the per-tab surfaces.
    /// </summary>
    private static void CaptureDialogTabs(
        List<SurfaceResult> results,
        string surfaceId,
        string outDir,
        Func<Window> factory,
        string[] tabNames,
        Func<string, (double Width, double Height)>? captureSizeResolver = null,
        string? captureOnlySurfaceId = null)
    {
        Window? dialog = null;
        try
        {
            dialog = factory();
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.ShowInTaskbar = false;
            dialog.ShowActivated = false;
            dialog.Left = -10000;
            dialog.Top = -10000;
            dialog.Show();
            PumpDispatcher();
            dialog.UpdateLayout();
            PumpDispatcher();

            var liveDialog = dialog;

            // Default surface (whatever tab the dialog opened on).
            if (captureOnlySurfaceId is null || captureOnlySurfaceId.Equals(surfaceId, StringComparison.Ordinal))
            {
                CaptureSurface(results, surfaceId, "dialog", outDir, () =>
                {
                    var captureSize = captureSizeResolver?.Invoke(surfaceId);
                    ApplyDialogClientCaptureSize(liveDialog, captureSize);
                    return captureSize is { } size
                        ? RenderDialog(liveDialog, size.Width, size.Height)
                        : RenderDialog(liveDialog);
                });
            }

            // The first TabControl drives most dialogs; the Options dialog instead switches its content via a
            // left-rail ListBox (TabList), so fall back to the first ListBox when no TabControl is present.
            var tabControl = FindVisualChildren<TabControl>(liveDialog).FirstOrDefault();
            var categoryList = tabControl is null ? FindVisualChildren<ListBox>(liveDialog).FirstOrDefault() : null;

            for (var i = 0; i < tabNames.Length; i++)
            {
                var tabSurfaceId = $"{surfaceId}.{tabNames[i]}";
                if (captureOnlySurfaceId is not null && !captureOnlySurfaceId.Equals(tabSurfaceId, StringComparison.Ordinal))
                    continue;

                var index = i;
                CaptureSurface(results, tabSurfaceId, "dialog", outDir, () =>
                {
                    if (tabControl is not null)
                    {
                        if (index >= tabControl.Items.Count)
                            throw new InvalidOperationException($"Tab index {index} is out of range (dialog has {tabControl.Items.Count} tabs).");
                        tabControl.SelectedIndex = index;
                    }
                    else if (categoryList is not null)
                    {
                        if (index >= categoryList.Items.Count)
                            throw new InvalidOperationException($"Category index {index} is out of range (dialog has {categoryList.Items.Count} categories).");
                        categoryList.SelectedIndex = index;
                    }
                    else
                    {
                        throw new InvalidOperationException("No TabControl or category ListBox found in the dialog visual tree.");
                    }

                    liveDialog.UpdateLayout();
                    PumpDispatcher();
                    var captureSize = captureSizeResolver?.Invoke(tabSurfaceId);
                    ApplyDialogClientCaptureSize(liveDialog, captureSize);
                    return captureSize is { } size
                        ? RenderDialog(liveDialog, size.Width, size.Height)
                        : RenderDialog(liveDialog);
                });
            }
        }
        catch (Exception ex)
        {
            AddMissing(results, surfaceId, "dialog", Flatten(ex));
            foreach (var tabName in tabNames)
                AddMissing(results, $"{surfaceId}.{tabName}", "dialog", Flatten(ex));
        }
        finally
        {
            try { dialog?.Close(); } catch { /* best-effort teardown */ }
            PumpDispatcher();
        }
    }

    private static void ApplyOuterDialogCaptureSize(Window dialog, (double Width, double Height)? captureSize)
    {
        if (captureSize is not { } size || size.Width <= 0 || size.Height <= 0)
            return;

        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = size.Width;
        dialog.Height = size.Height;
        dialog.MinWidth = size.Width;
        dialog.MinHeight = size.Height;
        PumpDispatcher();
        dialog.UpdateLayout();
        PumpDispatcher();
    }

    private static void ApplyDialogClientCaptureSize(Window dialog, (double Width, double Height)? captureSize)
    {
        if (captureSize is not { } size || size.Width <= 0 || size.Height <= 0)
            return;
        if (dialog.Content is not FrameworkElement content)
            throw new InvalidOperationException("A client-sized dialog capture requires FrameworkElement content.");

        dialog.UpdateLayout();
        PumpDispatcher();

        var nonClientWidth = Math.Max(0, dialog.ActualWidth - content.ActualWidth);
        var nonClientHeight = Math.Max(0, dialog.ActualHeight - content.ActualHeight);
        var outerWidth = size.Width + nonClientWidth;
        var outerHeight = size.Height + nonClientHeight;

        dialog.SizeToContent = SizeToContent.Manual;
        dialog.MinWidth = 0;
        dialog.MinHeight = 0;
        dialog.Width = outerWidth;
        dialog.Height = outerHeight;
        PumpDispatcher();
        dialog.UpdateLayout();
        PumpDispatcher();

        // Correct a possible fractional-DIP/DPI rounding residual after WPF has applied its native chrome.
        outerWidth += size.Width - content.ActualWidth;
        outerHeight += size.Height - content.ActualHeight;
        dialog.Width = outerWidth;
        dialog.Height = outerHeight;
        dialog.MinWidth = outerWidth;
        dialog.MinHeight = outerHeight;
        PumpDispatcher();
        dialog.UpdateLayout();
        PumpDispatcher();
    }

    internal static BitmapSource RenderDialogClientFrameForTest(Window dialog, double width, double height)
    {
        ApplyDialogClientCaptureSize(dialog, (width, height));
        return RenderDialog(dialog, width, height);
    }

    private static BitmapSource RenderDialog(Window dialog)
    {
        var width = dialog.ActualWidth > 0 ? dialog.ActualWidth : dialog.Width;
        var height = dialog.ActualHeight > 0 ? dialog.ActualHeight : dialog.Height;
        if (double.IsNaN(width) || width <= 0) width = 480;
        if (double.IsNaN(height) || height <= 0) height = 360;
        return RenderDialog(dialog, width, height);
    }

    private static BitmapSource RenderDialog(Window dialog, double width, double height)
    {
        if (dialog.Content is FrameworkElement content)
            return RenderElementOnBackground(content, width, height, Brushes.White);

        return RenderElement(dialog, width, height);
    }

    // ----- Rendering primitives -----

    private static BitmapSource RenderElement(FrameworkElement element, double width, double height)
    {
        if (double.IsNaN(width) || width <= 0) width = SurfaceWidth;
        if (double.IsNaN(height) || height <= 0) height = SurfaceHeight;

        // A top-level Window (and any element already hosted in a presentation source) is arranged by its
        // own layout pass — calling Measure/Arrange on it directly trips WPF's GetWindowMinMax invariant
        // (an unrecoverable FailFast). Only force-lay-out detached elements; rasterize live ones as-is.
        var alreadyHosted = element is Window || PresentationSource.FromVisual(element) is not null;
        if (!alreadyHosted)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
        }

        element.UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderElementOnBackground(
        FrameworkElement element,
        double width,
        double height,
        Brush background)
    {
        var content = RenderElement(element, width, height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, content.PixelWidth, content.PixelHeight);
            context.DrawRectangle(background, null, rect);
            context.DrawImage(content, rect);
        }

        var bitmap = new RenderTargetBitmap(content.PixelWidth, content.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void CaptureSurface(
        List<SurfaceResult> results, string surfaceId, string kind, string outDir, Func<BitmapSource> render,
        string note = "",
        string? evidenceProvenance = null)
    {
        var pngName = surfaceId + ".png";
        try
        {
            var bitmap = render();
            if (!HasVisiblePixels(bitmap))
                throw new InvalidOperationException("Rendered PNG was fully transparent or blank; refusing to record stale WPF parity-capture evidence.");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outDir, pngName));
            encoder.Save(stream);
            results.Add(new SurfaceResult(
                surfaceId,
                kind,
                pngName,
                true,
                note,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                evidenceProvenance));
        }
        catch (Exception ex)
        {
            AddMissing(results, surfaceId, kind, Flatten(ex));
        }
    }

    private static void CaptureNameBoxDropdownSurface(
        string outDir,
        MainWindow window,
        List<SurfaceResult> results)
    {
        try
        {
            var popup = window.OpenNameBoxDropdownForParityCapture();
            PumpDispatcher();
            CaptureSurface(
                results,
                "popup.nameBoxDropdown",
                "overlay",
                outDir,
                () => RenderElementOnBackground(
                    popup,
                    MainWindow.NameBoxDropdownParityCaptureWidth,
                    MainWindow.NameBoxDropdownParityCaptureHeight,
                    Brushes.White),
                note: "WPF production Name Box ComboBox popup rendered from the screenshot-tour fixture.",
                evidenceProvenance: "wpf-production-popup-render-target");
        }
        finally
        {
            window.CloseNameBoxDropdownForParityCapture();
            PumpDispatcher();
        }
    }

    private static void AddMissing(List<SurfaceResult> results, string surfaceId, string kind, string note)
    {
        // Avoid duplicate entries if the catastrophic path and the per-surface path both run.
        if (results.Any(r => string.Equals(r.Id, surfaceId, StringComparison.Ordinal)))
            return;
        results.Add(new SurfaceResult(surfaceId, kind, surfaceId + ".png", false, note));
    }

    internal static bool HasVisiblePixelsForTest(BitmapSource bitmap) =>
        HasVisiblePixels(bitmap);

    private static bool HasVisiblePixels(BitmapSource bitmap)
    {
        BitmapSource converted = bitmap.Format == PixelFormats.Pbgra32 || bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 0)
                continue;

            if (pixels[i] != 255 || pixels[i + 1] != 255 || pixels[i + 2] != 255)
                return true;
        }

        return false;
    }

    // ----- Helpers -----

    private static readonly (string SurfaceId, string CatalogId)[] RibbonTabSurfaces =
        BuildStaticRibbonTabSurfaces();

    private static readonly (string SurfaceId, string CatalogId)[] ContextualRibbonTabSurfaces =
        BuildContextualRibbonTabSurfaces();

    private static (string SurfaceId, string CatalogId)[] BuildStaticRibbonTabSurfaces()
    {
        var definition = FreeXRibbon.Build();
        return definition.VisibleTabs
            .Select(tab => ("tab." + SurfaceName(tab), tab.Id))
            .ToArray();
    }

    private static (string SurfaceId, string CatalogId)[] BuildContextualRibbonTabSurfaces()
    {
        var definition = FreeXRibbon.Build();
        return definition.ContextualTabs
            .Select(tab => ("contextual." + SurfaceName(tab), tab.Id))
            .ToArray();
    }

    private static string SurfaceName(SharedRibbon.RibbonTab tab) =>
        tab.Id.EndsWith("Tab", StringComparison.Ordinal)
            ? tab.Id[..^3]
            : tab.Id;

    private static bool TrySelectRibbonTab(MainWindow window, string catalogId, bool forceVisible = false)
    {
        if (window.FindName("RibbonTabs") is not TabControl tabs)
            return false;

        foreach (var item in tabs.Items)
        {
            if (item is TabItem tab &&
                string.Equals(RibbonMetadata.GetCatalogId(tab), catalogId, StringComparison.Ordinal))
            {
                if (forceVisible && tab.Visibility != Visibility.Visible)
                    tab.Visibility = Visibility.Visible;
                tabs.SelectedItem = tab;
                return true;
            }
        }

        return false;
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            Type.EmptyTypes)
            ?? throw new InvalidOperationException($"MainWindow.{methodName}() not found.");
        method.Invoke(window, null);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static string Flatten(Exception ex)
    {
        var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } tie } ? tie : ex;
        return $"{inner.GetType().Name}: {inner.Message}".Replace("\r", " ").Replace("\n", " ");
    }

    private static void WriteManifest(string outDir, IReadOnlyList<SurfaceResult> results)
    {
        var manifest = new
        {
            platform = "windows",
            shell = "wpf",
            surfaces = results.Select(r => new
            {
                id = r.Id,
                kind = r.Kind,
                png = r.Png,
                captured = r.Captured,
                note = r.Note,
                width = r.Width,
                height = r.Height,
                evidenceProvenance = r.EvidenceProvenance,
            }),
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), json, new UTF8Encoding(false));
    }
}
