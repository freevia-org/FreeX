using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class CellBordersDialogPlannerTests
{
    [Fact]
    public void PresetsPreserveWpfAuthorityOrderAndDistinctSelectionSemantics()
    {
        CellBordersDialogPlanner.Presets.Select(preset => preset.Label).Should().Equal(
            "All", "Outside", "Inside", "Top", "Bottom", "Left", "Right", "None");
        CellBordersDialogPlanner.Presets[0].Edges.Should().Be(CellBorderEdges.All);
        CellBordersDialogPlanner.Presets[1].Edges.Should().Be(CellBorderEdges.Outside);
        CellBordersDialogPlanner.Presets[2].Edges.Should().Be(CellBorderEdges.Inside);
        CellBordersDialogPlanner.Presets[^1].ClearEdges.Should().BeTrue();
    }

    [Fact]
    public void SubmissionProjectsPresetStyleColorAndCultureAwareWidth()
    {
        CellBordersDialogPlanner.TryBuildResult(
                new CellBordersDialogInput(
                    PresetIndex: 1,
                    StyleIndex: 2,
                    ColorIndex: 3,
                    WidthText: "1,5"),
                CultureInfo.GetCultureInfo("fr-FR"),
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().Be(new CellBordersDialogResult(
            CellBorderEdges.Outside,
            BorderLineStyle.Dashed,
            "#008000",
            1.5,
            ClearEdges: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("wide")]
    public void SubmissionRejectsInvalidWidths(string width)
    {
        CellBordersDialogPlanner.TryBuildResult(
                new CellBordersDialogInput(0, 0, 0, width),
                CultureInfo.InvariantCulture,
                out _,
                out var validation)
            .Should().BeFalse();
        validation.Should().Be(CellBordersDialogPlanner.WidthValidationMessage);
    }

    [Fact]
    public void NoneIsAnAcceptedClearOperationRatherThanCancellation()
    {
        CellBordersDialogPlanner.TryBuildResult(
                new CellBordersDialogInput(
                    CellBordersDialogPlanner.Presets.Count - 1,
                    0,
                    0,
                    "0.5"),
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result.ClearEdges.Should().BeTrue();
        result.Edges.Should().Be(CellBorderEdges.All);
    }

    [Fact]
    public void BothRenderersConsumeTheSharedDialogAndSelectionPolicies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));

        var wpfCommands = Read("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaCommands = Read("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var wpfEditor = Read("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaEditor = Read("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var wpfDialog = Read("freew", "FreeW.App.Host", "CellBordersDialog.cs");
        var avaloniaDialog = Read("freew", "FreeW.App.Avalonia", "CellBordersDialog.cs");

        wpfCommands.Should().Contain("CellBordersDialog.Prompt(Window.GetWindow(editor))")
            .And.NotContain("ShowBordersDialog(");
        avaloniaCommands.Should().Contain("Borders: OptionalHostCommand(callbacks.OpenCellBordersDialog)")
            .And.NotContain("Borders: EmptyRibbonCommand.Instance");
        foreach (var editor in new[] { wpfEditor, avaloniaEditor })
        {
            editor.Should().Contain("TableEdits.BorderEditsInRange(")
                .And.NotContain("ResolveEdgesForMergedCell(")
                .And.NotContain("ResolveEdgesForCell(");
        }
        foreach (var dialog in new[] { wpfDialog, avaloniaDialog })
            dialog.Should().Contain("CellBordersDialogPlanner.TryBuildResult(");
    }
}
