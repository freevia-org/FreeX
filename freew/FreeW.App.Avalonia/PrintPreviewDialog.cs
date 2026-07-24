using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Lightweight Avalonia print preview surface. It reuses the live paginated <see cref="DocumentView"/>
/// renderer over a document snapshot, while direct native printing remains a host-specific follow-up.
/// </summary>
internal sealed class PrintPreviewDialog : Window
{
    private readonly DocumentView _preview = new();
    private readonly TextBlock _pageCount = new();
    private readonly Func<Task>? _createPdf;
    private readonly Func<Task>? _directPrint;
    private readonly BackstageDirectPrintCapability _directPrintCapability;

    public PrintPreviewDialog(
        TextDocument document,
        string displayName,
        Func<Task>? createPdf = null,
        BackstageDirectPrintCapability? directPrintCapability = null,
        Func<Task>? directPrint = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        _createPdf = createPdf;
        _directPrint = directPrint;
        _directPrintCapability = directPrintCapability ?? BackstageDirectPrintCapability.Deferred();

        var titleName = string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName;
        Title = $"Print Preview - {titleName}";
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "FreeWPrintPreviewDialog");

        _preview.LoadDocument(document);
        _preview.ViewMode = DocumentViewMode.PrintLayout;
        _preview.Focusable = false;
        AutomationProperties.SetAutomationId(_preview, "PrintPreviewDocumentView");

        Content = BuildShell(document, titleName);
        Opened += (_, _) => UpdatePageCount();
    }

    private Control BuildShell(TextDocument document, string displayName)
    {
        var root = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        };

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("260,*"),
        };

        var summary = BuildSummaryPane(document, displayName, _directPrintCapability);
        Grid.SetColumn(summary, 0);
        grid.Children.Add(summary);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(36, 24),
            Content = _preview,
        };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);

        root.Children.Add(grid);
        return root;
    }

    private Control BuildToolbar()
    {
        var toolbar = new DockPanel
        {
            Background = Brushes.White,
            LastChildFill = true,
            Margin = new Thickness(0),
        };

        var canDirectPrint = _directPrintCapability.IsAvailable && _directPrint is not null;
        var usePdfFallback = !canDirectPrint && _createPdf is not null;
        var printButton = new Button
        {
            Content = canDirectPrint ? "Print" : BackstageViewTextResources.CreatePdfLabel,
            IsEnabled = canDirectPrint || usePdfFallback,
            Margin = new Thickness(12, 8, 6, 8),
            Padding = new Thickness(14, 6),
        };
        AutomationProperties.SetAutomationId(printButton, "PrintPreviewPrintButton");
        ToolTip.SetTip(
            printButton,
            _directPrintCapability.IsAvailable
                ? _directPrintCapability.ActionDescription
                : _directPrintCapability.DeferredNote ?? _directPrintCapability.ActionDescription);
        if (canDirectPrint)
        {
            var directPrint = _directPrint!;
            printButton.Click += async (_, _) => await directPrint();
        }
        else if (usePdfFallback)
        {
            var createPdf = _createPdf!;
            printButton.Click += async (_, _) => await createPdf();
        }
        DockPanel.SetDock(printButton, Dock.Left);
        toolbar.Children.Add(printButton);

        var closeButton = new Button
        {
            Content = "Close",
            Margin = new Thickness(6, 8, 12, 8),
            Padding = new Thickness(14, 6),
        };
        AutomationProperties.SetAutomationId(closeButton, "PrintPreviewCloseButton");
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        toolbar.Children.Add(closeButton);

        _pageCount.VerticalAlignment = VerticalAlignment.Center;
        _pageCount.Foreground = new SolidColorBrush(Color.FromRgb(0x35, 0x3C, 0x45));
        AutomationProperties.SetAutomationId(_pageCount, "PrintPreviewPageCount");
        toolbar.Children.Add(_pageCount);

        return toolbar;
    }

    private static Control BuildSummaryPane(
        TextDocument document,
        string displayName,
        BackstageDirectPrintCapability directPrintCapability)
    {
        var plan = BackstagePrintPanePlanner.Build(displayName, document.Page, directPrintCapability);
        var panel = new StackPanel
        {
            Spacing = 10,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Print settings",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x19, 0x1F, 0x28)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Preview uses the current paginated layout. {directPrintCapability.ActionDescription}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74)),
        });

        foreach (var field in plan.Fields)
        {
            panel.Children.Add(new TextBlock
            {
                Text = field.Label,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x35, 0x3C, 0x45)),
                Margin = new Thickness(0, 8, 0, 0),
            });
            panel.Children.Add(new TextBlock
            {
                Text = field.Value,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74)),
            });
        }

        return new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(18),
            Child = panel,
        };
    }

    private void UpdatePageCount()
    {
        var pages = Math.Max(1, _preview.PageCount);
        _pageCount.Text = pages == 1 ? "1 page" : $"{pages} pages";
    }
}
