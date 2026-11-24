using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;

namespace FreeW.App.Avalonia.Editing;

/// <summary>Enables Cocoa text context only for the verified, already loaded FreeW-local native patch.</summary>
internal static class FreeWNativeTextContextCapability
{
    internal const string UpstreamCommit = "a8dd6417fd8918570edefdbecd92d16ac7620069";
    internal const string PatchSha256 = "2f45f5945e3d2fbe64bdc02c6fd8f0c84146caa8a18f62c2a04c96ed1a7ab8c4";
    internal const string IdlSha256 = "e0c7b0e2b1155b38cb04306a91b992d7c89ccd78ef0905007bc1fd198fd76224";
    internal const string LibraryFileName = "libAvaloniaNative.dylib";
    internal const string ManifestFileName = "FreeW.NativeTextContext.json";
    internal const string AssemblyHashKey = "FreeW.NativeTextContext.Sha256";
    internal const string MarkerSymbol = "FreeW_AvaloniaNative_TextContext_V1";
    internal const string MarkerAbi = "cdecl:int32(void)";
    internal const string ManagedInformationalVersion = "12.0.4+" + UpstreamCommit;
    private static readonly Lazy<bool> CurrentProcessBridge = new(VerifyCurrentProcessBridge);

    internal static bool AllowsClient(TopLevel? topLevel) => AllowsBackend(
        OperatingSystem.IsMacOS(), topLevel?.TryGetPlatformHandle()?.HandleDescriptor,
        () => CurrentProcessBridge.Value);

    internal static bool AllowsBackend(bool isMacOS, string? handleDescriptor, Func<bool> verifyPackagedBridge) =>
        !isMacOS || handleDescriptor is not ("NSWindow" or "NSView") || verifyPackagedBridge();

    private static bool VerifyCurrentProcessBridge()
    {
        // The native backend must already exist before this Cocoa window can query a client.
        // Inspect that loaded assembly; never load a candidate assembly to manufacture a match.
        var managedBridges = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "Avalonia.Native").ToArray();
        if (managedBridges.Length != 1 || !MatchesManagedVersion(managedBridges[0]
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion))
            return false;
        var hashes = typeof(FreeWNativeTextContextCapability).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == AssemblyHashKey).Select(attribute => attribute.Value).ToArray();
        return hashes.Length == 1 && VerifyPackage(AppContext.BaseDirectory, hashes[0], VerifyAlreadyLoadedMarker);
    }

    internal static bool MatchesManagedVersion(string? version) =>
        string.Equals(version, ManagedInformationalVersion, StringComparison.Ordinal);

    // Kept separate from the loader so malformed/missing package evidence can be tested without
    // loading a native library. The signed library hash is also pinned in the managed assembly.
    internal static bool VerifyPackage(string directory, string? assemblyHash, Func<string, bool> verifyLoadedMarker)
    {
        if (!IsSha256(assemblyHash))
            return false;
        try
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            var libraryPath = Path.GetFullPath(Path.Combine(directory, LibraryFileName));
            var manifestFile = new FileInfo(manifestPath);
            var libraryFile = new FileInfo(libraryPath);
            if (!manifestFile.Exists || manifestFile.Length > 16 * 1024 || manifestFile.LinkTarget is not null
                || !libraryFile.Exists || libraryFile.Length <= 0 || libraryFile.Length > 64 * 1024 * 1024
                || libraryFile.LinkTarget is not null)
                return false;
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1
                || root.GetProperty("contractVersion").GetInt32() != 1
                || root.GetProperty("upstreamCommit").GetString() != UpstreamCommit
                || root.GetProperty("patchSha256").GetString() != PatchSha256
                || root.GetProperty("idlSha256").GetString() != IdlSha256
                || root.GetProperty("generatorVersion").GetString() != "0.11.4"
                || root.GetProperty("libraryFile").GetString() != LibraryFileName
                || root.GetProperty("markerSymbol").GetString() != MarkerSymbol
                || root.GetProperty("markerAbi").GetString() != MarkerAbi
                || !string.Equals(root.GetProperty("librarySha256").GetString(), assemblyHash, StringComparison.OrdinalIgnoreCase))
                return false;
            using var library = File.OpenRead(libraryPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(library));
            return string.Equals(actualHash, assemblyHash, StringComparison.OrdinalIgnoreCase)
                && verifyLoadedMarker(libraryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool VerifyAlreadyLoadedMarker(string path)
    {
        if (!OperatingSystem.IsMacOS())
            return false;
        // Darwin dlfcn.h: NOLOAD returns null unless this exact image is already loaded. LOCAL
        // neither publishes nor replaces process-wide symbols. Balance only our acquired reference.
        const int rtldLazy = 0x1, rtldLocal = 0x4, rtldNoLoad = 0x10;
        var handle = Dlopen(path, rtldLazy | rtldLocal | rtldNoLoad);
        if (handle == IntPtr.Zero)
            return false;
        try
        {
            if (!NativeLibrary.TryGetExport(handle, MarkerSymbol, out var marker)
                || Dladdr(marker, out var info) == 0 || Marshal.PtrToStringUTF8(info.FileName) is not { } loadedPath
                || !string.Equals(Path.GetFullPath(loadedPath), path, StringComparison.Ordinal))
                return false;
            return Marshal.GetDelegateForFunctionPointer<ContractVersion>(marker)() == 1;
        }
        finally { Dlclose(handle); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ContractVersion();

    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo
    {
        public IntPtr FileName;
        public IntPtr BaseAddress;
        public IntPtr SymbolName;
        public IntPtr SymbolAddress;
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr Dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dladdr")]
    private static extern int Dladdr(IntPtr address, out DlInfo info);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlclose")]
    private static extern int Dlclose(IntPtr handle);
}
