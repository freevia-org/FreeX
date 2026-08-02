namespace FreeW.Core.Model.Tests;

public sealed class AutomaticallyUpdateStylesFromTemplateModelTests
{
    [Fact]
    public void SettingDefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.AutomaticallyUpdateStylesFromTemplate.Should().BeFalse();

        document.AutomaticallyUpdateStylesFromTemplate = true;

        document.AutomaticallyUpdateStylesFromTemplate.Should().BeTrue();
    }
}
