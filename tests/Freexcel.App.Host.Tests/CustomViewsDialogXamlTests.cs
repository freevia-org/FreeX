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
