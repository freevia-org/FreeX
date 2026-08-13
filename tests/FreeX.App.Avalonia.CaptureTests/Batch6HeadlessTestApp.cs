using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Headless windowing with the production Skia text renderer for the Goal Seek capture.</summary>
public sealed class Batch6HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Batch6HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
