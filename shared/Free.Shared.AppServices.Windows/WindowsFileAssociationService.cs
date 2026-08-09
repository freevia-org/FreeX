using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Free.Shared.AppServices;
using System.Runtime.InteropServices;

namespace Free.Shared.AppServices.Windows;

/// <summary>
/// App-neutral per-user (HKCU) file-association registration for Windows. The owning app supplies
/// its own list of <see cref="FileAssociationDefinition"/>; owned types become the default handler,
/// while neutral types are only added to OpenWithProgids so existing defaults survive.
/// All operations are best-effort and never throw to the caller.
/// </summary>
public sealed class WindowsFileAssociationService : IFileAssociationService
{
    private readonly IReadOnlyList<FileAssociationDefinition> _definitions;
    private readonly string _classesRootPath;
    private readonly ILogger? _logger;

    public WindowsFileAssociationService(
        IReadOnlyList<FileAssociationDefinition> definitions,
        string classesRootPath = @"Software\Classes",
        ILogger? logger = null)
    {
        _definitions = definitions;
        _classesRootPath = classesRootPath;
        _logger = logger;
    }

    public void RegisterAll(string executablePath)
    {
        try
        {
            foreach (var def in _definitions)
                RegisterOne(def, executablePath);
            NotifyShell();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "RegisterAll failed."); }
    }

    public void UnregisterAll()
    {
        try
        {
            foreach (var def in _definitions)
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
        var def = _definitions.FirstOrDefault(d => d.Extension == extension);
        if (def is null) return false;
        try
        {
            using var ext = Registry.CurrentUser.OpenSubKey($@"{_classesRootPath}\{extension}");
            return (ext?.GetValue(null) as string) == def.ProgId;
        }
        catch (Exception ex)
        {
            // Locked-down or policy-managed machines can deny the read outright. Every other entry
            // point in this service already treats registry failure as "not registered"; match that
            // rather than letting a query throw at whatever UI happens to ask.
            _logger?.LogWarning(ex, "IsDefaultHandler failed for {Extension}.", extension);
            return false;
        }
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
