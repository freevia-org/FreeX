namespace FreeW.Core.Model.Tests;

public sealed class RemovePersonalInformationModelTests
{
    [Fact]
    public void RemovePersonalInformation_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.RemovePersonalInformation.Should().BeFalse();

        document.RemovePersonalInformation = true;

        document.RemovePersonalInformation.Should().BeTrue();
    }
}
