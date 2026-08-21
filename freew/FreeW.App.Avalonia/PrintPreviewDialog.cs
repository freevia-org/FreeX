using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Shell;
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
    private readonly FreeWPrintPreviewSession _session;

    public PrintPreviewDialog(
        TextDocument document,
        string displayName,
        Func<Task>? createPdf = null,
        BackstageDirectPrintCapability? directPrintCapability = null,
        Func<Task>? directPrint = null,
        FreeW.App.Presentation.DocumentView.ReviewDisplayState? reviewDisplayState = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        _createPdf = createPdf;
        _directPrint = directPrint;
        _session = new FreeWPrintPreviewSession(
            displayName,
            document.Page,
            directPrintCapability ?? BackstageDirectPrintCapability.Deferred(),
            canCreatePdf: createPdf is not null,
            canDirectPrint: directPrint is not null);
        var state = _session.State;

        Title = state.Title;
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "FreeWPrintPreviewDialog");

        _preview.LoadDocument(document);
        _preview.ViewMode = DocumentViewMode.PrintLayout;
        _preview.Focusable = false;
        if (reviewDisplayState is { } liveReviewState)
        {
            // Seed the preview's review-display state (Display for Review + the three Show Markup
            // toggles) from the live editor. The layout/line-breaking pass gates hidden/deleted runs
            // by this policy (see DocumentView.RevisionDecision(...).IsTextVisible), so a mismatch here
            // means the preview's content and page count would disagree with what ExportPdfAsync/
            // PrintAsync actually render from the live editor instance.
            _preview.ApplyDisplayForReview(liveReviewState.DisplayMode);
            _preview.ApplyShowMarkupInsertionsAndDeletions(liveReviewState.ShowInsertionsAndDeletions);
            _preview.ApplyShowMarkupComments(liveReviewState.ShowComments);
            _preview.ApplyShowMarkupFormatting(liveReviewState.ShowFormatting);
        }
        AutomationProperties.SetAutomationId(_preview, "PrintPreviewDocumentView");

        Content = BuildShell(state);
        Opened += (_, _) => UpdatePageCount();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private Control BuildShell(FreeWPrintPreviewState state)
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

        var summary = BuildSummaryPane(state);
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
        var action = _session.State.PrimaryAction;
        var toolbar = new DockPanel
        {
            Background = Brushes.White,
            LastChildFill = true,
            Margin = new Thickness(0),
        };

        var printButton = new Button
        {
            Content = action.Label,
            IsEnabled = action.IsEnabled,
            Margin = new Thickness(12, 8, 6, 8),
            Padding = new Thickness(14, 6),
        };
        AutomationProperties.SetAutomationId(printButton, "PrintPreviewPrintButton");
        ToolTip.SetTip(
            printButton,
            action.Description);
        if (action.Action == FreeWPrintPreviewPrimaryAction.DirectPrint)
        {
            var directPrint = _directPrint!;
            printButton.Click += async (_, _) => await directPrint();
        }
        else if (action.Action == FreeWPrintPreviewPrimaryAction.CreatePdf)
        {
            var createPdf = _createPdf!;
            printButton.Click += async (_, _) => await createPdf();
        }
        DockPanel.SetDock(printButton, Dock.Left);
        toolbar.Children.Add(printButton);

        var closeButton = new Button
        {
            Content = UiText.Get("Dialog_Close_Label"),
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

    private static Control BuildSummaryPane(FreeWPrintPreviewState state)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
        };

        panel.Children.Add(new TextBlock
        {
            Text = UiText.Get("PrintPreview_Settings_Heading"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x19, 0x1F, 0x28)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = state.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x74)),
        });

        foreach (var field in state.Fields)
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
        _pageCount.Text = _session.SetPageCount(_preview.PageCount).PageCountText;
    }
}
