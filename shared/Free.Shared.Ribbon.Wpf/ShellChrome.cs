using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Palette/identity for a <see cref="ShellChrome"/> title bar: the app-badge letter and the
/// title-bar / caption / badge colours. Defaults to the FreeX navy (#17324D) title bar with a
/// white caption and teal (#0F6D8C) badge so an app gets the family look with no configuration.
/// </summary>
public sealed class ShellChromeOptions
{
    public string BadgeLetter { get; init; } = "W";
    public Color TitleBarColor { get; init; } = Color.FromRgb(0x17, 0x32, 0x4D);
    public Color TitleBarForegroundColor { get; init; } = Colors.White;
    public Color BadgeColor { get; init; } = Color.FromRgb(0x0F, 0x6D, 0x8C);
    public double CaptionHeight { get; init; } = 34;

    /// <summary>
    /// Optional pack URI of the application icon (e.g. <c>"pack://application:,,,/Resources/FreeW.ico"</c>).
    /// When set, it becomes the window icon (taskbar / Alt-Tab) AND the title-bar badge shows the real icon
    /// instead of the drawn <see cref="BadgeLetter"/> tile — matching how FreeX carries its app identity.
    /// </summary>
    public string? IconUri { get; init; }
}

/// <summary>
/// The assembled shared title bar plus the two slots a host fills in: <see cref="QatHost"/> (add
/// quick-access buttons here) and <see cref="TitleText"/> (keep its text in sync with the document name).
/// </summary>
public sealed class ShellTitleBar
{
    public Border Root { get; }
    public StackPanel QatHost { get; }
    public TextBlock TitleText { get; }

    internal ShellTitleBar(Border root, StackPanel qatHost, TextBlock titleText)
    {
        Root = root;
        QatHost = qatHost;
        TitleText = titleText;
    }
}

/// <summary>
/// Builds the app-neutral window shell FreeX pioneered: a borderless <see cref="WindowChrome"/> window
/// with a custom integrated title bar (app badge + Quick Access Toolbar + centred title + embedded
/// minimize / maximize-restore / close buttons), Win11 rounded corners, and the maximized-state content
/// inset. Both the chrome styling (via <c>SharedChromeResources.xaml</c>) and this layout now live in the
/// shared tier so a second app assembles its window from shared parts instead of re-coding the chrome.
/// </summary>
public static class ShellChrome
{
    /// <summary>
    /// Turns <paramref name="window"/> into a borderless WindowChrome shell: no OS frame, a custom caption
    /// region, Win11 rounded corners, and a maximized-state inset so content isn't clipped under the screen
    /// edges. Merges the shared chrome resource dictionary (idempotently) so the caption / flat / status
    /// styles resolve. Call this before assigning <see cref="Window.Content"/>.
    /// </summary>
    public static void ConfigureWindow(Window window, ShellChromeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        options ??= new ShellChromeOptions();

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = options.CaptionHeight,
            ResizeBorderThickness = new Thickness(5),
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        EnsureChromeResources(window);

        if (LoadIcon(options.IconUri) is { } icon)
            window.Icon = icon;

        window.SourceInitialized += (_, _) =>
        {
            // Clamp the maximized size to the monitor work area so a borderless window doesn't cover the
            // taskbar or push the footer off-screen, then apply Win11 rounded corners.
            MaximizedWindowFix.Install(window);
            WindowCornerHelper.ApplyRoundedCorners(window);
        };
    }

