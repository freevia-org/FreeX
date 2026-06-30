using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;
using FreeW.Core.Model;
using AvaloniaContentControl = global::Avalonia.Controls.ContentControl;
using AvaloniaGrid = global::Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment;

namespace FreeW.App.Avalonia.Backstage;

/// <summary>
/// The FreeW Avalonia backstage (File screen): a full-window modal dialog with a left rail of pane
/// entries and a scrollable content area. The shared <see cref="BackstagePaneSurfacePlanner"/> owns
/// pane text and row planning; this file keeps the Avalonia-specific layout and controls.
///
/// Opened via <see cref="BackstageView.ShowAsync"/>; dismissed by the Back button or Escape.
/// </summary>
internal sealed class BackstageView : Window
{
    // ── Brand colors (FreeW teal) ────────────────────────────────────────────
    // Left rail: teal brand accent from the FreeW palette token set.
    private static readonly IBrush RailBackground = new SolidColorBrush(Color.FromRgb(0x19, 0x6E, 0x6C));
    private static readonly IBrush RailForeground = Brushes.White;
    private static readonly IBrush RailSelectedBackground = new SolidColorBrush(Color.FromRgb(0x12, 0x54, 0x52));
    private static readonly IBrush RailHoverBackground = new SolidColorBrush(Color.FromRgb(0x1B, 0x7D, 0x7B));

