using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Freexcel.App.Host;

public sealed class PrintPreviewDialog : Window
{
    public PrintPreviewDialog(string workbookName, FixedDocument document)
    {
        Title = CreateTitle(workbookName);
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new DocumentViewer { Document = document };
    }

    public static string CreateTitle(string workbookName) =>
        $"Print Preview - {workbookName.Trim()}";
}
