using Free.Shared.AppServices;
using FreeW.App.Localization;

namespace FreeW.App.Presentation.Dialogs;

public static class FreeWFileTextResources
{
    public static string PdfFileTypeName => Loc.Get("File_PdfFileTypeName");
    public static string PictureFileTypeName => Loc.Get("File_PictureFileTypeName");
    public static string TextFromFileTypeName => Loc.Get("File_TextFromFileTypeName");
    public static string ExportPdfPickerTitle => Loc.Get("File_ExportPdfPickerTitle");
    public static string PdfExportCommand => Loc.Get("File_PdfExportCommand");
    public static string XpsFileTypeName => Loc.Get("File_XpsFileTypeName");
    public static string ExportXpsPickerTitle => Loc.Get("File_ExportXpsPickerTitle");
    public static string XpsExportCommand => Loc.Get("File_XpsExportCommand");
    public static string InsertTextCommand => Loc.Get("File_InsertTextCommand");
    public static string NewWindowCommand => Loc.Get("File_NewWindowCommand");

    public static SisterAppFileTextSpec Document => new(
        OpenPickerTitle: Loc.Get("File_OpenDocumentPickerTitle"),
        SavePickerTitle: Loc.Get("File_SaveDocumentPickerTitle"),
        FallbackDisplayName: Loc.Get("File_DocumentFallbackDisplayName"),
        NewAction: Loc.Get("File_NewDocumentAction"),
        OpenAction: Loc.Get("File_OpenDocumentAction"),
        OpenCommand: Loc.Get("File_OpenCommand"),
        SaveCommand: Loc.Get("File_SaveCommand"),
        InsertPictureCommand: Loc.Get("File_InsertPictureCommand"),
        InsertPicturePickerTitle: Loc.Get("File_InsertPicturePickerTitle"),
        Status: StatusText);

    private static SisterAppFileStatusTextSpec StatusText =>
        SisterAppFileTextPlanner.CreateStatusText(Loc.Get);

    public static string FormatPdfExported(int pageCount, object backend, string fileName)
    {
        var pages = pageCount == 1 ? Loc.Get("File_PageSingular") : Loc.Get("File_PagePlural");
        return Loc.Format("File_PdfExportedStatusFormat", pageCount, pages, backend, fileName);
    }

    public static string FormatXpsExported(string path) =>
        Loc.Format("File_XpsExportedStatusFormat", path);
}
