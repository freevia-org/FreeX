namespace FreeW.Core.Model.Tests;

public sealed class HideSpellingErrorsModelTests
{
    [Fact]
    public void HideSpellingErrors_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.HideSpellingErrors.Should().BeFalse();

        document.HideSpellingErrors = true;

        document.HideSpellingErrors.Should().BeTrue();
    }
}
