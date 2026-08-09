namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class CupsPrintDialogTests
{
    [Fact]
    public void Shared_fixed_collation_ignores_control_state()
    {
        var collation = Free.Shared.Shell.Avalonia.AvaloniaPrintDialogCollation.Fixed(true);

        collation.Resolve(selectedValue: false).Should().BeTrue();
    }

    [Fact]
    public void Dialog_delegates_chrome_submission_and_lifecycle_to_shared_workflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var appSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "CupsPrintDialog.cs"));
        var sharedSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaPrintDialogWorkflow.cs"));

        appSource.Should().Contain("CupsPrintDialog : FreeWDialogWindow");
        appSource.Should().Contain("AvaloniaPrintDialogWorkflow.ShowAsync");
        appSource.Should().Contain("Collation = AvaloniaPrintDialogCollation.Fixed(true)");
        appSource.Should().Contain("ApplyCompactActionButtonChrome = true");
        appSource.Should().NotContain("PrintDialogSession");
        appSource.Should().NotContain("new ComboBox");
        appSource.Should().NotContain("FocusInvalidField");

        sharedSource.Should().Contain("PrintDialogSession.Start");
        sharedSource.Should().Contain("session.Submit");
        sharedSource.Should().Contain("PrintDialogSession.RangeVisibility");
        sharedSource.Should().Contain("PrintDialogText.DefaultEnglish");
        sharedSource.Should().Contain("AvaloniaPrintDialogCollation.Fixed(true)");
        sharedSource.Should().Contain("FocusInvalidField");
        sharedSource.Should().Contain("controls.Cancel.Click += (_, _) => dialog.Close();");
        sharedSource.Should().Contain("dialog.Opened += (_, _) => controls.Submit.Focus();");
        sharedSource.Should().Contain("if (args.Key != Key.Escape)");
        sharedSource.Should().Contain("args.Handled = true;");
        sharedSource.Should().NotContain("PrintSelectionPlanner.Build");
        sharedSource.Should().NotContain("int.TryParse");
    }
}
