namespace FreeW.Core.Model.Tests;

public sealed class DoNotDisplayPageBoundariesModelTests
{
    [Fact]
    public void SettingDefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.DoNotDisplayPageBoundaries.Should().BeFalse();

        document.DoNotDisplayPageBoundaries = true;

        document.DoNotDisplayPageBoundaries.Should().BeTrue();
    }
}
