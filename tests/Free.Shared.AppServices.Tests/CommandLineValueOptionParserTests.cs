namespace Free.Shared.AppServices.Tests;

public sealed class CommandLineValueOptionParserTests
{
    private static readonly CommandLineValueOptionSpec Output = new(
        "output",
        "--output",
        "output missing",
        "output blank",
        "output duplicate",
        AllowEqualsSyntax: true);

    [Fact]
    public void Parse_extracts_separate_and_inline_values_and_preserves_unrelated_arguments()
    {
        var parsed = CommandLineValueOptionParser.Parse(
            ["book.xlsx", "--OUTPUT=artifacts", "tail"],
            [Output],
            StringComparison.OrdinalIgnoreCase);

        parsed.Error.Should().BeNull();
        parsed.IsPresent("output").Should().BeTrue();
        parsed.Value("output").Should().Be("artifacts");
        parsed.RemainingArguments.Should().Equal("book.xlsx", "tail");

        CommandLineValueOptionParser.Parse(
                ["--output", "artifacts"],
                [Output])
            .Value("output").Should().Be("artifacts");
    }

    [Theory]
    [InlineData(new[] { "before", "--output" }, "output missing")]
    [InlineData(new[] { "before", "--output=" }, "output blank")]
    [InlineData(new[] { "before", "--output", "one", "--output", "two" }, "output duplicate")]
    public void Parse_reports_declared_diagnostics_and_retains_prior_unrelated_arguments(
        string[] args,
        string expectedError)
    {
        var parsed = CommandLineValueOptionParser.Parse(args, [Output]);

        parsed.Error.Should().Be(expectedError);
        parsed.RemainingArguments.Should().Equal("before");
    }

    [Fact]
    public void Parse_rejects_duplicate_keys_before_processing_arguments()
    {
        var duplicate = Output with { Name = "--other" };

        Action parse = () => CommandLineValueOptionParser.Parse([], [Output, duplicate]);

        parse.Should().Throw<ArgumentException>()
            .WithMessage("*keys must be unique*");
    }

    [Fact]
    public void ReadFirst_supports_separate_and_equals_syntax_without_filtering()
    {
        CommandLineValueOptionParser.ReadFirst(
                ["--output=result.json"],
                "--output",
                allowEqualsSyntax: true)
            .Should().Be(new CommandLineValueOption(true, "result.json"));

        CommandLineValueOptionParser.ReadFirst(["--other"], "--output")
            .Should().Be(new CommandLineValueOption(false, null));
    }
}
