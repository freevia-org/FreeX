using System;
using System.Reflection;
using Free.Shared.AppServices.Printing;
using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Compiled tripwire for the MSBuild bug that made <c>FREEP_WINDOWS_CAPTURE</c> undefined in every
/// build (see <see cref="WindowsPrinterSelectionTests"/> for the full history). Everything else that
/// covers this feature reads MainWindow.cs as text, so all of it passed while the code it asserts on
/// was never compiled -- and all of it would still pass if someone reverted
/// FreeP.App.Avalonia.csproj's PropertyGroup back to <c>'$(TargetPlatformIdentifier)' == 'Windows'</c>,
/// because the source text would be untouched.
///
/// These tests instead interrogate the built <c>FreeP.App.Avalonia</c> assembly. If the constant stops
/// being defined on a Windows build, the gated members simply are not in the assembly and these fail.
/// </summary>
public sealed class WindowsCaptureConstantTests
{
    private static readonly Assembly ShellAssembly = typeof(MainWindow).Assembly;

    [Fact]
    public void WindowsBuildsCompileTheWindowsCaptureRegion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        ShellAssembly.GetType("FreeP.App.Avalonia.AvaloniaOleInPlaceHost", throwOnError: false)
            .Should().NotBeNull(
                "AvaloniaOleInPlaceHost is declared entirely inside #if FREEP_WINDOWS_CAPTURE, so its " +
                "absence from a Windows build means the constant is undefined and the whole " +
                "Windows-native OLE/print/recording surface silently vanished from the shipped app");

        var mainWindow = typeof(MainWindow);
        mainWindow.GetMethod(
                "AddWindowsPrinterSelector",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().NotBeNull("the print backstage pane's Windows printer selector must be compiled in");
        mainWindow.GetMethod(
                "ShowWindowsPrinterDialog",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().NotBeNull("the native printer dialog entry point must be compiled in");
        mainWindow.GetField("_nativePrinterPicker", BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().NotBeNull("the printer picker combo box field must be compiled in");
        mainWindow.GetField("_activeOleHost", BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().NotBeNull("in-place OLE activation must be compiled in");
    }

    /// <summary>
    /// The shipped-behaviour consequence of the bug, rather than the mechanism: with the constant
    /// undefined, <c>CreatePlatformPrintService</c> passed <c>windowsFactory: null</c>, so FreeP's
    /// Avalonia shell fell through to the CUPS service on Windows -- printing through a Linux spooler
    /// bridge that is not there.
    /// </summary>
    [Fact]
    public void WindowsBuildsSelectTheWindowsPrintServiceRatherThanCups()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var factory = typeof(MainWindow).GetMethod(
            "CreatePlatformPrintService",
            BindingFlags.Static | BindingFlags.NonPublic);
        factory.Should().NotBeNull();

        var service = factory!.Invoke(null, Array.Empty<object>()) as IPlatformPrintService;

        service.Should().NotBeNull();
        // Compared by name, not typeof: the shared Windows print assembly is itself referenced only
        // under a Windows-conditioned ProjectReference, and this assertion must read as a statement
        // about what the shell selected, not about what this test project happens to link.
        service!.GetType().FullName.Should().Be(
            "Free.Shared.AppServices.Windows.WindowsPrintService",
            "a Windows build must print through the Windows spooler; falling back to " +
            "CupsPrintService here is exactly what the undefined FREEP_WINDOWS_CAPTURE constant caused");
    }
}
