namespace FreeW.Core.Model.Tests;

public sealed class HideGrammaticalErrorsModelTests
{
    [Fact]
    public void HideGrammaticalErrors_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.HideGrammaticalErrors.Should().BeFalse();

        document.HideGrammaticalErrors = true;

        document.HideGrammaticalErrors.Should().BeTrue();
    }
}
