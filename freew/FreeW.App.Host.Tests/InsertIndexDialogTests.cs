using FreeW.App.Host;

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
}
