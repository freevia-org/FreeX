namespace FreeW.Core.IO.Tests;

public sealed class OdtRunFormattingOverlayTests
{
    [Fact]
    public void MergeRunFormatting_PreservesWebHiddenFromEitherLayer()
    {
        var inherited = OdtFileAdapter.MergeRunFormatting(
            new RunFormatting { WebHidden = true },
            new RunFormatting { Bold = true });
        var direct = OdtFileAdapter.MergeRunFormatting(
            RunFormatting.Default,
            new RunFormatting { WebHidden = true, Italic = true });

        inherited.WebHidden.Should().BeTrue();
        inherited.Bold.Should().BeTrue();
        direct.WebHidden.Should().BeTrue();
        direct.Italic.Should().BeTrue();
    }

    [Fact]
    public void MergeRunFormatting_PreservesInheritedNoProof()
    {
        var merged = OdtFileAdapter.MergeRunFormatting(
            new RunFormatting { NoProof = true },
            new RunFormatting { Bold = true });

        merged.NoProof.Should().BeTrue();
        merged.Bold.Should().BeTrue();
    }

    [Fact]
    public void MergeRunFormatting_AppliesOverlayNoProof()
    {
        var merged = OdtFileAdapter.MergeRunFormatting(
            new RunFormatting { Italic = true },
            new RunFormatting { NoProof = true });

        merged.NoProof.Should().BeTrue();
        merged.Italic.Should().BeTrue();
    }
}
