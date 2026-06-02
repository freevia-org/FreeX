using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Theory]
    [InlineData(TextToColumnsDelimiterKind.Comma, null, ",")]
    [InlineData(TextToColumnsDelimiterKind.Semicolon, null, ";")]
    [InlineData(TextToColumnsDelimiterKind.Tab, null, "\t")]
    [InlineData(TextToColumnsDelimiterKind.Space, null, " ")]
    [InlineData(TextToColumnsDelimiterKind.Custom, "|", "|")]
    public void TextToColumnsResult_MapsDelimiterChoiceToDelimiterString(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter,
        string expectedDelimiter)
    {
        var result = TextToColumnsDialog.CreateResult(kind, customDelimiter);

        result.Delimiter.Should().Be(expectedDelimiter);
    }

    [Fact]
    public void TextToColumnsResult_CombinesCheckedDelimiters()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Tab, TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Custom],
            "|");

        result.Delimiters.Should().Be("\t,|");
        result.DelimiterKind.Should().Be(TextToColumnsDelimiterKind.Custom);
    }

    [Fact]
    public void TextToColumnsDelimiterPlanner_BuildsDistinctDelimiterPlan()
    {
        var plan = TextToColumnsDelimiterPlanner.CreatePlan(
            [
                TextToColumnsDelimiterKind.Space,
                TextToColumnsDelimiterKind.Comma,
                TextToColumnsDelimiterKind.Space,
                TextToColumnsDelimiterKind.Custom
            ],
            "|");

        plan.Should().Be(new TextToColumnsDelimiterPlan(TextToColumnsDelimiterKind.Custom, " ,|"));
        TextToColumnsDelimiterPlanner.DelimiterFor(TextToColumnsDelimiterKind.Tab).Should().Be("\t");
        var act = () => TextToColumnsDelimiterPlanner.DelimiterFor(TextToColumnsDelimiterKind.Custom);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Custom delimiter is required.*");
    }

    [Fact]
    public void TextToColumnsResult_RejectsEmptyDelimiterSelection()
    {
        var act = () => TextToColumnsDialog.CreateResult([]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Select at least one delimiter.*");
    }

    [Fact]
    public void TextToColumnsPreview_UsesSelectedTextRows()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East,42,Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West;7;Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue(""));

        TextToColumnsDialog.BuildPreviewRows(sheet, range).Should().Equal("East,42,Open", "West;7;Closed");
    }

    [Fact]
    public void TextToColumnsDialog_AllowsOnlySingleColumnSelections()
    {
        var sheetId = SheetId.New();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 1)))
            .Should()
            .BeTrue();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 2)))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimitedAndFixedWidthSplitChoices()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("UiText.Get(\"TextToColumns_OriginalDataTypeGroup\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Delimited\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FixedWidth\")");
        source.Should().Contain("CreateFixedWidthResult");
        source.Should().Contain("ParseFixedWidthBreakPositions");
        source.Should().Contain("UiText.Get(\"TextToColumns_ChooseDelimitersInstruction\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_DelimitersGroup\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_FixedWidth2\")");
        source.Should().Contain("_fixedWidthRuler");
        source.Should().Contain("MouseLeftButtonDown");
        source.Should().Contain("MouseMove");
        source.Should().Contain("MouseRightButtonDown");
        source.Should().Contain("UiText.Get(\"TextToColumns_ClickTheRulerToCreateABreakLineDragToMoveItOrRightClickALineToRemoveIt\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TextQualifierLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TreatConsecutiveDelimitersAsOne\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_DestinationLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_ColumnDataFormatGroup\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_General\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Text\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_Date\")");
        source.Should().Contain("_dateFormatBox");
        source.Should().Contain("UiText.Get(\"TextToColumns_DoNotImportColumnSkip\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_AdvancedGroup\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_DecimalSeparatorLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_ThousandsSeparatorLabel\")");
        source.Should().Contain("UiText.Get(\"TextToColumns_TrailingMinusForNegativeNumbers\")");
        source.Should().Contain("TryParseAdvancedSeparator(_decimalSeparatorBox.Text, out _)");
        source.Should().Contain("TryParseAdvancedSeparator(_thousandsSeparatorBox.Text, out _)");
        source.Should().Contain("FocusInvalidAdvancedSeparatorInput(_decimalSeparatorBox);");
        source.Should().Contain("FocusInvalidAdvancedSeparatorInput(_thousandsSeparatorBox);");
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimiterPreviewAffordances()
    {
        var source = ReadTextToColumnsDialogSources();

        foreach (var key in new[]
        {
            "TextToColumns_Tab",
            "TextToColumns_Semicolon",
            "TextToColumns_Comma",
            "TextToColumns_Space",
            "TextToColumns_Other",
            "TextToColumns_DataPreview"
        })
            source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("_previewGrid");
        source.Should().Contain("RefreshPreview");
        source.Should().Contain("TextToColumnsPlanner.SplitText");
        source.Should().Contain("_textQualifierBox");
        source.Should().Contain("SelectedTextQualifier");
        source.Should().Contain("TreatConsecutiveDelimitersAsOne");
        source.Should().Contain("_destinationBox");
        source.Should().Contain("_formatColumnBox");
        source.Should().Contain("BuildColumnFormats");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("TextToColumnsRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void TextToColumnsRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        TextToColumnsDialog.CreateRangeSelectionRequest(" F2 ")
            .Should()
            .Be(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
    }

    [Fact]
    public void TextToColumnsDestinationPicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var requests = new List<TextToColumnsRangeSelectionRequest>();
            var dialog = new TextToColumnsDialog(
                ["East,42"],
                new CellAddress(sheetId, 2, 6),
                requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select destination cell");

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDestinationPicker_RefocusesDestinationAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Delimiters.cs"));
        var handlerSource = source[source.IndexOf("private DockPanel CreateReferenceEditor", StringComparison.Ordinal)..];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void MainWindow_WiresTextToColumnsDestinationPickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new TextToColumnsDialog(");
        source.Should().Contain("request => ApplyTextToColumnsRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyTextToColumnsRangeSelection(");
        source.Should().Contain("TextToColumnsRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyRangeSelection(selectedRange.Start);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void TextToColumnsApplyRangeSelection_UpdatesDestinationBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new TextToColumnsDialog(["East,42"], new CellAddress(sheetId, 2, 6));
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection(new CellAddress(sheetId, 4, 8));

                FindVisualChildren<TextBox>(dialog)
                    .Single(box => box.Text == "H4")
                    .Text.Should().Be("H4");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDialog_ExposesAllExcelDateColumnFormats()
    {
        var dialogSource = ReadTextToColumnsDialogSources();
        var modelSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialogModel.cs"));

        foreach (var dateOrder in new[] { "MDY", "DMY", "YMD", "MYD", "DYM", "YDM" })
        {
            dialogSource.Should().Contain($"\"{dateOrder}\"");
            modelSource.Should().Contain($"Date{dateOrder}");
        }
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardChromeAroundDelimitedFlow()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("UiText.Format(\"TextToColumns_TextWizardStepOf3\", normalizedStep)");
        source.Should().Contain("CreateWizardButtonRow");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_BackButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_NextButton\")");
        source.Should().Contain("Content = UiText.Get(\"TextToColumns_FinishButton\")");
        source.Should().Contain("MoveWizardStep");
        source.Should().Contain("UpdateWizardStep");
        source.Should().Contain("_backButton.IsEnabled = plan.BackEnabled");
        source.Should().Contain("_nextButton.IsEnabled = plan.NextEnabled");
        source.Should().Contain("UiText.Get(\"TextToColumns_ChooseFileTypeInstruction\")");
        source.Should().Contain("NextDefault: normalizedStep < 3");
        source.Should().Contain("FinishDefault: normalizedStep == 3");
        source.Should().Contain("Accept()");
        source.Should().NotContain("Additional wizard steps are not supported yet.");
        source.Should().NotContain("This dialog opens on the split-options step.");
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardDefaultButtonsPerStep()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("private Button? _finishButton;");
        source.Should().Contain("_finishButton = new Button");
        source.Should().Contain("_nextButton.IsDefault = plan.NextDefault");
        source.Should().Contain("_finishButton.IsDefault = plan.FinishDefault");
    }

    [Fact]
    public void TextToColumnsDialogOpenedFromKeyboard_FocusesOriginalDataTypeChoice()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_delimitedButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_delimitedButton);");
    }

    [Fact]
    public void TextToColumnsWizardNavigation_FocusesFirstControlOnNewStep()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new TextToColumnsDialog(
                ["East,42"],
                new CellAddress(sheetId, 2, 6));
            dialog.Show();
            try
            {
                var next = FindVisualChildren<Button>(dialog)
                    .Single(button => Equals(button.Content, "_Next >"));

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var tabDelimiter = FindVisualChildren<CheckBox>(dialog)
                    .Single(checkBox => Equals(checkBox.Content, "_Tab"));
                Keyboard.FocusedElement.Should().BeSameAs(tabDelimiter);

                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var columnSelector = FindVisualChildren<ComboBox>(dialog)
                    .Single(comboBox => comboBox.Items.OfType<string>().Contains("Column 1"));
                Keyboard.FocusedElement.Should().BeSameAs(columnSelector);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDialogInvalidDestination_ReturnsToStepThreeAndFocusesDestination()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("FocusInvalidDestinationInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
        source.Should().Contain("FocusInvalidDestinationInput();");
        source.Should().Contain("private void FocusInvalidDestinationInput()");
        source.Should().Contain("_wizardStep = 3;");
        source.Should().Contain("UpdateWizardStep();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_destinationBox);");
    }

    [Fact]
    public void TextToColumnsDialogInvalidFixedWidthBreaks_ReturnsToStepTwoAndFocusesBreaks()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("TryParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text, FixedWidthMaxLength(), out _)");
        source.Should().Contain("FocusInvalidFixedWidthBreaksInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
        source.Should().Contain("private void FocusInvalidFixedWidthBreaksInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_fixedWidthButton.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_fixedWidthBreaksBox);");
    }

    [Fact]
    public void TextToColumnsDialogInvalidCustomDelimiter_ReturnsToStepTwoAndFocusesOtherDelimiter()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("FocusInvalidCustomDelimiterInput();");
        source.Should().Contain("RefocusInvalidInputAfterWarning(ex.Message);");
        source.Should().Contain("private void RefocusInvalidInputAfterWarning(string message)");
        source.Should().Contain("private void FocusInvalidCustomDelimiterInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_delimitedButton.IsChecked = true;");
        source.Should().Contain("_otherBox.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(_customBox);");
    }

    [Fact]
    public void TextToColumnsDialogNoDelimiterSelected_ReturnsToStepTwoAndFocusesDelimiterGroup()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("SelectedDelimiterKinds().Count == 0");
        source.Should().Contain("FocusInvalidDelimiterSelectionInput();");
        source.Should().Contain("throw new ArgumentException(UiText.Get(\"TextToColumns_SelectAtLeastOneDelimiter\"));");
        source.Should().Contain("string.Equals(message, UiText.Get(\"TextToColumns_SelectAtLeastOneDelimiter\"), StringComparison.Ordinal)");
        source.Should().Contain("private void FocusInvalidDelimiterSelectionInput()");
        source.Should().Contain("_wizardStep = 2;");
        source.Should().Contain("_delimitedButton.IsChecked = true;");
        source.Should().Contain("_tabBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_tabBox);");
        source.Should().NotContain("return kinds.Count == 0 ? [TextToColumnsDelimiterKind.Comma] : kinds;");
    }

    [Fact]
    public void TextToColumnsResult_ParsesFixedWidthBreakPositions()
    {
        TextToColumnsDialog.ParseFixedWidthBreakPositions("12, 4; 8 4")
            .Should()
            .Equal(4, 8, 12);

        var result = TextToColumnsDialog.CreateFixedWidthResult("4,8");
        result.SplitMode.Should().Be(TextToColumnsSplitMode.FixedWidth);
        result.FixedWidthBreakPositions.Should().Equal(4, 8);
    }

    [Theory]
    [InlineData("4,bad", 12)]
    [InlineData("0,4", 12)]
    [InlineData("4,12", 12)]
    [InlineData("", 12)]
    [InlineData("   ", 12)]
    [InlineData("1", 1)]
    public void TextToColumnsResult_RejectsInvalidFixedWidthBreakPositions(string text, int maxLength)
    {
        TextToColumnsDialog.TryParseFixedWidthBreakPositions(text, maxLength, out var positions).Should().BeFalse();
        positions.Should().BeEmpty();
    }

    [Fact]
    public void TextToColumnsResult_TryParseFixedWidthBreakPositionsRequiresPreviewRange()
    {
        TextToColumnsDialog.TryParseFixedWidthBreakPositions("8, 4; 4", 12, out var positions).Should().BeTrue();
        positions.Should().Equal(4, 8);
    }

    [Fact]
    public void TextToColumnsFixedWidthBreakHelpers_AddMoveAndRemoveBreaks()
    {
        TextToColumnsDialog.AddFixedWidthBreakPosition([8, 4], 12, maxLength: 20)
            .Should()
            .Equal(4, 8, 12);
        TextToColumnsDialog.AddFixedWidthBreakPosition([4, 8], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);

        TextToColumnsDialog.MoveFixedWidthBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);

        TextToColumnsDialog.RemoveFixedWidthBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }

    [Fact]
    public void TextToColumnsFixedWidthBreakPlanner_ParsesAndMutatesBreaks()
    {
        TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions("12, 4; x 8 4")
            .Should()
            .Equal(4, 8, 12);
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 4; 4", 12, out var parsed)
            .Should()
            .BeTrue();
        parsed.Should().Equal(4, 8);
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 12", 12, out _)
            .Should()
            .BeFalse();
        TextToColumnsFixedWidthBreakPlanner.AddBreakPosition([8, 4], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);
        TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);
        TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }

    [Fact]
    public void TextToColumnsDialogHelpers_ForwardFixedWidthBreakWorkToPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Helpers.cs"));

        source.Should().Contain("TextToColumnsDialogPlanner.BuildPreviewRows");
        source.Should().Contain("TextToColumnsDialogPlanner.TryParseDestination");
        source.Should().Contain("TextToColumnsDialogPlanner.NormalizeColumnFormats");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.AddBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions");
    }

    [Fact]
    public void TextToColumnsDialogPlanner_MapsColumnFormatState()
    {
        TextToColumnsDialogPlanner.TextQualifierFromSelectedIndex(1)
            .Should().Be(TextToColumnsTextQualifier.SingleQuote);
        TextToColumnsDialogPlanner.TextQualifierFromSelectedIndex(99)
            .Should().Be(TextToColumnsTextQualifier.DoubleQuote);
        TextToColumnsDialogPlanner.DateColumnFormatFromLabel("YDM")
            .Should().Be(TextToColumnsColumnFormat.DateYDM);
        TextToColumnsDialogPlanner.DateColumnFormatLabel(TextToColumnsColumnFormat.DateDYM)
            .Should().Be("DYM");
        TextToColumnsDialogPlanner.IsDateColumnFormat(TextToColumnsColumnFormat.Text)
            .Should().BeFalse();
        TextToColumnsDialogPlanner.BuildColumnFormats(
                4,
                new Dictionary<int, TextToColumnsColumnFormat>
                {
                    [1] = TextToColumnsColumnFormat.Text,
                    [2] = TextToColumnsColumnFormat.General,
                    [3] = TextToColumnsColumnFormat.General
                })
            .Should().Equal(TextToColumnsColumnFormat.General, TextToColumnsColumnFormat.Text);
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerPlanner_MapsBreaksAndNearestHit()
    {
        TextToColumnsFixedWidthRulerPlanner.PositionFromRulerX(110, rulerWidth: 440, maxLength: 20)
            .Should().Be(5);
        TextToColumnsFixedWidthRulerPlanner.RulerXFromPosition(10, rulerWidth: 440, maxLength: 20)
            .Should().Be(220);
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 5, rulerWidth: 440, maxLength: 20)
            .Should().Be(1);
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 1, rulerWidth: 440, maxLength: 20)
            .Should().Be(-1);
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerDrag_CancelsOnReleasedButtonOrLostCapture()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.cs"));
        var rulerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs"));

        var mouseMove = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseMove", StringComparison.Ordinal)..
            rulerSource.IndexOf("private void FixedWidthRuler_MouseLeftButtonUp", StringComparison.Ordinal)];
        var mouseUpAndLostCapture = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseLeftButtonUp", StringComparison.Ordinal)..
            rulerSource.IndexOf("private void FixedWidthRuler_MouseRightButtonDown", StringComparison.Ordinal)];
        var cancelHelper = rulerSource[
            rulerSource.IndexOf("private void CancelFixedWidthRulerDrag", StringComparison.Ordinal)..
            rulerSource.IndexOf("private int FindNearestBreakIndex", StringComparison.Ordinal)];

        dialogSource.Should().Contain("_fixedWidthRuler.LostMouseCapture += FixedWidthRuler_LostMouseCapture;");
        mouseMove.Should().Contain("if (_dragBreakIndex is not { } index)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("CancelFixedWidthRulerDrag();");
        mouseMove.Should().Contain("e.Handled = true;");
        mouseMove.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var positions = ParseFixedWidthBreakPositions", StringComparison.Ordinal));

        mouseUpAndLostCapture.Should().Contain("CancelFixedWidthRulerDrag();");
        mouseUpAndLostCapture.Should().Contain("if (_dragBreakIndex is null && !_fixedWidthRuler.IsMouseCaptured)");
        mouseUpAndLostCapture.Should().Contain("return;");
        mouseUpAndLostCapture.Should().Contain("private void FixedWidthRuler_LostMouseCapture");
        mouseUpAndLostCapture.Should().Contain("_dragBreakIndex = null;");
        mouseUpAndLostCapture.IndexOf("if (_dragBreakIndex is null && !_fixedWidthRuler.IsMouseCaptured)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseUpAndLostCapture.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal));
        cancelHelper.Should().Contain("_dragBreakIndex = null;");
        cancelHelper.Should().Contain("if (_fixedWidthRuler.IsMouseCaptured)");
        cancelHelper.Should().Contain("_fixedWidthRuler.ReleaseMouseCapture();");
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerRightClick_RemovesNearestBreakAndHandlesMouseEvent()
    {
        var rulerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs"));

        var rightClick = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseRightButtonDown", StringComparison.Ordinal)..
            rulerSource.IndexOf("private int AddFixedWidthBreakAt", StringComparison.Ordinal)];

        rightClick.Should().Contain("if (_fixedWidthButton.IsChecked != true)");
        rightClick.Should().Contain("CancelFixedWidthRulerDrag();");
        rightClick.Should().Contain("var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);");
        rightClick.Should().Contain("FindNearestBreakIndex(positions, e.GetPosition(_fixedWidthRuler).X, tolerance: 10)");
        rightClick.Should().Contain("UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));");
        rightClick.Should().Contain("e.Handled = true;");
        rightClick.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClick.IndexOf("var positions = ParseFixedWidthBreakPositions", StringComparison.Ordinal));
        rightClick.IndexOf("UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void TextToColumnsModeSwitch_CancelsFixedWidthRulerDragWhenLeavingFixedWidth()
    {
        var wizardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Wizard.cs"));

        var refreshMode = wizardSource[
            wizardSource.IndexOf("private void RefreshMode", StringComparison.Ordinal)..
            wizardSource.IndexOf("private void FocusCurrentWizardStepTarget", StringComparison.Ordinal)];

        refreshMode.Should().Contain("if (_fixedWidthButton.IsChecked != true)");
        refreshMode.Should().Contain("CancelFixedWidthRulerDrag();");
        refreshMode.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(refreshMode.IndexOf("_fixedWidthRuler.IsEnabled = plan.FixedWidthControlsEnabled;", StringComparison.Ordinal));
    }

    [Fact]
    public void TextToColumnsResult_CapturesTextQualifierAndConsecutiveDelimiterChoice()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            textQualifier: TextToColumnsTextQualifier.SingleQuote,
            treatConsecutiveDelimitersAsOne: true);

        result.Delimiters.Should().Be(",");
        result.TextQualifier.Should().Be(TextToColumnsTextQualifier.SingleQuote);
        result.TextQualifierChar.Should().Be('\'');
        result.TreatConsecutiveDelimitersAsOne.Should().BeTrue();
    }

    [Fact]
    public void TextToColumnsResult_NormalizesTrailingGeneralColumnFormats()
    {
        TextToColumnsDialog.NormalizeColumnFormats(
            [
                TextToColumnsColumnFormat.Text,
                TextToColumnsColumnFormat.DateMDY,
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.General
            ])
            .Should()
            .Equal(TextToColumnsColumnFormat.Text, TextToColumnsColumnFormat.DateMDY);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            columnFormats:
            [
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.Skip
            ]);

        result.ColumnFormats.Should().Equal(
            TextToColumnsColumnFormat.General,
            TextToColumnsColumnFormat.Skip);
    }

    [Fact]
    public void TextToColumnsResult_RequiresSingleDestinationCell()
    {
        var sheetId = SheetId.New();
        var defaultDestination = new CellAddress(sheetId, 2, 1);

        TextToColumnsDialog.TryParseDestination("", defaultDestination, out _).Should().BeFalse();

        TextToColumnsDialog.TryParseDestination(" F2 ", defaultDestination, out var parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("$F$2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("F$2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("$F2", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("R2C6", defaultDestination, out parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination(" ", defaultDestination, out _).Should().BeFalse();
        TextToColumnsDialog.TryParseDestination("F2:G3", defaultDestination, out _).Should().BeFalse();
    }

    [Fact]
    public void TextToColumnsCommand_WarnsBeforeOverwritingDestinationData()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("FindOverwriteTargets");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_TextToColumnsReplaceDataPrompt\")");
        source.Should().Contain("_messageService.AskYesNo");
        source.Should().Contain("TextToColumnsCommandPlanner.FindOverwriteTargets(_workbook, targetSheetIds, currentRange, dialog.Result)");
    }

    [Fact]
    public void TextToColumnsResult_CapturesAdvancedNumberOptions()
    {
        var advanced = new TextToColumnsAdvancedOptions(",", ".", TrailingMinusNumbers: true);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Semicolon],
            advancedOptions: advanced);

        result.AdvancedOptions.Should().Be(advanced);
    }

    [Theory]
    [InlineData(".", true, ".")]
    [InlineData(" , ", true, ",")]
    [InlineData("", false, "")]
    [InlineData("  ", false, "")]
    [InlineData("..", false, "")]
    public void TextToColumnsResult_TryParseAdvancedSeparatorRequiresSingleCharacter(
        string text,
        bool expectedResult,
        string expectedSeparator)
    {
        TextToColumnsDialog.TryParseAdvancedSeparator(text, out var separator).Should().Be(expectedResult);
        separator.Should().Be(expectedSeparator);
    }

    private static string ReadTextToColumnsDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.ColumnFormats.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Delimiters.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Wizard.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsWizardPlanner.cs")));
}
