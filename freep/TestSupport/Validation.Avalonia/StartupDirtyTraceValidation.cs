using System.Text.Json;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;

namespace FreeP.Validation.Avalonia;

internal sealed record StartupDirtyTraceOptions(string ReportPath)
{
    public const string Argument = "--startup-dirty-trace";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out StartupDirtyTraceOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        error = string.Empty;
        var filtered = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(args[i]);
                continue;
            }

            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                startupArguments = [];
                error = $"{Argument} requires a report path.";
                return false;
            }

            options = new StartupDirtyTraceOptions(args[++i]);
        }

        startupArguments = filtered.ToArray();
        return true;
    }
}

internal static class StartupDirtyTraceCoordinator
{
    private const int SettleTicks = 4;
    private const int PollMilliseconds = 100;

    public static void Start(MainWindow.ValidationAccessAdapter access, StartupDirtyTraceOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);

        access.StartWhenOpened(() =>
        {
            var ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMilliseconds) };
            timer.Tick += (_, _) =>
            {
                if (++ticks < SettleTicks)
                    return;

                timer.Stop();
                var report = new StartupDirtyTraceReport(
                    access.IsVisible,
                    access.Title,
                    access.IsDirty,
                    access.DirtyGeneration,
                    access.StartupDirtyTrace);
                WriteReport(options.ReportPath, report);
                access.Shutdown(report.IsPassed ? 0 : 1);
            };
            timer.Start();
            return Task.CompletedTask;
        });
    }

    private static void WriteReport(string path, StartupDirtyTraceReport report)
    {
        JsonArtifactIO.Write(
            path,
            report,
            new JsonSerializerOptions { WriteIndented = true });
        Console.Out.WriteLine($"startup-dirty-trace={(report.IsPassed ? "passed" : "failed")}");
        Console.Out.Flush();
    }
}

internal sealed record StartupDirtyTraceReport(
    bool WindowShown,
    string Title,
    bool IsDirty,
    int DirtyGeneration,
    IReadOnlyList<StartupDirtyTraceEntry> Events)
{
    public bool IsPassed =>
        WindowShown &&
        !IsDirty &&
        Events.Any(entry => string.Equals(entry.Stage, "startup-load-saved", StringComparison.Ordinal));
}
