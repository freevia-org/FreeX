using System.Diagnostics;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Materializes an embedded OLE payload so the operating system can activate it
/// in its registered host application.
/// </summary>
public static class OleActivationService
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = "xlsx",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = "pptx",
            ["application/vnd.ms-excel"] = "xls",
            ["application/msword"] = "doc",
            ["application/vnd.ms-powerpoint"] = "ppt",
        };

    /// <summary>
    /// Writes the embedded payload to a unique temporary file and asks the OS to
    /// open it. Returns false when the object has no usable payload or no host can
    /// be started.
    /// </summary>
    public static bool TryActivate(OleObjectInfo? oleObject)
    {
        if (oleObject is null || oleObject.EmbeddedBytes.Length == 0)
            return false;

        string extension = ResolveExtension(oleObject);
        string directory = Path.Combine(Path.GetTempPath(), "FreeP", "Ole");
        string path = Path.Combine(directory, $"embedded-{Guid.NewGuid():N}.{extension}");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, oleObject.EmbeddedBytes);
            return Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            }) is not null;
        }
        catch (Exception)
        {
            try { File.Delete(path); } catch { }
            return false;
        }
    }

    public static string ResolveExtension(OleObjectInfo oleObject)
    {
        string extension = NormalizeExtension(oleObject.EmbeddedExtension);
        if (extension != "bin")
            return extension;

        if (ContentTypeExtensions.TryGetValue(oleObject.EmbeddedContentType, out var contentExtension))
            return contentExtension;

        return "bin";
    }

    private static string NormalizeExtension(string? extension)
    {
        string candidate = (extension ?? string.Empty).Trim().TrimStart('.');
        return candidate.Length > 0 && candidate.All(char.IsLetterOrDigit)
            ? candidate.ToLowerInvariant()
            : "bin";
    }
}
