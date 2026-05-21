using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed class PrintPreviewDialog : Window
{
    public PrintPreviewDialog(string workbookName, FixedDocument document)
        : this(workbookName, document, new PrintSettingsPlan(["Print active sheet"]))
    {
    }

    public PrintPreviewDialog(
        string workbookName,
        FixedDocument document,
        PrintSettingsPlan settings,
        Action? showMargins = null,
        Action? showPageSetup = null)
    {
        Title = CreateTitle(workbookName);
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel();
        var toolbar = new ToolBar
        {
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var viewer = new DocumentViewer { Document = document };
        var previousButton = new Button
        {
            Content = "_Previous Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.PreviousPage,
            CommandTarget = viewer
        };
        var nextButton = new Button
        {
            Content = "_Next Page",
            Padding = new Thickness(10, 4, 10, 4),
            Command = NavigationCommands.NextPage,
            CommandTarget = viewer
        };
        var printButton = new Button
        {
            Content = "_Print...",
            Padding = new Thickness(12, 4, 12, 4)
        };
        printButton.Click += (_, _) => ShowNativePrintDialog(document);
        toolbar.Items.Add(previousButton);
        toolbar.Items.Add(nextButton);
        toolbar.Items.Add(new Separator());
        var totalPages = Math.Max(1, document.Pages.Count);
        var pageNumberBox = new TextBox
        {
            Width = 44,
            Text = "1",
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var pageStatusText = new TextBlock
        {
            Text = $"Page 1 of {totalPages}",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        toolbar.Items.Add(new Label
        {
            Content = "_Page:",
            Target = pageNumberBox,
            VerticalAlignment = VerticalAlignment.Center
        });
        pageNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            NavigateToPage(viewer, pageNumberBox, pageStatusText, totalPages);
            e.Handled = true;
        };
        pageNumberBox.CommandBindings.Add(new CommandBinding(
            NavigationCommands.GoToPage,
            (_, e) =>
            {
                NavigateToPage(viewer, pageNumberBox, pageStatusText, totalPages);
                e.Handled = true;
            }));
        pageNumberBox.InputBindings.Add(new KeyBinding(NavigationCommands.GoToPage, new KeyGesture(Key.Enter)));
        toolbar.Items.Add(pageNumberBox);
        toolbar.Items.Add(pageStatusText);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new Label
        {
            Content = "_Zoom:",
            VerticalAlignment = VerticalAlignment.Center
        });
        var zoomBox = new ComboBox
        {
            Width = 82,
            SelectedIndex = 2
        };
        foreach (var zoom in new[] { "50%", "75%", "100%", "125%", "Page Width" })
            zoomBox.Items.Add(zoom);
        zoomBox.SelectionChanged += (_, _) =>
        {
            if (zoomBox.SelectedItem is not string value)
                return;

            if (value == "Page Width")
                viewer.FitToWidth();
            else if (double.TryParse(value.TrimEnd('%'), out var zoom))
                viewer.Zoom = zoom;
        };
        toolbar.Items.Add(zoomBox);
        toolbar.Items.Add(new Separator());
        var marginsButton = new Button
        {
            Content = "_Margins",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Review worksheet margin settings before printing."
        };
        marginsButton.Click += (_, _) => showMargins?.Invoke();
        toolbar.Items.Add(marginsButton);
        var pageSetupButton = new Button
        {
            Content = "Page _Setup...",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Use Page Layout settings to change paper, orientation, margins, and scaling."
        };
        pageSetupButton.Click += (_, _) => showPageSetup?.Invoke();
        toolbar.Items.Add(pageSetupButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(printButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new TextBlock
        {
            Text = settings.Summary,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620
        });
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(viewer);
        Content = root;
    }

    public static string CreateTitle(string workbookName) =>
        $"Print Preview - {workbookName.Trim()}";

    private static void ShowNativePrintDialog(FixedDocument document)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(document.DocumentPaginator, "Freexcel worksheet");
    }

    private static void NavigateToPage(DocumentViewer viewer, TextBox pageNumberBox, TextBlock pageStatusText, int totalPages)
    {
        if (!int.TryParse(pageNumberBox.Text.Trim(), out var pageNumber))
            return;

        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        viewer.GoToPage(pageNumber);
        pageNumberBox.Text = pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        pageStatusText.Text = $"Page {pageNumber} of {totalPages}";
    }
}
