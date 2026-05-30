using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotTableDialog_CreateResult_CapturesExcelCreatePivotChoices()
    {
        var result = PivotTableDialog.CreateResult(
            "  Sales!A1:D20  ",
            PivotTableDestinationKind.ExistingWorksheet,
            "  Report!F3  ",
            openFieldList: true);

        result.SourceRangeText.Should().Be("Sales!A1:D20");
        result.DestinationKind.Should().Be(PivotTableDestinationKind.ExistingWorksheet);
        result.DestinationRangeText.Should().Be("Report!F3");
        result.OpenFieldList.Should().BeTrue();
    }

    [Fact]
    public void PivotTableDialog_DefaultResult_UsesNewWorksheetDestinationAndFieldList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);

            dialog.Result.SourceRangeText.Should().Be("Sales!A1:D20");
            dialog.Result.DestinationKind.Should().Be(PivotTableDestinationKind.NewWorksheet);
            dialog.Result.DestinationRangeText.Should().BeEmpty();
            dialog.Result.OpenFieldList.Should().BeTrue();
        });
    }

    [Fact]
    public void PivotTableDialog_ExposesReferencePickersForSourceAndExistingLocation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("AddLabeledReferenceEditor(");
        source.Should().Contain("_sourceRangeBox,");
        source.Should().Contain("_destinationRangeBox,");
        source.Should().Contain("CreateReferenceEditor(textBox, automationName, target, editorMargin)");
        source.Should().Contain("UiText.Get(\"PivotTable_SelectPivotTableSourceRange\")");
        source.Should().Contain("UiText.Get(\"PivotTable_SelectPivotTableLocation\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UpdateDestinationState");
    }

    [Fact]
    public void PivotTableDialog_ExposesOnlySupportedSourceAndPlacementChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("UiText.Get(\"PivotTable_ChooseDataHeader\")");
        source.Should().Contain("_selectTableRangeButton");
        source.Should().Contain("UiText.Get(\"PivotTable_NewWorksheet\")");
        source.Should().Contain("UiText.Get(\"PivotTable_ExistingWorksheet\")");
        source.Should().NotContain("_externalSourceButton");
        source.Should().NotContain("_dataModelBox");
        source.Should().NotContain("Use an _external data source");
        source.Should().NotContain("Add this data to the Data _Model");
    }

    [Fact]
    public void PivotTableDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"PivotTable_Create\")");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_NewWorksheet\")");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_ExistingWorksheet\")");
        source.Should().Contain("Content = UiText.Get(\"PivotTable_OpenPivotTableFieldsPane\")");
        source.Should().NotContain("Content = \"Use an _external data source\"");
        source.Should().NotContain("Content = \"Add this data to the Data _Model\"");
    }

    [Fact]
    public void PivotTableDialog_LabelsRangeEditorsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        foreach (var content in new[]
        {
            "AddLabeledReferenceEditor(",
            "UiText.Get(\"PivotTable_TableRangeLabel\")",
            "UiText.Get(\"PivotTable_LocationLabel\")",
            "_sourceRangeBox,",
            "_destinationRangeBox,",
            "new Label",
            "Target = textBox",
            "private void AddLabeledReferenceEditor",
            "CreateReferenceEditor(textBox, automationName, target, editorMargin)"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableDialog_RangeEditorsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_sourceRangeBox, UiText.Get(\"PivotTable_PivotTableSourceRange\"));");
        source.Should().Contain("AutomationProperties.SetName(_destinationRangeBox, UiText.Get(\"PivotTable_PivotTableLocation\"));");
    }

    [Fact]
    public void PivotTableDialogOpenedFromKeyboard_FocusesSourceRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_sourceRangeBox);");
    }

    [Fact]
    public void PivotTableDialogRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void UpdateDestinationState", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void PivotTableDialogInvalidRanges_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTable_EnterValidSourceRange\"), _sourceRangeBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTable_EnterDestinationCellOnActiveWorksheet\"), _destinationRangeBox);");
        source.Should().Contain("WorkbookRangeTextCodec.TryParse(_sourceSheetId, _sourceRangeBox.Text, ResolveSheetIdByName, out _)");
        source.Should().Contain("destinationRange.Start.Sheet != _sourceSheetId");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void PivotTableRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        PivotTableDialog.CreateRangeSelectionRequest(PivotTableRangeSelectionTarget.DestinationRange, " Report!F3 ")
            .Should()
            .Be(new PivotTableRangeSelectionRequest(
                PivotTableRangeSelectionTarget.DestinationRange,
                "Report!F3",
                CollapseDialog: true));
    }

    [Fact]
    public void PivotTableApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(PivotTableRangeSelectionTarget.SourceRange, "Sales!A1:E40");
                dialog.ApplyRangeSelection(PivotTableRangeSelectionTarget.DestinationRange, "Sales!H3");

                textBoxes[0].Text.Should().Be("Sales!A1:E40");
                textBoxes[1].Text.Should().Be("Sales!H3");
                textBoxes[1].IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresPivotTableRangePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        source.Should().Contain("new PivotTableDialog(");
        source.Should().Contain("request => ApplyPivotTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPivotTableRangeSelection(");
        source.Should().Contain("PivotTableRangeSelectionRequest request");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Theory]
    [InlineData("Select PivotTable source range", PivotTableRangeSelectionTarget.SourceRange, "Sales!A1:D20")]
    [InlineData("Select PivotTable location", PivotTableRangeSelectionTarget.DestinationRange, "Sales!F1")]
    public void PivotTableReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        PivotTableRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var requests = new List<PivotTableRangeSelectionRequest>();
            var dialog = new PivotTableDialog(workbook, sheet.Id, range, requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new PivotTableRangeSelectionRequest(
                    expectedTarget,
                    expectedText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PivotTableDataSourceDialog_CreateResult_TrimsSourceRangeText()
    {
        PivotTableDataSourceDialog.CreateResult("  Sales!A1:E200  ")
            .SourceRangeText
            .Should()
            .Be("Sales!A1:E200");
    }

    [Fact]
    public void PivotTableDataSourceRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        PivotTableDataSourceDialog.CreateRangeSelectionRequest(" Sales!A1:E200 ")
            .Should()
            .Be(new PivotTableDataSourceRangeSelectionRequest("Sales!A1:E200", CollapseDialog: true));
    }

    [Fact]
    public void PivotTableDataSourceDialog_ExposesReferencePickerForSourceRange()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("CreateReferenceEditor(_sourceBox");
        source.Should().Contain("UiText.Get(\"PivotTableDataSource_SelectPivotTableSourceRange\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("PivotTableDataSourceRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void PivotTableDataSourceDialog_SourceRangeEditorExposesAutomationName()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("AutomationProperties.SetName(_sourceBox, UiText.Get(\"PivotTableDataSource_PivotTableSourceRange\"));");
    }

    [Fact]
    public void PivotTableDataSourceDialogOpenedFromKeyboard_FocusesSourceRange()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_sourceBox);");
    }

    [Fact]
    public void PivotTableDataSourceRangePicker_RefocusesSourceInputAfterRequest()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");

        source.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void PivotTableDataSourceDialogInvalidRange_ShowsOwnedWarningAndRefocusesSource()
    {
        var source = ReadClassSource(
            "PivotTableDataSourceDialog.cs",
            "public sealed class PivotTableDataSourceDialog",
            "internal static class PivotDialogLayout");
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("WorkbookRangeTextCodec.TryParse(_sheetId, _sourceBox.Text, ResolveSheetIdByName, out _)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableDataSource_EnterValidSourceRange\"), _sourceBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("FocusRangeSelectionInput(target);");
        commandSource.Should().Contain("sheetId: sheet.Id");
    }

    [Fact]
    public void PivotTableDataSourceReferencePicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<PivotTableDataSourceRangeSelectionRequest>();
            var dialog = new PivotTableDataSourceDialog(" Sales!A1:E200 ", requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select PivotTable source range");

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new PivotTableDataSourceRangeSelectionRequest(
                    "Sales!A1:E200",
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PivotTableDataSourceApplyRangeSelection_UpdatesSourceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDataSourceDialog("Sales!A1:E200");
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection("Sales!B2:F40");

                var sourceBox = FindVisualChildren<TextBox>(dialog).Single();
                sourceBox.Text.Should().Be("Sales!B2:F40");
                sourceBox.SelectionLength.Should().Be("Sales!B2:F40".Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresPivotTableDataSourceRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        source.Should().Contain("new PivotTableDataSourceDialog(");
        source.Should().Contain("request => ApplyPivotTableDataSourceRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyPivotTableDataSourceRangeSelection(");
        source.Should().Contain("PivotTableDataSourceRangeSelectionRequest request");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(rangeText);");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void PivotAuxiliaryDialogs_LabelEditableFieldsWithAccessKeyTargets()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "PivotDialogLayout.AddLabeledControl(",
            "UiText.Get(\"PivotTableDataSource_TableRangeLabel\")",
            "CreateReferenceEditor(_sourceBox",
            "_sourceBox,",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_FieldToConnectLabel\"), _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_SlicerCaptionLabel\"), _nameBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_DateFieldToConnectLabel\"), _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get(\"PivotSlicerTimeline_TimelineCaptionLabel\"), _nameBox",
            "InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery",
            "AutomationProperties.SetName(_styleGallery, UiText.Get(\"PivotChartOptions_PivotChartStyleGallery\"))",
            "AddCombo(selectionPanel, UiText.Get(\"PivotFieldGrouping_FieldLabel\"), _fieldBox",
            "AddCombo(groupingPanel, UiText.Get(\"PivotFieldGrouping_GroupByLabel\"), _groupingBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_StartingAtLabel\"), _startBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_EndingAtLabel\"), _endBox",
            "AddTextBox(rangePanel, UiText.Get(\"PivotFieldGrouping_ByLabel\"), _intervalBox",
            "AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox",
            "AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_FormulaLabel\"), _formulaBox",
            "PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get(\"PivotCalculated_SourceFieldLabel\"), _fieldBox",
            "AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox",
            "AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_ItemFormulaLabel\"), _formulaBox",
            "public static void AddLabeledControl(Panel stack, string label, UIElement control",
            "Target = target"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void InsertSlicerDialog_CreateResult_CapturesFieldAndSlicerName()
    {
        InsertSlicerDialog.CreateResult("  Region  ", "  Region Slicer  ")
            .Should()
            .Be(new InsertSlicerDialogResult("Region", "Region Slicer"));
    }

    [Fact]
    public void InsertSlicerDialog_TryCreateResult_RejectsBlankFieldOrCaption()
    {
        InsertSlicerDialog.TryCreateResult(" ", "Region Slicer", out _, out var fieldError)
            .Should()
            .BeFalse();
        fieldError.Should().Be("Select a field to connect.");

        InsertSlicerDialog.TryCreateResult("Region", " ", out _, out var captionError)
            .Should()
            .BeFalse();
        captionError.Should().Be("Enter a slicer caption.");
    }

    [Fact]
    public void InsertSlicerDialog_AcceptWarnsAndRefocusesInvalidInput()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertSlicerDialog",
            "public sealed record InsertTimelineDialogResult");

        source.Should().Contain("if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"PivotSlicerTimeline_EnterSlicerOptions\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("Keyboard.Focus(target);");
        source.Should().Contain("textBox.SelectAll();");
    }

    [Fact]
    public void InsertSlicerDialog_ExposesExcelLikeFieldSelectionShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_ChooseFieldsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_FieldToConnectLabel\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_SlicerCaptionLabel\")");
        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("Slicers make it faster to filter a PivotTable");
    }

    [Fact]
    public void InsertSlicerDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertSlicerDialog",
            "public sealed record InsertTimelineDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void InsertTimelineDialog_CreateResult_CapturesDateFieldAndTimelineName()
    {
        InsertTimelineDialog.CreateResult("  Order Date  ", "  Order Date Timeline  ")
            .Should()
            .Be(new InsertTimelineDialogResult("Order Date", "Order Date Timeline"));
    }

    [Fact]
    public void InsertTimelineDialog_TryCreateResult_RejectsBlankDateFieldOrCaption()
    {
        InsertTimelineDialog.TryCreateResult(" ", "Order Date Timeline", out _, out var fieldError)
            .Should()
            .BeFalse();
        fieldError.Should().Be("Select a date field to connect.");

        InsertTimelineDialog.TryCreateResult("Order Date", " ", out _, out var captionError)
            .Should()
            .BeFalse();
        captionError.Should().Be("Enter a timeline caption.");
    }

    [Fact]
    public void InsertTimelineDialog_AcceptWarnsAndRefocusesInvalidInput()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertTimelineDialog",
            "");

        source.Should().Contain("if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? UiText.Get(\"PivotSlicerTimeline_EnterTimelineOptions\")");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("Keyboard.Focus(target);");
        source.Should().Contain("textBox.SelectAll();");
    }

    [Fact]
    public void InsertTimelineDialog_ExposesExcelLikeDateFieldSelectionShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_ChooseDateFieldsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_DateFieldToConnectLabel\")");
        source.Should().Contain("UiText.Get(\"PivotSlicerTimeline_TimelineCaptionLabel\")");
        source.Should().NotContain("Timelines filter PivotTables by date");
    }

    [Fact]
    public void InsertTimelineDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotSlicerTimelineDialogs.cs",
            "public sealed class InsertTimelineDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void PivotChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartTypeDialog(ChartType.Line);

            dialog.SelectedChartType.Should().Be(ChartType.Line);
            PivotChartTypeDialog.CreateResult(ChartType.StackedColumn)
                .Should()
                .Be(new PivotChartTypeDialogResult(ChartType.StackedColumn));
        });
    }

    [Fact]
    public void PivotChartTypeDialog_ExposesSelectableRecommendedPivotCharts()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Header = UiText.Get(\"PivotChartType_RecommendedPivotCharts\")");
        source.Should().Contain("Header = UiText.Get(\"PivotChartType_AllCharts\")");
        source.Should().Contain("private readonly ListBox _recommendedGallery");
        source.Should().Contain("CreateRecommendedChartsPanel(_recommendedGallery)");
        source.Should().Contain("SelectedGalleryChoice()");
        source.Should().NotContain("Pick a chart type for the selected PivotTable data");
        source.Should().Contain("InsertChartDialog.CreateAllChartsPanel");
        source.Should().Contain("UiText.Get(\"PivotChartType_ChartCategoriesAndChartSubtypeGalleryMatchTheInsertChartPicker\")");
        source.Should().NotContain("private readonly ComboBox _chartTypeBox");
    }

    [Fact]
    public void PivotChartTypeDialogOpenedFromKeyboard_FocusesRecommendedGallery()
    {
        var source = ReadClassSource(
            "PivotChartTypeDialog.cs",
            "public sealed class PivotChartTypeDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_recommendedGallery.Focus();");
        source.Should().Contain("Keyboard.Focus(_recommendedGallery);");
    }

    [Fact]
    public void PivotChartInsert_UsesTypeDialogInsteadOfHardCodedColumn()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotChartCommands.cs"));
        var methodStart = source.IndexOf("private void PivotChartBtn_Click", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void PivotChartChangeTypeBtn_Click", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        method.Should().Contain("dialog.Result.ChartType");
        method.Should().NotContain("new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column");
    }

    [Fact]
    public void PivotTableOptionsDialog_CreateResult_CapturesModeledLayoutAndStyleSettings()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "  PivotStyleMedium9  ",
            showRowHeaders: false,
            showColumnHeaders: true,
            showRowStripes: true,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Outline,
            emptyValueText: "  N/A  ",
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 42,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            pageOverThenDown: true,
            pageWrap: 4,
            compactRowLabelIndent: 3,
            enableDrill: false);

        result.Should().BeEquivalentTo(new
        {
            ShowRowGrandTotals = true,
            ShowColumnGrandTotals = false,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            StyleName = "PivotStyleMedium9",
            ShowRowHeaders = false,
            ShowColumnHeaders = true,
            ShowRowStripes = true,
            ShowColumnStripes = false,
            ReportLayout = PivotReportLayout.Outline,
            EmptyValueText = "N/A",
            ErrorValueText = (string?)null,
            RefreshOnOpen = true,
            SaveSourceData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 1_048_576,
            ShowExpandCollapseButtons = false,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            PageOverThenDown = true,
            PageWrap = 4,
            CompactRowLabelIndent = 3,
            EnableDrill = false
        });
    }

    [Fact]
    public void PivotTableOptionsDialog_CreateResult_CapturesEmptyAndErrorValueText()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "  N/A  ",
            errorValueText: "  #VALUE!  ");

        result.EmptyValueText.Should().Be("N/A");
        result.ErrorValueText.Should().Be("#VALUE!");

        var blankResult = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: " ",
            errorValueText: " \t ");

        blankResult.EmptyValueText.Should().BeNull();
        blankResult.ErrorValueText.Should().BeNull();
    }

    [Fact]
    public void PivotTableOptionsDialog_CreateResult_KeepsExistingPositionalOptionalOrder()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            true,
            true,
            true,
            PivotSubtotalPlacement.Bottom,
            false,
            false,
            "PivotStyleLight16",
            true,
            true,
            false,
            false,
            PivotReportLayout.Tabular,
            "empty",
            true,
            false,
            false,
            false,
            0,
            true,
            true,
            "title",
            "description",
            2,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            true,
            true,
            7,
            "error");

        result.ErrorValueText.Should().Be("error");
        result.EnableDrill.Should().BeTrue();
    }

    [Fact]
    public void PivotTableOptionsDialog_FromPivotTable_UsesConnectedCacheDataOptions()
    {
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            StyleName = "PivotStyleMedium4"
        };
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            RefreshOnLoad = true,
            SaveData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 0
        };

        PivotTableOptionsDialog.FromPivotTable(pivotTable, cache)
            .Should()
            .Match<PivotTableOptionsDialogResult>(result =>
                result.RefreshOnOpen &&
                !result.SaveSourceData &&
                !result.EnableRefresh &&
                !result.PreserveSourceSortFilter &&
                result.MissingItemsLimit == 0);
    }

    [Fact]
    public void PivotTableOptionsDialog_FromPivotTable_UsesCurrentPivotSettings()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId, 15, 1), new CellAddress(sheetId, 22, 4)),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            ReportLayout = PivotReportLayout.Compact,
            StyleName = "PivotStyleDark4",
            ShowRowHeaders = true,
            ShowColumnHeaders = false,
            ShowRowStripes = true,
            ShowColumnStripes = true,
            EmptyValueText = "-",
            ErrorCaption = "(error)",
            ShowExpandCollapseButtons = false,
            PrintExpandCollapseButtons = true,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            PageOverThenDown = true,
            PageWrap = 2,
            CompactRowLabelIndent = 5,
            EnableDrill = false
        };

        PivotTableOptionsDialog.FromPivotTable(pivotTable)
            .Should()
            .BeEquivalentTo(new
            {
                ShowRowGrandTotals = false,
                ShowColumnGrandTotals = true,
                ShowSubtotals = true,
                SubtotalPlacement = PivotSubtotalPlacement.Top,
                RepeatItemLabels = false,
                BlankLineAfterItems = true,
                StyleName = "PivotStyleDark4",
                ShowRowHeaders = true,
                ShowColumnHeaders = false,
                ShowRowStripes = true,
                ShowColumnStripes = true,
                ReportLayout = PivotReportLayout.Compact,
                EmptyValueText = "-",
                ErrorValueText = "(error)",
                PrintExpandCollapseButtons = true,
                ShowExpandCollapseButtons = false,
                AutofitColumnsOnUpdate = false,
                PreserveFormattingOnUpdate = false,
                ShowFieldHeaders = false,
                ShowContextualTooltips = false,
                ShowPropertiesInTooltips = false,
                ShowClassicLayout = true,
                MergeAndCenterLabels = true,
                PageOverThenDown = true,
                PageWrap = 2,
                CompactRowLabelIndent = 5,
                EnableDrill = false
            });
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesBroaderPivotStyleGalleryAndPreservesCurrentStyle()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId, 15, 1), new CellAddress(sheetId, 22, 4)),
            StyleName = "PivotStyleMedium10"
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableOptionsDialog(pivotTable);
            var styleBox = (ComboBox)typeof(PivotTableOptionsDialog)
                .GetField("_styleBox", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleNames = styleBox.Items.Cast<object>().Select(item => item.ToString()).ToList();

            styleNames.Should().Contain(["PivotStyleLight16", "PivotStyleMedium10", "PivotStyleDark7"]);
            styleNames.Should().HaveCountGreaterThan(12);
            styleBox.SelectedItem.Should().Be("PivotStyleMedium10");

            dialog.Close();
        });
    }

    [Fact]
    public void PivotStyleCatalog_ListsBuiltInLightMediumAndDarkStylesAndPreservesCustomCurrentStyle()
    {
        var styleNames = PivotStyleCatalog.GetStyleNames("  MyWorkbookPivotStyle  ");

        styleNames.Should().HaveCount(85);
        styleNames.Take(28).Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleLight{index}"));
        styleNames.Skip(28).Take(28).Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleMedium{index}"));
        styleNames.Skip(56).Take(28).Should().Equal(Enumerable.Range(1, 28).Select(index => $"PivotStyleDark{index}"));
        styleNames[^1].Should().Be("MyWorkbookPivotStyle");
    }

    [Fact]
    public void PivotStyleCatalog_DoesNotDuplicateBuiltInCurrentStyle()
    {
        PivotStyleCatalog.GetStyleNames("pivotstylemedium10")
            .Should()
            .HaveCount(84)
            .And
            .ContainSingle(styleName => string.Equals(styleName, "PivotStyleMedium10", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PivotStyleGalleryDialog_UsesCurrentStyleAsInitialSelectionAndPreservesCustomStyle()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotStyleGalleryDialog("CustomPivotStyle");
            var styleGallery = (ListBox)typeof(PivotStyleGalleryDialog)
                .GetField("_styleGallery", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleNames = styleGallery.Items.Cast<object>().Select(item => item.ToString()).ToList();

            styleNames.Should().HaveCount(85);
            styleNames.Should().Contain("CustomPivotStyle");
            styleGallery.SelectedItem.Should().Be("CustomPivotStyle");

            dialog.Close();
        });
    }

    [Fact]
    public void PivotStyleGalleryDialog_LabelsStyleGalleryWithAccessKeyAndAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotStyleGalleryDialog.cs"));

        source.Should().Contain("new Label { Content = UiText.Get(\"PivotStyleGallery_PivotTableStyle\"), Target = _styleGallery");
        source.Should().Contain("AutomationProperties.SetName(_styleGallery, UiText.Get(\"PivotStyleGallery_PivotTableStyleGallery\"));");
    }

    [Fact]
    public void PivotStyleGalleryDialog_CreateResult_NormalizesBlankStyleToDefault()
    {
        PivotStyleGalleryDialog.CreateResult("  PivotStyleDark28  ")
            .Should()
            .Be(new PivotStyleGalleryDialogResult("PivotStyleDark28"));

        PivotStyleGalleryDialog.CreateResult("  ")
            .Should()
            .Be(new PivotStyleGalleryDialogResult("PivotStyleLight16"));
    }

    [Fact]
    public void MainWindow_PivotStyleGalleryButton_OpensLightweightGalleryInsteadOfOptionsDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotDesignCommands.cs"));
        var handlerSource = source[
            source.IndexOf("private void PivotStyleGalleryBtn_Click", StringComparison.Ordinal)..
            source.IndexOf("private void PivotRowHeadersBtn_Click", StringComparison.Ordinal)];

        handlerSource.Should().Contain("ShowPivotStyleGalleryDialog();");
        handlerSource.Should().NotContain("ShowPivotTableOptionsDialog();");
        source.Should().Contain("private void ShowPivotStyleGalleryDialog()");
        source.Should().Contain("new PivotStyleGalleryDialog(pivotTable.StyleName)");
        source.Should().Contain("styleName: dialog.Result.StyleName");
    }

    [Fact]
    public void MainWindow_PivotStyleOptionButtons_PreserveCurrentStyleAndToggleOnlyTargetFlag()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotDesignCommands.cs"));

        AssertPivotStyleOptionHandler(source, "PivotRowHeadersBtn_Click", "!pivotTable.ShowRowHeaders");
        AssertPivotStyleOptionHandler(source, "PivotColumnHeadersBtn_Click", "!pivotTable.ShowColumnHeaders");
        AssertPivotStyleOptionHandler(source, "PivotBandedRowsBtn_Click", "!pivotTable.ShowRowStripes");
        AssertPivotStyleOptionHandler(source, "PivotBandedColumnsBtn_Click", "!pivotTable.ShowColumnStripes");
    }

    private static void AssertPivotStyleOptionHandler(string source, string handlerName, string toggledFlag)
    {
        var start = source.IndexOf($"private void {handlerName}", StringComparison.Ordinal);
        var end = source.IndexOf("    private void", start + 1, StringComparison.Ordinal);
        var handlerSource = source[start..end];

        handlerSource.Should().Contain("ApplyPivotOptions(");
        handlerSource.Should().Contain("pivotTable.StyleName");
        handlerSource.Should().Contain(toggledFlag);
        handlerSource.Should().NotContain("PivotStyleLight16");
        handlerSource.Should().NotContain("PivotStyleMedium");
        handlerSource.Should().NotContain("PivotStyleDark");
    }

    [Fact]
    public void PivotTableOptionsDialog_UsesExcelStyleTabbedOptionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotTableOptionsDialog.cs"));

        foreach (var content in new[]
        {
            "UiText.Get(\"PivotTableOptions_LayoutAndFormat\")",
            "UiText.Get(\"PivotTableOptions_TotalsAndFilters\")",
            "UiText.Get(\"PivotTableOptions_Display\")",
            "UiText.Get(\"PivotTableOptions_Data\")",
            "UiText.Get(\"PivotTableOptions_Printing\")",
            "UiText.Get(\"PivotTableOptions_AltText\")",
            "_emptyCellsBox",
            "_compactIndentBox",
            "_autofitColumnsBox",
            "_preserveFormattingBox",
            "_refreshOnOpenBox",
            "_enableRefreshBox",
            "_preserveSourceSortFilterBox",
            "_enableShowDetailsBox",
            "_missingItemsLimitBox",
            "_fieldHeadersBox",
            "_showExpandCollapseBox",
            "_printTitlesBox",
            "_printExpandCollapseBox",
            "_altTextTitleBox",
            "_altTextDescriptionBox",
            "Loaded += (_, _) => FocusInitialKeyboardTarget();",
            "private void FocusInitialKeyboardTarget()",
            "_reportLayoutBox.Focus();",
            "Keyboard.Focus(_reportLayoutBox);"
        })
            source.Should().Contain(content);
        source.Should().NotContain("Title and description metadata can be added in a future pass.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesPrintingTab()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Header = UiText.Get(\"PivotTableOptions_Printing\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_ShowExpandCollapseButtons\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_SetPrintTitles\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable\")");
        source.Should().NotContain("Print titles and print expand/collapse buttons are not yet available.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesExcelLikeGroupsInsideTabs()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "UiText.Get(\"PivotTableOptions_LayoutSectionGroup\")",
            "UiText.Get(\"PivotTableOptions_FormatSectionGroup\")",
            "UiText.Get(\"PivotTableOptions_GrandTotalsGroup\")",
            "UiText.Get(\"PivotTableOptions_PivotTableStyleOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_DataOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_PrintOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_AltTextGroup\")",
            "UiText.Get(\"PivotTableOptions_PreserveSourceSortAndFilterSettings\")",
            "UiText.Get(\"PivotTableOptions_RetainItemsDeletedLabel\")",
            "UiText.Get(\"PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns\")",
            "UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnRows\")",
            "UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnColumns\")"
        })
            source.Should().Contain(content);

        source.Should().NotContain("Field list and buttons remain available");
    }

    [Fact]
    public void PivotTableOptionsDialog_ModelsPreserveSourceSortFilterOption()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly CheckBox _preserveSourceSortFilterBox");
        source.Should().Contain("Content = UiText.Get(\"PivotTableOptions_PreserveSourceSortAndFilterSettings\")");
        source.Should().Contain("PreserveSourceSortFilter");
        source.Should().Contain("AddCheckBox(dataPanel, _preserveSourceSortFilterBox)");
        source.Should().NotContain("IsEnabled = false");
        source.Should().NotContain("changing this option is not modeled yet");
        source.Should().NotContain("new CheckBox { Content = \"Preserve source sort and _filter settings\"");
    }

    [Fact]
    public void PivotTableOptionsDialog_LabelsEditableOptionsWithAccessKeyTargets()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "AddLabeledControl(layoutPanel, UiText.Get(\"PivotTableOptions_ReportLayoutLabel\"), _reportLayoutBox",
            "AddLabeledControl(layoutPanel, UiText.Get(\"PivotTableOptions_CompactIndentLabel\"), _compactIndentBox",
            "AddLabeledControl(formatPanel, UiText.Get(\"PivotTableOptions_EmptyCellsLabel\"), _emptyCellsBox",
            "AddLabeledControl(formatPanel, UiText.Get(\"PivotTableOptions_ErrorValuesLabel\"), _errorValuesBox",
            "AddLabeledControl(dataPanel, UiText.Get(\"PivotTableOptions_RetainItemsDeletedLabel\"), _missingItemsLimitBox",
            "AddLabeledControl(filtersPanel, UiText.Get(\"PivotTableOptions_SubtotalPlacementLabel\"), _subtotalPlacementBox",
            "AddLabeledControl(stylePanel, UiText.Get(\"PivotTableOptions_PivotTableStyleLabel\"), _styleBox",
            "new Label",
            "Content = label",
            "Target = control"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialogInvalidNumericOptions_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotTableOptionsDialog.cs",
            "public sealed partial class PivotTableOptionsDialog",
            "");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableOptions_EnterCompactIndent\"), _compactIndentBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableOptions_EnterPageFieldsPerColumn\"), _pageWrapBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("_tabs.SelectedItem = _layoutTab;");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesAccessKeysForModeledCheckboxes()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "Content = UiText.Get(\"PivotTableOptions_ShowRowGrandTotals\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowColumnGrandTotals\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowSubtotals\")",
            "Content = UiText.Get(\"PivotTableOptions_RepeatItemLabels\")",
            "Content = UiText.Get(\"PivotTableOptions_InsertBlankLineAfterEachItem\")",
            "Content = UiText.Get(\"PivotTableOptions_RowHeaders\")",
            "Content = UiText.Get(\"PivotTableOptions_ColumnHeaders\")",
            "Content = UiText.Get(\"PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnRows\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnColumns\")",
            "Content = UiText.Get(\"PivotTableOptions_BandedRows\")",
            "Content = UiText.Get(\"PivotTableOptions_BandedColumns\")",
            "Content = UiText.Get(\"PivotTableOptions_AutofitColumnWidthsOnUpdate\")",
            "Content = UiText.Get(\"PivotTableOptions_PreserveCellFormattingOnUpdate\")",
            "Content = UiText.Get(\"PivotTableOptions_RefreshDataWhenOpeningTheFile\")",
            "Content = UiText.Get(\"PivotTableOptions_EnableRefresh\")",
            "Content = UiText.Get(\"PivotTableOptions_EnableShowDetails\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowExpandCollapseButtons\")",
            "Content = UiText.Get(\"PivotTableOptions_SetPrintTitles\")",
            "Content = UiText.Get(\"PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable\")"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialog_DataTabAccessKeysAreUnique()
    {
        string[] dataTabLabels =
        [
            "_Refresh data when opening the file",
            "_Save source data with file",
            "_Enable refresh",
            "Enable Show De_tails",
            "Preserve source sort and _filter settings",
            "Retain items _deleted from the data source"
        ];

        var accessKeys = dataTabLabels
            .Select(label => char.ToUpperInvariant(label[label.IndexOf('_') + 1]))
            .ToList();

        accessKeys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PivotTableOptionsDialog_ResultIncludesPrintingAndAltText()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            PivotSubtotalPlacement.Top,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            " PivotStyleMedium4 ",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: true,
            PivotReportLayout.Outline,
            emptyValueText: " - ",
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            missingItemsLimit: 0,
            compactRowLabelIndent: 6,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            showItemsWithNoDataOnRows: true,
            showItemsWithNoDataOnColumns: true,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "  Sales pivot ",
            altTextDescription: " Quarterly sales summary ");

        result.ShowExpandCollapseButtons.Should().BeFalse();
        result.AutofitColumnsOnUpdate.Should().BeFalse();
        result.PreserveFormattingOnUpdate.Should().BeFalse();
        result.ShowFieldHeaders.Should().BeFalse();
        result.ShowContextualTooltips.Should().BeFalse();
        result.ShowPropertiesInTooltips.Should().BeFalse();
        result.ShowClassicLayout.Should().BeTrue();
        result.MergeAndCenterLabels.Should().BeTrue();
        result.ShowItemsWithNoDataOnRows.Should().BeTrue();
        result.ShowItemsWithNoDataOnColumns.Should().BeTrue();
        result.EnableRefresh.Should().BeFalse();
        result.MissingItemsLimit.Should().Be(0);
        result.PrintTitles.Should().BeTrue();
        result.PrintExpandCollapseButtons.Should().BeTrue();
        result.CompactRowLabelIndent.Should().Be(6);
        result.AltTextTitle.Should().Be("Sales pivot");
        result.AltTextDescription.Should().Be("Quarterly sales summary");
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_TrimsFieldAndClampsNumberRangeInterval()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            "  Order Date  ",
            sourceFieldIndex: -3,
            PivotFieldGrouping.NumberRange,
            "  10  ",
            "  90  ",
            "  -5  ",
            ungroup: false);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Order Date",
            0,
            PivotFieldGrouping.NumberRange,
            10,
            90,
            1,
            false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_UngroupClearsGroupingSettings()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            " Region ",
            sourceFieldIndex: 2,
            PivotFieldGrouping.Month,
            "1",
            "12",
            "3",
            ungroup: true);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Region",
            2,
            PivotFieldGrouping.None,
            null,
            null,
            null,
            true));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_UsesCurrentFieldSettings()
    {
        var field = new PivotFieldModel(
            SourceFieldIndex: 1,
            Grouping: PivotFieldGrouping.Month,
            GroupStart: 44562,
            GroupEnd: 44927,
            GroupInterval: 2);

        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], field)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Order Date",
                1,
                PivotFieldGrouping.Month,
                44562,
                44927,
                2,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_DefaultsToFirstFieldWhenCurrentSettingsAreMissing()
    {
        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], currentField: null)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Region",
                0,
                PivotFieldGrouping.None,
                null,
                null,
                null,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_ExposesExcelLikeGroupingSections()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_SelectionGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_GroupByGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_RangeGroup\")");
        source.Should().NotContain("Select the PivotTable field and grouping interval");
    }

    [Fact]
    public void PivotFieldGroupingDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidNumberRangeIntervals_ShowOwnedWarningAndRefocusByBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterPositiveGroupingInterval\"), _intervalBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("string.IsNullOrWhiteSpace(value)");
        source.Should().Contain("!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out interval)");
        source.Should().Contain("interval <= 0");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidBounds_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterValidStartingValue\"), _startBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterValidEndingValue\"), _endBox);");
        source.Should().Contain("TryParseOptionalFiniteDouble(_startBox.Text, out _)");
        source.Should().Contain("TryParseOptionalFiniteDouble(_endBox.Text, out _)");
        source.Should().Contain("double.IsFinite(parsed)");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_CreateResult_TrimsAndBuildsModel()
    {
        var result = PivotCalculatedFieldDialog.CreateResult("  Revenue  ", "  Sales-Cost  ");

        result.Should().Be(new PivotCalculatedFieldDialogResult("Revenue", "Sales-Cost"));
        result.ToModel().Should().Be(new PivotCalculatedFieldModel("Revenue", "Sales-Cost"));
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotCalculated_NameAndFormulaGroup\")");
        source.Should().Contain("AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox");
        source.Should().Contain("UiText.Get(\"PivotCalculated_FormulaLabel\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
        source.Should().NotContain("Use field names in formulas");
        source.Should().NotContain("Calculated fields are added to the Values area");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesFieldsListAndInsertFieldControl()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableFieldsLabel\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertField\")");
        source.Should().Contain("InsertSelectedField");
        source.Should().Contain("InsertFormulaReference");
    }

    [Fact]
    public void PivotCalculatedDialogs_FieldAndItemListsExposeAutomationNames()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("AutomationProperties.SetName(_fieldList, UiText.Get(\"PivotCalculated_AvailableFields\"));");
        source.Should().Contain("AutomationProperties.SetName(_itemList, UiText.Get(\"PivotCalculated_AvailableItems\"));");
    }

    [Fact]
    public void PivotCalculatedFieldDialogInvalidRequiredInputs_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotCalculatedDialogs.cs",
            "public sealed class PivotCalculatedFieldDialog",
            "public sealed record PivotCalculatedItemDialogResult");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotCalculated_EnterCalculatedFieldName\"), _nameBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotCalculated_EnterCalculatedFieldFormula\"), _formulaBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_InsertFormulaReference_InsertsQuotedFieldAtCaret()
    {
        PivotCalculatedFieldDialog.InsertFormulaReference("Sales+Cost", "[Region Name]", 6, 0)
            .Should()
            .Be("Sales+[Region Name]Cost");
    }

    [Fact]
    public void PivotCalculatedItemDialog_CreateResult_TrimsClampsAndBuildsModel()
    {
        var result = PivotCalculatedItemDialog.CreateResult(
            "  Region  ",
            sourceFieldIndex: -8,
            "  East + West  ",
            "  East+West  ");

        result.Should().Be(new PivotCalculatedItemDialogResult("Region", 0, "East + West", "East+West"));
        result.ToModel().Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotCalculated_FieldAndItemGroup\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
        source.Should().NotContain("Calculated items are evaluated within the selected field");
        source.Should().Contain("PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get(\"PivotCalculated_SourceFieldLabel\"), _fieldBox");
        source.Should().Contain("AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox");
        source.Should().Contain("AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_ItemFormulaLabel\"), _formulaBox");
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesFieldItemListsAndInsertionControls()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("private readonly ListBox _itemList");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableItemsLabel\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertField\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertItem\")");
        source.Should().Contain("RefreshItemList");
        source.Should().Contain("InsertSelectedItem");
    }

    [Fact]
    public void PivotCalculatedItemDialogInvalidRequiredInputs_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotCalculatedDialogs.cs",
            "public sealed class PivotCalculatedItemDialog",
            "");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotCalculated_EnterCalculatedItemName\"), _nameBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotCalculated_EnterCalculatedItemFormula\"), _formulaBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotCalculatedItemDialog_InsertFormulaReference_ReplacesSelectedFormulaText()
    {
        PivotCalculatedItemDialog.InsertFormulaReference("East+West", "North", 5, 4)
            .Should()
            .Be("East+North");
    }

    [Fact]
    public void PivotChartOptionsDialog_CreateResult_ParsesAndClampsStyle()
    {
        PivotChartOptionsDialog.CreateResult(
                " 99 ",
                showFieldButtons: false,
                showReportFilterButtons: true,
                showAxisFieldButtons: false,
                showValueFieldButtons: true)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, false, true, false, true));

        PivotChartOptionsDialog.CreateResult(
                "not-a-style",
                showFieldButtons: true,
                showReportFilterButtons: false,
                showAxisFieldButtons: true,
                showValueFieldButtons: false)
            .Should()
            .Be(new PivotChartOptionsDialogResult(null, true, false, true, false));

        PivotChartOptionsDialog.CreateResult(
                99,
                showFieldButtons: true,
                showReportFilterButtons: true,
                showAxisFieldButtons: true,
                showValueFieldButtons: true,
                roundedCorners: true,
                showHiddenData: true,
                blankDisplayMode: ChartBlankDisplayMode.Zero)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, true, true, true, true, false, false, true, true, ChartBlankDisplayMode.Zero));
    }

    [Fact]
    public void PivotChartOptionsDialog_FromChart_UsesCurrentSettings()
    {
        var chart = new ChartModel
        {
            ChartStyleId = 12,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = false,
            ShowPivotChartValueFieldButtons = true,
            DataTable = new ChartDataTableModel { ShowLegendKeys = true },
            RoundedCorners = true,
            ShowDataInHiddenRowsAndColumns = true,
            BlankDisplayMode = ChartBlankDisplayMode.Span
        };

        PivotChartOptionsDialog.FromChart(chart)
            .Should()
            .Be(new PivotChartOptionsDialogResult(12, false, true, false, true, true, true, true, true, ChartBlankDisplayMode.Span));
    }

    [Fact]
    public void PivotChartOptionsDialog_ExposesExcelLikeStyleAndFieldButtonGroups()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotChartOptions_ChartStyle\")");
        source.Should().Contain("_styleGallery");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_PivotChartStyleGallery\")");
        source.Should().Contain("ChartStyleDialog.GetStyleOptions()");
        source.Should().NotContain("Chart _style ID");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_FieldButtonsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowFieldButtonsOnChart\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ReportFilterButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_AxisFieldButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ValueFieldButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowDataTable\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowLegendKeys\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_RoundedCorners\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowDataInHiddenRowsAndColumns\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_BlankCells\")");
        source.Should().NotContain("Style IDs match the built-in Excel chart style gallery");
        source.Should().NotContain("Field buttons let you filter and rearrange PivotChart data directly on the chart");
    }

    [Fact]
    public void PivotChartOptionsDialog_UsesVisualStyleGalleryAndPreservesCurrentStyle()
    {
        var chart = new ChartModel
        {
            IsPivotChart = true,
            ChartStyleId = 12
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartOptionsDialog(chart);
            var gallery = (ListBox)typeof(PivotChartOptionsDialog)
                .GetField("_styleGallery", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleOptions = gallery.Items.Cast<ChartStyleOption>().ToList();

            styleOptions.Should().HaveCount(49);
            styleOptions[0].Should().Be(new ChartStyleOption(null, "Automatic", "Use current chart formatting"));
            gallery.SelectedItem.Should().Be(styleOptions.Single(option => option.StyleId == 12));

            dialog.Close();
        });
    }

    [Fact]
    public void PivotChartOptionsDialogOpenedFromKeyboard_FocusesStyleGallery()
    {
        var source = ReadClassSource(
            "PivotChartOptionsDialog.cs",
            "public sealed class PivotChartOptionsDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_styleGallery.Focus();");
        source.Should().Contain("Keyboard.Focus(_styleGallery);");
    }

    [Fact]
    public void PivotAuxiliaryDialogs_ExposeAccessKeysForModeledCheckboxes()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ShowFieldButtonsOnChart\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ReportFilterButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_AxisFieldButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ValueFieldButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotFieldGrouping_UngroupSelectedField\")");
    }

    private static string ReadPivotWorkflowSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "PivotFieldGroupingDialog.cs",
                "PivotTableDataSourceDialog.cs",
                "PivotChartTypeDialog.cs",
                "PivotDialogLayout.cs",
                "PivotChartOptionsDialog.cs",
                "PivotSlicerTimelineDialogs.cs",
                "PivotCalculatedDialogs.cs",
                "PivotStyleCatalog.cs",
                "PivotStyleGalleryDialog.cs",
                "PivotTableOptionsDialog.cs",
                "PivotTableOptionsDialog.Result.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = string.IsNullOrEmpty(endMarker)
            ? source.Length
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            end = source.Length;
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
