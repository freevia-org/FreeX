namespace FreeX.Validation.Wpf;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (TesterReleaseSmoke.TryRun(args, out var exitCode))
            return exitCode;

        Console.Error.WriteLine($"Expected {TesterReleaseSmoke.CommandLineSwitch} [report-path].");
        return 2;
    }
}
