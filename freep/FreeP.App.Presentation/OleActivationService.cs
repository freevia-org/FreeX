using System.Collections.Concurrent;
using System.Diagnostics;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Materializes an embedded OLE payload so the operating system can activate it
/// in its registered host application.
/// </summary>
public static class OleActivationService
{
    private static readonly ConcurrentDictionary<int, OleActivationSession> ActiveSessions = new();

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
        if (oleObject is null)
            return false;

        return BeginActivation(
            oleObject.EmbeddedBytes,
            ResolveExtension(oleObject),
            updatedBytes => oleObject.EmbeddedBytes = updatedBytes) is not null;
    }

    /// <summary>
    /// Activates an embedded object carried by a rich-text replacement run. The
    /// same temporary-file lifecycle as slide OLE objects is used, and edits made
    /// by the external application are written back to the inline payload.
    /// </summary>
    public static bool TryActivate(
        InlineOleObjectInfo? inlineObject,
        Action<byte[]>? onPayloadUpdated = null)
    {
        if (inlineObject is null)
            return false;

        return BeginActivation(
            inlineObject.EmbeddedBytes,
            ResolveExtension(inlineObject),
            updatedBytes =>
            {
                inlineObject.EmbeddedBytes = updatedBytes;
                onPayloadUpdated?.Invoke(updatedBytes);
            }) is not null;
    }

    private static OleActivationSession? BeginActivation(
        IReadOnlyList<byte> embeddedBytes,
        string extension,
        Action<byte[]> updatePayload)
    {
        if (embeddedBytes.Count == 0)
            return null;

        string directory = Path.Combine(Path.GetTempPath(), "FreeP", "Ole");
        string path = Path.Combine(directory, $"embedded-{Guid.NewGuid():N}.{extension}");
        byte[] originalBytes = embeddedBytes.ToArray();

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, originalBytes);
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                TryDelete(path);
                return null;
            }

            var session = new OleActivationSession(
                process,
                path,
                originalBytes,
                updatePayload,
                CompleteSession);
            ActiveSessions[process.Id] = session;
            session.StartWatching();
            return session;
        }
        catch (Exception)
        {
            TryDelete(path);
            return null;
        }
    }

    internal static bool TryCommitEditedPayload(
        OleObjectInfo oleObject,
        string path,
        IReadOnlyList<byte> originalBytes)
    {
        return TryCommitEditedPayload(
            path,
            originalBytes,
            updatedBytes => oleObject.EmbeddedBytes = updatedBytes);
    }

    internal static bool TryCommitEditedPayload(
        InlineOleObjectInfo inlineObject,
        string path,
        IReadOnlyList<byte> originalBytes)
    {
        return TryCommitEditedPayload(
            path,
            originalBytes,
            updatedBytes => inlineObject.EmbeddedBytes = updatedBytes);
    }

    private static bool TryCommitEditedPayload(
        string path,
        IReadOnlyList<byte> originalBytes,
        Action<byte[]> updatePayload)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            byte[] updatedBytes = File.ReadAllBytes(path);
            if (updatedBytes.Length == 0
                || updatedBytes.SequenceEqual(originalBytes))
                return false;

            updatePayload(updatedBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CompleteSession(OleActivationSession session)
    {
        try
        {
            TryCommitEditedPayload(
                session.Path,
                session.OriginalBytes,
                session.UpdatePayload);
        }
        finally
        {
            ActiveSessions.TryRemove(session.ProcessId, out _);
            TryDelete(session.Path);
            session.DisposeProcess();
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private sealed class OleActivationSession
    {
        private readonly Process _process;
        private readonly Action<OleActivationSession> _complete;
        private int _completed;

        public string Path { get; }
        public byte[] OriginalBytes { get; }
        public Action<byte[]> UpdatePayload { get; }
        public int ProcessId => _process.Id;

        public OleActivationSession(
            Process process,
            string path,
            byte[] originalBytes,
            Action<byte[]> updatePayload,
            Action<OleActivationSession> complete)
        {
            _process = process;
            Path = path;
            OriginalBytes = originalBytes;
            UpdatePayload = updatePayload;
            _complete = complete;
        }

        public void StartWatching()
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;
            if (_process.HasExited)
                Complete();
        }

        private void OnExited(object? sender, EventArgs e) => Complete();

        private void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
                _complete(this);
        }

        public void DisposeProcess()
        {
            _process.Exited -= OnExited;
            _process.Dispose();
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

    public static string ResolveExtension(InlineOleObjectInfo inlineObject)
    {
        string extension = NormalizeExtension(Path.GetExtension(inlineObject.FileName));
        if (extension != "bin")
            return extension;

        return inlineObject.ClassName?.Trim().ToLowerInvariant() switch
        {
            "excel.sheet.12" => "xlsx",
            "excel.sheetmacroenabled.12" => "xlsm",
            "excel.sheet.8" => "xls",
            "word.document.12" => "docx",
            "word.document.8" => "doc",
            "powerpoint.show.12" => "pptx",
            "powerpoint.show.8" => "ppt",
            _ => "bin",
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        string candidate = (extension ?? string.Empty).Trim().TrimStart('.');
        return candidate.Length > 0 && candidate.All(char.IsLetterOrDigit)
            ? candidate.ToLowerInvariant()
            : "bin";
    }
}
