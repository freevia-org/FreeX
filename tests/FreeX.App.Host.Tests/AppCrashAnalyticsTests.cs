using System.IO;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class AppCrashAnalyticsTests
{
    [Fact]
    public void Options_CreateDefault_RequiresDsnAndUserOptIn()
    {
        AppCrashAnalyticsOptions.CreateDefault(
                sentryDsnProvider: () => "",
                crashAnalyticsEnabled: true)
            .IsEnabled.Should().BeFalse();

        AppCrashAnalyticsOptions.CreateDefault(
                sentryDsnProvider: () => "https://public@example.ingest.sentry.io/1",
                crashAnalyticsEnabled: false)
            .IsEnabled.Should().BeFalse();

        var options = AppCrashAnalyticsOptions.CreateDefault(
            sentryDsnProvider: () => "https://public@example.ingest.sentry.io/1",
            crashAnalyticsEnabled: true);

        options.IsEnabled.Should().BeTrue();
        options.Dsn.Should().Be("https://public@example.ingest.sentry.io/1");
        options.Environment.Should().Be("production");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public void Options_CreateDefault_MarksEnvironmentKillSwitch(string value)
    {
        using var environmentVariable = TestEnvironmentVariableScope.Set("FREEX_CRASH_ANALYTICS", value);

        var options = AppCrashAnalyticsOptions.CreateDefault(
            sentryDsnProvider: () => "https://public@example.ingest.sentry.io/1",
            crashAnalyticsEnabled: false);

        options.IsEnabled.Should().BeFalse();
        options.IsDisabledByEnvironment.Should().BeTrue();
        options.Dsn.Should().Be("https://public@example.ingest.sentry.io/1");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    public void Options_CreateDefault_AllowsExplicitControlledEnvironmentOptIn(string value)
    {
        using var consent = TestEnvironmentVariableScope.Set("FREEX_CRASH_ANALYTICS", value);
        using var environment = TestEnvironmentVariableScope.Set("FREEX_SENTRY_ENVIRONMENT", "controlled-test");

        var options = AppCrashAnalyticsOptions.CreateDefault(
            sentryDsnProvider: () => "https://public@example.ingest.sentry.io/1",
            crashAnalyticsEnabled: false);

        options.IsEnabled.Should().BeTrue();
        options.Environment.Should().Be("controlled-test");
    }

    [Fact]
    public void AppOptions_DefaultsCrashAnalyticsToOptOutUntilUserEnablesIt()
    {
        var options = new AppOptions();

        options.CrashAnalyticsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AppDiagnostics_ForwardsSafeBreadcrumbsAndCrashesToCrashAnalytics()
    {
        using var temp = new TestTemporaryDirectory();
        var crashAnalytics = new FakeCrashAnalytics();
        var diagnostics = new AppDiagnostics(
            new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true)),
            new AppDiagnosticsMetadata(
                AppVersion: "Version Test",
                SessionId: "session-1",
                RuntimeDescription: ".NET Test",
                OperatingSystemDescription: "Windows Test",
                ProcessArchitecture: "X64"),
            crashAnalytics);

        diagnostics.RecordEvent("workbook_opened", new Dictionary<string, string?>
        {
            ["extension"] = ".xlsx",
            ["workbookPath"] = "C:\\Users\\tester\\private.xlsx",
            ["worksheetCount"] = "3"
        });
        diagnostics.RecordCrash(new InvalidOperationException("boom"), "dispatcher");

        crashAnalytics.Breadcrumbs.Should().ContainSingle();
        crashAnalytics.Breadcrumbs[0].EventName.Should().Be("workbook_opened");
        crashAnalytics.Breadcrumbs[0].Properties.Should().ContainKey("extension");
        crashAnalytics.Breadcrumbs[0].Properties.Should().ContainKey("worksheetCount");
        crashAnalytics.Breadcrumbs[0].Properties.Should().NotContainKey("workbookPath");
        crashAnalytics.Crashes.Should().ContainSingle();
        crashAnalytics.Crashes[0].Source.Should().Be("dispatcher");
    }

    [Fact]
    public void AppDiagnostics_WritesLocalCrashReportWhenRemoteCrashAnalyticsFails()
    {
        using var temp = new TestTemporaryDirectory();
        var diagnostics = new AppDiagnostics(
            new AppDiagnosticsFileStore(new AppDiagnosticsOptions(temp.Path, IsEnabled: true)),
            new AppDiagnosticsMetadata(
                AppVersion: "Version Test",
                SessionId: "session-1",
                RuntimeDescription: ".NET Test",
                OperatingSystemDescription: "Windows Test",
                ProcessArchitecture: "X64"),
            new ThrowingCrashAnalytics());

        var reportPath = diagnostics.RecordCrash(new InvalidOperationException("boom"), "dispatcher");

        reportPath.Should().NotBeEmpty();
        File.Exists(reportPath).Should().BeTrue();
        File.ReadAllText(reportPath).Should().Contain("\"eventName\": \"crash\"");
    }

    private sealed class FakeCrashAnalytics : ICrashAnalytics
    {
        public List<(string EventName, IReadOnlyDictionary<string, string?> Properties)> Breadcrumbs { get; } = [];
        public List<(Exception Exception, string Source)> Crashes { get; } = [];

        public void Initialize(AppCrashAnalyticsOptions options, AppDiagnosticsMetadata metadata)
        {
        }

        public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
        {
            Breadcrumbs.Add((eventName, properties ?? new Dictionary<string, string?>()));
        }

        public void CaptureCrash(Exception exception, string source)
        {
            Crashes.Add((exception, source));
        }

        public bool SendTestReport() => true;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingCrashAnalytics : ICrashAnalytics
    {
        public void Initialize(AppCrashAnalyticsOptions options, AppDiagnosticsMetadata metadata)
        {
        }

        public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
        {
        }

        public void CaptureCrash(Exception exception, string source) =>
            throw new InvalidOperationException("remote unavailable");

        public bool SendTestReport() => false;

        public void Dispose()
        {
        }
    }

}
