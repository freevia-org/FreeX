namespace FreeW.Core.Model.Tests;

public sealed class UpdateFieldsOnOpenModelTests
{
    [Fact]
    public void UpdateFieldsOnOpen_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.UpdateFieldsOnOpen.Should().BeFalse();

        document.UpdateFieldsOnOpen = true;

        document.UpdateFieldsOnOpen.Should().BeTrue();
    }
}
