namespace FreeW.App.Presentation.Dialogs;

public static class FreeWFileTextResources
{
    public const string PdfFileTypeName = "PDF document";
    public const string PictureFileTypeName = "Pictures";
    public const string TextFromFileTypeName = "Documents";
    public const string ExportPdfPickerTitle = "Export to PDF";
    public const string PdfExportCommand = "PDF export";

    public static string FormatPdfExported(int pageCount, object backend, string fileName)
    {
        var pages = pageCount == 1 ? "page" : "pages";
        return $"Exported PDF ({pageCount} {pages}, {backend}): {fileName}";
    }
}
