namespace FreeP.App.Host;

/// <summary>
/// Compatibility type for existing WPF call sites. The option model itself is shared with Avalonia in the
/// presentation tier so both Backstage implementations report and apply the same settings.
/// </summary>
public sealed class FreePOptions : FreeP.App.Compositor.FreePOptions;