    /// <summary>
    /// Builds the integrated title bar for <paramref name="window"/>. The window buttons drive the window
    /// directly (minimize / maximize-restore / close, with the glyph kept in sync with the window state);
    /// the returned <see cref="ShellTitleBar"/> exposes the QAT host and title text for the host to fill.
    /// </summary>
    public static ShellTitleBar BuildTitleBar(Window window, ShellChromeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        options ??= new ShellChromeOptions();

        var bar = new DockPanel { LastChildFill = true };

        // App badge: the real application icon when one is supplied (matching FreeX carrying its identity),
        // otherwise a small drawn letter tile as the app-neutral default.
        var badge = BuildBadge(options);
        DockPanel.SetDock(badge, Dock.Left);
        bar.Children.Add(badge);

        // Window (caption) buttons, docked right. Right-dock order is right-to-left, so add Close first,
        // then Maximize/Restore, then Minimize, to read [_] [▢] [X] left-to-right.
        var closeButton = CaptionButton(window, CloseGlyph(options.TitleBarForegroundColor), "Close", isClose: true);
        closeButton.Click += (_, _) => window.Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        bar.Children.Add(closeButton);

        var maxRestoreGlyph = MaximizeGlyph(options.TitleBarForegroundColor);
        var maxRestoreButton = CaptionButton(window, maxRestoreGlyph, "Maximize", isClose: false);
        maxRestoreButton.Click += (_, _) => window.WindowState =
            window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        DockPanel.SetDock(maxRestoreButton, Dock.Right);
        bar.Children.Add(maxRestoreButton);

        var minimizeButton = CaptionButton(window, MinimizeGlyph(options.TitleBarForegroundColor), "Minimize", isClose: false);
        minimizeButton.Click += (_, _) => window.WindowState = WindowState.Minimized;
        DockPanel.SetDock(minimizeButton, Dock.Right);
        bar.Children.Add(minimizeButton);

        // Quick Access Toolbar host (the app adds Save / Undo / Redo etc.), docked left after the badge.
        var qatHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0)
        };
        DockPanel.SetDock(qatHost, Dock.Left);
        bar.Children.Add(qatHost);

        // Centred document title fills the remaining (draggable) caption space.
        var titleText = new TextBlock
        {
            Foreground = Freeze(options.TitleBarForegroundColor),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        bar.Children.Add(titleText);

        // Keep the maximize/restore glyph in sync with the window state.
        window.StateChanged += (_, _) => maxRestoreGlyph.Data = window.WindowState == WindowState.Maximized
            ? Geometry.Parse("M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z")
            : Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");

        var root = new Border
        {
            Background = Freeze(options.TitleBarColor),
            Padding = new Thickness(8, 0, 0, 0),
            Height = options.CaptionHeight,
            Child = bar
        };

        return new ShellTitleBar(root, qatHost, titleText);
    }

    private static void EnsureChromeResources(Window window)
    {
        foreach (var dict in window.Resources.MergedDictionaries)
        {
            if (dict.Source is { } source &&
                source.OriginalString.Contains("SharedChromeResources", StringComparison.OrdinalIgnoreCase))
                return;
        }

        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Free.Shared.Ribbon.Wpf;component/SharedChromeResources.xaml", UriKind.Relative)
        });
    }

    // The title-bar app badge: the real application icon (no tile) when an IconUri resolves, else a small
    // drawn letter tile in the badge colour.
    private static FrameworkElement BuildBadge(ShellChromeOptions options)
    {
        if (LoadIcon(options.IconUri) is { } icon)
        {
            return new Image
            {
                Source = icon,
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
        }

        return new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Freeze(options.BadgeColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = new TextBlock
            {
                Text = options.BadgeLetter,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    // Load an icon from a pack URI for the window icon / title-bar badge. Returns null (caller falls back)
    // when no URI is given or it cannot be resolved — a missing icon must never crash window construction.
    private static BitmapFrame? LoadIcon(string? iconUri)
    {
        if (string.IsNullOrWhiteSpace(iconUri))
            return null;
        try
        {
            var frame = BitmapFrame.Create(new Uri(iconUri, UriKind.RelativeOrAbsolute));
            frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Button CaptionButton(Window window, Path glyph, string automationName, bool isClose)
    {
        var button = new Button { Content = glyph, ToolTip = automationName };
        var styleKey = isClose ? "ChromeCaptionCloseButtonStyle" : "ChromeCaptionButtonStyle";
        if (window.TryFindResource(styleKey) is Style style)
            button.Style = style;
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static Path MinimizeGlyph(Color foreground) => CaptionGlyph("M0,5 H10", foreground);
    private static Path MaximizeGlyph(Color foreground) => CaptionGlyph("M0.5,0.5 H9.5 V9.5 H0.5 Z", foreground);
    private static Path CloseGlyph(Color foreground) => CaptionGlyph("M0,0 L10,10 M10,0 L0,10", foreground);

    private static Path CaptionGlyph(string data, Color foreground) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Freeze(foreground),
        StrokeThickness = 1,
        Width = 10,
        Height = 10,
        Stretch = Stretch.None,
        SnapsToDevicePixels = true,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
