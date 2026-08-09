using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Free.Shared.Shell.Avalonia;

public record SisterAppLaunchSmokeOptions(string ReportPath, string? DiagnosticsDirectory)
{
    public const string Argument = "--launch-smoke";
    public const string DiagnosticsDirectoryArgument = "--launch-smoke-diagnostics-dir";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out SisterAppLaunchSmokeOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        error = "";
        var filtered = new List<string>();
        string? reportPath = null;
        string? diagnosticsDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{Argument} requires a report path.";
                    return false;
                }

                reportPath = args[++i];
                continue;
            }

            if (string.Equals(arg, DiagnosticsDirectoryArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{DiagnosticsDirectoryArgument} requires a directory path.";
                    return false;
                }

                diagnosticsDirectory = args[++i];
                continue;
            }

            filtered.Add(arg);
        }

        if (reportPath is not null)
            options = new SisterAppLaunchSmokeOptions(reportPath, diagnosticsDirectory);

        startupArguments = filtered.ToArray();
        return true;
    }
}

public sealed record SisterAppLaunchSmokeReport(bool IsPassed, string Text);

public static class SisterAppLaunchSmokeCoordinator
{
    private const int DefaultMaxAttempts = 60;
    private const int DefaultPollMilliseconds = 200;

    public static void Start<TWindow>(
        TWindow window,
        SisterAppLaunchSmokeOptions options,
        Func<TWindow, SisterAppLaunchSmokeReport> capture,
        int maxAttempts = DefaultMaxAttempts,
        int pollMilliseconds = DefaultPollMilliseconds)
        where TWindow : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capture);

        window.Opened += (_, _) =>
        {
            var attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(pollMilliseconds) };
            timer.Tick += (_, _) =>
            {
                attempts++;

                // Avalonia has no dispatcher-level unhandled-exception hook, so an exception
                // escaping a timer tick takes the process down. A capture delegate that throws
                // should fail the smoke run, not kill the app under test — and it would otherwise
                // throw again on every subsequent tick.
                try
                {
                    var report = capture(window);
                    if (report.IsPassed || attempts >= maxAttempts)
                    {
                        timer.Stop();
                        Finish(report, options);
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    Finish(new SisterAppLaunchSmokeReport(false, ex.ToString()), options);
                }
            };
            timer.Start();
        };
    }

    private static void Finish(SisterAppLaunchSmokeReport report, SisterAppLaunchSmokeOptions options)
    {
        try
        {
            var directory = Path.GetDirectoryName(options.ReportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(options.ReportPath, report.Text);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"launch-smoke: failed to write report: {ex.Message}");
        }

        Console.Out.Write(report.Text);
        Console.Out.Flush();

        var exitCode = report.IsPassed ? 0 : 1;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(exitCode);
        else
            Environment.Exit(exitCode);
    }
}
