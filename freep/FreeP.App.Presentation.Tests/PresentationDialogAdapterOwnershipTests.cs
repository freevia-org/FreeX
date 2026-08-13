namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDialogAdapterOwnershipTests
{
    private static readonly string[] DialogFiles =
    [
        "ChartOptionsDialogChrome.cs",
        "CustomShowDialog.cs",
        "FindReplaceDialog.cs",
        "HeaderFooterDialog.cs",
        "HyperlinkDialog.cs",
        "MotionPathEditorDialog.cs",
        "RotationOptionsDialog.cs",
        "SlideShowSettingsDialog.cs",
        "SlideSizeDialog.cs",
        "ZoomObjectPropertiesDialog.cs",
    ];

    [Fact]
    public void Dialog_form_families_share_one_portable_field_value_contract()
    {
        var root = RepositoryRoot();
        var production = ReadProductionSources(root);

        production.Should().Contain("public sealed record PresentationDialogFieldValue(");
        production.Should().NotContain("ChartOptionsDialogFieldValue");
        production.Should().NotContain("HeaderFooterDialogFieldValue");
        production.Should().NotContain("SlideShowSettingsDialogFieldValue");
    }

    [Theory]
    [InlineData("FreeP.App.Host")]
    [InlineData("FreeP.App.Avalonia")]
    public void Native_dialogs_delegate_field_binding_to_one_renderer_adapter(string project)
    {
        var root = RepositoryRoot();
        var projectDirectory = Path.Combine(root, "freep", project);
        var adapterSource = File.ReadAllText(Path.Combine(
            projectDirectory,
            "PresentationDialogControlAdapter.cs"));

        adapterSource.Should().Contain("CaptureValue(Control control)");
        adapterSource.Should().Contain("ApplyValue(Control control, PresentationDialogFieldValue value)");
        adapterSource.Should().Contain("ApplySemantic<TField>(");

        foreach (var fileName in DialogFiles)
        {
            var source = File.ReadAllText(Path.Combine(projectDirectory, fileName));
            source.Should().Contain("PresentationDialogControlAdapter.", fileName);
            source.Should().NotContain("private static void ApplySemantic(", fileName);
            source.Should().NotContain("private static PresentationDialogFieldValue CaptureValue(", fileName);
            source.Should().NotContain("private static void ApplyValue(", fileName);
        }
    }

    private static string ReadProductionSources(string root) => string.Join(
        "\n",
        Directory.EnumerateFiles(
            Path.Combine(root, "freep", "FreeP.App.Presentation"),
            "*.cs",
            SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(
            Path.Combine(root, "freep", "FreeP.App.Host"),
            "*.cs",
            SearchOption.AllDirectories))
        .Concat(Directory.EnumerateFiles(
            Path.Combine(root, "freep", "FreeP.App.Avalonia"),
            "*.cs",
            SearchOption.AllDirectories))
        .Select(File.ReadAllText));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
