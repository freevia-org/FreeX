using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Services;
using FreeX.Ribbon.Avalonia;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

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
    private const int ParityCaptureTitleBarHeight = 30;
    private const int ParityCaptureDialogWaitMilliseconds = 8000;
    private const int ParityCaptureDialogPollMilliseconds = 50;
    private static readonly FontFamily ParityNarrowUiFontFamily =
        new("Segoe UI, Arial Narrow, Aptos Narrow, Liberation Sans Narrow, Nimbus Sans Narrow, DejaVu Sans Condensed, Arial, Liberation Sans, sans-serif");
    private static readonly IBrush ParityBackstageSidebarBrush = Brush(0x10, 0x25, 0x3A);
    private static readonly IBrush ParityBackstageSelectedBrush = Brush(0x24, 0x44, 0x5E);
    private static readonly IBrush ParityBackstageSeparatorBrush = Brush(0x24, 0x44, 0x5E);

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
            _ribbonContextSource.SetParityCaptureContext(null);
            LayoutWindow();
            _ribbonContextSource.SetParityCaptureContext(activationKey);
            LayoutWindow();
            ribbonTabControl = FindParityRibbonTabControl();
            results.Add(CaptureRibbonTab(outputDirectory, ribbonTabControl, surfaceId, tabId, ParitySurfaceKind.ContextualRibbonTab));
        }
        _ribbonContextSource.SetParityCaptureContext(null);
        LayoutWindow();

        // grid.demo: the worksheet over the startup demo workbook, framed by the Home tab.
        SelectParityRibbonTab(ribbonTabControl, "HomeTab");
        results.Add(CaptureWindowSurface(outputDirectory, "grid.demo", ParitySurfaceKind.Screen));
        PrepareSheetTabsOverflowParityCapture();
        SelectParityRibbonTab(ribbonTabControl, "HomeTab");
        results.Add(CaptureWindowSurface(outputDirectory, "grid.sheetTabsOverflow", ParitySurfaceKind.Screen));

        // ── Dialogs: open each, render the dialog window, close it. ──
        foreach (var (surfaceId, opener) in ParityDialogOpeners())
            results.Add(await CaptureModalSurfaceAsync(outputDirectory, surfaceId, ParitySurfaceKind.Dialog, opener));

        foreach (var surfaceId in ParityBackstageSurfaces)
            results.Add(CaptureBackstageSurface(outputDirectory, surfaceId));

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

    private void PrepareSheetTabsOverflowParityCapture()
    {
        while (_session.SheetTabs.Count < 20)
            AddNewSheet();

        _sheetTabsHost.Content = BuildSheetTabs();
        UpdateSheetTabNavigationVisibility();
        RefreshShell("Ready");
    }

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
            RenderWindowWithCapturedTitleBarToPng(this, ParityCaptureWindowWidth, ParityCaptureWindowHeight, Path.Combine(outputDirectory, pngName));
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: true, "");
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, kind, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ParitySurfaceResult CaptureBackstageSurface(string outputDirectory, string surfaceId)
    {
        var pngName = surfaceId + ".png";
        try
        {
            RenderVisualToPng(
                CreateParityCapturedBackstageSurface(surfaceId),
                ParityCaptureWindowWidth,
                ParityCaptureWindowHeight,
                Path.Combine(outputDirectory, pngName));
            return new ParitySurfaceResult(surfaceId, ParitySurfaceKind.Backstage, pngName, Captured: true, "");
        }
        catch (Exception ex)
        {
            return new ParitySurfaceResult(surfaceId, ParitySurfaceKind.Backstage, pngName, Captured: false, $"{ex.GetType().Name}: {ex.Message}");
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

    private static void RenderWindowWithCapturedTitleBarToPng(MainWindow window, int width, int height, string path)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);
        var contentHeight = Math.Max(1, pixelHeight - ParityCaptureTitleBarHeight);

        using var contentBitmap = RenderWindowClientContentToBitmap(window, pixelWidth, contentHeight);
        var composite = new AvaloniaGrid
        {
            Width = pixelWidth,
            Height = pixelHeight,
            Background = Brushes.White,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(ParityCaptureTitleBarHeight) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };

        AddGridChild(composite, CreateParityCapturedTitleBar(FormatParityCapturedWindowTitle(window.Title ?? "FreeX")), 0, 0);
        AddGridChild(composite, new Image
        {
            Source = contentBitmap,
            Stretch = Stretch.Fill,
            Width = pixelWidth,
            Height = contentHeight,
        }, 1, 0);

        RenderVisualToPng(composite, pixelWidth, pixelHeight, path);
    }

    private static RenderTargetBitmap RenderWindowClientContentToBitmap(MainWindow window, int width, int height)
    {
        var originalWidth = window.Width;
        var originalHeight = window.Height;

        try
        {
            window.Width = width;
            window.Height = height;
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var contentVisual = window.Content as Visual ?? window;
            return RenderVisualToBitmap(contentVisual, width, height);
        }
        finally
        {
            window.Width = originalWidth;
            window.Height = originalHeight;
            window.Measure(new Size(ParityCaptureWindowWidth, ParityCaptureWindowHeight));
            window.Arrange(new Rect(0, 0, ParityCaptureWindowWidth, ParityCaptureWindowHeight));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        }
    }

    private static Control CreateParityCapturedTitleBar(string title)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(15, 36, 58)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 1),
        };

        var dock = new DockPanel { LastChildFill = true };
        root.Child = dock;

        var systemButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
        };
        DockPanel.SetDock(systemButtons, Dock.Right);
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowMinimize));
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowMaximize));
        systemButtons.Children.Add(CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowClose));
        dock.Children.Add(systemButtons);

        var appIcon = CreateParityCapturedAppIcon();
        DockPanel.SetDock(appIcon, Dock.Left);
        dock.Children.Add(appIcon);

        var qat = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Save, width: 26, iconSize: 16, isEnabled: true));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Undo, width: 24, iconSize: 16, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.ChevronDown, width: 12, iconSize: 9, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.Redo, width: 24, iconSize: 16, isEnabled: false));
        qat.Children.Add(CreateParityCapturedQatButton(RibbonCommandIconKind.ChevronDown, width: 12, iconSize: 9, isEnabled: false));
        DockPanel.SetDock(qat, Dock.Left);
        dock.Children.Add(qat);

        dock.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontFamily = ParityNarrowUiFontFamily,
            FontWeight = FontWeight.Normal,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });

        return root;
    }

    private static string FormatParityCapturedWindowTitle(string title)
    {
        const string oldPrefix = "FreeX - ";
        return title.StartsWith(oldPrefix, StringComparison.Ordinal)
            ? title[oldPrefix.Length..] + " - FreeX"
            : title;
    }

    private static Control CreateParityCapturedAppIcon()
    {
        if (TryCreateParityCapturedAppIconFromResource() is { } resourceIcon)
            return resourceIcon;

        return CreateParityCapturedFallbackAppIcon();
    }

    private static Control? TryCreateParityCapturedAppIconFromResource()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "FreeX.ico");
        if (!File.Exists(iconPath))
            return null;

        try
        {
            var bitmap = TryDecodeParityCapturedIcoPngFrame(iconPath, desiredSize: 48)
                ?? DecodeParityCapturedIco(iconPath);
            return new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 8, 0),
                Child = new Viewbox
                {
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    Child = new Image
                    {
                        Source = bitmap,
                        Width = 20,
                        Height = 20,
                        Stretch = Stretch.Uniform,
                    },
                },
            };
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? DecodeParityCapturedIco(string iconPath)
    {
        using var stream = File.OpenRead(iconPath);
        return Bitmap.DecodeToWidth(stream, 22);
    }

    private static Bitmap? TryDecodeParityCapturedIcoPngFrame(string iconPath, int desiredSize)
    {
        var bytes = File.ReadAllBytes(iconPath);
        if (bytes.Length < 6 || BitConverter.ToUInt16(bytes, 0) != 0 || BitConverter.ToUInt16(bytes, 2) != 1)
            return null;

        var count = BitConverter.ToUInt16(bytes, 4);
        var bestOffset = 0;
        var bestSize = 0;
        var bestDelta = int.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 6 + index * 16;
            if (entryOffset + 16 > bytes.Length)
                return null;

            var width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            var height = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
            var imageSize = (int)BitConverter.ToUInt32(bytes, entryOffset + 8);
            var imageOffset = (int)BitConverter.ToUInt32(bytes, entryOffset + 12);
            if (width != height || imageSize <= 8 || imageOffset < 0 || imageOffset + imageSize > bytes.Length)
                continue;
            if (!IsPngSignature(bytes, imageOffset))
                continue;

            var delta = Math.Abs(width - desiredSize);
            if (delta >= bestDelta)
                continue;

            bestDelta = delta;
            bestOffset = imageOffset;
            bestSize = imageSize;
            if (delta == 0)
                break;
        }

        if (bestSize == 0)
            return null;

        var frame = new byte[bestSize];
        Array.Copy(bytes, bestOffset, frame, 0, bestSize);
        using var stream = new MemoryStream(frame);
        return new Bitmap(stream);
    }

    private static bool IsPngSignature(byte[] bytes, int offset) =>
        offset + 8 <= bytes.Length
        && bytes[offset] == 0x89
        && bytes[offset + 1] == 0x50
        && bytes[offset + 2] == 0x4E
        && bytes[offset + 3] == 0x47
        && bytes[offset + 4] == 0x0D
        && bytes[offset + 5] == 0x0A
        && bytes[offset + 6] == 0x1A
        && bytes[offset + 7] == 0x0A;

    private static Control CreateParityCapturedFallbackAppIcon()
    {
        var iconCanvas = new Canvas
        {
            Width = 20,
            Height = 20,
        };

        iconCanvas.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            Stroke = new SolidColorBrush(Color.FromRgb(222, 244, 249)),
            StrokeThickness = 1,
        });
        iconCanvas.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = 16,
            Height = 5,
            Fill = new SolidColorBrush(Color.FromRgb(15, 126, 155)),
        });
        Canvas.SetLeft(iconCanvas.Children[^1], 2);
        Canvas.SetTop(iconCanvas.Children[^1], 2);

        var f = CreateParityCapturedAppIconText("F", Brushes.White, new Thickness(0), zIndex: 1);
        f.FontSize = 10.5;
        f.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        f.VerticalAlignment = AvaloniaVerticalAlignment.Top;
        iconCanvas.Children.Add(f);
        Canvas.SetLeft(f, 3);
        Canvas.SetTop(f, 3);

        var x = CreateParityCapturedAppIconText("X", Brushes.White, new Thickness(0), zIndex: 1);
        x.FontSize = 12.5;
        x.HorizontalAlignment = AvaloniaHorizontalAlignment.Left;
        x.VerticalAlignment = AvaloniaVerticalAlignment.Top;
        iconCanvas.Children.Add(x);
        Canvas.SetLeft(x, 9);
        Canvas.SetTop(x, 6);

        return new Border
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 8, 0),
            Child = iconCanvas,
        };
    }

    private static TextBlock CreateParityCapturedAppIconText(string text, IBrush foreground, Thickness margin, int zIndex)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 14.5,
            FontFamily = new FontFamily("Segoe UI, Arial, Liberation Sans, sans-serif"),
            FontWeight = FontWeight.Bold,
            Foreground = foreground,
            Margin = margin,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        block.ZIndex = zIndex;
        return block;
    }

    private static Control CreateParityCapturedQatButton(RibbonCommandIconKind kind, double width, double iconSize, bool isEnabled) =>
        new Border
        {
            Width = width,
            Height = 22,
            Opacity = isEnabled ? 1.0 : 0.42,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), iconSize, Brushes.White),
        };

    private static Control CreateParityCapturedSaveQatButton()
    {
        var glyph = new AvaloniaGrid
        {
            Width = 14,
            Height = 14,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(4) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(glyph, new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(1, 1, 0, 0),
        }, 0, 0);
        AddGridChild(glyph, new Border
        {
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
            Margin = new Thickness(0, 3, 0, 0),
        }, 0, 0);
        AvaloniaGrid.SetRowSpan(glyph.Children[^1], 2);
        AddGridChild(glyph, new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 50, 77)),
            Height = 4,
            Margin = new Thickness(3, 7, 3, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        }, 1, 0);

        return new Border
        {
            Width = 26,
            Height = 22,
            Child = glyph,
        };
    }

    private static Control CreateParityCapturedTitleBarButton(RibbonCommandIconKind kind) =>
        new Border
        {
            Width = 46,
            Height = 28,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), 18, Brushes.White),
        };

    private static Control CreateParityCapturedBackstageSurface(string surfaceId)
    {
        var pane = surfaceId switch
        {
            { } id when id.EndsWith(".Info", StringComparison.Ordinal) => "Info",
            { } id when id.EndsWith(".Export", StringComparison.Ordinal) => "Export",
            { } id when id.EndsWith(".Account", StringComparison.Ordinal) => "Account",
            _ => "Home",
        };

        var root = new AvaloniaGrid
        {
            Width = ParityCaptureWindowWidth,
            Height = ParityCaptureWindowHeight,
            Background = Brushes.White,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(ParityCaptureTitleBarHeight) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(28) },
            },
        };

        AddGridChild(root, CreateParityCapturedTitleBar("Parity Demo - FreeX"), 0, 0);

        var body = new AvaloniaGrid
        {
            Background = Brushes.White,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(190) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(root, body, 1, 0);

        AddGridChild(body, CreateParityCapturedBackstageRail(pane), 0, 0);

        AddGridChild(body,
            string.Equals(pane, "Info", StringComparison.Ordinal)
                ? CreateParityCapturedBackstageInfoPane()
                : string.Equals(pane, "Account", StringComparison.Ordinal)
                    ? CreateParityCapturedBackstageAccountPane()
                : CreateParityCapturedBackstageHomePane(),
            0, 1);
        AddGridChild(root, CreateParityCapturedStatusBarFooter(), 2, 0);
        ApplyParityBackstageTypography(root);
        return root;
    }

    private static Control CreateParityCapturedStatusBarFooter()
    {
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(grid, new TextBlock
        {
            Text = "Ready",
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 0);

        var viewButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.Grid, isChecked: true));
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.Page, isChecked: false));
        viewButtons.Children.Add(CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind.PageBreak, isChecked: false));

        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 24,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        zoomPanel.Children.Add(CreateParityCapturedStatusBarZoomText("-"));
        zoomPanel.Children.Add(CreateParityCapturedStatusZoomSlider());
        zoomPanel.Children.Add(CreateParityCapturedStatusBarZoomText("+"));
        zoomPanel.Children.Add(new TextBlock
        {
            Text = "100%",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        });

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        right.Children.Add(viewButtons);
        right.Children.Add(zoomPanel);
        AddGridChild(grid, right, 0, 2);

        return new Border
        {
            Background = Brush(23, 50, 77),
            BorderThickness = new Thickness(0),
            Height = 28,
            Padding = new Thickness(8, 3),
            Child = grid,
        };
    }

    private static Control CreateParityCapturedStatusBarIconButton(RibbonCommandIconKind kind, bool isChecked) =>
        new Border
        {
            Width = 24,
            Height = 24,
            Background = isChecked ? Brush(15, 109, 140) : Brushes.Transparent,
            Child = AvaloniaRibbonIcons.Build(new RibbonCommandIcon(kind), 15, Brushes.White),
        };

    private static Control CreateParityCapturedStatusBarZoomText(string text) =>
        new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Width = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };

    private static Control CreateParityCapturedStatusZoomSlider()
    {
        var canvas = new Canvas
        {
            Width = 120,
            Height = 22,
        };
        canvas.Children.Add(new Border
        {
            Width = 104,
            Height = 4,
            Background = Brush(218, 222, 228),
            BorderBrush = Brush(175, 184, 193),
            BorderThickness = new Thickness(1),
        });
        Canvas.SetLeft(canvas.Children[^1], 8);
        Canvas.SetTop(canvas.Children[^1], 9);
        foreach (var left in new[] { 8d, 60d, 111d })
        {
            canvas.Children.Add(new Border
            {
                Width = 1,
                Height = 4,
                Background = Brush(232, 236, 240),
            });
            Canvas.SetLeft(canvas.Children[^1], left);
            Canvas.SetTop(canvas.Children[^1], 16);
        }
        canvas.Children.Add(new Border
        {
            Width = 9,
            Height = 16,
            Background = Brush(248, 249, 250),
            BorderBrush = Brush(124, 133, 143),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(1),
        });
        Canvas.SetLeft(canvas.Children[^1], 31);
        Canvas.SetTop(canvas.Children[^1], 3);
        return canvas;
    }

    private static Control CreateParityCapturedBackstageRail(string selectedPane)
    {
        var rail = new AvaloniaGrid
        {
            Background = ParityBackstageSidebarBrush,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto },
            },
        };

        var top = new StackPanel { Spacing = 0 };
        top.Children.Add(CreateParityCapturedBackstageBackButton());
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Grid, "Home", "Home", string.Equals(selectedPane, "Home", StringComparison.Ordinal)));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Insert, "New", "New", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.GetData, "Open", "Open", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Share, "Share", "Share", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailSeparator());
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Info, "Info", "Info", string.Equals(selectedPane, "Info", StringComparison.Ordinal)));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Save, "Save", "Save", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Save, "Save As", "Save As", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Print, "Print", "Print", isSelected: false));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Share, "Export", "Export", string.Equals(selectedPane, "Export", StringComparison.Ordinal)));
        top.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.WindowClose, "Close", "Close", isSelected: false));
        AddGridChild(rail, top, 0, 0);

        var bottom = new StackPanel { Spacing = 0 };
        bottom.Children.Add(CreateParityCapturedBackstageRailSeparator());
        bottom.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.Info, "Account", "Account", string.Equals(selectedPane, "Account", StringComparison.Ordinal)));
        bottom.Children.Add(CreateParityCapturedBackstageRailButton(RibbonCommandIconKind.View, "Options", "Options", isSelected: false));
        AddGridChild(rail, bottom, 2, 0);
        return rail;
    }

    private static Control CreateParityCapturedBackstageBackButton()
    {
        var row = new Border
        {
            Width = 190,
            Height = 50,
            Background = Brushes.Transparent,
            Child = new Border
            {
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0),
                Child = CreateParityCapturedBackstageBackArrowGlyph(),
            },
        };
        AutomationProperties.SetName(row, "Back");
        return row;
    }

    private static Control CreateParityCapturedBackstageBackArrowGlyph() =>
        new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M12,4 L5,11 L12,18 M6,11 L19,11"),
            Width = 18,
            Height = 18,
            Stroke = Brushes.White,
            StrokeThickness = 1.25,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
        };

    private static Control CreateParityCapturedBackstageRailButton(RibbonCommandIconKind iconKind, string text, string commandName, bool isSelected)
    {
        var content = new AvaloniaGrid
        {
            Width = 190,
            Height = 38,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(22) },
                new ColumnDefinition { Width = new GridLength(22) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddGridChild(content, AvaloniaRibbonIcons.BuildMonochrome(iconKind, 22, commandName, Brushes.White), 0, 1);
        AddGridChild(content, new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontFamily = ParityNarrowUiFontFamily,
            Foreground = Brushes.White,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 3);

        var row = new Border
        {
            Height = 38,
            Background = isSelected
                ? ParityBackstageSelectedBrush
                : Brushes.Transparent,
            Child = content,
        };
        AutomationProperties.SetName(row, text);
        return row;
    }

    private static Control CreateParityCapturedBackstageRailSeparator() =>
        new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4),
            Background = ParityBackstageSeparatorBrush,
        };

    private static void ApplyParityBackstageTypography(Control control)
    {
        if (control is TextBlock text)
            text.FontFamily = ParityNarrowUiFontFamily;

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                    ApplyParityBackstageTypography(child);
                break;
            case ContentControl { Content: Control child }:
                ApplyParityBackstageTypography(child);
                break;
            case Decorator { Child: Control child }:
                ApplyParityBackstageTypography(child);
                break;
        }
    }

    private static Control CreateParityCapturedBackstageHomePane()
    {
        var canvas = new Canvas
        {
            Background = Brushes.White,
        };

        PlaceBackstage(canvas, new TextBlock
        {
            Text = "Good evening",
            FontSize = 30,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        }, 40, 40);
        PlaceBackstage(canvas, new TextBlock
        {
            Text = "New",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        }, 40, 98);
        PlaceBackstage(canvas, CreateParityCapturedBlankWorkbookTile(), 44, 126);
        PlaceBackstage(canvas, new TextBlock
        {
            Text = "More templates (Excluded) ->",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 96, 128)),
        }, 692, 100);

        var recentHeader = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(recentHeader, new TextBlock
        {
            Text = "Recent",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 96, 128)),
            Margin = new Thickness(0, 0, 28, 0),
        }, 0, 0);
        AddGridChild(recentHeader, new TextBlock
        {
            Text = "Pinned",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
        }, 0, 1);
        PlaceBackstage(canvas, recentHeader, 40, 248);
        PlaceBackstage(canvas, new Border
        {
            Width = 64,
            Height = 2,
            Background = new SolidColorBrush(Color.FromRgb(0, 96, 128)),
        }, 40, 272);
        PlaceBackstage(canvas, new Border
        {
            Width = 198,
            Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(246, 246, 246)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
            BorderThickness = new Thickness(1),
        }, 692, 244);
        PlaceBackstage(canvas, CreateParityCapturedRecentHeaderRow(), 40, 286);
        PlaceBackstage(canvas, CreateParityCapturedBackstageRecentFile(), 40, 310);

        return new Border
        {
            Background = Brushes.White,
            Child = canvas,
        };
    }

    private static Control CreateParityCapturedBackstageAccountPane()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(44, 34, 46, 0),
            Spacing = 18,
        };
        root.Children.Add(new TextBlock
        {
            Text = "Account",
            FontSize = 30,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Local account information",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });

        var details = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(180) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        var rows = new (string Label, string Value)[]
        {
            ("FreeX user name", "quinn"),
            ("Local OS account", Environment.UserName),
            ("Device", Environment.MachineName),
            ("App version", AppHelpInfo.GetBuildVersionText(typeof(MainWindow).Assembly)),
            ("Options file", "Local profile settings"),
            ("Current workbook", "Parity Demo (not saved yet)"),
            ("Sharing", "Save As is required before Windows Share can send the workbook."),
            ("Export", "Ready for local PDF/XPS export to a chosen local path."),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddGridChild(details, new TextBlock
            {
                Text = rows[i].Label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                Margin = new Thickness(0, 0, 18, 10),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
            }, i, 0);
            AddGridChild(details, new TextBlock
            {
                Text = rows[i].Value,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 560,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
            }, i, 1);
        }
        root.Children.Add(details);

        return new Border
        {
            Background = Brushes.White,
            Child = root,
        };
    }

    private static void PlaceBackstage(Canvas canvas, Control child, double left, double top)
    {
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
        canvas.Children.Add(child);
    }

    private static Control CreateParityCapturedBlankWorkbookTile()
    {
        var canvas = new Canvas
        {
            Width = 108,
            Height = 80,
        };
        canvas.Children.Add(CreateParityCapturedThumbnailRect(0, 0, 18, 80, fill: Color.FromRgb(0xF0, 0xF0, 0xF0)));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(0, 0, 108, 14, fill: Color.FromRgb(0xF0, 0xF0, 0xF0)));
        foreach (var y in new[] { 27d, 40d, 53d, 66d })
            canvas.Children.Add(CreateParityCapturedThumbnailLine(0, y, 108, y));
        foreach (var x in new[] { 42d, 66d, 90d })
            canvas.Children.Add(CreateParityCapturedThumbnailLine(x, 14, x, 80));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(18, 14, 24, 13, fill: Color.FromRgb(0xE6, 0xF6, 0xFA)));
        canvas.Children.Add(CreateParityCapturedThumbnailRect(18, 14, 24, 13, stroke: Color.FromRgb(0x0F, 0x6D, 0x8C), strokeThickness: 1.5));

        var preview = new Border
        {
            Width = 108,
            Height = 80,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = canvas,
        };

        return new StackPanel
        {
            Width = 108,
            Children =
            {
                preview,
                new TextBlock
                {
                    Text = "Blank workbook",
                    FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 2),
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                },
            },
        };
    }

    private static Control CreateParityCapturedThumbnailRect(
        double left,
        double top,
        double width,
        double height,
        Color? fill = null,
        Color? stroke = null,
        double strokeThickness = 0.5)
    {
        var rect = new global::Avalonia.Controls.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = fill is { } fillColor ? new SolidColorBrush(fillColor) : Brushes.Transparent,
            Stroke = stroke is { } strokeColor ? new SolidColorBrush(strokeColor) : null,
            StrokeThickness = stroke is null ? 0 : strokeThickness,
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        return rect;
    }

    private static Control CreateParityCapturedThumbnailLine(double x1, double y1, double x2, double y2)
    {
        return new global::Avalonia.Controls.Shapes.Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            StrokeThickness = 0.5,
        };
    }

    private static Control CreateParityCapturedRecentHeaderRow()
    {
        var grid = new AvaloniaGrid
        {
            Width = 850,
            Height = 28,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
        };
        AddGridChild(grid, new TextBlock
        {
            Text = "Name",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 0);
        AddGridChild(grid, new TextBlock
        {
            Text = "Date modified",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 1);
        return new Border
        {
            Width = 850,
            Height = 28,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = grid,
        };
    }

    private static Control CreateParityCapturedBackstageRecentFile()
    {
        var grid = new AvaloniaGrid
        {
            Width = 850,
            Height = 44,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(150) },
                new ColumnDefinition { Width = new GridLength(36) },
            },
        };
        var nameColumn = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        nameColumn.Children.Add(new Border
        {
            Width = 26,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(15, 109, 140)),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "X",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            },
        });

        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = "01_pivot-tables_customer-products.xlsx",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        text.Children.Add(new TextBlock
        {
            Text = @"C:\Users\quinn\OneDrive\Documents\FreeX\FreeX\test-corpus\public\contextures",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        nameColumn.Children.Add(text);
        AddGridChild(grid, nameColumn, 0, 0);
        AddGridChild(grid, new TextBlock
        {
            Text = "Yesterday at 1:43 AM",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        }, 0, 1);
        AddGridChild(grid, AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Pin, 22, "Pin to list"), 0, 2);
        return grid;
    }

    private static Control CreateParityCapturedBackstageInfoPane()
    {
        var root = new AvaloniaGrid
        {
            Margin = new Thickness(44, 34, 46, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(420) },
                new ColumnDefinition { Width = new GridLength(1) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        var actions = new StackPanel { Spacing = 14 };
        actions.Children.Add(new TextBlock
        {
            Text = "Info",
            FontSize = 30,
            FontWeight = FontWeight.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        actions.Children.Add(new TextBlock
        {
            Text = "Workbook actions",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        actions.Children.Add(CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind.Protect, "Protect Workbook", "Control what types of changes people can make to this workbook."));
        actions.Children.Add(CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind.Accessibility, "Check Accessibility", "Inspect the workbook for content that people with disabilities might find difficult to read."));
        actions.Children.Add(CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind.Info, "Workbook Statistics", "View sheet, cell, chart, and object counts for the current workbook."));
        actions.Children.Add(CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind.Warning, "Error Checking", "Find common formula and workbook problems before sharing."));
        AddGridChild(root, actions, 0, 0);
        AddGridChild(root, new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            Margin = new Thickness(0, 52, 0, 0),
        }, 0, 1);

        var properties = new StackPanel
        {
            Margin = new Thickness(28, 52, 0, 0),
            Spacing = 10,
        };
        properties.Children.Add(new TextBlock
        {
            Text = "Properties",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
        });
        var workbook = ParityDemoWorkbookFactory.Create();
        var shareStatus = WorkbookShareReadinessPlanner.FormatStatus(
            WorkbookShareReadinessPlanner.CreatePlan(null, WorkbookShareSurface.WindowsShare, _ => false));
        var exportStatus = WorkbookExportReadinessPlanner.Create(workbook).StatusText;
        var workbookProtection = workbook.IsStructureProtected
            ? "Workbook structure protected."
            : "Workbook structure unprotected.";
        var activeSheetProtection = workbook.Sheets[workbook.ActiveSheetIndex ?? 0].IsProtected
            ? "Active sheet protected."
            : "Active sheet unprotected.";

        properties.Children.Add(CreateParityCapturedBackstageProperty("Workbook name", workbook.Name));
        properties.Children.Add(CreateParityCapturedBackstageProperty("File path", "Not saved yet"));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Sheets", workbook.Sheets.Count.ToString(CultureInfo.CurrentCulture)));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Format", ".xlsx"));
        properties.Children.Add(CreateParityCapturedBackstageProperty("File size", "Not saved yet"));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Last modified", "Not saved yet"));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Share", shareStatus));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Export", exportStatus));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Workbook protection", workbookProtection));
        properties.Children.Add(CreateParityCapturedBackstageProperty("Active sheet protection", activeSheetProtection));
        AddGridChild(root, properties, 0, 2);

        return new Border
        {
            Background = Brushes.White,
            Child = root,
        };
    }

    private static Control CreateParityCapturedBackstageInfoAction(RibbonCommandIconKind iconKind, string title, string detail)
    {
        _ = iconKind;
        return new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Children =
            {
                new Border
                {
                    Width = 220,
                    Height = 30,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(221, 221, 221)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = title,
                        FontSize = 12,
                        Foreground = Brushes.Black,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    },
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 11,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 292,
                },
            },
        };
    }

    private static Control CreateParityCapturedBackstageProperty(string name, string value) =>
        new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Children =
            {
                new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(95, 99, 104)),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    TextAlignment = TextAlignment.Left,
                },
                new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                    TextAlignment = TextAlignment.Left,
                },
            },
        };

    /// <summary>
    /// Renders <paramref name="visual"/> into an off-screen <see cref="RenderTargetBitmap"/> at the given
    /// pixel size and writes it as a PNG. The visual is measured/arranged first so an off-screen or
    /// not-yet-shown window still produces a populated bitmap.
    /// </summary>
    private static void RenderVisualToPng(Visual visual, int width, int height, string path)
    {
        using var bitmap = RenderVisualToBitmap(visual, width, height);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.Create(path);
        bitmap.Save(stream);
    }

    private static RenderTargetBitmap RenderVisualToBitmap(Visual visual, int width, int height)
    {
        var pixelWidth = Math.Max(1, width);
        var pixelHeight = Math.Max(1, height);

        if (visual is Layoutable layoutable)
        {
            layoutable.Measure(new Size(pixelWidth, pixelHeight));
            layoutable.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
        }
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        bitmap.Render(visual);
        return bitmap;
    }
}
