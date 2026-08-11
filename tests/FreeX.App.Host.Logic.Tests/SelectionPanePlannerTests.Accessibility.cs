using FluentAssertions;
using FreeX.Core.Model;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

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
    public void SelectionPaneDialog_LayoutKeepsFieldsAndActionsVisibleAtMinimumSize()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SelectionPaneDialog(
            [
                new SelectionPaneItem(SelectionPaneObjectKind.Shape, Guid.Parse("11111111-1111-1111-1111-111111111111"), "Selected rectangle", true, true, true),
                new SelectionPaneItem(SelectionPaneObjectKind.Chart, Guid.Parse("22222222-2222-2222-2222-222222222222"), "Quarterly revenue chart", true, false, false),
                new SelectionPaneItem(SelectionPaneObjectKind.Picture, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Logo picture", false, true, true),
                new SelectionPaneItem(SelectionPaneObjectKind.TextBox, Guid.Parse("44444444-4444-4444-4444-444444444444"), "Notes text box", true, true, true)
            ]);
            dialog.Width = dialog.MinWidth;
            dialog.Height = dialog.MinHeight;
            dialog.Show();

            try
            {
                dialog.UpdateLayout();

                dialog.ResizeMode.Should().Be(ResizeMode.CanResizeWithGrip);
                var root = dialog.Content.Should().BeOfType<Grid>().Subject;
                root.RowDefinitions[1].Height.GridUnitType.Should().Be(GridUnitType.Star);

                var searchBox = FindByAutomationId<TextBox>(dialog, "SelectionPaneSearchBox");
                var filterBox = FindByAutomationId<ComboBox>(dialog, "SelectionPaneFilterBox");
                var renameBox = FindByAutomationId<TextBox>(dialog, "SelectionPaneRenameBox");
                var list = FindByAutomationId<ListBox>(dialog, "SelectionPaneObjectList");

                searchBox.ActualWidth.Should().BeGreaterThan(150);
                filterBox.ActualWidth.Should().BeGreaterThan(120);
                renameBox.ActualWidth.Should().BeGreaterThan(150);
                list.ActualHeight.Should().BeGreaterThan(100);

                foreach (var automationId in new[]
                {
                    "SelectionPaneSearchBox",
                    "SelectionPaneFilterBox",
                    "SelectionPaneRenameBox",
                    "SelectionPaneRenameButton",
                    "SelectionPaneToggleVisibilityButton",
                    "SelectionPaneShowAllButton",
                    "SelectionPaneHideAllButton",
                    "SelectionPaneBringForwardButton",
                    "SelectionPaneSendBackwardButton",
                    "SelectionPaneOkButton",
                    "SelectionPaneCancelButton"
                })
                {
                    var element = FindByAutomationId<FrameworkElement>(dialog, automationId);
                    element.ActualWidth.Should().BeGreaterThan(0);
                    element.ActualHeight.Should().BeGreaterThan(0);
                    AssertInside(root, element);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SelectionPaneDialog_ObjectListExposesAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.cs");

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
        var source = DialogSourceTestSupport.ReadHostSources("SelectionPaneDialog.cs");

        foreach (var expected in new[]
        {
            "AutomationProperties.SetAutomationId(this, FreeXAutomationIdCatalog.SelectionPane.Dialog)",
            "AutomationProperties.SetAutomationId(_list, FreeXAutomationIdCatalog.SelectionPane.ObjectList)",
            "AutomationProperties.SetAutomationId(_searchBox, FreeXAutomationIdCatalog.SelectionPane.SearchBox)",
            "AutomationProperties.SetAutomationId(_filterBox, FreeXAutomationIdCatalog.SelectionPane.FilterBox)",
            "AutomationProperties.SetAutomationId(_renameBox, FreeXAutomationIdCatalog.SelectionPane.RenameBox)",
            "AutomationProperties.SetAutomationId(_renameButton, FreeXAutomationIdCatalog.SelectionPane.RenameButton)",
            "AutomationProperties.SetAutomationId(_toggleVisibilityButton, FreeXAutomationIdCatalog.SelectionPane.ToggleVisibilityButton)",
            "AutomationProperties.SetAutomationId(_moveUpButton, FreeXAutomationIdCatalog.SelectionPane.BringForwardButton)",
            "AutomationProperties.SetAutomationId(_moveDownButton, FreeXAutomationIdCatalog.SelectionPane.SendBackwardButton)",
            "AutomationProperties.SetAutomationId(_showAllButton, FreeXAutomationIdCatalog.SelectionPane.ShowAllButton)",
            "AutomationProperties.SetAutomationId(_hideAllButton, FreeXAutomationIdCatalog.SelectionPane.HideAllButton)",
            "AutomationProperties.SetAutomationId(okButton, FreeXAutomationIdCatalog.SelectionPane.OkButton)",
            "AutomationProperties.SetAutomationId(cancelButton, FreeXAutomationIdCatalog.SelectionPane.CancelButton)",
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

        var session = new SelectionPaneSession([item]);
        var dialogItem = new SelectionPaneDialogItem(session, session.Items.Single());

        dialogItem.AutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789ab");
        dialogItem.VisibilityAutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789abVisibilityBox");
        dialogItem.NameAutomationId.Should().Be("SelectionPaneItemPicture01890def56ab4cde92340123456789abNameBox");
    }

    [Fact]
    public void SelectionPaneDialog_ObjectListHelpTextDocumentsKeyboardShortcuts()
    {
        var resources = DialogSourceTestSupport.ReadLocalizationSources("Resources\\Strings.resx");

        resources.Should().Contain("Ctrl+Up or Ctrl+Down");
        resources.Should().Contain("Press F2 to rename");
        resources.Should().Contain("Space to show or hide");
    }

    private static T FindByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement =>
        WpfTestTree.FindVisualDescendants<T>(root)
            .Single(element => AutomationProperties.GetAutomationId(element) == automationId);

    private static void AssertInside(FrameworkElement root, FrameworkElement element)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

        bounds.Left.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Top.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Right.Should().BeLessThanOrEqualTo(root.ActualWidth + 0.5);
        bounds.Bottom.Should().BeLessThanOrEqualTo(root.ActualHeight + 0.5);
    }
}
