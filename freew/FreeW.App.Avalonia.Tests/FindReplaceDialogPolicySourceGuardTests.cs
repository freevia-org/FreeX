using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FindReplaceDialogPolicySourceGuardTests
{
    [Fact]
    public void FindReplaceDialog_DelegatesOptionPolicyValidationAndResultTextToPresentationPlanner()
    {
        var source = ReadAvaloniaSource("FindReplaceDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FindReplaceDialogPlanner.LabelFor(");
        source.Should().Contain("FindReplaceDialogPlanner.NormalizeOptions(");
        source.Should().Contain("FindReplaceDialogPlanner.IsOptionEnabled(");
        source.Should().Contain("FindReplaceDialogPlanner.TryCreateSearchRequest(");
        source.Should().Contain("FindReplaceDialogPlanner.TryCreateReplaceRequest(");
        source.Should().Contain("FindReplaceDialogPlanner.ValidationMessageFor(");
        source.Should().Contain("FindReplaceDialogPlanner.BuildFindStatus(");
        source.Should().Contain("FindReplaceDialogPlanner.BuildReplaceStatus(");
        source.Should().Contain("FindReplaceDialogPlanner.BuildReplaceAllStatus(");
        source.Should().Contain("_editor.FindNext(request.Term, request.Options)");
        source.Should().Contain("_editor.ReplaceNext(request!.Term, request.Replacement, request.Options)");
        source.Should().Contain("_editor.ReplaceAll(request!.Term, request.Replacement, request.Options)");
        source.Should().NotContain("TextSearch.FindAll(");
        source.Should().NotContain("internal static int CountMatches(");
        source.Should().NotContain("Content = \"Match case\"");
        source.Should().NotContain("Content = \"Whole word\"");
        source.Should().NotContain("Content = \"Use wildcards");
        source.Should().NotContain("\"Enter a search term.\"");
        source.Should().NotContain("not found.\"");
        source.Should().NotContain("Replaced {count}");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
