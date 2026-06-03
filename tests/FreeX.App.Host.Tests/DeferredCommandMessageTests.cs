using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class DeferredCommandMessageTests
{
    [Fact]
    public void WorkbookThemeMessage_NamesDeferredThemeModel()
    {
        var message = DeferredCommandMessages.WorkbookTheme("Themes");

        message.Title.Should().Be("Themes");
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("workbook theme model");
        message.Body.Should().Contain("documented parity gap");
    }

    [Theory]
    [InlineData("View Side by Side")]
    [InlineData("Synchronous Scrolling")]
    [InlineData("Reset Window Position")]
    public void MultiWindowMessage_NamesDeferredWindowHosting(string commandName)
    {
        var message = DeferredCommandMessages.MultiWindow(commandName);

        message.Title.Should().Be(commandName);
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("multi-window workbook hosting");
        message.Body.Should().Contain("documented parity gap");
    }

    [Fact]
    public void OnlineTemplatesMessage_NamesExternalMicrosoftServiceExclusion()
    {
        var message = DeferredCommandMessages.OnlineTemplatesExcluded();

        message.Title.Should().Be("Online Templates");
        message.Body.Should().Contain("excluded");
        message.Body.Should().Contain("external Microsoft template service");
    }

    [Fact]
    public void AccountMessage_NamesLocalAccountDecision()
    {
        var message = DeferredCommandMessages.LocalAccountInfo();

        message.Title.Should().Be("Account");
        message.Body.Should().Contain("Microsoft account integration");
        message.Body.Should().Contain("not implemented");
        message.Body.Should().Contain("local files");
        message.Body.Should().Contain("Options");
    }

    [Fact]
    public void PivotTableMessage_NamesModelFirstPivotSupport()
    {
        var message = DeferredCommandMessages.PivotTableModelFirst();

        message.Title.Should().Be("PivotTable");
        message.Body.Should().Contain("loads and saves PivotTable");
        message.Body.Should().Contain("pivot caches");
        message.Body.Should().Contain("preserves native PivotTable package parts");
        message.Body.Should().Contain("Field List");
        message.Body.Should().Contain("slicer/timeline");
        message.Body.Should().Contain("remain partial");
    }

    [Fact]
    public void OptionsSecondaryMessages_NameHonestUnsupportedBoundaries()
    {
        DeferredCommandMessages.AutoCorrectOptions().Body.Should().Contain("AutoCorrect replacement dictionaries");
        DeferredCommandMessages.EditingLanguages().Body.Should().Contain("language packs");
        DeferredCommandMessages.RibbonCustomizationImportExport().Body.Should().Contain("Custom Ribbon UI");
        DeferredCommandMessages.OfficeAddIns().Body.Should().Contain("not installed, loaded, or executed");
        DeferredCommandMessages.TrustCenterSettings().Body.Should().Contain("does not execute VBA macros");
    }
}
