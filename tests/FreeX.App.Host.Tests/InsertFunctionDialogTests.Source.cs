using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class InsertFunctionDialogTests
{
    [Fact]
    public void InsertFunctionDialogOpenedFromKeyboard_FocusesSearchBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => { RefreshList(); FocusInitialKeyboardTarget(); };");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_searchBox.Focus();");
        source.Should().Contain("_searchBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_searchBox);");
    }

    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"InsertFunction_OrSelectACategory\")");
        source.Should().Contain("Target = _categoryBox");
        source.Should().Contain("Content = UiText.Get(\"InsertFunction_SearchForAFunction\")");
        source.Should().Contain("Target = _searchBox");
        source.Should().Contain("Content = UiText.Get(\"InsertFunction_SelectAFunction\")");
        source.Should().Contain("Target = _listBox");
        source.Should().Contain("Content = UiText.Get(\"InsertFunction_HelpOnThisFunction\")");
        source.Should().Contain("ShowFunctionHelp");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
    }

    [Fact]
    public void InsertFunctionDialog_FunctionListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("using System.Windows.Automation;");
        source.Should().Contain("AutomationProperties.SetName(_listBox, UiText.Get(\"InsertFunction_Functions\"));");
    }

    [Fact]
    public void InsertFunctionDialog_FunctionListDoubleClickInvokesOkAndHandlesMouseEvent()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("_listBox.MouseDoubleClick += ListBox_MouseDoubleClick;");
        source.Should().Contain("private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("Ok_Click(sender, e);");
        source.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void DialogCommands_ExposeOnlyOkAsTheDefaultAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("var ok = new Button { Content = UiText.Ok, Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };");
        source.Should().Contain("var go = new Button { Content = UiText.Get(\"InsertFunction_Go\"), Width = 64, Height = 24, Margin = new Thickness(0, 0, 0, 6) };");
        source.Should().NotContain("Content = \"_Go\", Width = 64, Height = 24, Margin = new Thickness(0, 0, 0, 6), IsDefault = true");
    }

    [Fact]
    public void Dialog_ExposesExcelLikeSearchResultsAndHelpAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "InsertFunctionDialog.cs"));

        source.Should().Contain("UiText.Get(\"InsertFunction_SearchForAFunction\")");
        source.Should().Contain("UiText.Get(\"InsertFunction_OrSelectACategory\")");
        source.Should().Contain("MostRecentlyUsedCategory");
        source.Should().Contain("_categoryBox.SelectedItem = MostRecentlyUsedCategory");
        source.Should().Contain("UiText.Get(\"InsertFunction_Go\")");
        source.Should().Contain("UiText.Get(\"InsertFunction_SelectAFunction\")");
        source.Should().Contain("UiText.Get(\"InsertFunction_FormulaSyntaxAndHelp\")");
        source.Should().Contain("UiText.Get(\"InsertFunction_HelpOnThisFunction\")");
        source.Should().Contain("FunctionArgumentsDialog");
        source.Should().Contain("argumentsDialog.ResultFormula");
    }
}
