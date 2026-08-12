using FreeW.App.Avalonia;

namespace FreeW.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!TablePropertiesX11ValidationOptions.TryParse(
                args,
                out var options,
                out var startupArguments,
                out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (options is null)
        {
            Console.Error.WriteLine($"Expected {TablePropertiesX11ValidationOptions.Argument}.");
            return 2;
        }

        return FreeW.App.Avalonia.Program.RunToolHost(
            startupArguments,
            access => TablePropertiesX11ValidationCoordinator.Start(access, options));
    }
}
