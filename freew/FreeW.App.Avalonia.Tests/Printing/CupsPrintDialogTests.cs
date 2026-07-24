namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class CupsPrintDialogTests
{
    [Fact]
    public void Dialog_has_explicit_cancel_and_escape_lifecycle_hooks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "CupsPrintDialog.cs"));

        source.Should().Contain("cancel.Click += (_, _) => Close();");
        source.Should().Contain("Opened += (_, _) => _ok.Focus();");
        source.Should().Contain("if (args.Key != Key.Escape)");
        source.Should().Contain("args.Handled = true;");
    }
}
