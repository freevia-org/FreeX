namespace Free.Shared.AppServices.Tests;

public sealed class AppCrashAnalyticsOptionsTests
{
    private static readonly AppProductIdentity Product =
        new("FreeW", "FREEW_DIAGNOSTICS", "FreeW");

    [Fact]
    public void ReleaseBuildProperty_IsVisibleToRuntimeConfiguration()
    {
        const string expectationVariable = "FREEX_TEST_EXPECT_EMBEDDED_SENTRY_DSN";
        var expectedDsn = Environment.GetEnvironmentVariable(expectationVariable);
        var options = AppCrashAnalyticsOptions.CreateDefault(userConsent: true);

        if (string.IsNullOrWhiteSpace(expectedDsn))
            options.IsEnabled.Should().BeFalse("ordinary developer builds do not embed a Sentry DSN");
        else
        {
            options.IsEnabled.Should().BeTrue();
            options.Dsn.Should().Be(expectedDsn);
        }
    }

    [Fact]
    public void CreateDefault_IsDisabledWithoutDsnEvenWithConsent()
    {
        var values = new Dictionary<string, string?>
        {
            ["FREEW_CRASH_ANALYTICS"] = "1",
        };

        var options = AppCrashAnalyticsOptions.CreateDefault(Product, Get(values));

        options.IsEnabled.Should().BeFalse();
        options.Dsn.Should().BeNull();
    }

    [Fact]
    public void CreateDefault_IsDisabledWithoutExplicitConsentEvenWithDsn()
    {
        var values = new Dictionary<string, string?>
        {
            ["FREEW_SENTRY_DSN"] = "https://public@example.invalid/1",
        };

        var options = AppCrashAnalyticsOptions.CreateDefault(Product, Get(values));

        options.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void CreateDefault_EnablesOnlyWithDsnAndAffirmativeConsent(string consent)
    {
        var values = new Dictionary<string, string?>
        {
            ["FREEW_SENTRY_DSN"] = "https://public@example.invalid/1",
            ["FREEW_CRASH_ANALYTICS"] = consent,
            ["FREEW_SENTRY_ENVIRONMENT"] = "public-preview",
        };

        var options = AppCrashAnalyticsOptions.CreateDefault(Product, Get(values));

        options.IsEnabled.Should().BeTrue();
        options.Environment.Should().Be("public-preview");
    }

    [Fact]
    public void CreateDefault_UsesInjectedBuildDsnWithPersistedConsent()
    {
        var options = AppCrashAnalyticsOptions.CreateDefault(
            Product,
            _ => null,
            () => ("https://public@example.invalid/42", "stable"),
            persistedConsent: true);

        options.IsEnabled.Should().BeTrue();
        options.Dsn.Should().Be("https://public@example.invalid/42");
        options.Environment.Should().Be("stable");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public void CreateDefault_EnvironmentKillSwitchOverridesPersistedConsent(string killSwitch)
    {
        var values = new Dictionary<string, string?>
        {
            ["FREEW_CRASH_ANALYTICS"] = killSwitch,
        };

        var options = AppCrashAnalyticsOptions.CreateDefault(
            Product,
            Get(values),
            () => ("https://public@example.invalid/42", "stable"),
            persistedConsent: true);

        options.IsEnabled.Should().BeFalse();
    }

    private static Func<string, string?> Get(IReadOnlyDictionary<string, string?> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;
}
