using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;

namespace FreeX.App.Avalonia;

/// <summary>
/// Per-surface visual capture for the headless <c>--parity-capture</c> mode. Each surface is rendered to a PNG
/// via Avalonia's in-process <see cref="RenderTargetBitmap"/> (no external screenshot tooling), mirroring how
/// the WPF shell captures the same canonical surface ids so a cross-platform comparison runner can diff them:
/// <list type="bullet">
///   <item><c>tab.&lt;Name&gt;</c> — the live shell window with that ribbon tab selected.</item>
///   <item><c>grid.demo</c> — the live shell window over the startup demo workbook (Home tab).</item>
///   <item><c>dialog.&lt;Name&gt;</c> — each canonical dialog window, opened, rendered, then closed.</item>
///   <item><c>backstage.&lt;Pane&gt;</c> — each File-backstage pane window.</item>
/// </list>
/// Surface ids use the shared ribbon definition's tab ids and the canonical dialog / backstage names so the
/// WPF capture matches one-for-one. A surface that cannot be captured headlessly is recorded with
/// <c>captured:false</c> + a reason rather than aborting the run.
/// </summary>
public sealed partial class MainWindow
{
    // The capture canvas is sized to the shell's default window so ribbon/grid framing matches the WPF shell.
    private const int ParityCaptureWindowWidth = 1120;
    private const int ParityCaptureWindowHeight = 720;
    private const int ParityCaptureDialogWaitMilliseconds = 8000;
    private const int ParityCaptureDialogPollMilliseconds = 50;

    /// <summary>The ordered static ribbon-tab surface ids and the shared-definition tab id each maps to.</summary>
    private static readonly (string SurfaceId, string TabId)[] ParityStaticRibbonTabs =
        BuildStaticRibbonTabSurfaces();

    /// <summary>The contextual tab surfaces and the activation key that makes each tab visible.</summary>
    private static readonly (string SurfaceId, string TabId, string ActivationKey)[] ParityContextualRibbonTabs =
        BuildContextualRibbonTabSurfaces();

    private static readonly string[] ParityBackstageSurfaces =
    [
        "backstage.Export",
        "backstage.Info",
        "backstage.Account",
    ];

    /// <summary>
    /// Renders every app surface to <c>&lt;outputDirectory&gt;/&lt;surfaceId&gt;.png</c> and returns the per-surface
    /// outcome list that drives the manifest. Runs on the UI thread (the coordinator awaits it from the
    /// <see cref="Window.Opened"/> handler). Each surface is wrapped so one failure does not stop the others.
    /// </summary>
    internal async Task<IReadOnlyList<ParitySurfaceResult>> CaptureParitySurfacesAsync(string outputDirectory)
    {
        var results = new List<ParitySurfaceResult>();

        // ── Ribbon tabs + grid: render the live shell window with each tab selected. ──
        var ribbonTabControl = FindParityRibbonTabControl();
        foreach (var (surfaceId, tabId) in ParityStaticRibbonTabs)
        {
            results.Add(CaptureRibbonTab(outputDirectory, ribbonTabControl, surfaceId, tabId, ParitySurfaceKind.StaticRibbonTab));
        }

        foreach (var (surfaceId, tabId, activationKey) in ParityContextualRibbonTabs)
        {
            _ribbonContextSource.SetParityCaptureContext(activationKey);
            results.Add(CaptureRibbonTab(outputDirectory, ribbonTabControl, surfaceId, tabId, ParitySurfaceKind.ContextualRibbonTab));
        }
        _ribbonContextSource.SetParityCaptureContext(null);

        // grid.demo: the worksheet over the startup demo workbook, framed by the Home tab.
        SelectParityRibbonTab(ribbonTabControl, "HomeTab");
        results.Add(CaptureWindowSurface(outputDirectory, "grid.demo", ParitySurfaceKind.Screen));

        // ── Dialogs: open each, render the dialog window, close it. ──
        foreach (var (surfaceId, opener) in ParityDialogOpeners())
            results.Add(await CaptureModalSurfaceAsync(outputDirectory, surfaceId, ParitySurfaceKind.Dialog, opener));

        // The Avalonia shell currently exposes File Info/Export/Account as modal dialogs, while the WPF
        // shell captures the true full-window Backstage overlay. Record these honestly as missing instead
        // of comparing mismatched popup windows as if they were the same surface.
        foreach (var surfaceId in ParityBackstageSurfaces)
            results.Add(new ParitySurfaceResult(
                surfaceId,
                ParitySurfaceKind.Backstage,
                surfaceId + ".png",
                Captured: false,
                "Avalonia File surface is still dialog-based; true Backstage overlay capture is not ported yet."));

        return results;
    }

