using FreeW.App.Host;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class MarkIndexEntryDialogTests
{
    [StaFact]
    public void Dialog_DefaultsToCurrentPageAndSeedsMainEntry()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest("  Animals  ");

        dialog.CrossReferenceEnabledForTest.Should().BeFalse();
        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark("Animals"));
    }

    [StaFact]
    public void Dialog_CarriesSubentryAndCrossReferenceThroughSharedPlanner()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest(" Animals ", " Cats ", true, " See Pet care ");

        dialog.CrossReferenceEnabledForTest.Should().BeTrue();
        dialog.AcceptForTest().Should().BeTrue();
        dialog.ResultForTest!.Mark.Should().Be(new IndexMark("Animals", "Cats", "See Pet care"));
    }

    [StaFact]
    public void Dialog_BlankCrossReferenceStaysOpenWithoutResult()
    {
        var dialog = MarkIndexEntryDialog.CreateForTest();
        dialog.SetForTest("Transportation", null, true, "  ");

        dialog.AcceptForTest().Should().BeFalse();
        dialog.ResultForTest.Should().BeNull();
    }
}
