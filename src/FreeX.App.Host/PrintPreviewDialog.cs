using System.Windows;
using System.Windows.Documents;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class PrintPreviewDialog : Window
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
        Action? showPageSetup = null,
        Func<(FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreview = null,
        Func<PrintPreviewSettings, (FixedDocument Document, PrintSettingsPlan Settings)>? refreshPreviewWithSettings = null,
        SheetId sheetId = default,
        Sheet? sheet = null,
        Action<IWorkbookCommand>? executeCommand = null)
    {
        InitializePrintPreviewLayout(
            workbookName,
            document,
            settings,
            showMargins,
            showPageSetup,
            refreshPreview,
            refreshPreviewWithSettings,
            sheetId,
            sheet,
            executeCommand);
    }
}
