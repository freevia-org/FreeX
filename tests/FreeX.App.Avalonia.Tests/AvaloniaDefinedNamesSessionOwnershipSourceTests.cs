using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaDefinedNamesSessionOwnershipSourceTests
{
    [Fact]
    public void DefinedNamesRenderer_DelegatesPortableBehaviorToPresentationSession()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.DefinedNames.cs");

        source.Should().Contain("new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id)");
        source.Should().Contain("definedNames.ProjectRows(filter)");
        source.Should().Contain("definedNames.ValidateDraft(draft, seed?.Identity)");
        source.Should().Contain("definedNames.PlanSave(draft, seed?.Identity)");
        source.Should().Contain("definedNames.BuildDeleteCommand(row)");
        source.Should().Contain("definedNames.BuildCreateCommands(planned)");

        source.Should().NotContain("DefinedNamesShellGlue");
        source.Should().NotContain("DefinedNameValidator.Validate(");
        source.Should().NotContain("DefinedNameDraft.ValidateRefersTo(");
        source.Should().NotContain("WorkbookReferenceNavigator.TryParseReferenceRange(");
        source.Should().NotContain(".NamedRanges.Keys");
        source.Should().NotContain(".NamedFormulas.Keys");
        source.Should().NotContain(".ScopedNamedRanges");
        source.Should().NotContain(".ScopedNamedFormulas");
        source.Should().NotContain("new DefineNamedRangeCommand");
        source.Should().NotContain("new DefineNamedFormulaCommand");
        source.Should().NotContain("new RemoveNamedRangeCommand");

        File.Exists(RepoFile("src", "FreeX.App.Avalonia", "Dialogs", "DefinedNamesShellGlue.cs"))
            .Should().BeFalse();
    }

    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FreeX.slnx")))
            current = current.Parent;

        current.Should().NotBeNull();
        return Path.Combine([current!.FullName, .. parts]);
    }
}
