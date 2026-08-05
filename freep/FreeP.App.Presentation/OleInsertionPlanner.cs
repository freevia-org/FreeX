using System.Xml.Linq;
using Free.Shared.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared authoring contract for inserting an embedded OLE package from a file.</summary>
public static class OleInsertionPlanner
{
    public const string InsertEmbeddedObjectCommandId = "freep.object.insert-embedded";
    public const string PickerTitle = "Insert Embedded Object";

    private static readonly XNamespace Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";

    public static OleObjectInfo CreatePayload(
        byte[] bytes,
        string fileName,
        string? sourceProgId = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("The embedded object file cannot be empty.", nameof(bytes));

        var extension = NormalizeExtension(FilePathPolicy.GetExtensionOrEmpty(fileName));
        var contentType = ContentTypeFor(extension);
        var progId = string.IsNullOrWhiteSpace(sourceProgId)
            ? ProgIdFor(extension)
            : sourceProgId.Trim();
        var oleObj = new XElement(
            Presentation + "oleObj",
            new XAttribute("type", "Embed"),
            new XAttribute("progId", progId));

        return new OleObjectInfo
        {
            EmbeddedBytes = bytes.ToArray(),
            EmbeddedExtension = extension,
            EmbeddedContentType = contentType,
            ProgId = progId,
            OleObjXml = oleObj.ToString(SaveOptions.DisableFormatting),
        };
    }

    public static string NormalizeExtension(string? extension)
    {
        var normalized = (extension ?? string.Empty).Trim().TrimStart('.');
        return normalized.Length > 0 && normalized.All(char.IsLetterOrDigit)
            ? normalized.ToLowerInvariant()
            : "bin";
    }

    public static string ContentTypeFor(string extension) => NormalizeExtension(extension) switch
    {
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        "xls" => "application/vnd.ms-excel",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "doc" => "application/msword",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "ppt" => "application/vnd.ms-powerpoint",
        _ => "application/octet-stream",
    };

    public static string ProgIdFor(string extension) => NormalizeExtension(extension) switch
    {
        "xlsx" => "Excel.Sheet.12",
        "xlsm" => "Excel.SheetMacroEnabled.12",
        "xls" => "Excel.Sheet.8",
        "docx" => "Word.Document.12",
        "doc" => "Word.Document.8",
        "pptx" => "PowerPoint.Show.12",
        "ppt" => "PowerPoint.Show.8",
        _ => "Package",
    };
}
