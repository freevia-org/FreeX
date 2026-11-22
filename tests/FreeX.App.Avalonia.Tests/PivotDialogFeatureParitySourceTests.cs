using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Source contracts for the WPF PivotTable-dialog features the Avalonia shell carries: each test pins the
/// control, its WPF resource key, and the shared planner/session call that gives the control its behavior.
/// </summary>
public sealed class PivotDialogFeatureParitySourceTests
{
    [Fact]
    public void CreatePivotTableDialog_OffersAndHonoursTheFieldPaneCheckBox()
    {
        var source = ReadAppSource("MainWindow.PivotCreate.cs");

        source.Should().Contain("UiText.Get(\"PivotTable_OpenPivotTableFieldsPane\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(openFieldListBox, \"PivotOpenFieldListBox\");");
        source.Should().Contain("OpenFieldList: openFieldListBox.IsChecked == true));");
        source.Should().NotContain("OpenFieldList: true");
        source.Should().Contain("_pivotFieldPaneUserHidden = !outcome.Transition.RefreshFieldList;");

        // WPF order: location editor, field-pane check box, then the button row.
        source.IndexOf("CreatePivotDialogReferenceEditor(destinationPicker, destinationBox", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("                openFieldListBox,", StringComparison.Ordinal));
        source.IndexOf("                openFieldListBox,", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("                buttonRow,", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePivotTableDialog_HasNoPerColumnRoleListLikeWpfAndExcel()
    {
        var source = ReadAppSource("MainWindow.PivotCreate.cs");

        // The WPF dialog (and Excel's) only choose source and destination; the shared session proposes the
        // starting layout and the field pane refines it.
        source.Should().NotContain("PivotFieldRole");
        source.Should().NotContain("PivotLoc_Role");
        source.Should().NotContain("roleBoxes");
        source.Should().NotContain("PivotCreatePlanner.FieldRole");
        source.Should().Contain("OpenFieldList: openFieldListBox.IsChecked == true));");
    }

    [Fact]
    public void LabelAndValueFilterDialogs_HaveOnlyOkAndCancelLikeWpf_ValueFilterKeepsSummarizeBy()
    {
        var source = ReadAppSource("MainWindow.PivotFilters.cs");

        source.Should().NotContain("PivotLabelFilterClearButton");
        source.Should().NotContain("PivotValueFilterClearButton");
        source.Should().Contain("CreatePivotFilterFormContent(form, ok, cancel)");
        source.Should().NotContain("CreatePivotFilterFormContent(form, ok, clear, cancel)");

        // The measure picker is shared with the WPF dialog (Excel's Value Filter "Summarize by" row).
        source.Should().Contain("AutomationProperties.SetAutomationId(dataFieldBox, \"PivotValueFilterDataFieldBox\");");
        source.Should().Contain("UiText.Get(\"PivotFilter_SummarizeBy\")");

        var wpfXaml = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "PivotValueFilterDialog.xaml"));
        wpfXaml.Should().Contain("PivotFilter_SummarizeBy");
        wpfXaml.Should().Contain("PivotValueFilterDataFieldBox");
    }

    [Fact]
    public void CalculatedFieldAndItemDialogs_ExistingPickerAndDeleteAreSharedWithWpf()
    {
        var wpf = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "PivotCalculatedDialogs.cs"));
        var avaloniaField = ReadAppSource("MainWindow.PivotCalculatedField.cs");
        var avaloniaItem = ReadAppSource("MainWindow.PivotCalculatedItem.cs");

        foreach (var key in new[] { "PivotCalcField_ExistingLabel", "PivotCalcField_ExistingNone", "PivotCalcField_Delete",
                     "PivotCalcItem_ExistingLabel", "PivotCalcItem_ExistingNone", "PivotCalcItem_Delete" })
        {
            wpf.Should().Contain($"\"{key}\"");
        }

