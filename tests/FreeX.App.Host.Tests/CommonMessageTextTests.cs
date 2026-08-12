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
        // ShellStrings.Current is a ResourceShellStrings adapter over the host's UiText catalog,
        // so the localized text still originates from UiText.
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

        serviceSource.Should().Contain("DialogMessageHelper.ShowMessage(");
        serviceSource.Should().Contain("Application.Current?.MainWindow");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultErrorTitle, ShellStrings.Current.ErrorTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultWarningTitle, ShellStrings.Current.WarningTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultInformationTitle, ShellStrings.Current.InformationTitle)");
        source.Should().Contain("ResolveDefaultTitle(title, DefaultConfirmTitle, ShellStrings.Current.ConfirmTitle)");
    }

    [Fact]
    public void SharedWpfMessageHelpers_CentralizeRawMessageBoxRendering()
    {
        var realizerSource = DialogSourceTestSupport.ReadShellSources("WpfMessageBoxRealizer.cs");
        var dialogSource = DialogSourceTestSupport.ReadShellSources("DialogMessageHelper.cs");
        var serviceSource = DialogSourceTestSupport.ReadShellSources("WpfUserMessageService.cs");

        realizerSource.Should().Contain("MessageBox.Show(");
        dialogSource.Should().Contain("WpfMessageBoxRealizer.Show(");
        dialogSource.Should().NotContain("MessageBox.Show(");
        serviceSource.Should().Contain("DialogMessageHelper.ShowMessage(");
        serviceSource.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void StartupPrompts_UseSharedWpfUserMessageService()
    {
        var source = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        source.Should().Contain("new WpfUserMessageService()");
        source.Should().Contain("StartupMessageService.ShowMessage(");
        source.Should().Contain("UserMessageButtons.YesNo");
        source.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void SharedDialogChromeResources_LiveInShellWpfWithRibbonCompatibilityWrapper()
    {
        var shellDialogWindow = DialogSourceTestSupport.ReadShellSources("DialogWindow.cs");
        var shellDialogResources = DialogSourceTestSupport.ReadShellSources("DialogResources.xaml");
        var ribbonDialogWindow = WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.Ribbon.Wpf", "DialogWindow.cs");

        shellDialogWindow.Should().Contain("namespace Free.Shared.Shell.Wpf;");
        shellDialogWindow.Should().Contain("/Free.Shared.Shell.Wpf;component/DialogResources.xaml");
        shellDialogResources.Should().Contain("Shared dialog control theme.");
        ribbonDialogWindow.Should().Contain("Free.Shared.Shell.Wpf.DialogWindow");
        ribbonDialogWindow.Should().NotContain("DialogResources.xaml");
    }
}
