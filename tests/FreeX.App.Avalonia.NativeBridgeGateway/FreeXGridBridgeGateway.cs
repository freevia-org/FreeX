using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Native.Interop;
using MicroCom.Runtime;

namespace Avalonia.Native.UnitTests;

/// <summary>Test-only public gateway to the packaged Native friend implementation.</summary>
public sealed class FreeXGridBridgeGateway : IDisposable
{
    private readonly AutomationPeer _grid;
    private readonly HashSet<AutomationPeer> _seen = [];
    private readonly Probe _probe;
    private static int _manifestNumber;
    private static readonly HashSet<AutomationPeer> Retired = [];
    private static readonly List<AvnAutomationPeer> RetiredWrappers = [];

    public FreeXGridBridgeGateway(AutomationPeer grid)
    {
        _grid = grid;
        var backend = RequiredPath("FREEX_GRID_NATIVE_LIBRARY");
        var helper = RequiredPath("FREEX_GRID_NATIVE_HELPER");
        var hashesPath = RequiredPath("FREEX_GRID_EXPECTED_HASHES");
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashesPath))!;
        var assemblies = new[] { typeof(AutomationPeer).Assembly, typeof(AvnAutomationPeer).Assembly, typeof(AvaloniaObject).Assembly };
        var loaded = assemblies.Select(assembly => new
        {
            name = assembly.GetName().Name!, location = assembly.Location,
            sha256 = Hash(assembly.Location),
        }).ToArray();
        foreach (var assembly in loaded)
            Require(expected[assembly.name] == assembly.sha256, "Loaded managed package hash mismatch: " + assembly.name);
        Require(expected["libAvaloniaNative.dylib"] == Hash(backend), "Loaded native package hash mismatch");
        NativeLibrary.Load(backend); // Keep backend loaded for peer finalizers until process exit.
        var helperHandle = NativeLibrary.Load(helper);
        _probe = Marshal.GetDelegateForFunctionPointer<Probe>(NativeLibrary.GetExport(helperHandle, "FreeXGridBridgeProbe"));
        var imagePath = Marshal.GetDelegateForFunctionPointer<NativeImagePath>(NativeLibrary.GetExport(helperHandle, "FreeXGridBridgeImagePath"));
        var selectorImage = Marshal.PtrToStringUTF8(imagePath()) ?? throw new InvalidOperationException("Native grid selector has no owning image");
        Require(Path.GetFullPath(selectorImage) == Path.GetFullPath(backend), "Native selector belongs to an unexpected loaded image");
        var directory = Environment.GetEnvironmentVariable("FREEX_GRID_NATIVE_MANIFEST_DIRECTORY")!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"loaded-{Interlocked.Increment(ref _manifestNumber)}.json"),
            JsonSerializer.Serialize(new { assemblies = loaded, nativeLibrary = backend, selectorImage, nativeSha256 = Hash(selectorImage), appPeerAssembly = grid.GetType().Assembly.Location },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public unsafe void Verify(AutomationPeer cell, AutomationPeer? child = null, bool focusChild = false, bool pressChild = false)
    {
        Remember(_grid);
        Remember(cell);
        if (child is not null) Remember(child);
        var grid = AvnAutomationPeer.Wrap(_grid)!;
        var item = cell.GetProvider<IGridItemProvider>()!;
        var provider = _grid.GetProvider<IGridProvider>()!;
        var result = _probe(Owned(_grid), Owned(cell), child is null ? IntPtr.Zero : Owned(child),
            provider.RowCount, provider.ColumnCount, item.Row, item.Column,
            focusChild ? 1 : 0, pressChild ? 1 : 0);
        Require(result == 0, "Actual FreeX peer/native selector failure code " + result);
        // Native array order is checked below against independently sorted public peer data,
        // not against a second native GUID definition.
        var expectedVisible = _grid.GetChildren().Where(peer => peer.GetProvider<IGridItemProvider>() is { Row: >= 0, Column: >= 0 } location &&
                ReferenceEquals(location.ContainingGrid, _grid) && !peer.IsOffscreen())
            .OrderBy(peer => peer.GetProvider<IGridItemProvider>()!.Row).ThenBy(peer => peer.GetProvider<IGridItemProvider>()!.Column).ToArray();
        var visible = grid.Grid_GetVisibleCells();
        Require(visible.Count == expectedVisible.Length, "Visible count differs from actual logical peer subset");
        for (uint i = 0; i < visible.Count; i++)
            Require(ReferenceEquals(visible.Get(i), AvnAutomationPeer.Wrap(expectedVisible[i])), "Visible array identity/order differs");
        var expectedSelected = _grid.GetProvider<ISelectionProvider>()!.GetSelection()
            .Where(peer => peer.GetProvider<IGridItemProvider>() is { Row: >= 0, Column: >= 0 } location && ReferenceEquals(location.ContainingGrid, _grid))
            .OrderBy(peer => peer.GetProvider<IGridItemProvider>()!.Row).ThenBy(peer => peer.GetProvider<IGridItemProvider>()!.Column).ToArray();
        var selected = grid.Grid_GetSelectedCells();
        Require(selected.Count == expectedSelected.Length, "Selected count differs");
        for (uint i = 0; i < selected.Count; i++)
            Require(ReferenceEquals(selected.Get(i), AvnAutomationPeer.Wrap(expectedSelected[i])), "Selected array identity/order differs");
    }

    private void Remember(AutomationPeer peer)
    {
        if (!_seen.Add(peer)) return;
        foreach (var child in peer.GetChildren()) Remember(child);
    }
    private static unsafe IntPtr Owned(AutomationPeer peer) => (IntPtr)MicroComRuntime.GetNativePointer<IAvnAutomationPeer>(AvnAutomationPeer.Wrap(peer)!, true);
    private static string RequiredPath(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } path && File.Exists(path)
        ? path : throw new InvalidOperationException("Missing isolated native test input: " + name);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public void Dispose()
    {
        // A native node's owner must outlive all queued UI notifications. The headless
        // fixture closes its real windows and drains the dispatcher before retirement.
        Retired.UnionWith(_seen);
        _seen.Clear();
    }
    public static void RetireClosedPeers()
    {
        foreach (var peer in Retired)
        {
            var wrapper = AvnAutomationPeer.Wrap(peer)!;
            // The upstream wrapper has no detach/reset API. Keep this test-only wrapper
            // alive until the bounded test process exits after releasing its native node,
            // preventing its finalizer from revisiting that deliberately retired proxy.
            RetiredWrappers.Add(wrapper);
            wrapper.Node?.Dispose();
        }
        Retired.Clear();
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativeImagePath();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Probe(IntPtr grid, IntPtr cell, IntPtr child, int rows, int columns, int row, int column, int focus, int press);
}
