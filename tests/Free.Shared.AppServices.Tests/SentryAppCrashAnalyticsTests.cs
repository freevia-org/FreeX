namespace Free.Shared.AppServices.Tests;

public sealed class SentryAppCrashAnalyticsTests
{
    private static readonly AppProductIdentity Product =
        new("FreeW", "FREEW_DIAGNOSTICS", "FreeW");
    private static readonly AppDiagnosticsMetadata Metadata = new(
        "1.2.3",
        "session-test",
        ".NET test",
        "Test OS",
        "X64");

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://public@example.invalid/1", false)]
    public void Create_DoesNotConstructTransportWithoutBothDsnAndConsent(
        string? dsn,
        bool enabled)
    {
        var factoryCalls = 0;
        using var analytics = SentryAppCrashAnalytics.Create(
            Options(dsn, enabled),
            Metadata,
            Product,
            (_, _, _) =>
            {
                factoryCalls++;
                return new FakeTransport();
            });

        analytics.IsEnabled.Should().BeFalse();
        analytics.SendTestReport().Should().BeFalse();
        factoryCalls.Should().Be(0, "no transport may exist when configuration or consent is missing");
    }

    [Fact]
    public void SendTestReport_UsesInjectedTransportWithoutThrowingOrCreatingAnException()
    {
        var transport = new FakeTransport { TestReportResult = true };
        using var analytics = SentryAppCrashAnalytics.Create(
            Options("https://public@example.invalid/1", enabled: true),
            Metadata,
            Product,
            (_, _, _) => transport);

        analytics.SendTestReport().Should().BeTrue();
        transport.TestReportCalls.Should().Be(1);
        transport.Crashes.Should().BeEmpty("a test report is a synthetic event, not a thrown exception");
    }

    [Fact]
    public void RecordBreadcrumb_RedactsAllowedValuesAndDropsUnapprovedFields()
    {
        var transport = new FakeTransport();
        using var analytics = SentryAppCrashAnalytics.Create(
            Options("https://public@example.invalid/1", enabled: true),
            Metadata,
            Product,
            (_, _, _) => transport);

        analytics.RecordBreadcrumb("open_failed", new Dictionary<string, string?>
        {
            ["reason"] = "Could not open D:\\Clients\\Private Plan.xlsx",
            ["documentPath"] = "D:\\Clients\\Private Plan.xlsx",
        });

        transport.Breadcrumbs.Should().ContainSingle();
        transport.Breadcrumbs[0].Should().ContainKey("reason")
            .WhoseValue.Should().NotContain("Private Plan.xlsx");
        transport.Breadcrumbs[0].Should().NotContainKey("documentPath");
    }

    [Fact]
    public void Runtime_DoesNotRegisterDisabledAnalytics()
    {
        using var registration = AppCrashAnalyticsRuntime.Register(new FakeAnalytics(isEnabled: false));

        AppCrashAnalyticsRuntime.SendTestReport().Should().Be(CrashAnalyticsTestReportResult.Disabled);
    }

    [Fact]
    public void Runtime_InvokesOnlyRegisteredEnabledAnalyticsAndUnregistersOnDispose()
    {
        var analytics = new FakeAnalytics(isEnabled: true);
        var registration = AppCrashAnalyticsRuntime.Register(analytics);

        AppCrashAnalyticsRuntime.SendTestReport().Should().Be(CrashAnalyticsTestReportResult.Sent);
        analytics.TestReportCalls.Should().Be(1);

        registration.Dispose();
        AppCrashAnalyticsRuntime.SendTestReport().Should().Be(CrashAnalyticsTestReportResult.Disabled);
    }

    [Theory]
    [InlineData("Could not open D:\\Clients\\Private Plan.xlsx", "Private Plan.xlsx")]
    [InlineData("Could not open \\\\server\\confidential\\board.pptx", "board.pptx")]
    [InlineData("Could not open /Volumes/Clients/secret deck.pptx", "secret deck.pptx")]
    [InlineData("Failed while reading budget.xlsx", "budget.xlsx")]
    public void RedactText_RemovesArbitraryPathsAndDocumentNames(string input, string secret)
    {
        var redacted = AppCrashDataRedactor.RedactText(
            input,
            userProfilePath: "C:\\Users\\someone",
            userName: "someone");

        redacted.Should().NotContain(secret);
        redacted.Should().ContainAny("<path>", "<document>");
    }

    private static AppCrashAnalyticsOptions Options(string? dsn, bool enabled) => new(
        dsn,
        enabled,
        "tester",
        "FREEW_SENTRY_DSN",
        "FREEW_CRASH_ANALYTICS",
        "FREEW_SENTRY_ENVIRONMENT");

    private sealed class FakeTransport : IAppCrashAnalyticsTransport
    {
        public bool TestReportResult { get; init; }
        public int TestReportCalls { get; private set; }
        public List<Exception> Crashes { get; } = [];
        public List<IDictionary<string, string>> Breadcrumbs { get; } = [];

        public void AddBreadcrumb(string eventName, IDictionary<string, string> properties)
        {
            Breadcrumbs.Add(properties);
        }

        public void CaptureCrash(Exception exception, string source) => Crashes.Add(exception);

        public bool SendTestReport()
        {
            TestReportCalls++;
            return TestReportResult;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeAnalytics(bool isEnabled) : IAppCrashAnalytics
    {
        public bool IsEnabled { get; } = isEnabled;
        public int TestReportCalls { get; private set; }
        public void RecordBreadcrumb(string eventName, IReadOnlyDictionary<string, string?>? properties = null) { }
        public void CaptureCrash(Exception exception, string source) { }
        public bool SendTestReport()
        {
            TestReportCalls++;
            return true;
        }
        public void Dispose() { }
    }
}
