namespace Free.Shared.AppServices.Tests;

public sealed class SisterAppPackagingSmokeTests
{
    [Fact]
    public void TryRun_WhenArgumentIsAbsent_DoesNotExecuteOrWrite()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var executed = false;

        var handled = SisterAppPackagingSmoke.TryRun(
            ["document.fx"],
            output,
            error,
            _ =>
            {
                executed = true;
                return new SisterAppPackagingSmokeResult(
                    1,
                    SisterAppPackagingSmokeOutputTarget.StandardError,
                    "unexpected");
            },
            out var exitCode);

        handled.Should().BeFalse();
        executed.Should().BeFalse();
        exitCode.Should().Be(0);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void TryRun_ExecutesBodyAndAppliesResultContract()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"packaging-smoke-{Guid.NewGuid():N}");
        var reportPath = Path.Combine(directory, "nested", "report.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();
        IReadOnlyList<string>? startupArguments = null;

        try
        {
            var handled = SisterAppPackagingSmoke.TryRun(
                ["document.fx", "--PACKAGING-SMOKE", reportPath, "--other"],
                output,
                error,
                arguments =>
                {
                    startupArguments = arguments;
                    return new SisterAppPackagingSmokeResult(
                        3,
                        SisterAppPackagingSmokeOutputTarget.StandardError,
                        "console failure\n",
                        "persisted report\n");
                },
                out var exitCode);

            handled.Should().BeTrue();
            startupArguments.Should().Equal("document.fx", reportPath, "--other");
            exitCode.Should().Be(3);
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Be("console failure\n");
            File.ReadAllText(reportPath).Should().Be("persisted report\n");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRun_ConvertsBodyExceptionThroughProductHandler()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Exception? observed = null;

        var handled = SisterAppPackagingSmoke.TryRun(
            [SisterAppPackagingSmoke.Argument],
            output,
            error,
            _ => throw new InvalidDataException("broken package"),
            exception =>
            {
                observed = exception;
                return new SisterAppPackagingSmokeResult(
                    1,
                    SisterAppPackagingSmokeOutputTarget.StandardError,
                    $"failed: {exception.Message}{Environment.NewLine}");
            },
            out var exitCode);

        handled.Should().BeTrue();
        observed.Should().BeOfType<InvalidDataException>();
        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Be("failed: broken package" + Environment.NewLine);
    }

    [Fact]
    public void TryRun_WithoutExceptionHandler_PreservesBodyException()
    {
        var action = () => SisterAppPackagingSmoke.TryRun(
            [SisterAppPackagingSmoke.Argument],
            TextWriter.Null,
            TextWriter.Null,
            _ => throw new InvalidOperationException("unexpected"),
            out _);

        action.Should().Throw<InvalidOperationException>().WithMessage("unexpected");
    }
}
