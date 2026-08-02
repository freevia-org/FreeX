namespace FreeW.Core.Model.Tests;

public sealed class RunFormattingNoProofTests
{
    [Fact]
    public void NoProof_DefaultsToFalse()
    {
        RunFormatting.Default.NoProof.Should().BeFalse();
        new RunFormatting().NoProof.Should().BeFalse();
    }

    [Fact]
    public void NoProof_IsPreservedByRecordCopy()
    {
        var source = new RunFormatting { NoProof = true, Bold = true };

        var copy = source with { Bold = false };

        copy.NoProof.Should().BeTrue();
    }
}
