using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Minimal headless Avalonia app providing a Fluent theme so styled controls can measure.</summary>
public sealed class RibbonHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RibbonHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
