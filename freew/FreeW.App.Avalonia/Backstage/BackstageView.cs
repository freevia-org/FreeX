using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;
using FreeW.Core.Model;
using AvaloniaContentControl = global::Avalonia.Controls.ContentControl;
using AvaloniaGrid = global::Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment;

namespace FreeW.App.Avalonia.Backstage;

/// <summary>
/// The FreeW Avalonia backstage (File screen): a full-window modal dialog with a left rail of pane
/// entries and a scrollable content area. Each content pane is rendered from its portable planner
/// (<see cref="BackstageHomePanePlanner"/>, <see cref="BackstageOpenPanePlanner"/>, etc.) so the
/// data-shaping lives in the shared Presentation tier and this file only lays out.
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

        Title = "FreeW — File";
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
        AvaloniaGrid.SetColumn(BuildLeftRail(), 0);
        AvaloniaGrid.SetColumn(BuildContentArea(), 1);
        grid.Children.Add(BuildLeftRail());
        grid.Children.Add(BuildContentArea());
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
            Content = "← Back",
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
        AddNavEntry(panel, BackstagePane.Home, "Home");
        AddNavEntry(panel, BackstagePane.Open, "Open");
        AddNavEntry(panel, BackstagePane.SaveAs, "Save As");
        AddNavEntry(panel, BackstagePane.Print, "Print");
        AddNavEntry(panel, BackstagePane.Share, "Share");
        AddNavEntry(panel, BackstagePane.Export, "Export");
        AddNavEntry(panel, BackstagePane.Info, "Info");
        AddNavEntry(panel, BackstagePane.Account, "Account");

        // Spacer
        panel.Children.Add(new Border { Height = 16 });

        // Options placeholder (no implementation yet)
        var optionsBtn = new Button
        {
            Content = "Options",
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
        var area = new Border
        {
            Background = ContentBackground,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(32, 24),
                Content = _contentHost,
            },
        };
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
        _ => new TextBlock { Text = $"{pane} pane not yet implemented.", Foreground = SecondaryInk },
    };

    // ── Home pane ─────────────────────────────────────────────────────────────

    private Control BuildHomePane()
    {
        var recentEntries = _callbacks.GetRecentEntries();
        var groups = BackstageHomePanePlanner.Build(
            recentEntries,
            newDocument: () => { Close(); _callbacks.NewDocument(); },
            openRecent: path => { Close(); _callbacks.OpenRecent(path); },
            browse: () => { Close(); _callbacks.Browse(); },
            openMore: () => NavigateTo(BackstagePane.Open));

        return BuildActionGroupContent("Home", groups, "Start with a new document or reopen a recent file.");
    }

    // ── Open pane ─────────────────────────────────────────────────────────────

    private Control BuildOpenPane()
    {
        var recentEntries = _callbacks.GetRecentEntries();
        var groups = BackstageOpenPanePlanner.Build(
            recentEntries,
            openRecent: path => { Close(); _callbacks.OpenRecent(path); },
            browse: () => { Close(); _callbacks.Browse(); },
            recoverUnsaved: () => { Close(); _callbacks.RecoverUnsaved(); });

        return BuildActionGroupContent("Open", groups, "Open a document from your recent files or browse your PC.");
    }

    // ── Save As pane ─────────────────────────────────────────────────────────

    private Control BuildSaveAsPane()
    {
        var formats = _callbacks.GetFileFormats();
        var inlinePlan = BackstageSaveAsFileTypePlanner.BuildInlinePlan(
            formats,
            _callbacks.DisplayName,
            _callbacks.CurrentPath);

        var groups = BackstageSaveAsFileTypePlanner.Build(
            formats,
            saveAsExtension: ext => { Close(); _callbacks.SaveAsExtension(ext); });

        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(BuildPaneHeader("Save As", "Save this document in a different format."));

        // Inline plan info: current suggested filename + selected extension
        var infoGrid = CreateDetailGrid();
        AddDetailRow(infoGrid, "File name", inlinePlan.SuggestedFileName, "SaveAsSuggestedFileName");
        AddDetailRow(infoGrid, "Format", inlinePlan.SelectedExtension, "SaveAsSelectedExtension");
        content.Children.Add(infoGrid);

        // Format groups
        foreach (var group in groups)
            content.Children.Add(BuildActionGroup(group, isLast: group == groups[^1]));

        return content;
    }

    // ── Print pane ────────────────────────────────────────────────────────────

    private Control BuildPrintPane()
    {
        var page = _callbacks.GetPageSettings();
        var plan = BackstagePrintPanePlanner.Build(_callbacks.DisplayName, page);

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader("Print", plan.Description));

        // Document settings grid
        content.Children.Add(BuildSectionHeader("Document Settings"));
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
                var btn = new Button
                {
                    Content = action.Label,
                    Padding = new Thickness(12, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                    IsEnabled = false,
                };
                AutomationProperties.SetAutomationId(btn, $"PrintAction_{action.Kind}");

                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(btn, Dock.Left);
                row.Children.Add(btn);
                row.Children.Add(new TextBlock
                {
                    Text = action.Description,
                    Foreground = SecondaryInk,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                });
                content.Children.Add(row);
            }
        }

        content.Children.Add(new TextBlock
        {
            Text = "Note: Print is available in FreeW via Export to PDF (Ctrl+Shift+P). Direct printer output is planned for a future update.",
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyle.Italic,
            Margin = new Thickness(0, 8, 0, 0),
        });

        return content;
    }

    // ── Share pane ────────────────────────────────────────────────────────────

    private Control BuildSharePane()
    {
        var groups = BackstageSharePanePlanner.Build(
            currentPath: _callbacks.CurrentPath,
            fileExists: File.Exists,
            saveAs: () => { Close(); _callbacks.SaveAs(); },
            openContainingFolder: path => { Close(); _callbacks.OpenContainingFolder(path); },
            saveCopy: () => { Close(); _callbacks.SaveAs(); },   // saveCopy → SaveAs (no separate copy-save yet)
            exportPdf: () => { Close(); _callbacks.ExportPdf(); });

        return BuildActionGroupContent("Share", groups, "Share this document or send a copy.");
    }

    // ── Export pane ───────────────────────────────────────────────────────────

    private Control BuildExportPane()
    {
        var formats = _callbacks.GetFileFormats();
        var changeFileTypeGroup = BackstageExportFileTypePlanner.BuildChangeFileTypeGroup(
            formats,
            saveAsExtension: ext => { Close(); _callbacks.SaveAsExtension(ext); });

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader("Export", "Export this document to a different file format."));

        // PDF export action (real — wired to ExportPdf)
        content.Children.Add(BuildSectionHeader("Create PDF/XPS Document"));
        var pdfRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var pdfBtn = new Button
        {
            Content = "Create PDF",
            Padding = new Thickness(12, 6),
        };
        AutomationProperties.SetAutomationId(pdfBtn, "ExportCreatePdfButton");
        pdfBtn.Click += (_, _) => { Close(); _callbacks.ExportPdf(); };
        DockPanel.SetDock(pdfBtn, Dock.Left);
        pdfRow.Children.Add(pdfBtn);
        pdfRow.Children.Add(new TextBlock
        {
            Text = "Publish a fixed-layout PDF copy for sharing or printing.",
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        });
        content.Children.Add(pdfRow);

        // Change file type group from the planner
        content.Children.Add(BuildActionGroup(changeFileTypeGroup, isLast: true));

        return content;
    }

    // ── Info pane ─────────────────────────────────────────────────────────────

    private Control BuildInfoPane()
    {
        var safetyGroups = BackstageInfoSafetyPanePlanner.Build();

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader("Info", "Protect, inspect, and review document information."));

        // Document properties
        content.Children.Add(BuildSectionHeader("Document Properties"));
        var propsGrid = CreateDetailGrid();
        var name = string.IsNullOrWhiteSpace(_callbacks.DisplayName) ? "Untitled" : _callbacks.DisplayName;
        AddDetailRow(propsGrid, "Document", name, "InfoDocumentName");
        AddDetailRow(propsGrid, "Path", _callbacks.CurrentPath ?? "(not saved)", "InfoDocumentPath");
        if (_callbacks.CurrentPath is { } path && File.Exists(path))
        {
            try
            {
                var info = new FileInfo(path);
                AddDetailRow(propsGrid, "Size", FormatFileSize(info.Length), "InfoFileSize");
                AddDetailRow(propsGrid, "Modified", info.LastWriteTime.ToString("g"), "InfoLastModified");
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
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var btn = new Button
                {
                    Content = action.Label,
                    Padding = new Thickness(12, 6),
                    IsEnabled = false,   // placeholder — not implemented in FreeW Avalonia yet
                };
                AutomationProperties.SetAutomationId(btn, $"InfoAction_{action.Kind}");
                DockPanel.SetDock(btn, Dock.Left);
                row.Children.Add(btn);
                row.Children.Add(new TextBlock
                {
                    Text = action.Description,
                    Foreground = SecondaryInk,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                });
                content.Children.Add(row);
            }
        }

        return content;
    }

    // ── Account pane ─────────────────────────────────────────────────────────

    private Control BuildAccountPane()
    {
        var version = typeof(BackstageView).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var plan = BackstageAccountPanePlanner.Build(
            productName: "FreeW",
            version: version,
            userName: SafeEnvironment(() => Environment.UserName),
            machineName: SafeEnvironment(() => Environment.MachineName),
            dataFolder: SafeEnvironment(() =>
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeW")));

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(BuildPaneHeader("Account", plan.Description));

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
            stack.Children.Add(new Border
            {
                Height = 1,
                Background = SeparatorBrush,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        return stack;
    }

    private Control BuildActionRow(BackstageActionRow action)
    {
        var label = new TextBlock
        {
            Text = action.Label,
            Foreground = PrimaryInk,
            FontWeight = FontWeight.Medium,
            FontSize = 13,
        };
        var desc = new TextBlock
        {
            Text = action.Description,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        var inner = new StackPanel { Children = { label, desc } };

        var btn = new Button
        {
            Content = inner,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 6),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(btn, $"BackstageAction_{action.Label.Replace(' ', '_')}");
        btn.Click += (_, _) => action.Invoke();
        return btn;
    }

    // ── Chrome helpers ────────────────────────────────────────────────────────

    private static Control BuildPaneHeader(string title, string description)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = PrimaryInk,
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 13,
            Foreground = SecondaryInk,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new Border { Height = 1, Background = SeparatorBrush, Margin = new Thickness(0, 4, 0, 0) });
        return stack;
    }

    internal static TextBlock BuildSectionHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = PrimaryInk,
            Margin = new Thickness(0, 4, 0, 2),
        };

    internal static AvaloniaGrid CreateDetailGrid() =>
        new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 2, 0, 0),
        };

    internal static void AddDetailRow(AvaloniaGrid grid, string label, string value, string automationId)
    {
        var rowIndex = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = SecondaryInk,
            Margin = new Thickness(0, 3, 16, 3),
            VerticalAlignment = VerticalAlignment.Top,
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
        AutomationProperties.SetAutomationId(valueBlock, automationId);
        AvaloniaGrid.SetColumn(valueBlock, 1);
        AvaloniaGrid.SetRow(valueBlock, rowIndex);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

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
