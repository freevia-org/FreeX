using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class SourceManagementDialogPolicySourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesSourceManagementPolicyToPresentationPlanner()
    {
        var source = ReadHostRibbonSource();

        source.Should().Contain("using FreeW.App.Presentation.Ribbon;");
        source.Should().Contain("SourceManagementDialogPlanner.TryBuildCitationSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildPickerItems(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildEntryFieldPlans(");
        source.Should().Contain("SourceManagementDialogPlanner.CreateEntry(");
        source.Should().Contain("fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),");
        source.Should().Contain("entry);");
        source.Should().Contain("SourceManagementDialogPlanner.BuildInitialState(");
        source.Should().Contain("SourceManagementDialogPlanner.AddMasterSource(");
        source.Should().Contain("SourceManagementDialogPlanner.DeleteMasterSource(");
        source.Should().Contain("SourceManagementDialogPlanner.CopyMasterToCurrent(");
        source.Should().Contain("SourceManagementDialogPlanner.AddCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.EditCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.DeleteCurrentSource(");
        source.Should().Contain("SourceManagementDialogPlanner.BuildResult(");
    }

    [Fact]
    public void FreeWRibbonCommands_DoesNotOwnSourceManagementPolicy()
    {
        var source = ReadHostRibbonSource();

        source.Should().NotContain("private sealed record SourcePick");
        source.Should().NotContain("private sealed record SourceEntry");
        source.Should().NotContain("private static bool HasSourceData(");
        source.Should().NotContain("private static Source BuildSource(");
        source.Should().NotContain("private static Source CloneSource(");
        source.Should().NotContain("private static string DescribeSource(Source");
        source.Should().NotContain(".Split(';')");
        source.Should().NotContain("PersonalAuthors =");
        source.Should().NotContain("workingDoc");
        source.Should().NotContain("workingMaster");
        source.Should().NotContain("new SourceRecord");
        source.Should().NotContain("entry.Author.Length == 0 && entry.Title.Length == 0 && entry.Year.Length == 0");
        source.Should().NotContain("Any(s => s.Tag == src.Tag)");
    }

    private static string ReadHostRibbonSource()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
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
