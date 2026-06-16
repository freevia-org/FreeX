using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using FreeX.App.Services.FileAssociations;
using System.Runtime.InteropServices;

namespace FreeX.App.Host.FileAssociations;

/// <summary>
/// Per-user (HKCU) file-association registration for Windows. Owned types become the default
/// handler; neutral/Office types are only added to OpenWithProgids so existing defaults survive.
/// All operations are best-effort and never throw to the caller.
/// </summary>
public sealed class WindowsFileAssociationService : IFileAssociationService
{
    private readonly string _classesRootPath;
    private readonly ILogger? _logger;

    public WindowsFileAssociationService(string classesRootPath = @"Software\Classes", ILogger? logger = null)
    {
        _classesRootPath = classesRootPath;
        _logger = logger;
    }

    public void RegisterAll(string executablePath)
    {
        try
        {
            foreach (var def in FileAssociationDefinition.All)
                RegisterOne(def, executablePath);
            NotifyShell();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "RegisterAll failed."); }
    }

    public void UnregisterAll()
    {
        try
        {
            foreach (var def in FileAssociationDefinition.All)
            {
                // Remove the ProgId tree.
                Registry.CurrentUser.DeleteSubKeyTree($@"{_classesRootPath}\{def.ProgId}", throwOnMissingSubKey: false);

                // Remove our OpenWith entry; if we own the default and it still points at us, clear it.
                using var ext = Registry.CurrentUser.OpenSubKey($@"{_classesRootPath}\{def.Extension}", writable: true);
                if (ext is null) continue;
                using (var ow = ext.OpenSubKey("OpenWithProgids", writable: true))
                    ow?.DeleteValue(def.ProgId, throwOnMissingValue: false);
                if (def.Ownership == AssociationOwnership.Default &&
                    (ext.GetValue(string.Empty) as string) == def.ProgId)
                    ext.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
            NotifyShell();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "UnregisterAll failed."); }
    }

    public bool IsDefaultHandler(string extension)
    {
        var def = FileAssociationDefinition.All.FirstOrDefault(d => d.Extension == extension);
        if (def is null) return false;
        using var ext = Registry.CurrentUser.OpenSubKey($@"{_classesRootPath}\{extension}");
        return (ext?.GetValue(null) as string) == def.ProgId;
    }

    private void RegisterOne(FileAssociationDefinition def, string executablePath)
    {
        // ProgId: friendly name, icon, open command.
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{_classesRootPath}\{def.ProgId}"))
        {
            progId.SetValue(null, def.FriendlyName);
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{executablePath}\",0");
            using (var cmd = progId.CreateSubKey(@"shell\open\command"))
                cmd.SetValue(null, $"\"{executablePath}\" \"%1\"");
        }

        // Extension key.
        using var ext = Registry.CurrentUser.CreateSubKey($@"{_classesRootPath}\{def.Extension}");
        using (var ow = ext.CreateSubKey("OpenWithProgids"))
            ow.SetValue(def.ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        // Only owned types take the default; neutral types must not steal an existing default.
        if (def.Ownership == AssociationOwnership.Default)
            ext.SetValue(null, def.ProgId);
    }

    private void NotifyShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0x0000 /*SHCNF_IDLIST*/, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
