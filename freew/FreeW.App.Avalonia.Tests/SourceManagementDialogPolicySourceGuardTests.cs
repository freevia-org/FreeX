using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class SourceManagementDialogPolicySourceGuardTests
{
    [Fact]
    public void ReferencesDialogs_DelegatesSourceAuthorPolicyToPresentationPlanner()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().Contain("SourceManagementDialogPlanner.BuildSourceTypeChoices(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildEntryFieldPlans(");
        source.Should().Contain("SourceManagementDialogPlanner.CreateEntry(");
        source.Should().Contain("_fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),");
        source.Should().Contain("_initialEntry);");
    }

    [Fact]
    public void ReferencesDialogs_DoesNotOwnSourceAuthorParsingPolicy()
    {
        var source = ReadReferencesDialogsSource();

        source.Should().NotContain(".Split(';')");
        source.Should().NotContain("PersonalAuthors =");
        source.Should().NotContain("CorporateAuthor =");
        source.Should().NotContain("SourceAuthorPerson.Create(");
    }

    private static string ReadReferencesDialogsSource()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Avalonia",
            "ReferencesDialogs.cs");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
