using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogVisualParitySourceTests
{
    [Fact]
    public void FindReplaceDialog_UsesWpfColumnAndResultSurfaceMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog);");
        source.Should().Contain("var findBox = new TextBox { Text = _session.LastFindText, MinWidth = 260 };");
        source.Should().Contain("findFormatButton.Margin = new Thickness(8, 0, 0, 0);");
        source.Should().Contain("findChooseFormatButton.Margin = new Thickness(6, 0, 0, 0);");
        source.Should().Contain("dialog.Opened += (_, _) => resultsList.Background = Brush(242, 242, 242);");
    }

    [Fact]
    public void GoToSpecialDialog_UsesCompactRowsAndBottomDockedActions()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("numbersBox.Opacity = enabled ? 1 : 0.7;");
        source.Should().Contain("Margin = new Thickness(8, 0, 8, 0),");
        source.Should().Contain("Margin = new Thickness(0, 0, 12, 1),");
        source.Should().Contain("var root = new DockPanel { Margin = new Thickness(0) };");
        source.Should().Contain("DockPanel.SetDock(buttonRow, Dock.Bottom);");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
