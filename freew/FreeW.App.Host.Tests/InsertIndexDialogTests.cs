using FreeW.App.Host;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Tests;

public sealed class InsertIndexDialogTests
{
    [StaFact]
    public void Dialog_BlankIdentifierBuildsDefaultIndexResult()
    {
        var dialog = InsertIndexDialog.CreateForTest();

        dialog.AcceptForTest();

        dialog.ResultForTest.Should().NotBeNull();
        dialog.ResultForTest!.Identifier.Should().BeNull();
    }

    [StaFact]
    public void Dialog_TrimsOptionalIdentifier()
    {
        var dialog = InsertIndexDialog.CreateForTest();
        dialog.SetIdentifierForTest(" People ");

        dialog.AcceptForTest();

        dialog.ResultForTest.Should().BeEquivalentTo(new { Identifier = "People" });
    }

    [StaFact]
    public void UpdateDialog_UsesUpdateChromeAndTrimsOptionalIdentifier()
    {
        var dialog = InsertIndexDialog.CreateForUpdateTest(" People ");

        dialog.Title.Should().Be(InsertIndexDialogPlanner.UpdateTitle);
        dialog.ActionLabelForTest.Should().Be(InsertIndexDialogPlanner.UpdateButtonLabel);
        dialog.AcceptForTest();

        dialog.ResultForTest.Should().BeEquivalentTo(new { Identifier = "People" });
    }
}
