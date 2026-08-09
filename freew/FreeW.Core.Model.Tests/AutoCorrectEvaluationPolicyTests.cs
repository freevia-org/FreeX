using FluentAssertions;
using Xunit;

namespace FreeW.Core.Model.Tests;

public sealed class AutoCorrectEvaluationPolicyTests
{
    [Fact]
    public void Evaluate_PrefersAutoCorrectTableOverAutoFormat()
    {
        var options = AutoCorrectOptions.AllOff;
        options.ReplaceText = true;
        options.Replacements = [new AutoCorrectReplacement("teh", "authority")];

        var result = AutoCorrectEvaluationPolicy.Evaluate(
            "teh",
            ' ',
            options,
            new AutoFormatOptions());

        result.Applies.Should().BeTrue();
        result.Insert.Should().Be("authority ");
    }

    [Fact]
    public void Evaluate_FallsBackToAutoFormatWhenAutoCorrectDoesNotApply()
    {
        var result = AutoCorrectEvaluationPolicy.Evaluate(
            "word-",
            '-',
            new AutoCorrectOptions(),
            new AutoFormatOptions());

        result.Applies.Should().BeTrue();
        result.Insert.Should().NotBe("-");
    }
}