    /// <summary>The canonical dialog surfaces and the shell method that opens each. Ordered for stable output.</summary>
    private IReadOnlyList<(string SurfaceId, Func<Task> Opener)> ParityDialogOpeners() =>
    [
        ("dialog.FormatCells", () => ShowFormatCellsDialogAsync()),
        ("dialog.FindReplace", () => ShowFindDialogAsync()),
        ("dialog.GoTo", () => ShowGoToDialogAsync()),
        ("dialog.Sort", () => ShowSortDialogAsync()),
        ("dialog.DataValidation", () => ShowDataValidationDialogAsync()),
        ("dialog.ConditionalFormatNewRule", () => ShowConditionalFormatNewRuleDialogAsync()),
        ("dialog.ConditionalFormatManage", () => ShowManageConditionalFormatsDialogAsync()),
        ("dialog.PageSetup", () => ShowPageSetupDialogAsync()),
        ("dialog.Options", () => ShowOptionsDialogAsync()),
    ];

    private static (string SurfaceId, string TabId)[] BuildStaticRibbonTabSurfaces()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        return definition.VisibleTabs
            .Select(tab => ("tab." + SurfaceName(tab), tab.Id))
            .ToArray();
    }

    private static (string SurfaceId, string TabId, string ActivationKey)[] BuildContextualRibbonTabSurfaces()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        return definition.ContextualTabs
            .Where(tab => tab.Context is not null)
            .Select(tab => ("contextual." + SurfaceName(tab), tab.Id, tab.Context!.ActivationKey))
            .ToArray();
    }

    private static string SurfaceName(RibbonTab tab) =>
        tab.Id.EndsWith("Tab", StringComparison.Ordinal)
            ? tab.Id[..^3]
            : tab.Id;

    private ParitySurfaceResult CaptureRibbonTab(
        string outputDirectory,
        TabControl? ribbonTabControl,
        string surfaceId,
        string tabId,
        ParitySurfaceKind kind)
    {
        if (ribbonTabControl is null)
            return new ParitySurfaceResult(surfaceId, kind, surfaceId + ".png", Captured: false, "Ribbon tab control not found in the shell visual tree.");

        if (!SelectParityRibbonTab(ribbonTabControl, tabId))
            return new ParitySurfaceResult(surfaceId, kind, surfaceId + ".png", Captured: false, $"Ribbon tab '{tabId}' is not present in the strip.");

        return CaptureWindowSurface(outputDirectory, surfaceId, kind);
    }

    /// <summary>Renders the whole shell window to <c>&lt;surfaceId&gt;.png</c>.</summary>
    private ParitySurfaceResult CaptureWindowSurface(string outputDirectory, string surfaceId, ParitySurfaceKind kind)
    {
        var pngName = surfaceId + ".png";
        try
        {
            RenderVisualToPng(this, ParityCaptureWindowWidth, ParityCaptureWindowHeight, Path.Combine(outputDirectory, pngName));
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: true, "");
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a modal surface via <paramref name="opener"/> (fire-and-forget, since it blocks on
    /// <c>ShowDialog</c>), polls this window's owned-window list for the freshly opened dialog, renders it, then
    /// closes it so the opener's task completes. The actual content of the dialog (its fields) does not need to
    /// be inspected — the renderer captures whatever the shell laid out.
    /// </summary>
    private async Task<ParitySurfaceResult> CaptureModalSurfaceAsync(
        string outputDirectory,
        string surfaceId,
        ParitySurfaceKind kind,
        Func<Task> opener)
    {
        var pngName = surfaceId + ".png";
        var preexisting = OwnedWindows.ToHashSet();

        Task openerTask;
        try
        {
            // Fire-and-forget: ShowDialog blocks until the window closes, so we must NOT await it here.
            openerTask = opener();
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"Opener threw: {ex.GetType().Name}: {ex.Message}");
        }

        var dialog = await WaitForOwnedDialogAsync(preexisting);
        if (dialog is null)
        {
            // The opener may have early-returned (e.g. a guard) without showing a window; record honestly.
            await AwaitOpenerQuietlyAsync(openerTask);
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, "Dialog window did not open within the wait window (guard or unavailable surface).");
        }

        ParitySurfaceResult result;
        try
        {
            var width = (int)Math.Ceiling(dialog.Bounds.Width > 0 ? dialog.Bounds.Width : dialog.Width);
            var height = (int)Math.Ceiling(dialog.Bounds.Height > 0 ? dialog.Bounds.Height : dialog.Height);
            RenderVisualToPng(dialog, width, height, Path.Combine(outputDirectory, pngName));
            result = new ParitySurfaceResult(surfaceId, kind, pngName, Captured: true, "");
        }
        catch (Exception ex)
        {
            result = new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { dialog.Close(); } catch { /* closing best-effort */ }
            await AwaitOpenerQuietlyAsync(openerTask);
        }

        return result;
    }

    private async Task<Window?> WaitForOwnedDialogAsync(HashSet<Window> preexisting)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ParityCaptureDialogWaitMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var candidate = OwnedWindows.FirstOrDefault(w => !preexisting.Contains(w));
            if (candidate is not null && candidate.IsVisible && candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0)
                return candidate;
            await Task.Delay(ParityCaptureDialogPollMilliseconds);
        }
        // Last chance: a window may be owned but not yet reporting bounds; take it anyway if present.
        return OwnedWindows.FirstOrDefault(w => !preexisting.Contains(w));
    }

    private static async Task AwaitOpenerQuietlyAsync(Task openerTask)
    {
        try
        {
            // Give the now-closed dialog's ShowDialog continuation a moment to unwind.
            var completed = await Task.WhenAny(openerTask, Task.Delay(1000));
            if (completed == openerTask)
                await openerTask;
        }
        catch
        {
            // The opener's post-dialog work (applying a result, etc.) is irrelevant to capture; swallow.
        }
    }

    /// <summary>Locates the ribbon's <see cref="TabControl"/> — the top-docked strip whose items carry tab-id tags.</summary>
    private TabControl? FindParityRibbonTabControl()
    {
        foreach (var tabControl in this.GetVisualDescendants().OfType<TabControl>())
        {
            if (tabControl.Items.OfType<TabItem>().Any(item => item.Tag is string tag && tag.EndsWith("Tab", StringComparison.Ordinal)))
                return tabControl;
        }
        return null;
    }

    private bool SelectParityRibbonTab(TabControl? ribbonTabControl, string tabId)
    {
        if (ribbonTabControl is null)
            return false;

        for (var i = 0; i < ribbonTabControl.Items.Count; i++)
        {
            if (ribbonTabControl.Items[i] is TabItem item && item.Tag is string tag && string.Equals(tag, tabId, StringComparison.Ordinal))
            {
                ribbonTabControl.SelectedIndex = i;
                LayoutWindow();
                return true;
            }
        }
        return false;
    }

    /// <summary>Forces a synchronous layout pass so a just-changed tab / selection is reflected before render.</summary>
    private void LayoutWindow()
    {
        Measure(new Size(ParityCaptureWindowWidth, ParityCaptureWindowHeight));
        Arrange(new Rect(0, 0, ParityCaptureWindowWidth, ParityCaptureWindowHeight));
        UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    }

    /// <summary>
    /// Renders <paramref name="visual"/> into an off-screen <see cref="RenderTargetBitmap"/> at the given
    /// pixel size and writes it as a PNG. The visual is measured/arranged first so an off-screen or
    /// not-yet-shown window still produces a populated bitmap.
    /// </summary>
    private static void RenderVisualToPng(Visual visual, int width, int height, string path)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);

        if (visual is Layoutable layoutable)
        {
            layoutable.Measure(new Size(pixelWidth, pixelHeight));
            layoutable.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
        }
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        bitmap.Render(visual);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        bitmap.Save(stream);
    }
}
