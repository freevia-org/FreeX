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
}
