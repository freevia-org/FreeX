using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private static readonly RibbonOverflowKeytipTourTarget[] RibbonOverflowKeytipTourTargets =
    [
        new("Home", "Home", "HomeEditingGroup", "Editing", "home-editing-overflow-menu", "freex_ribbon_overflow_home_editing_menu"),
        new("Insert", "Insert", "InsertChartsGroup", "Charts", "insert-charts-overflow-menu", "freex_ribbon_overflow_insert_charts_menu"),
        new("View", "View", "ViewWindowGroup", "Window", "view-window-overflow-menu", "freex_ribbon_overflow_view_window_menu")
    ];

    private static readonly RibbonScreenshotTourWidth[] RibbonOverflowKeytipNarrowWidths =
    [
        new("760", 760),
        new("700", 700),
        new("640", 640),
        new("580", 580)
    ];

    private async Task CaptureRibbonOverflowKeytipTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteRibbonOverflowKeytipTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Height = 768;
        await Task.Delay(700);

        var sheet = EnsureRibbonOverflowKeytipTourContext();
        var captures = new List<RibbonOverflowKeytipTourManifestCapture>();

        try
        {
            foreach (var target in RibbonOverflowKeytipTourTargets)
                captures.Add(await CaptureRibbonOverflowGroupMenuAsync(outputDir, target));

            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("760", 760));
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
            SheetGrid.Focus();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            ExitRibbonKeyTipMode();
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            var topLevelBadgeCount = KeyTipOverlay.Children.OfType<Border>().Count();
            if (topLevelBadgeCount == 0)
                throw new InvalidOperationException("Ribbon overflow/keytip tour expected top-level keytip badges before Escape cancellation.");

            const string beforeCancelFileName = "freex_keytip_escape_before_top_level";
            await CaptureCurrentWindowAsync(outputDir, beforeCancelFileName, ScreenshotTourCaptureHeight);
            captures.Add(CreateRibbonOverflowKeytipCapture(
                state: "keytip-before-escape",
                surface: "Ribbon keytip overlay",
                tabHeader: "Home",
                groupCatalogId: "",
                groupName: "",
                fileName: beforeCancelFileName,
                captureMethod: "RenderTargetBitmap-window-top-band",
                logicalWidth: ActualWidth,
                logicalHeight: ScreenshotTourCaptureHeight,
                menuItemCount: 0,
                menuHeaders: [],
                badgeCount: topLevelBadgeCount,
                collapsedGroupBadgeCount: 0,
                collapsedGroupWasVisible: false,
                focusedElement: Keyboard.FocusedElement,
                evidenceSummary: "Top-level Alt/F10 keytip badges are visible before the Escape cancellation path is exercised."));

            HandleActiveRibbonKeyTip(Key.Escape);
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            var afterCancelBadgeCount = KeyTipOverlay.Children.OfType<Border>().Count();
            if (afterCancelBadgeCount != 0 || KeyTipOverlay.Visibility == Visibility.Visible)
                throw new InvalidOperationException("Ribbon overflow/keytip tour expected Escape to clear the keytip overlay.");

            const string afterCancelFileName = "freex_keytip_escape_after_cancel";
            await CaptureCurrentWindowAsync(outputDir, afterCancelFileName, ScreenshotTourCaptureHeight);
            captures.Add(CreateRibbonOverflowKeytipCapture(
                state: "keytip-after-escape-cancel",
                surface: "Ribbon keytip overlay cleared",
                tabHeader: "Home",
                groupCatalogId: "",
                groupName: "",
                fileName: afterCancelFileName,
                captureMethod: "RenderTargetBitmap-window-top-band",
                logicalWidth: ActualWidth,
                logicalHeight: ScreenshotTourCaptureHeight,
                menuItemCount: 0,
                menuHeaders: [],
                badgeCount: afterCancelBadgeCount,
                collapsedGroupBadgeCount: 0,
                collapsedGroupWasVisible: false,
                focusedElement: Keyboard.FocusedElement,
                evidenceSummary: "Escape cancellation clears the keytip overlay while leaving the workbook/ribbon surface visible for continued keyboard navigation."));

            ExitRibbonKeyTipMode();
            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("640", 640));
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
            RefreshActiveDeclarativeRibbonLayout(forceLayout: true);
            EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            var collapsedBadgeCount = GetVisibleKeyTipElements(RibbonKeyTipScope.Commands).Count(RibbonMetadata.IsCollapsedGroupButton);
            if (collapsedBadgeCount == 0)
                throw new InvalidOperationException("Ribbon overflow/keytip tour expected collapsed-group badges in narrow Home command keytip scope.");

            const string collapsedBadgesFileName = "freex_keytip_narrow_home_collapsed_badges";
            await CaptureCurrentWindowAsync(outputDir, collapsedBadgesFileName, ScreenshotTourCaptureHeight);
            captures.Add(CreateRibbonOverflowKeytipCapture(
                state: "keytip-narrow-home-collapsed-badges",
                surface: "Narrow Home command keytip overlay",
                tabHeader: "Home",
                groupCatalogId: "HomeEditingGroup",
                groupName: "Editing",
                fileName: collapsedBadgesFileName,
                captureMethod: "RenderTargetBitmap-window-top-band",
                logicalWidth: ActualWidth,
                logicalHeight: ScreenshotTourCaptureHeight,
                menuItemCount: 0,
                menuHeaders: [],
                badgeCount: KeyTipOverlay.Children.OfType<Border>().Count(),
                collapsedGroupBadgeCount: collapsedBadgeCount,
                collapsedGroupWasVisible: true,
                focusedElement: Keyboard.FocusedElement,
                evidenceSummary: "Narrow Home command-scope keytips include collapsed-group badges that can route into overflow groups."));

            ExitRibbonKeyTipMode();

            ValidateRibbonOverflowKeytipTourEvidence(outputDir, captures);
            await WriteRibbonOverflowKeytipTourManifestAsync(outputDir, sheet, captures);
        }
        catch
        {
            DeleteRibbonOverflowKeytipTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ExitRibbonKeyTipMode();
        }
    }

    private Sheet EnsureRibbonOverflowKeytipTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Ribbon overflow/keytip tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 12; row++)
        {
            for (uint col = 1; col <= 6; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row == 1)
                    sheet.SetCell(address, new TextValue($"Ribbon Field {col}"));
                else if (col == 1)
                    sheet.SetCell(address, new TextValue($"Item {row - 1}"));
                else
                    sheet.SetCell(address, new NumberValue(row * 10 + col));
            }
        }

        var active = new CellAddress(sheet.Id, 2, 2);
        SetActiveCell(active);
        SheetGrid.SelectedRange = new GridRange(active, active);
        SheetGrid.SelectedRanges = null;
        UpdateViewport();
        RefreshStatusBar();
        return sheet;
    }

    private async Task<RibbonOverflowKeytipTourManifestCapture> CaptureRibbonOverflowGroupMenuAsync(
        string outputDir,
        RibbonOverflowKeytipTourTarget target)
    {
        var tab = RibbonScreenshotTourPlanner.DefaultTabs.Single(candidate => candidate.Header == target.TabHeader);
        Button? collapsedButton = null;
        FrameworkElement? group = null;
        RibbonScreenshotTourWidth? appliedWidth = null;

        foreach (var width in RibbonOverflowKeytipNarrowWidths)
        {
            await ApplyScreenshotTourWidthAsync(width);
            SelectRibbonTourTab(tab);
            RefreshActiveDeclarativeRibbonLayout(forceLayout: true);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);

            if (TryFindVisibleCollapsedRibbonGroupButton(target.GroupCatalogId, out group, out collapsedButton))
            {
                appliedWidth = width;
                break;
            }
        }

        if (group is null || collapsedButton is null || appliedWidth is null)
            throw new InvalidOperationException($"Ribbon overflow/keytip tour could not collapse '{target.GroupCatalogId}' on the {target.TabHeader} tab.");

        var menu = collapsedButton.ContextMenu
            ?? throw new InvalidOperationException($"Ribbon overflow/keytip tour could not find a collapsed menu for '{target.GroupCatalogId}'.");

        try
        {
            OpenRibbonContextMenu(collapsedButton, menu);
            await Task.Delay(350);
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            var headers = new List<string>();
            AddMenuHeaders(menu, headers);
            var menuItemCount = GetMenuItems(menu).Count();
            if (menuItemCount == 0)
                throw new InvalidOperationException($"Ribbon overflow/keytip tour opened an empty collapsed menu for '{target.GroupCatalogId}'.");

            await CaptureElementAsync(menu, outputDir, target.FileName);
            return CreateRibbonOverflowKeytipCapture(
                state: target.State,
                surface: "Collapsed ribbon group overflow menu",
                tabHeader: target.TabHeader,
                groupCatalogId: target.GroupCatalogId,
                groupName: GetRibbonGroupName(group),
                fileName: target.FileName,
                captureMethod: "RenderTargetBitmap-collapsed-group-context-menu",
                logicalWidth: menu.ActualWidth,
                logicalHeight: menu.ActualHeight,
                menuItemCount: menuItemCount,
                menuHeaders: headers,
                badgeCount: 0,
                collapsedGroupBadgeCount: 0,
                collapsedGroupWasVisible: collapsedButton.Visibility == Visibility.Visible,
                focusedElement: Keyboard.FocusedElement,
                evidenceSummary: $"{target.TabHeader} {target.GroupName} is collapsed at {appliedWidth.Label}px and its overflow menu preserves the group's command list and menu keytip text.");
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private bool TryFindVisibleCollapsedRibbonGroupButton(
        string groupCatalogId,
        out FrameworkElement? group,
        out Button? collapsedButton)
    {
        group = null;
        collapsedButton = null;
        var activePanel = GetActiveDeclarativeRibbonPanel();
        if (activePanel is null)
            return false;

        var groupHost = activePanel.Children
            .OfType<Free.Shared.Ribbon.Wpf.RibbonGroupHost>()
            .FirstOrDefault(candidate =>
                RibbonMetadata.TryGetCatalogId(candidate.GroupContent, out var catalogId) &&
                string.Equals(catalogId, groupCatalogId, StringComparison.Ordinal));
        if (groupHost is null || !groupHost.Collapsed)
            return false;

        group = groupHost.GroupContent;
        var groupName = RibbonMetadata.TryGetGroupName(group, out var candidateGroupName)
            ? candidateGroupName
            : groupHost.GroupName;
        collapsedButton = EnumerateVisualDescendants(groupHost)
            .Concat(EnumerateLogicalDescendants(groupHost))
            .OfType<Button>()
            .FirstOrDefault(button =>
                RibbonMetadata.IsCollapsedGroupButton(button) &&
                string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal) &&
                button.Visibility == Visibility.Visible);

        return collapsedButton is not null;
    }

    private static string GetRibbonGroupName(FrameworkElement group) =>
        RibbonMetadata.TryGetGroupName(group, out var groupName) ? groupName : string.Empty;

    private RibbonOverflowKeytipTourManifestCapture CreateRibbonOverflowKeytipCapture(
        string state,
        string surface,
        string tabHeader,
        string groupCatalogId,
        string groupName,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        int menuItemCount,
        IReadOnlyList<string> menuHeaders,
        int badgeCount,
        int collapsedGroupBadgeCount,
        bool collapsedGroupWasVisible,
        IInputElement? focusedElement,
        string evidenceSummary)
    {
        var focusedDependencyObject = focusedElement as DependencyObject;
        return new RibbonOverflowKeytipTourManifestCapture(
            CaptureKey: $"ribbon-overflow-keytip:{state}",
            PairKey: $"interactive:ribbon-overflow-keytip:{state}",
            ScenarioId: "ribbon-overflow-keytip:visual-evidence",
            State: state,
            Surface: surface,
            TabHeader: tabHeader,
            GroupCatalogId: groupCatalogId,
            GroupName: groupName,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            MenuItemCount: menuItemCount,
            MenuHeaders: menuHeaders,
            KeyTipBadgeCount: badgeCount,
            CollapsedGroupBadgeCount: collapsedGroupBadgeCount,
            CollapsedGroupWasVisible: collapsedGroupWasVisible,
            KeyTipOverlayVisible: KeyTipOverlay.Visibility == Visibility.Visible,
            FocusedElementType: focusedElement?.GetType().Name ?? string.Empty,
            FocusedElementAutomationId: focusedDependencyObject is null ? string.Empty : AutomationProperties.GetAutomationId(focusedDependencyObject),
            IsForegroundGuarded: !IsScreenshotTourBackgroundRenderAllowed(),
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteRibbonOverflowKeytipTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_ribbon_overflow_*.png"))
            File.Delete(file);
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_keytip_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, RibbonOverflowKeytipTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateRibbonOverflowKeytipTourEvidence(
        string outputDir,
        IReadOnlyList<RibbonOverflowKeytipTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Ribbon overflow/keytip tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Ribbon overflow/keytip tour created blank capture(s): {string.Join(", ", blank)}.");
    }

    private static bool IsNonBlankPng(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        BitmapSource bitmap = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        if (pixels.Length < 8)
            return false;

        var b = pixels[0];
        var g = pixels[1];
        var r = pixels[2];
        var a = pixels[3];
        for (var index = 4; index < pixels.Length; index += 4)
        {
            if (pixels[index] != b ||
                pixels[index + 1] != g ||
                pixels[index + 2] != r ||
                pixels[index + 3] != a)
            {
                return true;
            }
        }

        return false;
    }

    private async Task WriteRibbonOverflowKeytipTourManifestAsync(
        string outputDir,
        Sheet sheet,
        IReadOnlyList<RibbonOverflowKeytipTourManifestCapture> captures)
    {
        var manifest = new RibbonOverflowKeytipTourManifest(
            Tool: "FREEX_RIBBON_OVERFLOW_KEYTIP_TOUR",
            EvidenceFamily: "ribbon-overflow-keytip",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "ribbon:collapsed-overflow-keytip-cancel",
            OutputDirectory: outputDir,
            OutputNaming: "freex_ribbon_overflow_<Tab>_<Group>_menu.png and freex_keytip_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-RIBBON-002A", "UI-CAT-RIBBON-002B", "UI-CMD-KEYTIP-001"],
            EntryPaths:
            [
                "Narrow Home > Editing collapsed group",
                "Narrow Insert > Charts collapsed group",
                "Narrow View > Window collapsed group",
                "Alt/F10 keytip mode > Escape cancellation"
            ],
            SheetName: sheet.Name,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Window-band captures abort unless the FreeX main window owns foreground focus; collapsed menu captures are in-process element renders."),
            Captures: captures,
            CoveredStates:
            [
                "Home Editing collapsed-group overflow menu at narrow ribbon width.",
                "Insert Charts collapsed-group overflow menu at narrow ribbon width.",
                "View Window/Arrange collapsed-group overflow menu at narrow ribbon width.",
                "Top-level keytip overlay before Escape cancellation.",
                "Escape cancellation with keytip overlay cleared.",
                "Narrow Home command-scope keytips with collapsed-group badges."
            ],
            Limitations:
            [
                "This tour drives FreeX's in-process ribbon state and keytip state machine; it does not synthesize physical Alt/F10/Escape or mouse input.",
                "Collapsed group menus are WPF ContextMenu element renders, not OS CopyFromScreen foreground captures.",
                "The visual evidence is bounded to representative Home Editing, Insert Charts, and View Window groups plus one keytip cancellation path.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, RibbonOverflowKeytipTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.RibbonOverflowKeytipTourManifest);
    }

    private sealed record RibbonOverflowKeytipTourTarget(
        string TabHeader,
        string TabFileName,
        string GroupCatalogId,
        string GroupName,
        string State,
        string FileName);

    private sealed record RibbonOverflowKeytipTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        string SelectedRange,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<RibbonOverflowKeytipTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record RibbonOverflowKeytipTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string TabHeader,
        string GroupCatalogId,
        string GroupName,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        int MenuItemCount,
        IReadOnlyList<string> MenuHeaders,
        int KeyTipBadgeCount,
        int CollapsedGroupBadgeCount,
        bool CollapsedGroupWasVisible,
        bool KeyTipOverlayVisible,
        string FocusedElementType,
        string FocusedElementAutomationId,
        bool IsForegroundGuarded,
        string EvidenceSummary);
}