    // Content area chrome
    private static readonly IBrush ContentBackground = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9));
    internal static readonly IBrush PrimaryInk = new SolidColorBrush(Color.FromRgb(0x19, 0x1F, 0x28));
    internal static readonly IBrush SecondaryInk = new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74));
    private static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    private static readonly AvaloniaBackstageChromeStyle BackstageChromeStyle = new(PrimaryInk, SecondaryInk)
    {
        SeparatorBrush = SeparatorBrush,
        DetailLabelVerticalAlignment = VerticalAlignment.Top,
    };

    private readonly BackstageCallbacks _callbacks;
    private readonly AvaloniaContentControl _contentHost = new();
    private readonly List<Button> _navButtons = [];
    private BackstagePane _currentPane = BackstagePane.Home;

    // ── Public factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Build and show the backstage modal. <paramref name="owner"/> is the main window.
    /// </summary>
    public static Task ShowAsync(Window owner, BackstageCallbacks callbacks, BackstagePane initialPane = BackstagePane.Home)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callbacks);

        var view = new BackstageView(callbacks, initialPane);
        return view.ShowDialog(owner);
    }

    // ── Construction ─────────────────────────────────────────────────────────

    internal BackstageView(BackstageCallbacks callbacks, BackstagePane initialPane = BackstagePane.Home)
    {
        _callbacks = callbacks;

        Title = BackstageViewTextResources.WindowTitle;
        Width = 840;
        Height = 620;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "FreeWBackstageWindow");

        var shell = BuildShell();
        Content = shell;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        NavigateTo(initialPane);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private AvaloniaGrid BuildShell()
    {
        // Two columns: left rail (200 px) + content area (*)
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*"),
        };
        // Build each child ONCE and set its column on the instance that is actually added — building
        // twice (and setting the column on the discarded copy) left both children in column 0, so the
        // content area overlapped the nav rail.
        var leftRail = BuildLeftRail();
        AvaloniaGrid.SetColumn(leftRail, 0);
        grid.Children.Add(leftRail);
        var contentArea = BuildContentArea();
        AvaloniaGrid.SetColumn(contentArea, 1);
        grid.Children.Add(contentArea);
        return grid;
    }

    private Panel BuildLeftRail()
    {
        var panel = new StackPanel
        {
            Background = RailBackground,
            Spacing = 0,
        };
        AutomationProperties.SetAutomationId(panel, "BackstageLeftRail");

        // Back button
        var backBtn = new Button
        {
            Content = BackstageViewTextResources.BackButton,
            Background = Brushes.Transparent,
            Foreground = RailForeground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 12),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            FontSize = 13,
        };
        AutomationProperties.SetAutomationId(backBtn, "BackstageBackButton");
        backBtn.Click += (_, _) => Close();
        panel.Children.Add(backBtn);

        // Separator
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = RailSelectedBackground,
            Margin = new Thickness(0, 4, 0, 4),
        });

        // Nav entries
        foreach (var entry in BackstageViewTextResources.RailEntries)
        {
            if (Enum.TryParse<BackstagePane>(entry.PaneKey, out var pane))
                AddNavEntry(panel, pane, entry.Label);
        }

        // Spacer
        panel.Children.Add(new Border { Height = 16 });

        // Options placeholder (no implementation yet)
        var optionsBtn = new Button
        {
            Content = BackstageViewTextResources.OptionsButton,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            FontSize = 13,
            IsEnabled = false,
        };
        AutomationProperties.SetAutomationId(optionsBtn, "BackstageOptionsButton");
        panel.Children.Add(optionsBtn);

        return panel;
    }

    private void AddNavEntry(Panel parent, BackstagePane pane, string label)
    {
        var btn = new Button
        {
            Content = label,
            Background = Brushes.Transparent,
            Foreground = RailForeground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
            FontSize = 13,
            Tag = pane,
        };
        AutomationProperties.SetAutomationId(btn, $"BackstageNav_{pane}");
        btn.Click += (_, _) => NavigateTo(pane);
        _navButtons.Add(btn);
        parent.Children.Add(btn);
    }

    private Border BuildContentArea()
    {
        var area = AvaloniaBackstageChrome.CreateContentArea(new AvaloniaBackstageContentAreaSpec(
            _contentHost,
            ContentBackground));
        AvaloniaGrid.SetColumn(area, 1);
        return area;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void NavigateTo(BackstagePane pane)
    {
        _currentPane = pane;

        // Update rail button selection highlight
        foreach (var btn in _navButtons)
        {
            var isSelected = btn.Tag is BackstagePane p && p == pane;
            btn.Background = isSelected ? RailSelectedBackground : Brushes.Transparent;
        }

        _contentHost.Content = BuildPane(pane);
    }

    private Control BuildPane(BackstagePane pane) => pane switch
    {
        BackstagePane.Home => BuildHomePane(),
        BackstagePane.Open => BuildOpenPane(),
        BackstagePane.SaveAs => BuildSaveAsPane(),
        BackstagePane.Print => BuildPrintPane(),
        BackstagePane.Share => BuildSharePane(),
        BackstagePane.Export => BuildExportPane(),
        BackstagePane.Info => BuildInfoPane(),
        BackstagePane.Account => BuildAccountPane(),
        _ => new TextBlock { Text = $"{pane} {BackstageViewTextResources.NotImplementedSuffix}", Foreground = SecondaryInk },
    };

    // ── Home pane ─────────────────────────────────────────────────────────────

    private Control BuildHomePane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildHomePane(
            _callbacks.GetRecentEntries(),
            newDocument: () => { Close(); _callbacks.NewDocument(); },
            openRecent: path => { Close(); _callbacks.OpenRecent(path); },
            browse: () => { Close(); _callbacks.Browse(); },
            openMore: () => NavigateTo(BackstagePane.Open));

        return BuildActionGroupContent(surface);
    }

    // ── Open pane ─────────────────────────────────────────────────────────────

    private Control BuildOpenPane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildOpenActionPane(
            _callbacks.GetRecentEntries(),
            openRecent: path => { Close(); _callbacks.OpenRecent(path); },
            browse: () => { Close(); _callbacks.Browse(); },
            recoverUnsaved: () => { Close(); _callbacks.RecoverUnsaved(); });

        return BuildActionGroupContent(surface);
    }

    // ── Save As pane ─────────────────────────────────────────────────────────

    private Control BuildSaveAsPane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildSaveAsPane(
            _callbacks.GetFileFormats(),
            _callbacks.DisplayName,
            _callbacks.CurrentPath,
            saveAs: () => { Close(); _callbacks.SaveAs(); },
            saveAsExtension: ext => { Close(); _callbacks.SaveAsExtension(ext); });

        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(BuildPaneHeader(surface.Title, surface.Description));

        // Inline plan info: current suggested filename + selected extension
        var infoGrid = CreateDetailGrid();
        AddDetailRow(infoGrid, surface.Inline.FileNameHeading, surface.InlinePlan.SuggestedFileName, "SaveAsSuggestedFileName");
        AddDetailRow(infoGrid, surface.Inline.SaveAsTypeHeading, surface.InlinePlan.SelectedExtension, "SaveAsSelectedExtension");
        content.Children.Add(infoGrid);

        foreach (var group in surface.Groups)
            content.Children.Add(BuildActionGroup(group, isLast: group == surface.Groups[^1]));

        return content;
    }

    // ── Print pane ────────────────────────────────────────────────────────────

    private Control BuildPrintPane()
    {
        var page = _callbacks.GetPageSettings();
        var plan = BackstagePrintPanePlanner.Build(_callbacks.DisplayName, page);

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader(BackstageViewTextResources.Print.Title, plan.Description));

        // Document settings grid
        content.Children.Add(BuildSectionHeader(BackstageViewTextResources.DocumentSettingsSection));
        var fieldGrid = CreateDetailGrid();
        foreach (var field in plan.Fields)
            AddDetailRow(fieldGrid, field.Label, field.Value, $"PrintField_{field.Label}");
        content.Children.Add(fieldGrid);

        // Print action groups — all disabled (real print is a deferred parity item)
        foreach (var group in plan.Groups)
        {
            content.Children.Add(BuildSectionHeader(group.Heading));
            foreach (var action in group.Actions)
            {
                content.Children.Add(AvaloniaBackstageChrome.CreateDescribedActionRow(
                    new AvaloniaBackstageDescribedActionRowSpec(
                        action.Label,
                        action.Description,
                        $"PrintAction_{action.Kind}")
                    {
                        IsEnabled = false,
                    },
                    BackstageChromeStyle));
            }
        }

        content.Children.Add(AvaloniaBackstageChrome.CreateNote(
            BackstageViewTextResources.DirectPrintDeferredNote,
            BackstageChromeStyle,
            fontStyle: FontStyle.Italic,
            margin: new Thickness(0, 8, 0, 0)));

        return content;
    }

    // ── Share pane ────────────────────────────────────────────────────────────

    private Control BuildSharePane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildSharePane(
            currentPath: _callbacks.CurrentPath,
            fileExists: File.Exists,
            saveAs: () => { Close(); _callbacks.SaveAs(); },
            openContainingFolder: path => { Close(); _callbacks.OpenContainingFolder(path); },
            saveCopy: () => { Close(); _callbacks.SaveAs(); },   // saveCopy → SaveAs (no separate copy-save yet)
            exportPdf: () => { Close(); _callbacks.ExportPdf(); });

        return BuildActionGroupContent(surface);
    }

    // ── Export pane ───────────────────────────────────────────────────────────

    private Control BuildExportPane()
    {
        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            _callbacks.GetFileFormats(),
            exportPdf: () => { Close(); _callbacks.ExportPdf(); },
            exportXps: null,
            saveAsExtension: ext => { Close(); _callbacks.SaveAsExtension(ext); });

        return BuildActionGroupContent(surface);

    }

    // ── Info pane ─────────────────────────────────────────────────────────────

    private Control BuildInfoPane()
    {
        var safetyGroups = BackstageInfoSafetyPanePlanner.Build();

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader(BackstageViewTextResources.Info.Title, BackstageViewTextResources.Info.Description));

        // Document properties
        content.Children.Add(BuildSectionHeader(BackstageViewTextResources.DocumentPropertiesSection));
        var propsGrid = CreateDetailGrid();
        var name = string.IsNullOrWhiteSpace(_callbacks.DisplayName) ? BackstageViewTextResources.UntitledValue : _callbacks.DisplayName;
        AddDetailRow(propsGrid, BackstageViewTextResources.DocumentLabel, name, "InfoDocumentName");
        AddDetailRow(propsGrid, BackstageViewTextResources.PathLabel, _callbacks.CurrentPath ?? BackstageViewTextResources.NotSavedValue, "InfoDocumentPath");
        if (_callbacks.CurrentPath is { } path && File.Exists(path))
        {
            try
            {
                var info = new FileInfo(path);
                AddDetailRow(propsGrid, BackstageViewTextResources.SizeLabel, FormatFileSize(info.Length), "InfoFileSize");
                AddDetailRow(propsGrid, BackstageViewTextResources.ModifiedLabel, info.LastWriteTime.ToString("g"), "InfoLastModified");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        content.Children.Add(propsGrid);

        // Safety groups (all placeholder — actions not yet implemented for FreeW)
        foreach (var group in safetyGroups)
        {
            content.Children.Add(BuildSectionHeader(group.Heading));
            foreach (var action in group.Actions)
            {
                content.Children.Add(AvaloniaBackstageChrome.CreateDescribedActionRow(
                    new AvaloniaBackstageDescribedActionRowSpec(
                        action.Label,
                        action.Description,
                        $"InfoAction_{action.Kind}")
                    {
                        IsEnabled = false,
                    },
                    BackstageChromeStyle));
            }
        }

        return content;
    }

    // ── Account pane ─────────────────────────────────────────────────────────

    private Control BuildAccountPane()
    {
        var version = typeof(BackstageView).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                BackstageViewTextResources.ProductName,
                version,
                SafeEnvironment(() => Environment.UserName),
                SafeEnvironment(() => Environment.MachineName),
                SafeEnvironment(() =>
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeW"))));

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader(BackstageViewTextResources.Account.Title, plan.Description));

        foreach (var group in plan.Groups)
        {
            content.Children.Add(BuildSectionHeader(group.Heading));
            var fieldGrid = CreateDetailGrid();
            foreach (var field in group.Fields)
                AddDetailRow(fieldGrid, field.Label, field.Value, $"Account_{group.Heading}_{field.Label}");
            content.Children.Add(fieldGrid);
        }

        // Options placeholder (same as rail — not yet implemented)
        var optionsBtn = new Button
        {
            Content = plan.OptionsText,
            Padding = new Thickness(12, 6),
            IsEnabled = false,
        };
        AutomationProperties.SetAutomationId(optionsBtn, "AccountOptionsButton");
        content.Children.Add(optionsBtn);

        return content;
    }

    // ── Generic action-group renderer ────────────────────────────────────────

    private Control BuildActionGroupContent(string title, IReadOnlyList<BackstageActionGroup> groups, string description)
    {
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(BuildPaneHeader(title, description));
        for (var i = 0; i < groups.Count; i++)
            content.Children.Add(BuildActionGroup(groups[i], isLast: i == groups.Count - 1));
        return content;
    }

    private Control BuildActionGroupContent(BackstageActionPaneSurfaceSpec surface) =>
        BuildActionGroupContent(surface.Title, surface.Groups, surface.Description);

    private Control BuildActionGroup(BackstageActionGroup group, bool isLast)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(BuildSectionHeader(group.Heading));

        foreach (var action in group.Actions)
        {
            var row = BuildActionRow(action);
            stack.Children.Add(row);
        }

        if (!isLast)
        {
            stack.Children.Add(AvaloniaBackstageChrome.CreateSeparator(
                BackstageChromeStyle,
                new Thickness(0, 12, 0, 0)));
        }

        return stack;
    }

    private static Control BuildActionRow(BackstageActionRow action) =>
        AvaloniaBackstageChrome.CreateStackedActionButton(
            new AvaloniaBackstageStackedActionButtonSpec(
                action.Label,
                action.Description,
                $"BackstageAction_{action.Label.Replace(' ', '_')}",
                action.Invoke),
            BackstageChromeStyle);

    // ── Chrome helpers ────────────────────────────────────────────────────────

    private static Control BuildPaneHeader(string title, string description) =>
        AvaloniaBackstageChrome.CreatePaneHeader(title, description, BackstageChromeStyle);

    internal static TextBlock BuildSectionHeader(string text) =>
        AvaloniaBackstageChrome.CreateSectionHeader(text, BackstageChromeStyle);

    internal static AvaloniaGrid CreateDetailGrid() =>
        AvaloniaBackstageChrome.CreateDetailGrid();

    internal static void AddDetailRow(AvaloniaGrid grid, string label, string value, string automationId) =>
        AvaloniaBackstageChrome.AddDetailRow(grid, label, value, automationId, BackstageChromeStyle);

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    private static string SafeEnvironment(Func<string> read)
    {
        try { return read(); }
        catch (InvalidOperationException) { return string.Empty; }
        catch (PlatformNotSupportedException) { return string.Empty; }
    }
}
