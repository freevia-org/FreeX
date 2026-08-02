namespace FreeP.App.Avalonia.Tests;

public sealed class CupsPrintDialogTests
{
    [Fact]
    public void PortableDialog_has_real_settings_controls_and_cancel_lifecycle()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            repo,
            "freep",
            "FreeP.App.Avalonia",
            "Printing",
            "CupsPrintDialog.cs"));

        source.Should().Contain("PrintSelectionPlanner.Build");
        source.Should().Contain("FreePPortablePrinterPicker");
        source.Should().Contain("FreePPortablePrintCopies");
        source.Should().Contain("FreePPortablePrintPageRange");
        source.Should().Contain("FreePPortablePrintOrientation");
        source.Should().Contain("FreePPortablePrintCollation");
        source.Should().Contain("cancel.Click += (_, _) => Close();");
        source.Should().Contain("Opened += (_, _) => _ok.Focus();");
        source.Should().Contain("if (args.Key != Key.Escape)");
        source.Should().Contain("args.Handled = true;");
    }
}
