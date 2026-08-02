using System.IO;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia.Tests;

public sealed class MailMergeCheckErrorsParityTests
{
    [Fact]
    public void CheckForErrorsPlanner_exposes_the_WPF_choices_in_order()
    {
        var choices = MailMergeCheckForErrorsPlanner.GetChoices();

        choices.Select(choice => choice.Mode).Should().Equal(
            MailMergeCheckForErrorsMode.SimulateAndReport,
            MailMergeCheckForErrorsMode.CompleteAndPause,
            MailMergeCheckForErrorsMode.CompleteWithoutPausing);
        choices.Select(choice => choice.Label).Should().Equal(
            "Simulate the merge and report errors in a new document",
            "Complete the merge, pausing to report each error",
            "Complete the merge without pausing");
    }

    [Fact]
    public void CheckForErrorsPlanner_uses_the_WPF_default_for_an_invalid_selection()
    {
        MailMergeCheckForErrorsPlanner.GetMode(-1).Should()
            .Be(MailMergeCheckForErrorsMode.SimulateAndReport);
        MailMergeCheckForErrorsPlanner.GetMode(99).Should()
            .Be(MailMergeCheckForErrorsMode.SimulateAndReport);
    }

    [Fact]
    public void Avalonia_check_for_errors_matches_WPF_feedback_cancel_and_focus_contract()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain(
            "Select recipients first (Mailings > Select Recipients), then check for errors.");
        source.Should().Contain("if (mode is not { } selected)\n            return;");
        source.Should().Contain(
            "var result = _mailMerge.CheckForErrors(selected);");
        source.Should().Contain(
            "await FreeWInfoDialog.ShowAsync(this, result.Message);");
        source.Should().NotContain(
            "_status.Text = $\"Mail merge error check selected: {selected}.\";");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return Path.Combine([directory, .. parts]);
    }
}
