using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMnemonicLabelSourceTests
{
    [Fact]
    public void CustomCheckAndRadioTemplates_RenderAccessKeys()
    {
        var source = File.ReadAllText(RepoFile(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaCompactDialogChrome.cs"));

        source.Split("RecognizesAccessKey = true", StringSplitOptions.None).Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void MnemonicDialogLabels_TargetTheirEditors()
    {
        var comments = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Comments.cs"));
        var textToColumns = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TextToColumns.cs"));

        comments.Should().Contain("Target = rootBox");
        comments.Should().Contain("Target = replyBox");
        comments.Should().Contain("Target = selector");
        comments.Should().Contain("Target = selectedReplyBox");
        comments.Should().NotContain("new TextBlock { Text = UiText.Get(\"ThreadedComment_");
        textToColumns.Should().Contain("Target = decimalSeparatorBox");
        textToColumns.Should().Contain("Target = thousandsSeparatorBox");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
