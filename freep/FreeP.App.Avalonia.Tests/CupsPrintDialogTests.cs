namespace FreeP.App.Avalonia.Tests;

public sealed class CupsPrintDialogTests
{
    [Fact]
    public void Shared_selectable_collation_uses_control_state()
    {
        var collation = Free.Shared.Shell.Avalonia.AvaloniaPrintDialogCollation.Selectable;

        collation.Resolve(selectedValue: false).Should().BeFalse();
        collation.Resolve(selectedValue: true).Should().BeTrue();
    }

    [Fact]
    public void PortableDialog_supplies_product_options_to_shared_print_workflow()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var appSource = File.ReadAllText(Path.Combine(
            repo,
            "freep",
            "FreeP.App.Avalonia",
            "Printing",
            "CupsPrintDialog.cs"));
        var sharedSource = File.ReadAllText(Path.Combine(
            repo,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaPrintDialogWorkflow.cs"));

        appSource.Should().Contain("CupsPrintDialog : FreePDialogWindow");
        appSource.Should().Contain("AvaloniaPrintDialogWorkflow.ShowAsync");
        appSource.Should().Contain("LayoutSummary = layoutSummary");
        appSource.Should().Contain("Collation = AvaloniaPrintDialogCollation.Selectable");
        appSource.Should().Contain("ApplyCompactActionButtonChrome = true");
        appSource.Should().Contain("FreePPortablePrinterPicker");
        appSource.Should().Contain("FreePPortablePrintCopies");
        appSource.Should().Contain("FreePPortablePrintPageRange");
        appSource.Should().Contain("FreePPortablePrintOrientation");
        appSource.Should().Contain("FreePPortablePrintCollation");
        appSource.Should().Contain("FreePPortablePrintSubmit");
        appSource.Should().NotContain("PrintDialogSession");
        appSource.Should().NotContain("new ComboBox");
        appSource.Should().NotContain("FocusInvalidField");
        appSource.Should().NotContain("CupsPrintDialog : Window");

        sharedSource.Should().Contain("PrintDialogSession.Start");
        sharedSource.Should().Contain("session.Submit");
        sharedSource.Should().Contain("PrintDialogSession.RangeVisibility");
        sharedSource.Should().Contain("options.Collation.Resolve");
        sharedSource.Should().Contain("options.LayoutSummary");
        sharedSource.Should().Contain("ApplyAutomationId");
        sharedSource.Should().Contain("FocusInvalidField");
        sharedSource.Should().Contain("controls.Cancel.Click += (_, _) => dialog.Close();");
        sharedSource.Should().NotContain("PrintSelectionPlanner.Build");
        sharedSource.Should().NotContain("int.TryParse");
    }
}
