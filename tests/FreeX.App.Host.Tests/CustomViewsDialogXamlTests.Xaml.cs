using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    [Fact]
    public void DialogList_ExposesAccessibleName()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("CustomViewsDialog.xaml");

        xaml.Should().Contain($"AutomationProperties.Name=\"{UiText.Get("CustomViews_CustomViews")}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"CustomViewsList\"");
        xaml.Should().Contain($"AutomationProperties.HelpText=\"{UiText.Get("CustomViews_ShowsSavedWorkbookViewsThatCanBeShownOrDeleted")}");
    }

    [Fact]
    public void ShowButton_IsDefaultDialogAction()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("CustomViewsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "ShowButton");

        showButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }

    [Fact]
    public void Dialog_ExposesKeyboardAccessKeys()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("CustomViewsDialog.xaml");
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
    public void DialogActionButtons_ExposeAutomationMetadata()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("CustomViewsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertButtonAutomation(
            document,
            presentation,
            UiText.Get("CustomViews_Show"),
            "CustomViewsShowButton",
            UiText.Get("CustomViews_ApplySelectedCustomView"));
        AssertButtonAutomation(
            document,
            presentation,
            UiText.Get("CustomViews_Add"),
            "CustomViewsAddButton",
            UiText.Get("CustomViews_CreateNewCustomViewFromCurrentWorkbookState"));
        AssertButtonAutomation(
            document,
            presentation,
            UiText.Get("CustomViews_Delete"),
            "CustomViewsDeleteButton",
            UiText.Get("CustomViews_DeleteSelectedCustomView"));
        AssertButtonAutomation(
            document,
            presentation,
            UiText.Get("CustomViews_Close"),
            "CustomViewsCloseButton",
            UiText.Get("CustomViews_CloseCustomViewsDialog"));

        static void AssertButtonAutomation(
            XDocument document,
            XNamespace presentation,
            string content,
            string automationId,
            string helpText)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute("Content")?.Value == content);

            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            button.Attribute("AutomationProperties.HelpText")?.Value.Should().Be(helpText);
        }
    }

    [Fact]
    public void DialogList_ShowsPrintAndFilterSettingIndicators()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("CustomViewsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GridViewColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["Print settings", "Hidden rows, columns and filter settings"]);
    }
}
