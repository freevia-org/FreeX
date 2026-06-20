using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;
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

    private sealed record SurfaceResult(string Id, string Kind, string Png, bool Captured, string Note);

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
    /// Renders every surface to <paramref name="outDir"/> and writes the manifest. Runs on the WPF dispatcher
    /// thread (already STA). Swallows per-surface failures; only a catastrophic I/O failure on the manifest
    /// propagates.
    /// </summary>
    public static void Run(string outDir, Func<MainWindow> mainWindowFactory)
    {
        Directory.CreateDirectory(outDir);
        var results = new List<SurfaceResult>();

        // Closing the (only) main window must not tear down the Application before the dialog surfaces are
        // captured. Default ShutdownMode is OnLastWindowClose, which would shut us down mid-run; switch to
        // explicit so we control teardown via the caller's Shutdown().
        if (Application.Current is { } app)
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        CaptureRibbonAndShell(outDir, mainWindowFactory, results);
        CaptureDialogs(outDir, results);

        WriteManifest(outDir, results);

        var captured = results.Count(r => r.Captured);
        Console.WriteLine(
            $"[parity-capture] wrote {captured}/{results.Count} surfaces + manifest.json to {outDir}");
    }

    // ----- Ribbon tabs + grid + backstage: driven from one live, offscreen MainWindow -----

    private static void CaptureRibbonAndShell(
        string outDir, Func<MainWindow> mainWindowFactory, List<SurfaceResult> results)
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

            // Backstage panes. WPF exposes Info as a true backstage pane; Export and Account are rail
            // *actions* (they open the Export-options dialog / show account info) rather than dedicated
            // panes, so we render the backstage Home host with those rail entries present and note it.
            CaptureSurface(results, "backstage.Info", "backstage", outDir, () =>
                RenderBackstage(window!, "ShowInfoView"));
            CaptureSurface(results, "backstage.Export", "backstage", outDir,
                () => RenderBackstage(window!, "ShowHomeView"),
                note: "WPF Export is a backstage rail action (opens Export dialog); rendered the backstage rail host.");
            CaptureSurface(results, "backstage.Account", "backstage", outDir,
                () => RenderBackstage(window!, "ShowHomeView"),
                note: "WPF Account is a backstage rail action; rendered the backstage rail host.");
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
            AddMissing(results, "backstage.Info", "backstage", note);
            AddMissing(results, "backstage.Export", "backstage", note);
            AddMissing(results, "backstage.Account", "backstage", note);
        }
        finally
        {
            try { window?.Hide(); } catch { /* best-effort teardown */ }
            try { window?.Close(); } catch { /* best-effort teardown */ }
            PumpDispatcher();
        }
    }

    private static BitmapSource RenderBackstage(MainWindow window, string showViewMethod)
    {
        InvokePrivate(window, "ShowStartScreen");
        window.UpdateLayout();
        PumpDispatcher();
        InvokePrivate(window, showViewMethod);
        window.UpdateLayout();
        PumpDispatcher();

        if (window.FindName("StartScreenOverlay") is not FrameworkElement overlay ||
            overlay.Visibility != Visibility.Visible)
        {
            // Fall back to the whole window if the overlay did not materialize.
            return RenderElement(window, SurfaceWidth, SurfaceHeight);
        }

        return RenderElement(overlay, overlay.ActualWidth, overlay.ActualHeight);
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

    private static void CaptureDialogs(string outDir, List<SurfaceResult> results)
    {
        var workbook = new Workbook("ParityDemo");
        var sheet = workbook.SheetCount > 0 ? workbook.GetSheetAt(0) : workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 5));

        CaptureDialog(results, "dialog.FormatCells", outDir, () =>
            new FormatCellsDialog(CellStyle.Default, FormatCellsDialogTab.Number));

        CaptureDialog(results, "dialog.FindReplace", outDir, () =>
            new FindReplaceDialog(
                getWorkbook: () => workbook,
                commandBus: new CommandBus(_ => new WorkbookCommandContext(workbook)),
                navigateTo: _ => { },
                replaceMode: false));

        CaptureDialog(results, "dialog.GoTo", outDir, () =>
            new GoToDialog(sheet.Id));

        CaptureDialog(results, "dialog.Sort", outDir, () =>
            new SortDialog());

        CaptureDialog(results, "dialog.DataValidation", outDir, () =>
            new DataValidationDialog());

        CaptureDialog(results, "dialog.ConditionalFormatNewRule", outDir, () =>
            new NewConditionalFormatRuleDialog("Greater Than", range));

        CaptureDialog(results, "dialog.ConditionalFormatManage", outDir, () =>
            new ManageConditionalFormatsDialog(sheet, range));

        CaptureDialog(results, "dialog.PageSetup", outDir, () =>
            new PageSetupDialog(sheet));

        CaptureDialog(results, "dialog.Options", outDir, () =>
            new OptionsDialog(FreeXOptions.Load()));
    }

    private static void CaptureDialog(
        List<SurfaceResult> results, string surfaceId, string outDir, Func<Window> factory)
    {
        CaptureSurface(results, surfaceId, "dialog", outDir, () =>
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

                var width = dialog.ActualWidth > 0 ? dialog.ActualWidth : dialog.Width;
                var height = dialog.ActualHeight > 0 ? dialog.ActualHeight : dialog.Height;
                if (double.IsNaN(width) || width <= 0) width = 480;
                if (double.IsNaN(height) || height <= 0) height = 360;
                return RenderElement(dialog, width, height);
            }
            finally
            {
                try { dialog?.Close(); } catch { /* best-effort teardown */ }
                PumpDispatcher();
            }
        });
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

    private static void CaptureSurface(
        List<SurfaceResult> results, string surfaceId, string kind, string outDir, Func<BitmapSource> render,
        string note = "")
    {
        var pngName = surfaceId + ".png";
        try
        {
            var bitmap = render();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outDir, pngName));
            encoder.Save(stream);
            results.Add(new SurfaceResult(surfaceId, kind, pngName, true, note));
        }
        catch (Exception ex)
        {
            AddMissing(results, surfaceId, kind, Flatten(ex));
        }
    }

    private static void AddMissing(List<SurfaceResult> results, string surfaceId, string kind, string note)
    {
        // Avoid duplicate entries if the catastrophic path and the per-surface path both run.
        if (results.Any(r => string.Equals(r.Id, surfaceId, StringComparison.Ordinal)))
            return;
        results.Add(new SurfaceResult(surfaceId, kind, surfaceId + ".png", false, note));
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
            }),
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), json, new UTF8Encoding(false));
    }
}
