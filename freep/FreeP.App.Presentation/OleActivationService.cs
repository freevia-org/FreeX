using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public interface IOleActivationTempFile : IDisposable
{
    string Path { get; }
    byte[] ReadAllBytes();
}

public interface IOleActivationTempFileStore
{
    IOleActivationTempFile Materialize(OleActivationPlan plan);
}

public interface IOleActivationProcess : IDisposable
{
    Task ExitTask { get; }
    bool SupportsEditBack { get; }
}

public interface IOleActivationLauncher
{
    IOleActivationProcess Launch(string path);
}

/// <summary>
/// Materializes a packaged OLE payload and opens it through the host OS file service.
/// Native in-place OLE activation remains a Windows-only renderer concern; this workflow
/// deliberately handles the portable packaged-file boundary shared by WPF and Avalonia.
/// </summary>
public static class OleActivationService
{
    /// <summary>Maximum lifetime for a detached OS handoff payload.</summary>
    public static readonly TimeSpan DetachedPayloadRetention = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentDictionary<int, OleActivationSession> ActiveSessions = new();
    private static int _nextSessionId;

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

    public static bool TryActivate(OleObjectInfo? oleObject) =>
        TryActivate(OleActivationPlanner.TryBuild(oleObject),
            bytes => { if (oleObject is not null) oleObject.EmbeddedBytes = bytes; });

    public static bool TryActivate(
        InlineOleObjectInfo? inlineObject,
        Action<byte[]>? onPayloadUpdated = null) =>
        TryActivate(OleActivationPlanner.TryBuild(inlineObject), bytes =>
        {
            if (inlineObject is null) return;
            inlineObject.EmbeddedBytes = bytes;
            onPayloadUpdated?.Invoke(bytes);
        });

    internal static bool TryActivate(
        OleActivationPlan? plan,
        Action<byte[]> updatePayload,
        IOleActivationTempFileStore tempStore,
        IOleActivationLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(updatePayload);
        ArgumentNullException.ThrowIfNull(tempStore);
        ArgumentNullException.ThrowIfNull(launcher);
        if (plan is null || plan.Payload.Length == 0 || IsBlockedExtension(plan.Extension))
            return false;

        IOleActivationTempFile? tempFile = null;
        try
        {
            tempFile = tempStore.Materialize(plan);
            var process = launcher.Launch(tempFile.Path);
            var session = new OleActivationSession(process, tempFile, plan.Payload, updatePayload);
            ActiveSessions[session.Id] = session;
            _ = CompleteSessionAsync(session);
            return true;
        }
        catch
        {
            tempFile?.Dispose();
            return false;
        }
    }

    private static bool TryActivate(OleActivationPlan? plan, Action<byte[]> updatePayload) =>
        TryActivate(plan, updatePayload, new DefaultTempFileStore(), new DefaultLauncher());

    internal static bool TryCommitEditedPayload(OleObjectInfo oleObject, string path, IReadOnlyList<byte> originalBytes) =>
        TryCommitEditedPayload(path, originalBytes, bytes => oleObject.EmbeddedBytes = bytes);

    internal static bool TryCommitEditedPayload(InlineOleObjectInfo inlineObject, string path, IReadOnlyList<byte> originalBytes) =>
        TryCommitEditedPayload(path, originalBytes, bytes => inlineObject.EmbeddedBytes = bytes);

