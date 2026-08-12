using System.Diagnostics;
using System.Text.Json;
using FreeP.Validation.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class StartupDirtyTraceTests
{
    [Fact]
    public void Startup_dirty_trace_parser_removes_only_its_switch()
    {
        var ok = StartupDirtyTraceOptions.TryParse(
            [StartupDirtyTraceOptions.Argument, "trace.json", "deck.pptx", "--other"],
            out var options,
            out var startupArguments,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
        options!.ReportPath.Should().Be("trace.json");
        startupArguments.Should().Equal("deck.pptx", "--other");
    }

    [Fact]
    public void Production_app_lifetime_startup_argument_path_starts_clean()
    {
        if (OperatingSystem.IsLinux() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return;

        var documentPath = RepoFile("tools/FreeP.RenderCompare/corpus/01-title-slide.pptx");
        var appAssemblyPath = typeof(StartupDirtyTraceOptions).Assembly.Location;
        using var temporaryDirectory = new TestTemporaryDirectory("freep-startup-dirty-");
        var reportPath = Path.Combine(temporaryDirectory.Path, "startup-dirty.json");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(appAssemblyPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "exec",
                appAssemblyPath,
                StartupDirtyTraceOptions.Argument,
                reportPath,
                documentPath,
            },
        });
        process.Should().NotBeNull();

        try
        {
            // 60s (not 30s): this launches a real app process, and the release gate runs the
            // FreeX/FreeW/FreeP verify jobs in parallel. Under that contention a correct, deterministic
            // shutdown can still take ~35s (dotnet exec startup + JIT + Avalonia window realization
            // compete for CPU); the app-shutdown path itself is unconditional and known-good.
            process!.WaitForExit(60_000).Should().BeTrue("the startup trace must shut down its owned app");
            process.ExitCode.Should().Be(0);
            File.Exists(reportPath).Should().BeTrue();

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            report.RootElement.GetProperty("IsDirty").GetBoolean().Should().BeFalse();
            report.RootElement.GetProperty("DirtyGeneration").GetInt32().Should().Be(0);
            report.RootElement.GetProperty("Title").GetString().Should().NotContain("*");
            report.RootElement.GetProperty("Events").EnumerateArray()
                .Select(row => row.GetProperty("Stage").GetString())
                .Should().Contain(["startup-load-saved", "window-opened"]);
        }
        finally
        {
            if (!process!.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Startup_dirty_trace_harness_is_owned_by_validation_support()
    {
        var coordinator = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "StartupDirtyTraceValidation.cs"));
        var app = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "App.cs"));
        var program = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "Program.cs"));
        var adapter = File.ReadAllText(RepoFile(
            "freep", "FreeP.App.Avalonia", "MainWindow.ValidationAccessAdapter.cs"));

        coordinator.Should().Contain("DispatcherTimer");
        coordinator.Should().Contain("File.WriteAllText");
        coordinator.Should().Contain("StartupDirtyTraceReport");
        app.Should().NotContain("StartupDirtyTraceOptions");
        app.Should().NotContain("StartupDirtyTraceCoordinator");
        program.Should().NotContain("StartupDirtyTraceOptions");
        program.Should().NotContain("--startup-dirty-trace");
        adapter.Should().Contain("StartupDirtyTrace =>");
        adapter.Should().NotContain("DispatcherTimer");
        adapter.Should().NotContain("File.WriteAllText");
        File.Exists(Path.Combine(
            Path.GetDirectoryName(RepoFile("freep", "FreeP.App.Avalonia", "Program.cs"))!,
            "Smoke",
            "StartupDirtyTrace.cs")).Should().BeFalse();
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", parts);
}
