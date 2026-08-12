using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionClipboardSessionTests
{
    [Fact]
    public void ClipboardReadFailure_UsesSharedFeedbackPlannerMessage()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        session.ActiveSheet.SetCell(session.ActiveCell, new TextValue("owned"));
        _ = session.TryCopySelectedRangeText();

        var result = session.PasteClipboardTextAtActiveCell(null, clipboardReadFailed: true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(ClipboardFeedbackPlanner.ReadFailed.FallbackText);
    }

    [Fact]
    public void CopyResultMarker_AuthorizesInternalPasteDespiteRacyTextProjection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var sheet = session.ActiveSheet;
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 4);
        sheet.SetCell(source, new TextValue("owned"));
        session.SelectCell(source);
        var copy = session.TryCopySelectedRangeText();
        session.SelectCell(destination);

        var result = session.PasteClipboardTextAtActiveCell(
            "older projection",
            clipboardReadFailed: true,
            clipboardMarker: copy.ClipboardMarker);

        copy.ClipboardMarker.Should().MatchRegex("^[0-9a-f]{32}$");
        result.Success.Should().BeTrue();
        sheet.GetValue(destination).Should().Be(new TextValue("owned"));
        session.HasPendingClipboardMarquee.Should().BeTrue("copy remains reusable after paste");
    }

    [Fact]
    public void ChangedTextAndMarker_PastesExternalTextAndInvalidatesSession()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var sheet = session.ActiveSheet;
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(source, new TextValue("owned"));
        session.SelectCell(source);
        var copy = session.TryCopySelectedRangeText();
        session.SelectCell(destination);

        var result = session.PasteClipboardTextAtActiveCell(
            "external",
            clipboardMarker: "different-session");

        copy.ClipboardMarker.Should().NotBeNull();
        result.Success.Should().BeTrue();
        sheet.GetValue(destination).Should().Be(new TextValue("external"));
        session.HasPendingClipboardMarquee.Should().BeFalse();
    }
}
