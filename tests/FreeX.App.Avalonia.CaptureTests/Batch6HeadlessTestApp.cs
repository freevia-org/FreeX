using Avalonia;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Headless windowing with the production Skia text renderer for the Goal Seek capture.</summary>
public sealed class Batch6HeadlessApp : HeadlessFluentApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Batch6HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
