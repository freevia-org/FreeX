using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Opc;
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

    /// <summary>
    /// Activates a slide-level embedded object. <paramref name="onPayloadUpdated"/> mirrors the
    /// <see cref="TryActivate(InlineOleObjectInfo?, Action{byte[]}?)"/> overload's hook: it fires
    /// only when the launched app's edited bytes are actually committed back onto the model
    /// (<see cref="TryCommitEditedPayload(string, IReadOnlyList{byte}, Action{byte[]})"/>), so a
    /// caller can hang dirty-tracking / undo notification off a real content change instead of
    /// having to poll <see cref="OleObjectInfo.EmbeddedBytes"/> for changes itself.
    /// </summary>
    public static bool TryActivate(OleObjectInfo? oleObject, Action<byte[]>? onPayloadUpdated = null) =>
        TryActivate(OleActivationPlanner.TryBuild(oleObject), BuildOleObjectUpdateCallback(oleObject, onPayloadUpdated));

    /// <summary>
    /// Builds the payload-commit callback for a slide-level embedded object: writes the edited
    /// bytes onto the model and then reports the commit via <paramref name="onPayloadUpdated"/>.
    /// Extracted so tests can verify the notification fires without driving a real OS process
    /// launch through the public <see cref="TryActivate(OleObjectInfo?, Action{byte[]}?)"/> entry
    /// point (that overload always uses the real launcher/temp-file store).
    /// </summary>
    public static Action<byte[]> BuildOleObjectUpdateCallback(OleObjectInfo? oleObject, Action<byte[]>? onPayloadUpdated = null) =>
        bytes =>
        {
            if (oleObject is null) return;
            oleObject.EmbeddedBytes = bytes;
            onPayloadUpdated?.Invoke(bytes);
        };

    public static bool TryActivate(
        InlineOleObjectInfo? inlineObject,
        Action<byte[]>? onPayloadUpdated = null) =>
        TryActivate(OleActivationPlanner.TryBuild(inlineObject), BuildInlineOleObjectUpdateCallback(inlineObject, onPayloadUpdated));

    /// <summary>
    /// Builds the payload-commit callback for an inline embedded object. Mirrors
    /// <see cref="BuildOleObjectUpdateCallback"/> and is shared by external activation and the
    /// native renderer hosts so payload assignment and notification ordering cannot drift.
    /// </summary>
    public static Action<byte[]> BuildInlineOleObjectUpdateCallback(InlineOleObjectInfo? inlineObject, Action<byte[]>? onPayloadUpdated = null) =>
        bytes =>
        {
            if (inlineObject is null) return;
            inlineObject.EmbeddedBytes = bytes;
            onPayloadUpdated?.Invoke(bytes);
        };

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

    /// <summary>
    /// Test-only override for the OS boundary of external activation: the temp-file store and the
    /// process launcher. End-to-end tests use it to drive the real editor/host activation path --
    /// including the payload-commit notification callers hang dirty-tracking off -- without
    /// launching a real application from a test process. Always null in shipping code.
    /// </summary>
    internal static Func<(IOleActivationTempFileStore Store, IOleActivationLauncher Launcher)>?
        ExternalActivationOverrideForTests
    { get; set; }

    private static bool TryActivate(OleActivationPlan? plan, Action<byte[]> updatePayload)
    {
        if (ExternalActivationOverrideForTests is { } createOverride)
        {
            var (store, launcher) = createOverride();
            return TryActivate(plan, updatePayload, store, launcher);
        }

        return TryActivate(plan, updatePayload, new DefaultTempFileStore(), new DefaultLauncher());
    }

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
        var extension = FilePathPolicy.NormalizeSafeExtension(oleObject.EmbeddedExtension);
        return extension != "bin" ? extension :
            OpcMediaTypes.TryGetOfficeEmbeddedObjectExtension(oleObject.EmbeddedContentType, out var value) ? value : "bin";
    }

    public static string ResolveExtension(InlineOleObjectInfo inlineObject)
    {
        var extension = FilePathPolicy.NormalizeSafeExtension(FilePathPolicy.GetExtensionOrEmpty(inlineObject.FileName));
        if (extension != "bin")
            return extension;

        return inlineObject.ClassName?.Trim().ToLowerInvariant() switch
        {
            "excel.sheet.12" => "xlsx", "excel.sheetmacroenabled.12" => "xlsm", "excel.sheet.8" => "xls",
            "word.document.12" => "docx", "word.document.8" => "doc",
            "powerpoint.show.12" => "pptx", "powerpoint.show.8" => "ppt", _ => "bin"
        };
    }

    /// <summary>
    /// The extensions an embedded OLE payload may be activated with. Activation hands the file to
    /// the OS shell (DesktopPathLauncher.CreateOpenFileProcessStartInfo), so whatever is registered
    /// for the extension runs.
    ///
    /// r182: this was a DENYLIST of executable and script extensions, and the extension it filtered
    /// comes verbatim from the OPC relationship target inside the .pptx -- i.e. from the document
    /// author. A denylist cannot be complete on Windows: .hta, .scr, .lnk, .cpl, .msc, .jar, .reg,
    /// .application, .pif, .ws, .settingcontent-ms and more all execute through the shell and none
    /// were listed, so opening a crafted presentation and activating its embedded object ran
    /// attacker-chosen code. Inverted to an allowlist of the document and media types an embedded
    /// object legitimately carries: anything unrecognised -- including the "bin" fallback
    /// ResolveExtension produces for an unknown or path-traversing target -- is simply not opened.
    /// </summary>
    private static readonly HashSet<string> ActivatableExtensions = new(StringComparer.Ordinal)
    {
        // Office
        "doc", "docx", "docm", "dot", "dotx", "dotm", "rtf",
        "xls", "xlsx", "xlsm", "xlsb", "xlt", "xltx", "xltm", "csv",
        "ppt", "pptx", "pptm", "pot", "potx", "potm", "pps", "ppsx",
        // OpenDocument
        "odt", "ods", "odp", "odg",
        // This suite own formats
        "fxl", "fxw", "fxp",
        // Documents and plain data
        "pdf", "txt", "xml",
        // The explicit unknown-payload fallback ResolveExtension produces for a target it cannot
        // identify, including one that tried to traverse out of the package. Kept activatable
        // because it is inert -- Windows registers no handler for .bin, so the shell either does
        // nothing or asks the user to choose an app -- and because refusing it would break
        // activating an ordinary embedded object whose type the package did not declare.
        "bin",
        // Images an embedded object commonly carries
        "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "emf", "wmf", "svg",
    };

    private static bool IsBlockedExtension(string extension) =>
        !ActivatableExtensions.Contains(extension.ToLowerInvariant());

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
