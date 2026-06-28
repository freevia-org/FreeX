using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PasteNamesDialogSourceTests
{
    [Fact]
    public void Dialog_ExposesNameListOkPasteListAndCancelActions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PasteNamesDialog.cs");

        source.Should().Contain("Title = UiText.Get(\"PasteNames_Title\")");
        source.Should().Contain("Header = UiText.Get(\"NamedRange_Name\")");
        source.Should().Contain("Header = UiText.Get(\"NamedRange_RefersTo\")");
        source.Should().Contain("IReadOnlyList<PasteNamesItem> items");
        source.Should().Contain("nameof(PasteNamesItem.Name)");
        source.Should().Contain("nameof(PasteNamesItem.RefersTo)");
        source.Should().Contain("Content = UiText.Get(\"PasteNames_PasteList\")");
        source.Should().Contain("AcceptSelectedName()");
        source.Should().Contain("AcceptPasteList()");
        source.Should().Contain("IsCancel = true");
        source.Should().NotContain("PasteNamesDialogItem");
    }

    [Fact]
    public void Dialog_ProvidesStableAutomationIds()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PasteNamesDialog.cs");

        source.Should().Contain("\"PasteNamesList\"");
        source.Should().Contain("\"PasteNamesOkButton\"");
        source.Should().Contain("\"PasteNamesPasteListButton\"");
        source.Should().Contain("\"PasteNamesCancelButton\"");
    }
}
