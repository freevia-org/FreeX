using System.IO;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class MailMergeFindRecipientParityTests
{
    [Fact]
    public void FindRecipientPlanner_wraps_from_current_record_and_reports_the_found_record()
    {
        var data = MergeData.FromCsv("Name,City\nAda,London\nGrace,New York");

        var result = MailMergeFindRecipientPlanner.Find(data, "ada", startIndex: 1);

        result.Found.Should().BeTrue();
        result.Index.Should().Be(0);
        result.Message.Should().Be("Found recipient 1 of 2.");
    }

    [Fact]
    public void FindRecipientPlanner_preserves_the_start_record_when_no_match_exists()
    {
        var data = MergeData.FromCsv("Name,City\nAda,London\nGrace,New York");

        var result = MailMergeFindRecipientPlanner.Find(data, "Tokyo", startIndex: 1);

        result.Found.Should().BeFalse();
        result.Index.Should().Be(1);
        result.Message.Should().Be("No recipient contains \"Tokyo\".");
    }

    [Fact]
    public void Avalonia_find_recipient_matches_WPF_modal_feedback_contract()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain(
            "ValidateMailMergeOperationAsync(MailMergeOperation.FindRecipient)");
        source.Should().Contain("await FreeWInfoDialog.ShowAsync(this, result.Message);");
        source.Should().Contain("await FreeWInfoDialog.ShowAsync(this, validation.Message);");
        source.Should().NotContain("_status.Text = result.Message;");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return Path.Combine([directory, .. parts]);
    }
}
