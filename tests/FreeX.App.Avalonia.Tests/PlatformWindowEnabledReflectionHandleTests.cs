using System.Reflection;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// MainWindow.DialogRangeSelection.cs disables the platform window while a dialog range selection
/// is in progress by reflecting into Avalonia's <c>IWindowImpl.SetEnabled</c>. That is a
/// third-party interface member, so it cannot become an internal compiled seam the way this
/// repo's own members did.
///
/// <para>
/// The handle is a nullable static consumed as
/// <c>SetPlatformWindowEnabledMethod?.Invoke(platformImpl, [isEnabled])</c>. If an Avalonia
/// upgrade renames or removes <c>SetEnabled</c>, the lookup yields null, the <c>?.</c>
/// short-circuits, and the platform window silently stops being disabled -- the dialog range
/// picker keeps working just enough to look fine while the modal guard it depends on is gone. No
/// exception, no crash.
/// </para>
///
/// <para>
/// The existing source-hygiene assertion pins the TEXT of that call, which survives the rot
/// untouched, so it cannot catch this. This asserts the lookup actually resolved.
/// </para>
/// </summary>
public sealed class PlatformWindowEnabledReflectionHandleTests
{
    [Fact]
    public void SetPlatformWindowEnabledHandle_ResolvesAgainstTheReferencedAvaloniaVersion()
    {
        var handle = typeof(MainWindow).GetField(
            "SetPlatformWindowEnabledMethod",
            BindingFlags.Static | BindingFlags.NonPublic);

        handle.Should().NotBeNull(
            "MainWindow.DialogRangeSelection.cs caches IWindowImpl.SetEnabled in this static; if it " +
            "was renamed, retarget this guard rather than deleting it");

        handle!.GetValue(null).Should().NotBeNull(
            "the call site is null-conditional, so an unresolved handle does not throw -- the platform " +
            "window just silently stops being disabled during dialog range selection. A null here means " +
            "the referenced Avalonia version no longer exposes IWindowImpl.SetEnabled");
    }
}
