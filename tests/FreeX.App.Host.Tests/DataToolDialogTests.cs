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

public sealed class DataToolDialogTests
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
        source.Should().Contain("BuildTextToColumnsEdits");
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsColumnOffsetSelectionAndBulkToggleStates()
    {
        var columns = RemoveDuplicatesDialog.SelectAll(4);
        columns.Should().AllSatisfy(column => column.IsSelected.Should().BeTrue());

        var cleared = RemoveDuplicatesDialog.ClearAll(columns);
        cleared.Should().AllSatisfy(column => column.IsSelected.Should().BeFalse());

        var selected = RemoveDuplicatesDialog.CreateResult(
            [
                new RemoveDuplicateColumnChoice(0, "Region", true),
                new RemoveDuplicateColumnChoice(1, "Sales", false),
                new RemoveDuplicateColumnChoice(2, "Rep", true)
            ]);

        selected.SelectedColumnOffsets.Should().Equal(0u, 2u);
    }

    [Fact]
    public void RemoveDuplicatesDialog_BulkButtonsReflectCurrentColumnSelectionState()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new RemoveDuplicatesDialog(
                [
                    new RemoveDuplicateColumnChoice(0, "Region", true),
                    new RemoveDuplicateColumnChoice(1, "Sales", true)
                ]);
            dialog.Show();
            try
            {
                var buttons = FindVisualChildren<Button>(dialog)
                    .Where(button => button.Content is string)
                    .ToDictionary(button => (string)button.Content);
                var boxes = FindVisualChildren<CheckBox>(dialog)
                    .Where(box => box.Content is "Region" or "Sales")
                    .ToList();

                buttons["_Select All"].IsEnabled.Should().BeFalse();
                buttons["_Unselect All"].IsEnabled.Should().BeTrue();

                buttons["_Unselect All"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                boxes.Should().AllSatisfy(box => box.IsChecked.Should().BeFalse());
                buttons["_Select All"].IsEnabled.Should().BeTrue();
                buttons["_Unselect All"].IsEnabled.Should().BeFalse();

                boxes[0].IsChecked = true;

                buttons["_Select All"].IsEnabled.Should().BeTrue();
                buttons["_Unselect All"].IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new RemoveDuplicatesDialog(
                [
                    new RemoveDuplicateColumnChoice(0, "Region", true),
                    new RemoveDuplicateColumnChoice(1, "Sales", true)
                ],
                [
                    new RemoveDuplicateColumnChoice(0, "Column A", true),
                    new RemoveDuplicateColumnChoice(1, "Column B", true)
                ]);
            dialog.Show();
            try
            {
                var headersBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, "_My data has headers"));
                AutomationProperties.GetName(headersBox).Should().Be("My data has headers");
                AutomationProperties.GetAutomationId(headersBox).Should().Be("RemoveDuplicatesHasHeadersBox");
                AutomationProperties.GetHelpText(headersBox).Should().Be("Select when the first row contains column headers.");

                var columnsPanel = FindVisualChildren<StackPanel>(dialog)
                    .Single(panel => AutomationProperties.GetAutomationId(panel) == "RemoveDuplicatesColumnsPanel");
                AutomationProperties.GetName(columnsPanel).Should().Be("Columns");
                AutomationProperties.GetHelpText(columnsPanel).Should().Be("Choose the columns used to identify duplicate rows.");

                var buttons = FindVisualChildren<Button>(dialog)
                    .Where(button => button.Content is string)
                    .ToDictionary(button => (string)button.Content);
                AutomationProperties.GetAutomationId(buttons["_Select All"]).Should().Be("RemoveDuplicatesSelectAllButton");
                AutomationProperties.GetName(buttons["_Select All"]).Should().Be("Select all columns");
                AutomationProperties.GetHelpText(buttons["_Select All"]).Should().Be("Select every column for duplicate detection.");
                AutomationProperties.GetAutomationId(buttons["_Unselect All"]).Should().Be("RemoveDuplicatesUnselectAllButton");
                AutomationProperties.GetName(buttons["_Unselect All"]).Should().Be("Unselect all columns");
                AutomationProperties.GetHelpText(buttons["_Unselect All"]).Should().Be("Clear every column selection.");

                var regionBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "RemoveDuplicatesColumn0Box");
                AutomationProperties.GetName(regionBox).Should().Be("Region column");
                AutomationProperties.GetHelpText(regionBox).Should().Be("Select to include this column when identifying duplicate rows.");

                headersBox.IsChecked = false;

                regionBox.Content.Should().Be("Column A");
                AutomationProperties.GetName(regionBox).Should().Be("Column A column");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", true),
            new RemoveDuplicateColumnChoice(1, "Sales", true),
            new RemoveDuplicateColumnChoice(2, "Column C", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsGenericColumnChoicesWhenHeadersAreDisabled()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range, hasHeaders: false).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Column B", true),
            new RemoveDuplicateColumnChoice(1, "Column C", true),
            new RemoveDuplicateColumnChoice(2, "Column D", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_GuessesHeadersFromFirstRowShape()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(42));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));

        RemoveDuplicatesDialog.GuessHasHeaders(sheet, range).Should().BeTrue();

        var numericSheet = new Sheet(sheetId, "Numbers");
        numericSheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 1, 2), new NumberValue(20));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(30));

        RemoveDuplicatesDialog.GuessHasHeaders(numericSheet, range).Should().BeFalse();
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExcludesHeaderRowOnlyWhenHeadersAreEnabled()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: true).Should().Be(new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 8, 3)));
        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: false).Should().Be(range);
        RemoveDuplicatesDialog.ExcludeHeaderRow(new GridRange(range.Start, range.Start), hasHeaders: true)
            .Should()
            .Be(new GridRange(range.Start, range.Start));
    }

    [Fact]
    public void RemoveDuplicatesDialog_ResultCapturesHeaderFlag()
    {
        var result = new RemoveDuplicatesDialogResult([0u, 2u], HasHeaders: true);

        result.SelectedColumnOffsets.Should().Equal(0u, 2u);
        result.HasHeaders.Should().BeTrue();
    }

    [Fact]
    public void SubtotalDialog_CreatesOptionsUsingSubtotalFunctionServiceNames()
    {
        var result = SubtotalDialog.CreateResult(
            groupColumnOffset: 0,
            subtotalColumnOffsets: [1u, 3u],
            functionText: "average",
            replaceCurrentSubtotals: true,
            pageBreakBetweenGroups: true,
            summaryBelowData: false);

        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(1);
        result.ReplaceCurrentSubtotals.Should().BeTrue();
        result.PageBreakBetweenGroups.Should().BeTrue();
        result.SummaryBelowData.Should().BeFalse();
        result.Action.Should().Be(SubtotalDialogAction.Apply);
    }

    [Fact]
    public void SubtotalDialog_CreatesRemoveAllResultWithoutSubtotalColumns()
    {
        var result = SubtotalDialog.CreateRemoveAllResult();

        result.Action.Should().Be(SubtotalDialogAction.RemoveAll);
        result.SubtotalColumnOffsets.Should().BeEmpty();
        result.ReplaceCurrentSubtotals.Should().BeFalse();
        result.PageBreakBetweenGroups.Should().BeFalse();
        result.SummaryBelowData.Should().BeTrue();
    }

    [Fact]
    public void SubtotalDialog_RejectsApplyWithoutSubtotalColumns()
    {
        var act = () => SubtotalDialog.CreateResult(
            groupColumnOffset: 0,
            subtotalColumnOffsets: [],
            functionText: "Sum",
            replaceCurrentSubtotals: true,
            pageBreakBetweenGroups: false,
            summaryBelowData: true);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"{UiText.Get("Subtotal_AtLeastOneSubtotalColumnIsRequired")}*");
    }

    [Fact]
    public void SubtotalDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 3), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        SubtotalDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new SubtotalColumnChoice(0, "Region", false),
            new SubtotalColumnChoice(1, "Sales", true),
            new SubtotalColumnChoice(2, UiText.Format("Subtotal_ColumnLabel", "D"), true));
    }

    [Fact]
    public void SubtotalDialog_DefaultsMatchNoRiskExcelFlow()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SubtotalDialog(
                [
                    new SubtotalColumnChoice(0, "Region", false),
                    new SubtotalColumnChoice(1, "Sales", true),
                    new SubtotalColumnChoice(2, "Units", true)
                ]);
            dialog.Show();
            try
            {
                var comboBoxes = FindVisualChildren<ComboBox>(dialog).ToList();
                var checkBoxes = FindVisualChildren<CheckBox>(dialog).ToList();
                var buttons = FindVisualChildren<Button>(dialog).ToList();

                comboBoxes[0].SelectedValue.Should().Be(0u);
                comboBoxes[1].SelectedValue.Should().Be("Sum");
                checkBoxes.Single(box => Equals(box.Content, "Region")).IsChecked.Should().BeFalse();
                checkBoxes.Single(box => Equals(box.Content, "Sales")).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, "Units")).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_ReplaceCurrentSubtotals"))).IsChecked.Should().BeTrue();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_PageBreakBetweenGroups"))).IsChecked.Should().BeFalse();
                checkBoxes.Single(box => Equals(box.Content, UiText.Get("Subtotal_SummaryBelowData"))).IsChecked.Should().BeTrue();
                buttons.Should().Contain(button => Equals(button.Content, UiText.Get("Subtotal_RemoveAll")));
                buttons.Should().Contain(button => Equals(button.Content, UiText.Ok) && button.IsDefault);
                buttons.Should().Contain(button => Equals(button.Content, UiText.Cancel) && button.IsCancel);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SubtotalDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SubtotalDialog(
                [
                    new SubtotalColumnChoice(0, "Region", false),
                    new SubtotalColumnChoice(1, "Sales", true),
                    new SubtotalColumnChoice(2, "Units", true)
                ]);
            dialog.Show();
            try
            {
                var comboBoxes = FindVisualChildren<ComboBox>(dialog).ToList();
                var groupColumnBox = comboBoxes.Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalGroupColumnBox");
                AutomationProperties.GetName(groupColumnBox).Should().Be("At each change in");
                AutomationProperties.GetHelpText(groupColumnBox).Should().Be("Choose the column that defines each subtotal group.");

                var functionBox = comboBoxes.Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalFunctionBox");
                AutomationProperties.GetName(functionBox).Should().Be("Use function");
                AutomationProperties.GetHelpText(functionBox).Should().Be("Choose the function used to calculate each subtotal.");

                var columnsPanel = FindVisualChildren<StackPanel>(dialog)
                    .Single(panel => AutomationProperties.GetAutomationId(panel) == "SubtotalColumnsPanel");
                AutomationProperties.GetName(columnsPanel).Should().Be("Add subtotal to");
                AutomationProperties.GetHelpText(columnsPanel).Should().Be("Choose columns that receive subtotal calculations.");

                var salesBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "SubtotalColumn1Box");
                AutomationProperties.GetName(salesBox).Should().Be("Sales subtotal column");
                AutomationProperties.GetHelpText(salesBox).Should().Be("Select to add a subtotal calculation to this column.");

                AssertCheckBoxAutomation("SubtotalReplaceCurrentBox", "Replace current subtotals", "Replace existing subtotals with the new subtotal settings.");
                AssertCheckBoxAutomation("SubtotalPageBreakBox", "Page break between groups", "Insert a page break after each subtotal group.");
                AssertCheckBoxAutomation("SubtotalSummaryBelowBox", "Summary below data", "Place subtotal rows below each group.");

                var removeAll = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetAutomationId(button) == "SubtotalRemoveAllButton");
                AutomationProperties.GetName(removeAll).Should().Be("Remove all subtotals");
                AutomationProperties.GetHelpText(removeAll).Should().Be("Remove all subtotal rows from the selected data.");

                void AssertCheckBoxAutomation(string automationId, string name, string helpText)
                {
                    var checkBox = FindVisualChildren<CheckBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(checkBox).Should().Be(name);
                    AutomationProperties.GetHelpText(checkBox).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SubtotalDialog_ExposesKeyboardAccessKeysForStaticOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        foreach (var key in new[]
        {
            "Subtotal_ReplaceCurrentSubtotals",
            "Subtotal_PageBreakBetweenGroups",
            "Subtotal_SummaryBelowData",
            "Subtotal_AtEachChangeIn",
            "Subtotal_AddSubtotalTo",
            "Subtotal_UseFunction",
            "Subtotal_RemoveAll"
        })
            source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("new Label { Content = UiText.Get(\"Subtotal_AddSubtotalTo\"), Target = _subtotalColumnPanel");
        source.Should().Contain("_subtotalColumnPanel.Focusable = true");
        source.Should().Contain("_subtotalColumnPanel.GotKeyboardFocus");
    }

    [Fact]
    public void SubtotalDialog_ExposesExcelStyleFunctionDropdownAndSubtotalChecklist()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("ComboBox _functionBox = new()");
        source.Should().Contain("CreateSubtotalFunctionChoices");
        source.Should().Contain("ItemsSource = CreateSubtotalFunctionChoices()");
        source.Should().Contain("SelectedValue = DefaultSubtotalFunction");
        source.Should().Contain("SelectedValuePath = nameof(SubtotalFunctionChoice.FunctionText)");
        source.Should().NotContain("Header = \"Add subtotal to:\"");
        source.Should().Contain("_subtotalColumnPanel");
    }

    [Fact]
    public void SubtotalDialogOpenedFromKeyboard_FocusesGroupColumnChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_groupColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_groupColumnBox);");
    }

    [Fact]
    public void SubtotalDialogInvalidInputs_FocusInvalidControl()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("FocusInvalidInput(ex.Message);");
        source.Should().Contain("private void FocusInvalidInput(string message)");
        source.Should().Contain("FocusFunctionChoice();");
        source.Should().Contain("private void FocusFunctionChoice()");
        source.Should().Contain("_functionBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_functionBox);");
        source.Should().Contain("FocusSubtotalColumnChoices();");
        source.Should().Contain("private void FocusSubtotalColumnChoices()");
        source.Should().Contain("_subtotalColumnBoxes.FirstOrDefault()");
        source.Should().Contain("firstColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(firstColumnBox);");
    }

    [Fact]
    public void SubtotalDialog_OrdersControlsLikeExcelSubtotalDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SubtotalDialog.cs"));

        source.IndexOf("UiText.Get(\"Subtotal_AtEachChangeIn\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_UseFunction\")", StringComparison.Ordinal));
        source.IndexOf("UiText.Get(\"Subtotal_UseFunction\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_AddSubtotalTo\")", StringComparison.Ordinal));
        source.IndexOf("UiText.Get(\"Subtotal_AddSubtotalTo\")", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("UiText.Get(\"Subtotal_ReplaceCurrentSubtotals\")", StringComparison.Ordinal));
        source.Should().Contain("CreateSubtotalButtonRow");
    }

    [Fact]
    public void SubtotalCommandSurface_RoutesRemoveAllToRemoveSubtotalRowsCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("SubtotalDialogAction.RemoveAll");
        source.Should().Contain("new RemoveSubtotalRowsCommand(_currentSheetId, currentRange)");
        source.Should().Contain("dialog.Result.ReplaceCurrentSubtotals");
        source.Should().Contain("new CompositeWorkbookCommand(\"Subtotal\", [new RemoveSubtotalRowsCommand(_currentSheetId, currentRange), subtotalCommand])");
        source.Should().Contain("dialog.Result.PageBreakBetweenGroups");
        source.Should().Contain("dialog.Result.SummaryBelowData");
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesRangesAndOptionalCopyToCellOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "J1",
            uniqueRecordsOnly: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 6), new CellAddress(sheetId, 2, 7)));
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 10)));
        result.UniqueRecordsOnly.Should().BeTrue();
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

    [Fact]
    public void AdvancedFilterDialog_ParsesCopyToHeaderRange()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "J1:L1",
            uniqueRecordsOnly: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 12)));
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsListRangeWithoutDataRows()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1",
            criteriaRangeText: "C3",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("List range must include headers and at least one data row.");
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsCriteriaRangeWithoutCriteriaRows()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:C5",
            criteriaRangeText: "F1:G1",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Criteria range must include headers and at least one criteria row.");
    }

    [Theory]
    [InlineData("", "F1:G2", "Enter a valid list range.")]
    [InlineData("   ", "F1:G2", "Enter a valid list range.")]
    [InlineData("A1:C5", "", "Enter a valid criteria range.")]
    [InlineData("A1:C5", "   ", "Enter a valid criteria range.")]
    public void AdvancedFilterDialog_RejectsMissingRequiredRanges(
        string listRangeText,
        string criteriaRangeText,
        string expectedError)
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: listRangeText,
            criteriaRangeText: criteriaRangeText,
            copyToCellText: "",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesSheetQualifiedListAndCriteriaRanges()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var criteriaSheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            currentSheetId,
            listRangeText: "Data!A1:D20",
            criteriaRangeText: "Criteria!F1:G2",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            resolveSheetId: sheetName => sheetName switch
            {
                "Data" => dataSheetId,
                "Criteria" => criteriaSheetId,
                _ => null
            },
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(dataSheetId, 1, 1), new CellAddress(dataSheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(criteriaSheetId, 1, 6), new CellAddress(criteriaSheetId, 2, 7)));
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsInvalidCopyToCell()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "NotACell",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid copy-to cell or one-row header range.");
    }

    [Fact]
    public void AdvancedFilterDialog_InPlaceModeIgnoresCopyToText()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "NotACell",
            copyToAnotherLocation: false,
            uniqueRecordsOnly: false,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.CopyToCell.Should().BeNull();
    }

    [Fact]
    public void AdvancedFilterDialog_ExposesExcelStyleModesAndReferencePickers()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AdvancedFilterDialog.cs"));
        var pickerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DialogReferencePicker.cs"));

        source.Should().Contain("_filterInPlaceButton");
        source.Should().Contain("_copyToAnotherLocationButton");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_FilterTheListInPlace\")");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_CopyToAnotherLocation\")");
        source.Should().Contain("Content = UiText.Get(\"AdvancedFilter_UniqueRecordsOnly\")");
        source.Should().Contain("new GroupBox { Header = UiText.Get(\"AdvancedFilter_Action\")");
        source.Should().NotContain("Text = \"Action\"");
        source.Should().Contain("AddReferenceRow(rangesGrid, 0, UiText.Get(\"AdvancedFilter_ListRange2\"), _listRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 1, UiText.Get(\"AdvancedFilter_CriteriaRange2\"), _criteriaRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 2, UiText.Get(\"AdvancedFilter_CopyTo2\"), _copyToBox");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        pickerSource.Should().Contain("Collapse dialog and select range");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().NotContain("Text = \"E1:F2\"");
        source.Should().Contain("Header = UiText.Get(\"AdvancedFilter_Action\")");
        source.Should().Contain("UiText.Get(\"AdvancedFilter_CriteriaShouldIncludeColumnLabelsInTheFirstRowMatchingExcelAdvancedFilte\")");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
    }

    [Fact]
    public void AdvancedFilterDialog_UsesUniqueAccessKeysForActionAndRangeControls()
    {
        var accessKeyLabels = new[]
        {
            "_Filter the list, in-place",
            "_Copy to another location",
            "_List range:",
            "Criteria _range:",
            "Copy _to:",
            "_Unique records only"
        };

        accessKeyLabels
            .GroupBy(GetAccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group)}")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void AdvancedFilterDialog_DefaultsToNoRiskInPlaceModeWithBlankCriteria()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                var radioButtons = FindVisualChildren<RadioButton>(dialog).ToList();
                var uniqueRecordsOnly = FindVisualChildren<CheckBox>(dialog)
                    .Single(checkBox => Equals(checkBox.Content, UiText.Get("AdvancedFilter_UniqueRecordsOnly")));
                var copyToPicker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select copy-to cell");

                radioButtons.Single(button => Equals(button.Content, UiText.Get("AdvancedFilter_FilterTheListInPlace")))
                    .IsChecked.Should().BeTrue();
                radioButtons.Single(button => Equals(button.Content, UiText.Get("AdvancedFilter_CopyToAnotherLocation")))
                    .IsChecked.Should().BeFalse();
                textBoxes[0].Text.Should().Be("A1:C12");
                textBoxes[1].Text.Should().BeEmpty();
                textBoxes[2].Text.Should().BeEmpty();
                textBoxes[2].IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();
                uniqueRecordsOnly.IsChecked.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialog_ExposesAccessibleReferenceFields()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                textBoxes.Select(AutomationProperties.GetAutomationId)
                    .Should()
                    .ContainInOrder(
                        "AdvancedFilterListRangeBox",
                        "AdvancedFilterCriteriaRangeBox",
                        "AdvancedFilterCopyToBox");
                textBoxes.Select(AutomationProperties.GetHelpText)
                    .Should()
                    .ContainInOrder(
                        UiText.Get("AdvancedFilter_EnterTheListRangeToFilterIncludingColumnLabels"),
                        UiText.Get("AdvancedFilter_EnterTheCriteriaRangeIncludingCriteriaLabels"),
                        UiText.Get("AdvancedFilter_EnterTheDestinationCellOrOneRowHeaderRangeWhenCopyingFilteredRecords"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialog_ActionControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                AssertRadioAutomation("AdvancedFilterInPlaceButton", "Filter the list, in-place", "Filter the list in its current location.");
                AssertRadioAutomation("AdvancedFilterCopyToAnotherLocationButton", "Copy to another location", "Copy filtered records to the Copy to destination.");

                var uniqueRecordsOnly = FindVisualChildren<CheckBox>(dialog)
                    .Single(checkBox => AutomationProperties.GetAutomationId(checkBox) == "AdvancedFilterUniqueRecordsOnlyBox");
                AutomationProperties.GetName(uniqueRecordsOnly).Should().Be("Unique records only");
                AutomationProperties.GetHelpText(uniqueRecordsOnly).Should().Be("Show or copy only unique records.");

                void AssertRadioAutomation(string automationId, string name, string helpText)
                {
                    var radioButton = FindVisualChildren<RadioButton>(dialog)
                        .Single(button => AutomationProperties.GetAutomationId(button) == automationId);
                    AutomationProperties.GetName(radioButton).Should().Be(name);
                    AutomationProperties.GetHelpText(radioButton).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterDialogOpenedFromKeyboard_FocusesInPlaceAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AdvancedFilterDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_filterInPlaceButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_filterInPlaceButton);");
    }

    [Fact]
    public void AdvancedFilterDialogInvalidRange_RefocusesAndSelectsInvalidRangeInput()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AdvancedFilterDialog.cs"));

        source.Should().Contain("FocusInvalidRangeInput(error);");
        source.Should().Contain("private void FocusInvalidRangeInput(string? error)");
        source.Should().Contain("UiText.Get(\"AdvancedFilter_CriteriaRangeMustIncludeHeaders\")");
        source.Should().Contain("_copyToAnotherLocationButton.IsChecked = true;");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void AdvancedFilterRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AdvancedFilterDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void AdvancedFilterCopyToReferencePicker_DisabledUntilCopyToAnotherLocationSelected()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                var copyToBox = textBoxes[2];
                var copyToPicker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select copy-to cell");
                var inPlace = FindVisualChildren<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Filter the list, in-place"));
                var copyToAnotherLocation = FindVisualChildren<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Copy to another location"));

                copyToBox.IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();

                copyToAnotherLocation.IsChecked = true;

                copyToBox.IsEnabled.Should().BeTrue();
                copyToPicker.IsEnabled.Should().BeTrue();

                inPlace.IsChecked = true;

                copyToBox.IsEnabled.Should().BeFalse();
                copyToPicker.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterCopyToLabel_DisabledUntilCopyToAnotherLocationSelected()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var copyToLabel = FindVisualChildren<Label>(dialog)
                    .Single(label => Equals(label.Content, "Copy _to:"));
                var inPlace = FindVisualChildren<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Filter the list, in-place"));
                var copyToAnotherLocation = FindVisualChildren<RadioButton>(dialog)
                    .Single(button => Equals(button.Content, "_Copy to another location"));

                copyToLabel.IsEnabled.Should().BeFalse();

                copyToAnotherLocation.IsChecked = true;

                copyToLabel.IsEnabled.Should().BeTrue();

                inPlace.IsChecked = true;

                copyToLabel.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresAdvancedFilterReferencePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new AdvancedFilterDialog(");
        source.Should().Contain("ResolveSheetIdByName,");
        source.Should().Contain("request => ApplyAdvancedFilterRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyAdvancedFilterRangeSelection(");
        source.Should().Contain("AdvancedFilterRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
        source.Should().Contain("ExecuteRepeatable(");
        source.Should().Contain("new AdvancedFilterCommand(");
        source.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        source.Should().Contain("SetActiveCell(destinationCell);");
    }

    [Fact]
    public void AdvancedFilterApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AdvancedFilterDialog(SheetId.New(), "A1:C12");
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.ListRange, "Sheet2!A1:D20");
                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.CriteriaRange, "E1:F4");
                dialog.ApplyRangeSelection(AdvancedFilterRangeSelectionTarget.CopyTo, "H1:J1");

                textBoxes[0].Text.Should().Be("Sheet2!A1:D20");
                textBoxes[1].Text.Should().Be("E1:F4");
                textBoxes[2].Text.Should().Be("H1:J1");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AdvancedFilterRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AdvancedFilterDialog.CreateRangeSelectionRequest(AdvancedFilterRangeSelectionTarget.CriteriaRange, " E1:F4 ")
            .Should()
            .Be(new AdvancedFilterRangeSelectionRequest(
                AdvancedFilterRangeSelectionTarget.CriteriaRange,
                "E1:F4",
                CollapseDialog: true));
    }

    [Theory]
    [InlineData("Select list range", AdvancedFilterRangeSelectionTarget.ListRange, "A1:C12")]
    [InlineData("Select criteria range", AdvancedFilterRangeSelectionTarget.CriteriaRange, "E1:F4")]
    [InlineData("Select copy-to cell", AdvancedFilterRangeSelectionTarget.CopyTo, "H1:J1")]
    public void AdvancedFilterReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        AdvancedFilterRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<AdvancedFilterRangeSelectionRequest>();
            var dialog = new AdvancedFilterDialog(SheetId.New(), " A1:C12 ", requestRangeSelection: requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                textBoxes[1].Text = " E1:F4 ";
                textBoxes[2].Text = " H1:J1 ";
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new AdvancedFilterRangeSelectionRequest(
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
    public void ConsolidateDialog_ValidatesSameSizeSourceRanges()
    {
        var sheetId = SheetId.New();
        var first = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));
        var second = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var different = new GridRange(new CellAddress(sheetId, 10, 1), new CellAddress(sheetId, 12, 3));

        ConsolidateDialog.HaveSameSize([first, second]).Should().BeTrue();
        ConsolidateDialog.HaveSameSize([first, different]).Should().BeFalse();

        var result = ConsolidateDialog.CreateResult(
            [first, second],
            new CellAddress(sheetId, 9, 1),
            ConsolidateFunction.Sum);
        result.SourceRanges.Should().Equal(first, second);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 9, 1));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_DelegatesSourceAndDestinationParsing()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.SourceRanges.Should().Equal(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5)));
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 10, 7));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_CapturesSelectedFunctionAndOptions()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            function: ConsolidateFunction.Average,
            useTopRowLabels: true,
            useLeftColumnLabels: true,
            createLinksToSourceData: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Function.Should().Be(ConsolidateFunction.Average);
        result.UseTopRowLabels.Should().BeTrue();
        result.UseLeftColumnLabels.Should().BeTrue();
        result.CreateLinksToSourceData.Should().BeTrue();
    }

    [Fact]
    public void ConsolidateDialog_JoinsAllReferencesListForExistingParser()
    {
        ConsolidateDialog.SplitSourceRangeText("A1:B3; D5:E7").Should().Equal("A1:B3", "D5:E7");
        ConsolidateDialog.JoinSourceRanges(["A1:B3", "D5:E7"]).Should().Be("A1:B3; D5:E7");
        ConsolidateDialogPlanner.JoinSourceRanges([" A1:B3 ", "", " D5:E7 "]).Should().Be("A1:B3; D5:E7");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("A1:B3", false)]
    [InlineData("A1:B3; D5:E7", false)]
    [InlineData("not-a-range", true)]
    public void ConsolidateDialog_HasPendingReferenceText_IgnoresBlankOrAlreadyListedReferences(
        string referenceText,
        bool expected)
    {
        ConsolidateDialog.HasPendingReferenceText(["A1:B3", "D5:E7"], referenceText)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ConsolidateDialog_HasPendingReferenceText_DetectsUnaddedTypedReference()
    {
        ConsolidateDialog.HasPendingReferenceText(["A1:B3"], "D5:E7")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ConsolidateDialog_TryAddReference_RejectsMalformedReferenceImmediately()
    {
        var sheetId = SheetId.New();

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "nope",
                out var unchanged,
                out var error)
            .Should()
            .BeFalse();

        unchanged.Should().Equal("A1:B3");
        error.Should().Be("Enter a valid source range: nope.");

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "D5:E7",
                out var updated,
                out error)
            .Should()
            .BeTrue();

        updated.Should().Equal("A1:B3", "D5:E7");
        error.Should().BeNull();
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleAllReferencesWorkflow()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_referenceBox");
        source.Should().Contain("_referencesList");
        source.Should().Contain("UiText.Get(\"Consolidate_Reference\")");
        source.Should().Contain("UiText.Get(\"Consolidate_AllReferences\")");
        source.Should().Contain("UiText.Get(\"Consolidate_DestinationCell\")");
        source.Should().Contain("Text = UiText.Get(\"Consolidate_UseLabelsIn\")");
        source.Should().NotContain("Use _labels in:");
        source.Should().Contain("Content = UiText.Get(\"Consolidate_Add\")");
        source.Should().Contain("Content = UiText.Get(\"Consolidate_Delete\")");
        source.Should().Contain("_deleteReferenceButton");
        source.Should().Contain("UpdateReferenceButtons");
        source.Should().Contain("_referencesList.SelectionChanged");
        source.Should().Contain("_referencesList.KeyDown");
        source.Should().Contain("private void ReferencesList_KeyDown");
        source.Should().Contain("if (e.Key == Key.Delete)");
        source.Should().Contain("AddReferenceButton_Click");
        source.Should().Contain("DeleteReferenceButton_Click");
        source.Should().Contain("CreateReferenceEditor(_referenceBox");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void ConsolidateDialog_AllReferencesListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_referencesList, UiText.Get(\"Consolidate_AllReferences2\"));");
    }

    [Fact]
    public void ConsolidateDialog_RangeEditorsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_referenceBox, UiText.Get(\"Consolidate_Reference2\"));");
        source.Should().Contain("AutomationProperties.SetName(_destinationBox, UiText.Get(\"Consolidate_DestinationCell2\"));");
    }

    [Fact]
    public void ConsolidateDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ConsolidateDialog(SheetId.New(), "A1:B3; D5:E7", "G10");
            dialog.Show();
            try
            {
                var functionBox = FindVisualChildren<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "ConsolidateFunctionBox");
                AutomationProperties.GetName(functionBox).Should().Be("Function");
                AutomationProperties.GetHelpText(functionBox).Should().Be("Choose the function used to combine source ranges.");

                AssertTextBoxAutomation("ConsolidateReferenceBox", "Reference", "Enter a source range to add to the All references list.");
                AssertTextBoxAutomation("ConsolidateDestinationCellBox", "Destination cell", "Enter the upper-left destination cell for the consolidated result.");

                var referencesList = FindVisualChildren<ListBox>(dialog)
                    .Single(list => AutomationProperties.GetAutomationId(list) == "ConsolidateAllReferencesList");
                AutomationProperties.GetName(referencesList).Should().Be("All references");
                AutomationProperties.GetHelpText(referencesList).Should().Be("Lists the source ranges that will be consolidated.");

                AssertCheckBoxAutomation("ConsolidateTopRowLabelsBox", "Top row labels", "Use labels from the top row of each source range.");
                AssertCheckBoxAutomation("ConsolidateLeftColumnLabelsBox", "Left column labels", "Use labels from the left column of each source range.");
                AssertCheckBoxAutomation("ConsolidateCreateLinksBox", "Create links to source data", "Create formulas that link the result to the source cells.");

                AssertButtonAutomation("ConsolidateAddReferenceButton", "Add reference", "Add the reference range to the All references list.");
                AssertButtonAutomation("ConsolidateDeleteReferenceButton", "Delete reference", "Delete the selected reference range.");

                void AssertTextBoxAutomation(string automationId, string name, string helpText)
                {
                    var textBox = FindVisualChildren<TextBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(textBox).Should().Be(name);
                    AutomationProperties.GetHelpText(textBox).Should().Be(helpText);
                }

                void AssertCheckBoxAutomation(string automationId, string name, string helpText)
                {
                    var checkBox = FindVisualChildren<CheckBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(checkBox).Should().Be(name);
                    AutomationProperties.GetHelpText(checkBox).Should().Be(helpText);
                }

                void AssertButtonAutomation(string automationId, string name, string helpText)
                {
                    var button = FindVisualChildren<Button>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(button).Should().Be(name);
                    AutomationProperties.GetHelpText(button).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ConsolidateDialogOpenedFromKeyboard_FocusesFunctionChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_functionBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_functionBox);");
    }

    [Fact]
    public void ConsolidateDialogInvalidFinalValidation_RefocusesInvalidEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("FocusInvalidFinalValidation(error);");
        source.Should().Contain("private void FocusInvalidFinalValidation(string? error)");
        source.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("FocusDestinationInput();");
        source.Should().Contain("_referencesList.Focus();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_destinationBox);");
    }

    [Fact]
    public void ConsolidateDialogPendingReference_RequiresAddBeforeOk()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("HasPendingReferenceText(_referencesList.Items.Cast<string>(), _referenceBox.Text)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, UiText.Get(\"Consolidate_AddTheReferenceBeforeClickingOk\")");
        source.Should().Contain("FocusPendingReferenceInput();");
        source.Should().Contain("private void FocusPendingReferenceInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_referenceBox);");
    }

    [Fact]
    public void ConsolidateDialogInvalidAddReference_RefocusesReferenceWithKeyboardFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));
        var addHandlerSource = source[
            source.IndexOf("private void AddReferenceButton_Click", StringComparison.Ordinal)..
            source.IndexOf("private void DeleteReferenceButton_Click", StringComparison.Ordinal)];

        addHandlerSource.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("DialogFocus.FocusAndSelect(_referenceBox);");
    }

    [Fact]
    public void ConsolidateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        ConsolidateDialog.CreateRangeSelectionRequest(ConsolidateRangeSelectionTarget.Reference, " A1:B3 ")
            .Should()
            .Be(new ConsolidateRangeSelectionRequest(
                ConsolidateRangeSelectionTarget.Reference,
                "A1:B3",
                CollapseDialog: true));
    }

    [Fact]
    public void ConsolidateRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void MainWindow_WiresConsolidateReferencePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new ConsolidateDialog(");
        source.Should().Contain("request => ApplyConsolidateRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyConsolidateRangeSelection(");
        source.Should().Contain("ConsolidateRangeSelectionRequest request");
        source.Should().Contain("request.Target == ConsolidateRangeSelectionTarget.DestinationCell");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("FormatCellReference(selectedRange.Start)");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
    }

    [Fact]
    public void ConsolidateApplyRangeSelection_UpdatesRequestedReferenceBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ConsolidateDialog(SheetId.New(), "A1:B3", "G10");
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(ConsolidateRangeSelectionTarget.Reference, "Sheet2!A1:D20");
                dialog.ApplyRangeSelection(ConsolidateRangeSelectionTarget.DestinationCell, "K5");

                textBoxes[0].Text.Should().Be("Sheet2!A1:D20");
                textBoxes[1].Text.Should().Be("K5");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("Select reference range", ConsolidateRangeSelectionTarget.Reference, "A1:B3")]
    [InlineData("Select destination cell", ConsolidateRangeSelectionTarget.DestinationCell, "G10")]
    public void ConsolidateReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        ConsolidateRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<ConsolidateRangeSelectionRequest>();
            var dialog = new ConsolidateDialog(SheetId.New(), " A1:B3 ", " G10 ", requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new ConsolidateRangeSelectionRequest(
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
    public void ConsolidateDialog_ExposesExcelStyleFunctionLabelsAndLinkOptions()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_functionBox");
        source.Should().Contain("_topRowBox");
        source.Should().Contain("_leftColumnBox");
        source.Should().Contain("_createLinksBox");
        source.Should().Contain("UiText.Get(\"Consolidate_Function\")");
        source.Should().Contain("UiText.Get(\"Consolidate_TopRow\")");
        source.Should().Contain("UiText.Get(\"Consolidate_LeftColumn\")");
        source.Should().Contain("UiText.Get(\"Consolidate_CreateLinksToSourceData\")");
        var accessKeyLabels = new[]
        {
            "_Function:",
            "_Reference:",
            "_All references:",
            "_Destination cell:",
            "_Top row",
            "Left _column",
            "Create _links to source data"
        };
        accessKeyLabels
            .GroupBy(GetAccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group)}")
            .Should()
            .BeEmpty();
        source.Should().Contain("Enum.GetValues<ConsolidateFunction>()");
        source.Should().Contain("FunctionLabel(function)");
        source.Should().Contain("ConsolidateFunction.CountNumbers => UiText.Get(\"Consolidate_FunctionCountNumbers\")");
        source.Should().Contain("SelectedFunction()");
        source.Should().NotContain("DisableUnsupported(_functionBox, SumOnlyHelpText)");
        source.Should().NotContain("DisableUnsupported(_topRowBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_leftColumnBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_createLinksBox, SourceLinksHelpText)");
        source.Should().NotContain("Source links are not available yet");
        source.Should().Contain("UseTopRowLabels");
        source.Should().Contain("UseLeftColumnLabels");
        source.Should().Contain("CreateLinksToSourceData");
        source.Should().Contain("UiText.Get(\"Consolidate_WriteFormulasThatReferenceTheSourceCellsWhileKeepingTheConsolidatedResul\")");
    }

    private static string ReadConsolidateDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialog.Planning.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ConsolidateDialogPlanner.cs"));

    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        return char.ToUpperInvariant(label[index + 1]);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMalformedSourceRange()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; nope",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid source range: nope.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMismatchedSourceSizes()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:F7",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Source ranges must be the same size.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsInvalidDestinationCell()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3",
            destinationCellText: "nope",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid destination cell.");
    }

    [Fact]
    public void DataTableDialog_ParsesOneAndTwoVariableInputs()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var oneVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "C1",
            out var oneVariable,
            out var oneVariableError);

        oneVariableParsed.Should().BeTrue(oneVariableError);
        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 3));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var rowInputParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "",
            out var rowInput,
            out var rowInputError);

        rowInputParsed.Should().BeTrue(rowInputError);
        rowInput.Mode.Should().Be(DataTableMode.OneVariable);
        rowInput.Orientation.Should().Be(DataTableInputOrientation.Row);
        rowInput.FormulaCell.Should().Be(new CellAddress(sheetId, 3, 2));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.Mode.Should().Be(DataTableMode.TwoVariable);
        twoVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        twoVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 2));
        twoVariable.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));
    }

    [Fact]
    public void DataTableDialog_ParsesExcelAbsoluteAndR1C1InputCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "$A$1",
            columnInputCellText: "R1C6",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        result.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 6));
    }

    [Fact]
    public void DataTableDialog_RejectsMissingInputCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter either a row input cell or a column input cell.");
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidOptionalInputCell()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "not-a-cell",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid column input cell.");
    }

    [Theory]
    [InlineData("B2", "", "Row input cell cannot be inside the data table range.")]
    [InlineData("", "C3", "Column input cell cannot be inside the data table range.")]
    public void DataTableDialog_RejectsInputCellInsideTableRange(
        string rowInputCellText,
        string columnInputCellText,
        string expectedError)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText,
            columnInputCellText,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    [Fact]
    public void DataTableDialog_RejectsSameRowAndColumnInputCell()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "A1",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Row and column input cells must be different.");
    }

    [Fact]
    public void DataTableDialog_ExposesReferencePickersForCellInputs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("UiText.Get(\"DataTable_RowInputLabel\")");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputLabel\")");
        source.Should().NotContain("_formulaBox");
        source.Should().NotContain("_modeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UiText.Get(\"DataTable_RowInputPickerAutomationName\")");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputPickerAutomationName\")");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().NotContain("Substitute values in the selected data table using worksheet input cells.");
        source.Should().NotContain("Header = \"Inputs\"");
        source.Should().Contain("DataTableInputParser.GetDefaultFormulaCell");
    }

    [Fact]
    public void DataTableDialog_CellInputEditorsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var dialog = new DataTableDialog(sheetId, range);
            dialog.Show();
            try
            {
                AssertTextBoxAutomation(
                    "DataTableRowInputCellBox",
                    UiText.Get("DataTable_RowInputAutomationName"),
                    UiText.Get("DataTable_RowInputAutomationHelpText"));
                AssertTextBoxAutomation(
                    "DataTableColumnInputCellBox",
                    UiText.Get("DataTable_ColumnInputAutomationName"),
                    UiText.Get("DataTable_ColumnInputAutomationHelpText"));

                void AssertTextBoxAutomation(string automationId, string name, string helpText)
                {
                    var textBox = FindVisualChildren<TextBox>(dialog)
                        .Single(box => AutomationProperties.GetAutomationId(box) == automationId);
                    AutomationProperties.GetName(textBox).Should().Be(name);
                    AutomationProperties.GetHelpText(textBox).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });

        source.Should().Contain("AutomationProperties.SetName(_rowInputBox, UiText.Get(\"DataTable_RowInputAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_rowInputBox, UiText.Get(\"DataTable_RowInputAutomationHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_columnInputBox, UiText.Get(\"DataTable_ColumnInputAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_columnInputBox, UiText.Get(\"DataTable_ColumnInputAutomationHelpText\"));");
    }

    [Fact]
    public void DataTableDialogOpenedFromKeyboard_FocusesRowInputCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_rowInputBox);");
    }

    [Fact]
    public void DataTableDialogInvalidInput_RefocusesInvalidCellEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("FocusInvalidInput(error);");
        source.Should().Contain("private void FocusInvalidInput(string? error)");
        source.Should().Contain("UiText.Get(\"DataTable_ColumnInputInsideRangeMessage\")");
        source.Should().Contain("UiText.Get(\"DataTable_SameInputCellMessage\")");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void DataTableDialogRangePicker_RefocusesSelectedInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataTableDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeSelectionInput(request.Target);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Fact]
    public void DataTableDialogRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        DataTableDialog.CreateRangeSelectionRequest(DataTableRangeSelectionTarget.ColumnInputCell, " $C$1 ")
            .Should()
            .Be(new DataTableRangeSelectionRequest(
                DataTableRangeSelectionTarget.ColumnInputCell,
                "$C$1",
                CollapseDialog: true));
    }

    [Fact]
    public void MainWindow_WiresDataTableReferencePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new DataTableDialog(");
        source.Should().Contain("request => ApplyDataTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyDataTableRangeSelection(");
        source.Should().Contain("DataTableRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, selectedRange.Start);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void DataTableApplyRangeSelection_UpdatesRequestedInputBox()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var dialog = new DataTableDialog(sheetId, range);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();

                dialog.ApplyRangeSelection(
                    DataTableRangeSelectionTarget.RowInputCell,
                    new CellAddress(sheetId, 3, 1));
                dialog.ApplyRangeSelection(
                    DataTableRangeSelectionTarget.ColumnInputCell,
                    new CellAddress(sheetId, 1, 6));

                textBoxes[0].Text.Should().Be("A3");
                textBoxes[1].Text.Should().Be("F1");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("Select row input cell", DataTableRangeSelectionTarget.RowInputCell, "A1")]
    [InlineData("Select column input cell", DataTableRangeSelectionTarget.ColumnInputCell, "C1")]
    public void DataTableDialogReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        DataTableRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var requests = new List<DataTableRangeSelectionRequest>();
            var dialog = new DataTableDialog(sheetId, range, requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                textBoxes[0].Text = " A1 ";
                textBoxes[1].Text = " C1 ";
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new DataTableRangeSelectionRequest(
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
    public void CreateTableDialog_ExposesHeadersCheckboxAndRangePicker()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("_headersBox");
        source.Should().Contain("Content = UiText.Get(\"CreateTable_HeadersCheckBox\")");
        source.Should().Contain("new Label { Content = UiText.Get(\"CreateTable_RangeLabel\"), Target = _rangeBox");
        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("UiText.Get(\"CreateTable_RangePickerAutomationName\")");
        UiText.Get("CreateTable_HeadersCheckBox").Should().Be("_My table has headers");
    }

    [Fact]
    public void CreateTableDialog_ControlsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        StaTestRunner.Run(() =>
        {
            var dialog = new CreateTableDialog(SheetId.New(), "A1:C12", "TableStyleMedium2");
            dialog.Show();
            try
            {
                var rangeBox = FindVisualChildren<TextBox>(dialog).Single();
                AutomationProperties.GetName(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationName"));
                AutomationProperties.GetAutomationId(rangeBox).Should().Be("CreateTableRangeBox");
                AutomationProperties.GetHelpText(rangeBox).Should().Be(UiText.Get("CreateTable_RangeAutomationHelpText"));

                var headersBox = FindVisualChildren<CheckBox>(dialog)
                    .Single(box => Equals(box.Content, UiText.Get("CreateTable_HeadersCheckBox")));
                AutomationProperties.GetName(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationName"));
                AutomationProperties.GetAutomationId(headersBox).Should().Be("CreateTableHeadersBox");
                AutomationProperties.GetHelpText(headersBox).Should().Be(UiText.Get("CreateTable_HeadersAutomationHelpText"));
            }
            finally
            {
                dialog.Close();
            }
        });

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"CreateTable_RangeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(\"CreateTable_RangeAutomationHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_headersBox, UiText.Get(\"CreateTable_HeadersAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_headersBox, UiText.Get(\"CreateTable_HeadersAutomationHelpText\"));");
    }

    [Fact]
    public void CreateTableDialogOpenedFromKeyboard_FocusesRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialogInvalidRange_RefocusesAndSelectsRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("FocusRangeBox();");
        source.Should().Contain("private void FocusRangeBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialog_ParsesRangeHeadersAndStyle()
    {
        var sheetId = SheetId.New();

        var parsed = CreateTableDialog.TryParse(
            sheetId,
            rangeText: " A1:C12 ",
            firstRowHasHeaders: false,
            tableStyleName: "TableStyleMedium2",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 3)));
        result.FirstRowHasHeaders.Should().BeFalse();
        result.TableStyleName.Should().Be("TableStyleMedium2");
    }

    [Fact]
    public void CreateTableDialog_RangePickerRaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<CreateTableRangeSelectionRequest>();
            var dialog = new CreateTableDialog(
                SheetId.New(),
                " A1:C12 ",
                "TableStyleMedium2",
                requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Where(button => Equals(button.Content, "..."))
                    .Single();

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresCreateTableRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("new CreateTableDialog(");
        source.Should().Contain("request => ApplyCreateTableRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyCreateTableRangeSelection(");
        source.Should().Contain("CreateTableRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End));");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void CreateTableApplyRangeSelection_UpdatesRangeBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new CreateTableDialog(SheetId.New(), "A1:C12", "TableStyleMedium2");
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection("B2:D8");

                FindVisualChildren<TextBox>(dialog).Single().Text.Should().Be("B2:D8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CreateTableDialogRangePicker_RefocusesRangeBoxAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CreateTableDialog.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private void FocusInitialKeyboardTarget", StringComparison.Ordinal)];

        handlerSource.Should().Contain("FocusRangeBox();");
        source.Should().Contain("private void FocusRangeBox()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void CreateTableRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        CreateTableDialog.CreateRangeSelectionRequest(" A1:C12 ")
            .Should()
            .Be(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
    }

    [Fact]
    public void DialogReferencePicker_RaisesSelectionRequestAndMarksCollapseAffordance()
    {
        StaTestRunner.Run(() =>
        {
            var box = new TextBox { Text = "A1:C10" };
            DialogReferencePickerRequest? captured = null;

            var request = DialogReferencePicker.RequestSelection(
                box,
                "Select table range",
                next => captured = next);

            request.Target.Should().BeSameAs(box);
            request.AutomationName.Should().Be("Select table range");
            request.CurrentText.Should().Be("A1:C10");
            captured.Should().Be(request);

            var button = DialogReferencePicker.CreateButton(box, "Select table range");
            button.ToolTip.Should().Be("Collapse dialog and select range");

            var pickerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DialogReferencePicker.cs"));
            pickerSource.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExposesExcelStyleBulkHeaderAndColumnListControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.Planning.cs"));
        var mainWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("UiText.Get(\"RemoveDuplicates_SelectAll\")");
        source.Should().Contain("UiText.Get(\"RemoveDuplicates_UnselectAll\")");
        source.Should().Contain("UiText.Get(\"RemoveDuplicates_MyDataHasHeaders\")");
        source.Should().Contain("_columnsPanel");
        source.Should().Contain("Content = UiText.Get(\"RemoveDuplicates_Columns\")");
        source.Should().Contain("Target = _columnsPanel");
        source.Should().Contain("_columnsPanel.Focusable = true");
        source.Should().Contain("_columnsPanel.GotKeyboardFocus");
        source.Should().NotContain("new TextBlock { Text = \"Columns:\"");
        source.Should().Contain("SelectAllButton_Click");
        source.Should().Contain("UnselectAllButton_Click");
        source.Should().Contain("RefreshColumnLabels");
        source.Should().Contain("HasHeaders");
        mainWindowSource.Should().Contain("RemoveDuplicatesDialog.ExcludeHeaderRow(currentRange, dialog.Result.HasHeaders)");
        mainWindowSource.Should().Contain("UiText.Format(\"MainWindowMessage_RemoveDuplicatesRemovedRows\", command?.RemovedRowCount ?? 0)");
    }

    [Fact]
    public void RemoveDuplicatesDialogOpenedFromKeyboard_FocusesHeaderChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_hasHeadersBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_hasHeadersBox);");
    }

    [Fact]
    public void RemoveDuplicatesDialogInvalidColumnSelection_FocusesColumnChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemoveDuplicatesDialog.cs"));

        source.Should().Contain("FocusFirstColumnChoice();");
        source.Should().Contain("private void FocusFirstColumnChoice()");
        source.Should().Contain("_boxes.FirstOrDefault()");
        source.Should().Contain("firstColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(firstColumnBox);");
    }

    [Fact]
    public void DataValidationNoSelectionWarning_UsesOwnedMessage()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("UiText.Get(\"MainWindowMessage_SelectRangeFirst\")");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_DataValidationTitle\")");
        source.Should().NotContain("MessageBox.Show(\"Select a range first.\", \"Data Validation\")");
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
