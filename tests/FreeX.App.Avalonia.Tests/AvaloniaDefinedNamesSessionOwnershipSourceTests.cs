using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;

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
        source.Should().Contain("DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get)");

        source.Should().NotContain("DefinedNamesShellGlue");
        source.Should().NotContain("DefinedNameValidator.Validate(");
        source.Should().NotContain("DefinedNameError.Blank =>");
        source.Should().NotContain("InsertLoc_NameErrorBlank");
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

    [Theory]
    [InlineData(DefinedNameError.Blank)]
    [InlineData(DefinedNameError.InvalidFirstCharacter)]
    [InlineData(DefinedNameError.Duplicate)]
    public void DefinedNamesRenderer_ResolvesSharedValidationMessage(DefinedNameError error)
    {
        var method = typeof(MainWindow).GetMethod(
            "DescribeNameError",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, [error]).Should().Be(
            DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get));
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
