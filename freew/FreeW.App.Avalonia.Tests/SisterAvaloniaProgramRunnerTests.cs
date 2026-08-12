using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class SisterAvaloniaProgramRunnerTests
{
    [Fact]
    public void Run_InstallsIdentityBeforePreparationThenRegistersDiagnosticsAndStartsApp()
    {
        var originalProduct = AppProduct.Current;
        var identity = new AppProductIdentity("TestApp", "TESTAPP_DIAGNOSTICS", "Test App");
        var diagnostics = new CapturingDiagnostics();
        var order = new List<string>();
        string? resolvedVersion = null;
        string[]? startedArguments = null;
        Action<Exception, string>? ribbonFaultHandler = null;

        try
        {
            var exitCode = SisterAvaloniaProgramRunner.Run(
                ["raw"],
                new SisterAvaloniaProgramSpec(
                    identity,
                    arguments =>
                    {
                        order.Add("prepare");
                        AppProduct.Current.Should().BeSameAs(identity);
                        arguments.Should().Equal("raw");
                        return SisterAvaloniaLaunchPreparation.Continue(["prepared"]);
                    },
                    arguments =>
                    {
                        order.Add("start");
                        startedArguments = arguments;
                        return 7;
                    }),
                new SisterAvaloniaProgramRuntime
                {
                    ResolveVersion = () => "1.2.3",
                    CreateDiagnostics = version =>
                    {
                        order.Add("diagnostics");
                        resolvedVersion = version;
                        diagnostics.OnRegister = () => order.Add("register");
                        return diagnostics;
                    },
                    RegisterRibbonCommandFaultHandler = handler =>
                    {
                        order.Add("ribbon");
                        ribbonFaultHandler = handler;
                    },
                });

            exitCode.Should().Be(7);
            resolvedVersion.Should().Be("1.2.3");
            startedArguments.Should().Equal("prepared");
            order.Should().Equal("prepare", "diagnostics", "register", "ribbon", "start");
            var commandFailure = new InvalidOperationException("command");
            ribbonFaultHandler.Should().NotBeNull();
            ribbonFaultHandler!(commandFailure, "freew.test");
            diagnostics.Exception.Should().BeSameAs(commandFailure);
            diagnostics.Source.Should().Be("ribbon_command:freew.test");
        }
        finally
        {
            AppProduct.Current = originalProduct;
        }
    }

    [Fact]
    public void Run_WhenPreparationExits_SkipsDiagnosticsAndAppStart()
    {
        var originalProduct = AppProduct.Current;
        var identity = new AppProductIdentity("TestApp", "TESTAPP_DIAGNOSTICS", "Test App");
        var started = false;

        try
        {
            var exitCode = SisterAvaloniaProgramRunner.Run(
                [],
                new SisterAvaloniaProgramSpec(
                    identity,
                    _ => SisterAvaloniaLaunchPreparation.Exit(2),
                    _ =>
                    {
                        started = true;
                        return 0;
                    }),
                new SisterAvaloniaProgramRuntime
                {
                    CreateDiagnostics = _ => throw new InvalidOperationException("Diagnostics should not be created.")
                });

            exitCode.Should().Be(2);
            started.Should().BeFalse();
            AppProduct.Current.Should().BeSameAs(identity);
        }
        finally
        {
            AppProduct.Current = originalProduct;
        }
    }

    [Fact]
    public void Run_WhenAppStartFails_RecordsCrashAndRethrows()
    {
        var originalProduct = AppProduct.Current;
        var identity = new AppProductIdentity("TestApp", "TESTAPP_DIAGNOSTICS", "Test App");
        var diagnostics = new CapturingDiagnostics();
        var failure = new InvalidOperationException("boom");

        try
        {
            Action action = () => SisterAvaloniaProgramRunner.Run(
                [],
                new SisterAvaloniaProgramSpec(
                    identity,
                    _ => SisterAvaloniaLaunchPreparation.Continue([]),
                    _ => throw failure),
                new SisterAvaloniaProgramRuntime
                {
                    CreateDiagnostics = _ => diagnostics,
                    RegisterRibbonCommandFaultHandler = _ => { },
                });

            action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            diagnostics.Registered.Should().BeTrue();
            diagnostics.Exception.Should().BeSameAs(failure);
            diagnostics.Source.Should().Be("avalonia_startup");
        }
        finally
        {
            AppProduct.Current = originalProduct;
        }
    }

    [Fact]
    public void SisterPrograms_DelegateCommonLifecycleToSharedRunner()
    {
        var freeWProgram = ReadSource("freew", "FreeW.App.Avalonia", "Program.cs");
        var freePProgram = ReadSource("freep", "FreeP.App.Avalonia", "Program.cs");
        var freeXProgram = ReadSource("src", "FreeX.App.Avalonia", "Program.cs");
        var freeXApp = ReadSource("src", "FreeX.App.Avalonia", "App.cs");
        var sharedProgramRunner = ReadSource(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaProgramRunner.cs");
        var sharedApplicationRunner = ReadSource(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaApplicationStartupRunner.cs");

        foreach (var source in new[] { freeWProgram, freePProgram })
        {
            source.Should().Contain("SisterAvaloniaProgramRunner.Run(");
            source.Should().Contain("SisterAvaloniaLaunchPreparation.Continue(startupArguments)");
            source.Should().NotContain("LocalAppDiagnostics.CreateDefault");
            source.Should().NotContain("diagnostics.RegisterCrashHandlers");
            source.Should().NotContain("diagnostics.RecordCrash");
            source.Should().NotContain("RibbonCommandFaultReporter.Handler");
        }

        freeXProgram.Should().Contain("SisterAvaloniaApplicationStartupRunner.Run(")
            .And.Contain("RegisterUnhandledExceptionHandlers: () => diagnostics.RegisterCrashHandlers()")
            .And.Contain("RecordCrash: (exception, source) => diagnostics.RecordCrash(exception, source)")
            .And.Contain("CompletedExitCode = 0")
            .And.NotContain("RibbonCommandFaultReporter.Handler");
        freeXApp.Should().NotContain("RibbonCommandFaultReporter.Handler");
        sharedProgramRunner.Should().Contain("SisterAvaloniaApplicationStartupRunner.Run(")
            .And.NotContain("catch (Exception ex)");
        sharedApplicationRunner.Should().Contain("spec.RegisterRibbonCommandFaultHandler(")
            .And.Contain("RibbonCommandCrashSourcePrefix + commandId")
            .And.Contain("spec.RecordCrash(ex, spec.StartupCrashSource)");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private sealed class CapturingDiagnostics : ISisterAvaloniaProgramDiagnostics
    {
        public Action? OnRegister { get; set; }

        public bool Registered { get; private set; }

        public Exception? Exception { get; private set; }

        public string? Source { get; private set; }

        public void RegisterCrashHandlers()
        {
            Registered = true;
            OnRegister?.Invoke();
        }

        public void RecordCrash(Exception exception, string source)
        {
            Exception = exception;
            Source = source;
        }
    }
}
