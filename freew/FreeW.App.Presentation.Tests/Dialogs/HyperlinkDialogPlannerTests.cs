using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class HyperlinkDialogPlannerTests
{
    [Theory]
    [InlineData(HyperlinkDialogMode.Insert, "Insert Hyperlink")]
    [InlineData(HyperlinkDialogMode.Edit, "Edit Hyperlink")]
    public void Build_projects_mode_and_initial_values(HyperlinkDialogMode mode, string expectedTitle)
    {
        var presentation = HyperlinkDialogPlanner.Build(mode, "Visible", "example.test");

        presentation.Mode.Should().Be(mode);
        presentation.Title.Should().Be(expectedTitle);
        presentation.InitialDisplayText.Should().Be("Visible");
        presentation.InitialAddress.Should().Be("example.test");
        presentation.DisplayLabel.Should().NotBeNullOrWhiteSpace();
        presentation.AddressLabel.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("  Visible text  ", " example.test ", "Visible text", "https://example.test", false)]
    [InlineData("Jump", " #Target ", "Jump", "#Target", true)]
    [InlineData("Mail", "team@example.test", "Mail", "mailto:team@example.test", false)]
    public void PlanAcceptance_normalizes_the_shared_result(
        string display,
        string address,
        string expectedDisplay,
        string expectedAddress,
        bool internalTarget)
    {
        var acceptance = HyperlinkDialogPlanner.PlanAcceptance(display, address);

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.DisplayText.Should().Be(expectedDisplay);
        acceptance.Address.Should().Be(expectedAddress);
        acceptance.Target.IsInternal.Should().Be(internalTarget);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("not a valid host")]
    public void PlanAcceptance_rejects_missing_or_invalid_targets(string? address)
    {
        HyperlinkDialogPlanner.PlanAcceptance("Visible", address).IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void Both_renderers_project_the_shared_contract_and_the_route_is_paired()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpfDialog = Read(root, "freew", "FreeW.App.Host", "HyperlinkDialog.cs");
        var wpfCommands = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var wpfEditor = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaDialog = Read(root, "freew", "FreeW.App.Avalonia", "InsertDialogs.cs");
        var avaloniaHost = Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var catalog = Read(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        wpfDialog.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("HyperlinkDialogPlanner.Build(")
            .And.Contain("HyperlinkDialogPlanner.PlanAcceptance(");
        avaloniaDialog.Should().Contain(": FreeWDialogWindow")
            .And.Contain("HyperlinkDialogPlanner.Build(")
            .And.Contain("HyperlinkDialogPlanner.PlanAcceptance(");
        wpfCommands.Should().Contain("HyperlinkDialog.Ask(")
            .And.Contain("editor.InsertHyperlink(accepted.DisplayText, accepted.Address)")
            .And.Contain("editor.EditHyperlink(accepted.Address, accepted.DisplayText)")
            .And.NotContain("var url = HyperlinkPrompt.Ask(Window.GetWindow(editor), seed, dialogText.Title");
        wpfEditor.Should().Contain("HyperlinkTarget.TryParse(target, out var parsedTarget)")
            .And.Contain("public string? HyperlinkTargetAtCaret()")
            .And.Contain("public string? HyperlinkDisplayTextAtCaret()")
            .And.Contain("private IReadOnlyList<WpfHyperlink> HyperlinkSpanAtCaret()")
            .And.Contain("SameLogicalHyperlink(previous, current)");
        avaloniaHost.Should().Contain("initialDisplay: _editor.HyperlinkDisplayTextAtCaret()")
            .And.Contain("initialAddress: _editor.HyperlinkTargetAtCaret()");
        catalog.Should().Contain("Pair(\"hyperlink\", \"HyperlinkDialog\")")
            .And.NotContain("AvaloniaOnly(\"hyperlink\"");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([root, .. relativeParts]));
}
