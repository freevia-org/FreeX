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
        // DialogButtonRowFactory was extracted into Free.Shared.Shell, where it resolves its
        // localized button labels and accessibility text through ShellStrings.Current. In FreeX,
        // ShellStrings.Current is FreeXShellStrings, which simply delegates to the host's UiText
        // catalog, so the localized text still originates from UiText.
        var source = DialogSourceTestSupport.ReadShellSources("DialogButtonRowFactory.cs");

        source.Should().Contain("ResolveDefaultAcceptContent(acceptContent)");
        source.Should().Contain("? ShellStrings.Current.Ok");
        source.Should().Contain("var cancelContent = ShellStrings.Current.Cancel;");
        source.Should().Contain("ShellStrings.Current.CreateAutomationName(resolvedAcceptContent)");
        source.Should().Contain("SetAcceleratorKey(ok, resolvedAcceptContent);");
        source.Should().Contain("ShellStrings.Current.CreateAutomationName(cancelContent)");
        source.Should().Contain("SetAcceleratorKey(cancel, cancelContent);");
    }

    [Fact]
    public void SharedDialogMessageHelper_TitlesResolveThroughShellStrings()
    {
        // DialogMessageHelper routes through the shared WPF message realizer, where default
        // message-box titles resolve through ShellStrings.Current (which delegates to UiText in FreeX).
        var dialogSource = DialogSourceTestSupport.ReadShellSources("DialogMessageHelper.cs");
        var source = DialogSourceTestSupport.ReadShellSources("WpfMessageBoxRealizer.cs");

        dialogSource.Should().Contain("WpfMessageBoxRealizer.Show(");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultErrorTitle, ShellStrings.Current.ErrorTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultWarningTitle, ShellStrings.Current.WarningTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultInformationTitle, ShellStrings.Current.InformationTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultConfirmTitle, ShellStrings.Current.ConfirmTitle)");
    }

    [Fact]
    public void SharedWpfUserMessageService_TitlesResolveThroughShellStrings()
    {
        var serviceSource = DialogSourceTestSupport.ReadShellSources("WpfUserMessageService.cs");
        var source = DialogSourceTestSupport.ReadShellSources("WpfMessageBoxRealizer.cs");

        serviceSource.Should().Contain("WpfMessageBoxRealizer.Show(");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultErrorTitle, ShellStrings.Current.ErrorTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultWarningTitle, ShellStrings.Current.WarningTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultInformationTitle, ShellStrings.Current.InformationTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultConfirmTitle, ShellStrings.Current.ConfirmTitle)");
    }

    [Fact]
    public void SharedWpfMessageHelpers_CentralizeRawMessageBoxRendering()
    {
        var realizerSource = DialogSourceTestSupport.ReadShellSources("WpfMessageBoxRealizer.cs");

        realizerSource.Should().Contain("MessageBox.Show(");
        foreach (var sourceFile in new[]
        {
            "DialogMessageHelper.cs",
            "FileCommandMessageBox.cs",
            "WpfUserMessageService.cs"
        })
        {
            var source = DialogSourceTestSupport.ReadShellSources(sourceFile);
            source.Should().Contain("WpfMessageBoxRealizer.Show(");
            source.Should().NotContain("MessageBox.Show(");
        }
    }
}
