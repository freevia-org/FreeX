namespace Free.Shared.AppServices.Tests;

public sealed class ResourceAndFileOutcomeContractTests
{
    [Fact]
    public void ResourceTextDescriptor_OwnsLocalizedFallbackResolution()
    {
        var descriptor = new ResourceTextDescriptor("Common_Open", "_Open");

        descriptor.Resolve(key => key).Should().Be("_Open");
        descriptor.Resolve(key => $"[[{key}]]").Should().Be("_Open");
        descriptor.Resolve(_ => "_Ouvrir", stripMnemonics: true).Should().Be("Ouvrir");
    }

    [Theory]
    [InlineData(null, FileDialogSelectionStatus.Cancelled, false)]
    [InlineData("", FileDialogSelectionStatus.Cancelled, false)]
    [InlineData("   ", FileDialogSelectionStatus.Cancelled, false)]
    [InlineData("C:\\Docs\\Letter.docx", FileDialogSelectionStatus.Chosen, true)]
    public void FileDialogSelection_ClassifiesPathOutcome(
        string? path,
        FileDialogSelectionStatus expectedStatus,
        bool expectedChosen)
    {
        var selection = new FileDialogSelection(path);

        selection.Status.Should().Be(expectedStatus);
        selection.Chosen.Should().Be(expectedChosen);
    }

    [Fact]
    public void FileDialogResult_DelegatesSelectionSemanticsToSharedContract()
    {
        FileDialogResult.Cancelled.Selection.Should().Be(FileDialogSelection.Cancelled);
        FileDialogResult.Cancelled.Chosen.Should().BeFalse();

        var result = new FileDialogResult("C:\\Docs\\Letter.docx");
        result.Selection.Status.Should().Be(FileDialogSelectionStatus.Chosen);
        result.Chosen.Should().BeTrue();
    }
}
