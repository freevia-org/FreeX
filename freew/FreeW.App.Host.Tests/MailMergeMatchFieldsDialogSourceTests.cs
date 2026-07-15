using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeMatchFieldsDialogSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesMatchFieldsDialogPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var dialogSource = ExtractMatchFieldsDialogSource(source);

        dialogSource.Should().Contain("MailMergeMatchFieldsDialogPlanner.GetRolePlans(");
        dialogSource.Should().Contain("MailMergeMatchFieldsDialogPlanner.GetColumnChoices(");
        dialogSource.Should().Contain("MailMergeMatchFieldsDialogPlanner.CreateResult(");
        dialogSource.Should().NotContain("private static readonly FieldRole[] AllRoles");
        dialogSource.Should().NotContain("private static readonly Dictionary<FieldRole, string> RoleLabels");
        dialogSource.Should().NotContain("Text = RoleLabels.TryGetValue(");
        dialogSource.Should().NotContain("header.Contains(mapped, StringComparer.OrdinalIgnoreCase)");
        dialogSource.Should().NotContain("var mapping = new FieldMapping();");
        dialogSource.Should().NotContain("sel == \"(not matched)\"");
    }

    private static string ExtractMatchFieldsDialogSource(string source)
    {
        var start = source.IndexOf("private static class MatchFieldsDialog", StringComparison.Ordinal);
        var end = source.IndexOf("private static class FilterSortRecipientsDialog", StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);

        return source[start..end];
    }

}
