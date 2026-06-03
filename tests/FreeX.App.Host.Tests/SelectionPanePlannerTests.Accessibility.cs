using FluentAssertions;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    [Fact]
    public void SelectionPaneDialog_ExposesShowAllAndHideAllBulkButtons()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_showAllButton");
        source.Should().Contain("_hideAllButton");
        source.Should().Contain("SetAllVisibility(true)");
        source.Should().Contain("SetAllVisibility(false)");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("Content = UiText.Get(\"SelectionPane_BringForwardButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_SendBackwardButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_ShowAllButton\")");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_HideAllButton\")");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
    }

    [Fact]
    public void SelectionPaneDialog_ExposesSearchFilterRenameAndEyeLikeVisibilityAffordances()
    {
        var source = ReadSelectionPaneDialogSources();

        source.Should().Contain("_searchBox");
        source.Should().Contain("_filterBox");
        source.Should().Contain("_renameBox");
        source.Should().Contain("Content = UiText.Get(\"SelectionPane_NameLabel\")");
        source.Should().Contain("_renameButton");
        source.Should().Contain("_toggleVisibilityButton");
        source.Should().Contain("CreateEyeIcon()");
        source.Should().NotContain("Content = \"Eye\"");
        source.Should().Contain("ApplySearchAndFilter");
        source.Should().Contain("RenameSelectedItem");
        source.Should().Contain("ToggleSelectedVisibility");
        source.Should().Contain("ToolTip = UiText.Get(\"SelectionPane_ToggleVisibilityToolTip\")");
    }

    [Fact]
    public void SelectionPaneDialog_ObjectListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.cs"));

        foreach (var key in new[]
        {
            "SelectionPane_ObjectListAutomationName",
            "SelectionPane_ObjectListHelpText",
            "SelectionPane_SearchAutomationName",
            "SelectionPane_SearchHelpText",
            "SelectionPane_FilterAutomationName",
            "SelectionPane_FilterHelpText",
            "SelectionPane_ObjectNameAutomationName",
            "SelectionPane_ObjectNameHelpText",
            "SelectionPane_RenameButtonAutomationName",
            "SelectionPane_RenameButtonHelpText",
            "SelectionPane_ToggleVisibilityAutomationName",
            "SelectionPane_ToggleVisibilityHelpText",
            "SelectionPane_BringForwardAutomationName",
            "SelectionPane_BringForwardHelpText",
            "SelectionPane_SendBackwardAutomationName",
            "SelectionPane_SendBackwardHelpText",
            "SelectionPane_ShowAllAutomationName",
            "SelectionPane_ShowAllHelpText",
            "SelectionPane_HideAllAutomationName",
            "SelectionPane_HideAllHelpText",
            "SelectionPane_OkAutomationName",
            "SelectionPane_OkHelpText",
            "SelectionPane_CancelAutomationName",
            "SelectionPane_CancelHelpText",
            "SelectionPane_ItemVisibilityAutomationName",
            "SelectionPane_ItemVisibilityHelpText"
        })
        {
            source.Should().Contain($"UiText.Get(\"{key}\")");
        }
    }

    [Fact]
    public void SelectionPaneDialog_ExposesStableAutomationIds()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SelectionPaneDialog.cs"));

        foreach (var expected in new[]
        {
            "AutomationProperties.SetAutomationId(this, \"SelectionPaneDialog\")",
            "AutomationProperties.SetAutomationId(_list, \"SelectionPaneObjectList\")",
            "AutomationProperties.SetAutomationId(_searchBox, \"SelectionPaneSearchBox\")",
            "AutomationProperties.SetAutomationId(_filterBox, \"SelectionPaneFilterBox\")",
            "AutomationProperties.SetAutomationId(_renameBox, \"SelectionPaneRenameBox\")",
            "AutomationProperties.SetAutomationId(_renameButton, \"SelectionPaneRenameButton\")",
            "AutomationProperties.SetAutomationId(_toggleVisibilityButton, \"SelectionPaneToggleVisibilityButton\")",
            "AutomationProperties.SetAutomationId(_moveUpButton, \"SelectionPaneBringForwardButton\")",
            "AutomationProperties.SetAutomationId(_moveDownButton, \"SelectionPaneSendBackwardButton\")",
            "AutomationProperties.SetAutomationId(_showAllButton, \"SelectionPaneShowAllButton\")",
            "AutomationProperties.SetAutomationId(_hideAllButton, \"SelectionPaneHideAllButton\")",
            "AutomationProperties.SetAutomationId(okButton, \"SelectionPaneOkButton\")",
            "AutomationProperties.SetAutomationId(cancelButton, \"SelectionPaneCancelButton\")",
            "AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.AutomationId))",
            "AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.VisibilityAutomationId))",
            "AutomationProperties.AutomationIdProperty, new System.Windows.Data.Binding(nameof(SelectionPaneDialogItem.NameAutomationId))"
        })
        {
            source.Should().Contain(expected);
        }
    }

    [Fact]
    public void SelectionPaneDialogItem_AutomationIdsIncludeKindAndObjectId()
    {
        var id = Guid.Parse("01890def-56ab-4cde-9234-0123456789ab");
        var item = new SelectionPaneItem(
            SelectionPaneObjectKind.Picture,
            id,
            "Picture 1",
            IsVisible: true,
            CanMoveUp: true,
            CanMoveDown: true);

        var dialogItem = new SelectionPaneDialogItem(item);

        dialogItem.AutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789ab");
        dialogItem.VisibilityAutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789abVisibilityBox");
        dialogItem.NameAutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789abNameBox");
    }

    [Fact]
    public void SelectionPaneDialog_ObjectListHelpTextDocumentsKeyboardShortcuts()
    {
        var resources = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "Resources",
            "Strings.resx"));

        resources.Should().Contain("Ctrl+Up or Ctrl+Down");
        resources.Should().Contain("Press F2 to rename");
        resources.Should().Contain("Space to show or hide");
    }
}
