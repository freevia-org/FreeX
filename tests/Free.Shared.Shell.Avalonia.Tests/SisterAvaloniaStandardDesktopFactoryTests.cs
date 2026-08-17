using Avalonia;
using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class SisterAvaloniaStandardDesktopFactoryTests
{
    [Fact]
    public void Run_InstallsIdentityAndKeepsLifetimeArgumentsSeparateFromWindowArguments()
    {
        var originalProduct = AppProduct.Current;
        var identity = new AppProductIdentity("TestApp", "TESTAPP_DIAGNOSTICS", "Test App");
        var profile = CreateProfile(identity);
        var launch = new SisterAvaloniaStandardDesktopLaunch<TestWindow>(["document.test"]);

        try
        {
            var exitCode = SisterAvaloniaStandardDesktopFactory.Run(
                ["--lifetime"],
                profile,
                launch,
                lifetimeArguments =>
                {
                    AppProduct.Current.Should().BeSameAs(identity);
                    lifetimeArguments.Should().Equal("--lifetime");
                    profile.GetPendingLaunch().StartupArguments.Should().Equal("document.test");
                    return 7;
                },
                NoDiagnosticsRuntime());

            exitCode.Should().Be(7);
            profile.GetPendingLaunch().StartupArguments.Should().BeEmpty();
        }
        finally
        {
            AppProduct.Current = originalProduct;
        }
    }

    [Fact]
    public void ThemeDescriptor_SetsActiveThemeBeforeApplyingResources()
    {
        var order = new List<string>();
        var plan = new ApplicationThemeStartupPlan<string>(
            "TEST_THEME",
            "dark",
            "light-theme",
            "dark-theme",
            "Test");
        var descriptor = new SisterAvaloniaThemeStartupDescriptor<string>(
            plan,
            theme => order.Add("active:" + theme),
            (_, theme, prefix) => order.Add($"resources:{theme}:{prefix}"));

        descriptor.Apply(new TestApplication(), name => name == "TEST_THEME" ? "dark" : null);

        order.Should().Equal("active:dark-theme", "resources:dark-theme:Test");
    }

    private static SisterAvaloniaStandardDesktopProfile<TestApplication, TestWindow, TestOptions> CreateProfile(
        AppProductIdentity identity) =>
        new(
            identity,
            new SisterAvaloniaLocalizationStartupDescriptor(() => { }),
            new SisterAvaloniaThemeStartupDescriptor<string>(
                new ApplicationThemeStartupPlan<string>("TEST_THEME", "dark", "light", "dark", "Test"),
                _ => { },
                (_, _, _) => { }),
            new SisterAvaloniaOptionsStartupDescriptor<TestOptions>(
                () => new InMemoryApplicationOptionsStore<TestOptions>()),
            new SisterAvaloniaWindowStartupDescriptor<TestWindow, TestOptions>(
                (_, _, _) => new TestWindow()));

    private static SisterAvaloniaProgramRuntime NoDiagnosticsRuntime() =>
        new()
        {
            CreateDiagnostics = (_, _) => new SisterAvaloniaProgramDiagnostics(() => { }, (_, _) => { }),
            RegisterRibbonCommandFaultHandler = _ => { },
        };

    private sealed class TestApplication : Application;

    private sealed class TestWindow : Window;

    private sealed class TestOptions : INormalizableApplicationOptions
    {
        public void Normalize()
        {
        }
    }
}
