using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class CellBordersDialogSessionTests
{
    [Fact]
    public void OpensWithNoPresetAndTheFirstColourSelected_AndApplyWaitsForAPreset()
    {
        var session = new CellBordersDialogSession();

        session.PresetIndex.Should().Be(-1);
        session.CanApply.Should().BeFalse();
        session.IsColorSelected(0).Should().BeTrue();
        session.IsColorSelected(1).Should().BeFalse();
        CellBordersDialogSession.DefaultWidthText.Should().Be("0.5");

        session.SelectPreset(2);
        session.SelectColor(3);

        session.CanApply.Should().BeTrue();
        session.IsColorSelected(3).Should().BeTrue();
        session.IsColorSelected(0).Should().BeFalse();
    }

    [Fact]
    public void Acceptance_ProjectsTheSessionSelectionAndRejectsABadWidthWithTheSharedMessage()
    {
        var session = new CellBordersDialogSession();
        session.SelectPreset(1);
        session.SelectColor(2);

        var accepted = session.PlanAcceptance(styleIndex: 0, "1,5", CultureInfo.GetCultureInfo("fr-FR"));

        accepted.IsAccepted.Should().BeTrue();
        accepted.Result!.Edges.Should().Be(CellBordersDialogPlanner.Presets[1].Edges);
        accepted.Result.ColorHex.Should().Be(CellBordersDialogPlanner.Palette[2]);
        accepted.Result.WidthPt.Should().Be(1.5);

        var rejected = session.PlanAcceptance(0, "99", CultureInfo.InvariantCulture);
        rejected.IsAccepted.Should().BeFalse();
        rejected.ValidationMessage.Should().Be(CellBordersDialogPlanner.WidthValidationMessage);
        session.PlanAcceptance(0, null, CultureInfo.InvariantCulture).IsAccepted.Should().BeFalse();
    }

    [Theory]
    [InlineData("FreeW.App.Host", "CellBordersDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "CellBordersDialog.cs")]
    public void Hosts_DelegateSelectionStateAndAcceptanceToTheSession(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", project, fileName));

        source.Should().Contain("CellBordersDialogSession _session = new();");
        source.Should().Contain("_session.SelectPreset(");
        source.Should().Contain("_session.SelectColor(");
        source.Should().Contain("_session.CanApply");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("private int _presetIndex");
        source.Should().NotContain("private int _colorIndex");
        source.Should().NotContain("CellBordersDialogPlanner.TryBuildResult(");
        source.Should().NotContain("\"0.5\"");
    }
}