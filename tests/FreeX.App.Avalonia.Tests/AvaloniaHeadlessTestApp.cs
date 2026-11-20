using Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Minimal headless Avalonia app providing a Fluent theme so styled controls can measure.</summary>
public sealed class RibbonHeadlessApp : HeadlessFluentApplication
{
    public static AppBuilder BuildAvaloniaApp() => ConfigureHeadless<RibbonHeadlessApp>();
}
