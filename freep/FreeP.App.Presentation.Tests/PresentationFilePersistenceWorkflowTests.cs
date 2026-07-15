using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFilePersistenceWorkflowTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.PresentationFilePersistenceWorkflowTests", Guid.NewGuid().ToString("N"));

    public PresentationFilePersistenceWorkflowTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Theory]
    [InlineData("deck.pptx", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.PPTX", PresentationFilePersistenceFormat.PowerPoint)]
    [InlineData("deck.fxp", PresentationFilePersistenceFormat.LegacyFxp)]
    [InlineData("deck.FXP", PresentationFilePersistenceFormat.LegacyFxp)]
    [InlineData("deck", PresentationFilePersistenceFormat.PowerPoint)]
    public void ResolveFormat_UsesLegacyFxpOnlyForFxpExtension(
        string path,
        PresentationFilePersistenceFormat expected) =>
        PresentationFilePersistenceWorkflow.ResolveFormat(path).Should().Be(expected);

    [Theory]
    [InlineData("deck.pptx", true)]
    [InlineData("deck.fxp", true)]
    [InlineData("deck.pdf", false)]
    [InlineData("deck", false)]
    public void IsSupportedPresentationPath_IsRestrictedToOpenablePresentationFiles(string path, bool expected) =>
        PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(path).Should().Be(expected);

    [Fact]
    public void Open_LoadsPptxAndMarksDocumentSavedAtSourcePath()
    {
        var path = WritePptx("Opened.pptx", "Quarterly Review");

        var result = PresentationFilePersistenceWorkflow.Open(path);

        result.Presentation.Properties.Title.Should().Be("Quarterly Review");
        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
    }

    [Fact]
    public void Open_LoadsLegacyFxpAndMarksDocumentSavedAtSourcePath()
    {
        var path = WriteFxp("Legacy.fxp", "Legacy Review");

        var result = PresentationFilePersistenceWorkflow.Open(path);

        result.Presentation.Properties.Title.Should().Be("Legacy Review");
        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
    }

    [Fact]
    public void Save_WritesPptxAtomicallyAndReturnsSavedPathMetadata()
    {
        var path = Path.Combine(_tempDir, "Saved.pptx");

        var result = PresentationFilePersistenceWorkflow.Save(path, CreatePresentation("Saved Deck"));

        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("Saved Deck");
    }

    [Fact]
    public void Save_WritesLegacyFxpAtomicallyAndReturnsSavedPathMetadata()
    {
        var path = Path.Combine(_tempDir, "Saved.fxp");

        var result = PresentationFilePersistenceWorkflow.Save(path, CreatePresentation("Saved Legacy"));

        result.SavedPath.Should().Be(path);
        result.SuppressRecentFiles.Should().BeFalse();
        FxpFormat.Read(path).Properties.Title.Should().Be("Saved Legacy");
    }

    [Fact]
    public void WorkflowOwnsAtomicWritePolicyForBothFormats()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Presentation",
            "PresentationFilePersistenceWorkflow.cs"));

        source.Should().Contain("ExportAtomicWriter.WriteAllBytes(path, SerializePresentation(path, presentation));");
        source.Should().Contain("FxpFormat.Serialize(presentation)");
        source.Should().Contain("PptxPackageWriter.Write(presentation, stream)");
        source.Should().NotContain("FxpFormat.Write(");
        source.Should().NotContain("File.Create(");
    }

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    private string WriteFxp(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        FxpFormat.Write(CreatePresentation(title), path);
        return path;
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }

}
