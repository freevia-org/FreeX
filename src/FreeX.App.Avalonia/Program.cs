using Avalonia;
using Avalonia.Fonts.Inter;

namespace FreeX.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.StartupArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
