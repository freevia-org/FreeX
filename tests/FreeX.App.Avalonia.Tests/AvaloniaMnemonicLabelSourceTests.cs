using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMnemonicLabelSourceTests
{
    [Fact]
    public void CustomCheckAndRadioTemplates_RenderAccessKeys()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "DialogControlStyles.cs"));

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

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
