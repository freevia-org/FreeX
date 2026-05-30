using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CommonMessageTextTests
{
    [Fact]
    public void UiText_LoadsNeutralCommonButtonAndMessageTitleStrings()
    {
        UiText.GetNeutralResourceKeys()
            .Should()
            .Contain([
                "Common_Ok",
                "Common_Cancel",
                "Common_ErrorTitle",
                "Common_WarningTitle",
                "Common_InformationTitle",
                "Common_ConfirmTitle"
            ]);

        UiText.Ok.Should().Be("_OK");
        UiText.Cancel.Should().Be("_Cancel");
        UiText.ErrorTitle.Should().Be("Error");
        UiText.WarningTitle.Should().Be("Warning");
        UiText.InformationTitle.Should().Be("Information");
        UiText.ConfirmTitle.Should().Be("Confirm");
    }

    [Fact]
    public void DialogButtonRowFactory_DefaultButtonsResolveContentAndAccessibilityTextThroughUiText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DialogButtonRowFactory.cs"));

        source.Should().Contain("ResolveDefaultAcceptContent(acceptContent)");
        source.Should().Contain("? UiText.Ok");
        source.Should().Contain("var cancelContent = UiText.Cancel;");
        source.Should().Contain("UiText.CreateAutomationName(resolvedAcceptContent)");
        source.Should().Contain("SetAcceleratorKey(ok, resolvedAcceptContent);");
        source.Should().Contain("UiText.CreateAutomationName(cancelContent)");
        source.Should().Contain("SetAcceleratorKey(cancel, cancelContent);");
    }

    [Theory]
    [InlineData("DialogMessageHelper.cs")]
    [InlineData("WpfUserMessageService.cs")]
    public void DefaultMessageBoxTitlesResolveThroughUiText(string fileName)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));

        source.Should().Contain("ResolveDefaultTitle(title, DefaultErrorTitle, UiText.ErrorTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultWarningTitle, UiText.WarningTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultInformationTitle, UiText.InformationTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultConfirmTitle, UiText.ConfirmTitle)");
    }
}
