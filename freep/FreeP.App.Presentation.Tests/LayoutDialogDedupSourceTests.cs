namespace FreeP.App.Compositor.Tests;

public sealed class LayoutDialogDedupSourceTests
{
    [Fact]
    public void HeaderFooterRenderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources("HeaderFooterDialog.cs"))
        {
            source.Should().Contain("new HeaderFooterDialogSession(editor, focus)");
            source.Should().Contain("HeaderFooterDialogSession.CreateInput(");
            source.Should().Contain("_session.TryApply(ReadInput(), scope)");
            source.Should().NotContain("HeaderFooterCommandPlanner.BuildState(");
            source.Should().NotContain("HeaderFooterCommandPlanner.BuildDefaultOptions(");
            source.Should().NotContain("HeaderFooterCommandPlanner.TryApply(");
            source.Should().NotContain("new HeaderFooterApplyOptions(");
            source.Should().NotContain("DateFormatOptions.FirstOrDefault");
        }
    }

    [Fact]
    public void SlideSizeRenderers_DelegatePortableWorkflowToSharedSession()
    {
        foreach (var source in RendererSources("SlideSizeDialog.cs"))
        {
            source.Should().Contain("new SlideSizeDialogSession(editor)");
            source.Should().Contain("_session.SelectPreset(");
            source.Should().Contain("_session.ChangeUnit(");
            source.Should().Contain("_session.TryApply(");
            source.Should().NotContain("SlideSizeDialogPlanner.");
            source.Should().NotContain("ToPresetIndex");
            source.Should().NotContain("PresetFromIndex");
            source.Should().NotContain("private SlideSizeDialogUnit _unit");
            source.Should().NotContain("private readonly EditingSession _editor");
        }
    }

    private static IEnumerable<string> RendererSources(string fileName)
    {
        yield return ReadWorkspaceFile("freep", "FreeP.App.Host", fileName);
        yield return ReadWorkspaceFile("freep", "FreeP.App.Avalonia", fileName);
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);

            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate workspace file.",
            Path.Combine(relativeParts));
    }
}
