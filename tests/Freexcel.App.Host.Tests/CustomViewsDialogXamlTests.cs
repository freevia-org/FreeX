using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CustomViewsDialogXamlTests
{
    [Fact]
    public void DialogList_ExposesAccessibleName()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));

        xaml.Should().Contain("AutomationProperties.Name=\"Custom views\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Shows saved workbook views");
    }

    [Fact]
    public void ShowButton_IsDefaultDialogAction()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "ShowButton");

        showButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }

    [Fact]
    public void Dialog_ExposesKeyboardAccessKeys()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GroupBox")
            .Single()
            .Attribute("Header")?.Value.Should().Be("_Views");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Show", "_Add...", "_Delete", "_Close"]);
    }

    [Fact]
    public void DialogList_ShowsPrintAndFilterSettingIndicators()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GridViewColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["Print settings", "Hidden rows, columns and filter settings"]);
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesViewsList()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));
        var dialogSource = source[
            source.IndexOf("public sealed partial class CustomViewsDialog", StringComparison.Ordinal)..
            source.IndexOf("internal sealed class CustomViewViewModel", StringComparison.Ordinal)];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("ViewsList.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(ViewsList);");
    }

    [Fact]
    public void DialogCommandFailure_FocusesViewsList()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));
        var dialogSource = source[
            source.IndexOf("public sealed partial class CustomViewsDialog", StringComparison.Ordinal)..
            source.IndexOf("internal sealed class CustomViewViewModel", StringComparison.Ordinal)];

        dialogSource.Should().Contain("FocusViewsList();");
        dialogSource.Should().Contain("private void FocusViewsList()");
        dialogSource.Split("FocusViewsList();").Length.Should().BeGreaterThanOrEqualTo(5);
        dialogSource.Should().Contain("ViewsList.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(ViewsList);");
    }

    [Fact]
    public void DialogSelectionGuards_FocusViewsListWhenNoViewIsSelected()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));

        source.Should().Contain("if (ViewsList.SelectedItem is not CustomViewViewModel vm) { FocusViewsList(); return; }");
    }

    [Fact]
    public void CustomViewNameDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));

        source.Should().Contain("new Label { Content = \"_Name:\"");
        source.Should().Contain("Target = _nameBox");
        source.Should().Contain("Content = \"_Print settings\"");
        source.Should().Contain("Content = \"_Hidden rows, columns and filter settings\"");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void CustomViewNameDialog_CreateResult_TrimsViewName()
    {
        CustomViewNameDialog.CreateResult("  Quarter Close  ", includePrintSettings: false, includeHiddenRowsColumnsAndFilterSettings: true)
            .Should()
            .Be(new CustomViewNameDialogResult("Quarter Close", IncludePrintSettings: false, IncludeHiddenRowsColumnsAndFilterSettings: true));
    }

    [Fact]
    public void CustomViewNameDialogOpenedFromKeyboard_FocusesNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("_nameBox.Focus();");
        dialogSource.Should().Contain("_nameBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_nameBox);");
    }

    [Fact]
    public void CustomViewNameDialogBlankName_WarnsAndFocusesNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("MessageBox.Show(this, \"Enter a view name.\", Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
        dialogSource.Should().Contain("FocusNameInput();");
        dialogSource.Should().Contain("private void FocusNameInput()");
        dialogSource.Should().Contain("_nameBox.Focus();");
        dialogSource.Should().Contain("_nameBox.SelectAll();");
        dialogSource.Should().Contain("Keyboard.Focus(_nameBox);");
    }

    [Fact]
    public void CustomViewsDialog_ThreadsAddViewIncludeOptionsIntoCommandAndIndicators()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));

        source.Should().Contain("dialog.Result.IncludePrintSettings");
        source.Should().Contain("dialog.Result.IncludeHiddenRowsColumnsAndFilterSettings");
        source.Should().Contain("new SaveCustomViewCommand(");
        source.Should().Contain("view.IncludePrintSettings ? \"Included\" : \"Not included\"");
        source.Should().Contain("view.IncludeHiddenRowsColumnsAndFilterSettings ? \"Included\" : \"Not included\"");
    }
}