        wpf.Should().Contain("PivotCalculatedFieldSession.Create(pivotTable");
        wpf.Should().Contain("PivotCalculatedItemSession.Create(");
        wpf.Should().Contain("_session.SelectExisting(");
        wpf.Should().Contain("_session.PlanDelete(");
        avaloniaField.Should().Contain("calculatedFieldSession.SelectExisting(");
        avaloniaItem.Should().Contain("calculatedItemSession.SelectExisting(");
    }

    [Fact]
    public void CalculatedItemDialog_ListsTheSelectedFieldItemsAndInsertsThem()
    {
        var source = ReadAppSource("MainWindow.PivotCalculatedItem.cs");

        source.Should().Contain("PivotApplication.ReadSourceItems(pivotTarget, layoutField.SourceFieldIndex)");
        source.Should().Contain("itemNamesBySourceFieldIndex: itemNamesBySourceFieldIndex");
        source.Should().Contain("itemsList.ItemsSource = itemReferences;");
        source.Should().Contain("calculatedItemSession.ItemReferences");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableItems\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableItemsLabel\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertItem\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(itemsList, \"PivotCalcItemItemList\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(insertItemButton, \"PivotCalcItemInsertItemButton\");");
        source.Should().Contain("insertItemButton.Click += (_, _) => InsertSelectedItem();");
        source.Should().Contain("itemsList.DoubleTapped += (_, _) => InsertSelectedItem();");
        source.Should().Contain("MinHeight = 560 - WpfNoResizeClientFrameHeight,");

        var sessionSource = File.ReadAllText(RepoFile(
            "src", "FreeX.App.Presentation", "PivotUI", "PivotCalculatedItemSession.cs"));
        sessionSource.Should().Contain("BuildItemReferences(itemNamesBySourceFieldIndex)");
    }

    [Fact]
    public void GroupFieldDialog_SubmitsTheUngroupCheckBoxThroughTheSharedPlanner()
    {
        var source = ReadAppSource("MainWindow.PivotGroupField.cs");

        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_UngroupSelectedField\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(ungroupBox, \"PivotGroupUngroupBox\");");
        source.Should().Contain("grouping, ungroupBox.IsChecked == true, startBox.Text, endBox.Text, intervalBox.Text,");
        source.Should().Contain("submission!.Ungroup || selectedGrouping == PivotFieldGrouping.None");
        source.Should().NotContain("ungroup: false");

        // WPF order: Range group, ungroup check box, then the button row.
        source.IndexOf("UiText.Get(\"PivotFieldGrouping_RangeGroup\"), rangePanel", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("content.Children.Add(ungroupBox);", StringComparison.Ordinal));
        source.IndexOf("content.Children.Add(ungroupBox);", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("content.Children.Add(CreatePivotDialogButtonRow(ok, cancel));", StringComparison.Ordinal));
    }

    [Fact]
    public void PivotChartOptionsDialog_LeadsWithTheSharedChartStyleGallery()
    {
        var source = ReadAppSource("MainWindow.PivotChartOptions.cs");

        source.Should().Contain("ChartStylePlanner.GetStyleOptions()");
        source.Should().Contain("ChartStylePlanner.FindStyleOptionIndex(current.ChartStyleId)");
        source.Should().Contain("new UniformGrid { Columns = 4 }");
        source.Should().Contain("Height = 126,");
        source.Should().Contain("ApplyFieldAutomation(styleGallery, PivotChartOptionsDialogFieldId.ChartStyle);");
        source.Should().Contain("PivotChartOptionsPlanner.GetChartStyleSection().HeaderResourceKey");
        source.Should().Contain("ConfigurePivotDialogLifecycle(dialog, styleGallery);");
        source.Should().Contain("selectedStyleId,");
        source.Should().NotContain("current.ChartStyleId,");

        // WPF order: chart-style group, field-buttons group, layout group.
        source.IndexOf("GetChartStyleSection().HeaderResourceKey", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("GetFieldButtonsSection().HeaderResourceKey", StringComparison.Ordinal));
    }

    [Fact]
    public void InsertSlicerAndTimelineDialogs_UseAnEditableFieldComboAndCaptionBox()
    {
        var source = ReadAppSource("MainWindow.InsertSlicer.cs");

        source.Should().Contain("IsEditable = true,");
        source.Should().Contain("fieldBox.Text = initialField;");
        source.Should().Contain("UiText.Format(text.DefaultNameKey, initialField)");
        foreach (var key in new[]
                 {
                     "PivotSlicerTimeline_FieldToConnectLabel",
                     "PivotSlicerTimeline_SlicerCaptionLabel",
                     "PivotSlicerTimeline_DefaultSlicerName",
                     "PivotSlicerTimeline_SelectFieldToConnect",
                     "PivotSlicerTimeline_EnterSlicerCaption",
                     "PivotSlicerTimeline_DateFieldToConnectLabel",
                     "PivotSlicerTimeline_TimelineCaptionLabel",
                     "PivotSlicerTimeline_DefaultTimelineName",
                     "PivotSlicerTimeline_SelectDateFieldToConnect",
                     "PivotSlicerTimeline_EnterTimelineCaption",
                 })
        {
            source.Should().Contain($"\"{key}\"");
        }

        source.Should().Contain("MinHeight = PivotSlicerTimelineDialogContract.Height - WpfNoResizeClientFrameHeight,");
        source.Should().Contain("PivotApplication.PlanInsertSlicer(");
        source.Should().Contain("PivotApplication.PlanInsertTimeline(");
        source.Should().Contain("dialog.UpdateLayout();");
        source.Should().NotContain("new CheckBox");
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
