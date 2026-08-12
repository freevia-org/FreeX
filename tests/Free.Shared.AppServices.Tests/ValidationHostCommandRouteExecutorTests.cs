namespace Free.Shared.AppServices.Tests;

public sealed class ValidationHostCommandRouteExecutorTests
{
    private sealed record TestOptions(string Value);

    [Fact]
    public void Run_PassesRemainingArgumentsToLaterRoutesAndReturnsHandledExitCode()
    {
        var error = new StringWriter();
        IReadOnlyList<string>? secondRouteArguments = null;

        var exitCode = ValidationHostCommandRouteExecutor.Run(
            ["--first", "document.fx"],
            error,
            "Expected a command.",
            _ => ValidationHostCommandRouteResult.NotMatched(["document.fx"]),
            arguments =>
            {
                secondRouteArguments = arguments;
                return ValidationHostCommandRouteResult.Handled(7);
            },
            _ => throw new InvalidOperationException("Routing should stop after a match."));

        exitCode.Should().Be(7);
        secondRouteArguments.Should().Equal("document.fx");
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Run_WhenRouteIsInvalid_ReportsItsErrorAndStops()
    {
        var error = new StringWriter();

        var exitCode = ValidationHostCommandRouteExecutor.Run(
            [],
            error,
            "Expected a command.",
            _ => ValidationHostCommandRouteResult.Invalid("Invalid value."),
            _ => throw new InvalidOperationException("Routing should stop after a parse error."));

        exitCode.Should().Be(ValidationHostCommandRouteExecutor.UsageErrorExitCode);
        error.ToString().Should().Be("Invalid value." + Environment.NewLine);
    }

    [Fact]
    public void Run_WhenNoRouteMatches_ReportsProductUsage()
    {
        var error = new StringWriter();

        var exitCode = ValidationHostCommandRouteExecutor.Run(
            ["document.fx"],
            error,
            "Expected a validation command.",
            arguments => ValidationHostCommandRouteResult.NotMatched(arguments));

        exitCode.Should().Be(ValidationHostCommandRouteExecutor.UsageErrorExitCode);
        error.ToString().Should().Be("Expected a validation command." + Environment.NewLine);
    }

    [Fact]
    public void Immediate_AdaptsTryRunCommand()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var route = ValidationHostCommandRouteExecutor.Immediate(
            (IReadOnlyList<string> arguments, TextWriter _, TextWriter _, out int exitCode) =>
            {
                exitCode = 4;
                return arguments.Contains("--run");
            },
            output,
            error);

        ValidationHostCommandRouteExecutor.Run(
                ["document.fx"],
                error,
                "Expected command.",
                route,
                _ => ValidationHostCommandRouteResult.Handled(5))
            .Should().Be(5);
        ValidationHostCommandRouteExecutor.Run(
                ["--run"],
                error,
                "Expected command.",
                route)
            .Should().Be(4);
    }

    [Fact]
    public void Parsed_AdaptsParserAndRunsMatchedOptions()
    {
        IReadOnlyList<string>? runArguments = null;
        var route = ValidationHostCommandRouteExecutor.Parsed<TestOptions>(
            (IReadOnlyList<string> arguments,
                out TestOptions? options,
                out string[] remainingArguments,
                out string? error) =>
            {
                options = arguments.Contains("--test") ? new TestOptions("matched") : null;
                remainingArguments = ["document.fx"];
                error = null;
                return true;
            },
            (options, arguments) =>
            {
                options.Value.Should().Be("matched");
                runArguments = arguments;
                return 6;
            });

        var exitCode = ValidationHostCommandRouteExecutor.Run(
            ["--test", "document.fx"],
            new StringWriter(),
            "Expected command.",
            route);

        exitCode.Should().Be(6);
        runArguments.Should().Equal("document.fx");
    }
}
