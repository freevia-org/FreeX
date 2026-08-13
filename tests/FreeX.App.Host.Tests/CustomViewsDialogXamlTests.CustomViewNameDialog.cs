using FluentAssertions;
using FreeX.App.Presentation.CustomViews;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    [Fact]
    public void CustomViewNameDialog_ExposesKeyboardAccessKeys()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewNameDialog.cs");

        source.Should().Contain("new Label { Content = UiText.Get(\"CustomViewName_NameLabel\")");
        source.Should().Contain("Target = _nameBox");
        source.Should().Contain("Content = UiText.Get(\"CustomViewName_PrintSettingsCheckBox\")");
        source.Should().Contain("Content = UiText.Get(\"CustomViewName_HiddenFilterSettingsCheckBox\")");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
        UiText.Get("CustomViewName_NameLabel").Should().Be("_Name:");
    }

    [Fact]
    public void CustomViewNameDialog_FieldsExposeAutomationMetadata()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewNameDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_nameBox, UiText.Get(\"CustomViewName_NameAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_nameBox, \"CustomViewNameBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_nameBox, UiText.Get(\"CustomViewName_NameHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_printSettingsBox, UiText.Get(\"CustomViewName_PrintSettingsAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_printSettingsBox, \"CustomViewPrintSettingsCheckBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_printSettingsBox, UiText.Get(\"CustomViewName_PrintSettingsHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_hiddenFilterSettingsBox, UiText.Get(\"CustomViewName_HiddenFilterSettingsAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_hiddenFilterSettingsBox, \"CustomViewHiddenFilterSettingsCheckBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_hiddenFilterSettingsBox, UiText.Get(\"CustomViewName_HiddenFilterSettingsHelpText\"));");
        UiText.Get("CustomViewName_NameAutomationName").Should().Be("Custom view name");
    }

    [Fact]
    public void CustomViewNameDialog_CreateResult_TrimsViewName()
    {
        CustomViewNameDialog.CreateResult("  Quarter Close  ", includePrintSettings: false, includeHiddenRowsColumnsAndFilterSettings: true)
            .Should()
            .Be(new CustomViewsPlanner.NameSubmission("Quarter Close", IncludePrintSettings: false, IncludeHiddenRowsColumnsAndFilterSettings: true));

        var source = DialogSourceTestSupport.ReadHostSources("CustomViewNameDialog.cs");
        source.Should().Contain("public CustomViewsPlanner.NameSubmission Result");
        source.Should().NotContain("CustomViewNameDialogResult");
    }

    [Fact]
    public void CustomViewNameDialogOpenedFromKeyboard_FocusesNameBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewNameDialog.cs");
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }

    [Fact]
    public void CustomViewNameDialogBlankName_WarnsAndFocusesNameBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewNameDialog.cs");
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"CustomViewName_BlankNameMessage\"), Title);");
        dialogSource.Should().Contain("FocusNameInput();");
        dialogSource.Should().Contain("private void FocusNameInput()");
        dialogSource.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }
}
