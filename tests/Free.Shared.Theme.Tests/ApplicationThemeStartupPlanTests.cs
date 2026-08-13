namespace Free.Shared.Theme.Tests;

public sealed class ApplicationThemeStartupPlanTests
{
    private static readonly ApplicationThemeStartupPlan<string> Plan = new(
        EnvironmentVariableName: "PRODUCT_THEME",
        AlternateThemeValue: "midnight",
        DefaultTheme: "default",
        AlternateTheme: "alternate",
        ResourceKeyPrefix: "Product");

    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("other", "default")]
    [InlineData("midnight", "alternate")]
    [InlineData("MIDNIGHT", "alternate")]
    public void Resolve_interprets_the_named_environment_value_case_insensitively(
        string? configuredValue,
        string expectedTheme)
    {
        string? requestedVariable = null;

        var resolved = Plan.Resolve(variableName =>
        {
            requestedVariable = variableName;
            return configuredValue;
        });

        requestedVariable.Should().Be("PRODUCT_THEME");
        resolved.Should().Be(expectedTheme);
    }

    [Fact]
    public void Apply_sets_active_theme_before_materializing_native_resources()
    {
        var events = new List<string>();

        var resolved = Plan.Apply(
            _ => "MIDNIGHT",
            theme => events.Add("active:" + theme),
            (theme, prefix) => events.Add($"native:{theme}:{prefix}"));

        resolved.Should().Be("alternate");
        events.Should().Equal(
            "active:alternate",
            "native:alternate:Product");
    }

    [Fact]
    public void Wpf_startup_runner_delegates_theme_policy_to_the_portable_plan()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "WpfApplicationStartupRunner.cs"));

        source.Should().Contain("ApplicationThemeStartupPlan<TTheme> Plan");
        source.Should().Contain("Plan.Apply(");
        source.Should().NotContain("getEnvironmentVariable(EnvironmentVariableName)");
    }
}