    private static bool TryCommitEditedPayload(string path, IReadOnlyList<byte> originalBytes, Action<byte[]> updatePayload)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var updatedBytes = File.ReadAllBytes(path);
            if (updatedBytes.Length == 0 || updatedBytes.SequenceEqual(originalBytes)) return false;
            updatePayload(updatedBytes);
            return true;
        }
        catch { return false; }
    }

    private static async Task CompleteSessionAsync(OleActivationSession session)
    {
        try
        {
            await session.Process.ExitTask.ConfigureAwait(false);
            if (session.Process.SupportsEditBack)
                TryCommitEditedPayload(session.TempFile.Path, session.OriginalBytes, session.UpdatePayload);
        }
        catch { }
        finally
        {
            ActiveSessions.TryRemove(session.Id, out _);
            session.Dispose();
        }
    }

    public static string ResolveExtension(OleObjectInfo oleObject)
    {
        var extension = NormalizeExtension(oleObject.EmbeddedExtension);
        return extension != "bin" ? extension :
            ContentTypeExtensions.TryGetValue(oleObject.EmbeddedContentType, out var value) ? value : "bin";
    }

    public static string ResolveExtension(InlineOleObjectInfo inlineObject)
    {
        var extension = NormalizeExtension(FilePathPolicy.GetExtensionOrEmpty(inlineObject.FileName));
        if (extension != "bin")
            return extension;

        return inlineObject.ClassName?.Trim().ToLowerInvariant() switch
        {
            "excel.sheet.12" => "xlsx", "excel.sheetmacroenabled.12" => "xlsm", "excel.sheet.8" => "xls",
            "word.document.12" => "docx", "word.document.8" => "doc",
            "powerpoint.show.12" => "pptx", "powerpoint.show.8" => "ppt", _ => "bin"
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        var candidate = (extension ?? string.Empty).Trim().TrimStart('.');
        return candidate.Length > 0 && candidate.All(char.IsLetterOrDigit)
            ? candidate.ToLowerInvariant() : "bin";
    }

    private static bool IsBlockedExtension(string extension) => extension.ToLowerInvariant() switch
    {
        "exe" or "com" or "msi" or "dll" or "so" or "dylib" or
        "bat" or "cmd" or "ps1" or "sh" or "bash" or "zsh" or
        "js" or "jse" or "vbs" or "vbe" or "wsf" or "wsh" => true,
        _ => false,
    };

    private sealed class OleActivationSession : IDisposable
    {
        public OleActivationSession(IOleActivationProcess process, IOleActivationTempFile tempFile, byte[] originalBytes, Action<byte[]> updatePayload)
        { Process = process; TempFile = tempFile; OriginalBytes = originalBytes; UpdatePayload = updatePayload; Id = Interlocked.Increment(ref _nextSessionId); }
        public int Id { get; }
        public IOleActivationProcess Process { get; }
        public IOleActivationTempFile TempFile { get; }
        public byte[] OriginalBytes { get; }
        public Action<byte[]> UpdatePayload { get; }
        public void Dispose() { Process.Dispose(); TempFile.Dispose(); }
    }

    private sealed class DefaultTempFileStore : IOleActivationTempFileStore
    {
        public IOleActivationTempFile Materialize(OleActivationPlan plan)
        {
            var root = Path.Combine(Path.GetTempPath(), "FreeP", "Ole");
            CleanupStale(root);
            var directory = TemporaryDirectoryLease.Create(string.Empty, root);
            try
            {
                var path = Path.Combine(directory.Path, plan.FileName);
                File.WriteAllBytes(path, plan.Payload);
                return new DefaultTempFile(path, directory);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        private static void CleanupStale(string root)
        {
            if (!Directory.Exists(root))
                return;

            var activeDirectories = ActiveSessions.Values
                .Select(session => Path.GetDirectoryName(session.TempFile.Path))
                .Where(path => path is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (activeDirectories.Contains(directory)
                    || DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < DetachedPayloadRetention)
                    continue;
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }

    private sealed class DefaultTempFile : IOleActivationTempFile
    {
        private readonly TemporaryDirectoryLease _directory;
        public DefaultTempFile(string path, TemporaryDirectoryLease directory) { Path = path; _directory = directory; }
        public string Path { get; }
        public byte[] ReadAllBytes() => File.ReadAllBytes(Path);
        public void Dispose() => _directory.Dispose();
    }

    private sealed class DefaultLauncher : IOleActivationLauncher
    {
        public IOleActivationProcess Launch(string path)
        {
            var info = DesktopPathLauncher.CreateOpenFileProcessStartInfo(
                path,
                waitForApplicationExit: RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
            var process = Process.Start(info) ?? throw new InvalidOperationException("The host OS file service did not start.");
            return new DefaultProcess(
                process,
                supportsEditBack: !RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                exitTask: RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? Task.Delay(DetachedPayloadRetention)
                    : process.WaitForExitAsync());
        }
    }

    private sealed class DefaultProcess : IOleActivationProcess
    {
        private readonly Process _process;
        public DefaultProcess(Process process, bool supportsEditBack, Task exitTask)
        {
            _process = process;
            SupportsEditBack = supportsEditBack;
            ExitTask = exitTask;
        }
        public Task ExitTask { get; }
        public bool SupportsEditBack { get; }
        public void Dispose() => _process.Dispose();
    }
}
