using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Freexcel.App.Host;

public sealed class PrintPreviewDialog : Window
{
    public PrintPreviewDialog(string workbookName, FixedDocument document)
        : this(workbookName, document, new PrintSettingsPlan(["Print active sheet"]))
    {
    }

    public PrintPreviewDialog(string workbookName, FixedDocument document, PrintSettingsPlan settings)
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
        var printButton = new Button
        {
            Content = "_Print...",
            Padding = new Thickness(12, 4, 12, 4)
        };
        printButton.Click += (_, _) => ShowNativePrintDialog(document);
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
        root.Children.Add(new DocumentViewer { Document = document });
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
}
