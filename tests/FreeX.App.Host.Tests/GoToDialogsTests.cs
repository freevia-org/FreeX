using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class GoToDialogsTests
{
    [Fact]
    public void TryParseAddress_AcceptsA1ReferenceOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseAddress("B5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Fact]
    public void TryParseAddress_AcceptsExcelAbsoluteA1Reference()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseAddress("$B$5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotACell")]
    [InlineData("A0")]
    public void TryParseAddress_RejectsInvalidReference(string input)
    {
        GoToDialog.TryParseAddress(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseReference_ResolvesDefinedNameToRangeStart()
    {
        var sheetId = SheetId.New();
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = new(
                new CellAddress(sheetId, 10, 2),
                new CellAddress(sheetId, 12, 4))
        };

        GoToDialog.TryParseReference("sales_total", sheetId, names, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 10, 2));
    }

    [Fact]
    public void TryParseReferenceRange_ResolvesDefinedNameToFullRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 10, 2),
            new CellAddress(sheetId, 12, 4));
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = range
        };

        GoToDialog.TryParseReferenceRange("sales_total", sheetId, names, out var parsed).Should().BeTrue();

        parsed.Should().Be(range);
    }

    [Fact]
    public void TryParseReferenceRange_AcceptsTypedCurrentSheetRange()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseReferenceRange("A1:C3", sheetId, definedNames: null, out var range).Should().BeTrue();

        range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void TryParseReferenceRange_AcceptsExcelAbsoluteA1Range()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseReferenceRange("$A$1:$C$3", sheetId, definedNames: null, out var range).Should().BeTrue();

        range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void BuildReferenceChoices_PutsDefaultThenRecentThenSortedNamesWithoutDuplicates()
    {
        var choices = GoToDialog.BuildReferenceChoices(
            "B5",
            ["B5", "D10"],
            ["zName", "Alpha"]);

        choices.Should().Equal("B5", "D10", "Alpha", "zName");
    }

    [Fact]
    public void GoToDialog_ExposesKeyboardAccessKeysForReferenceAndButtons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToDialog.cs");

        source.Should().Contain("Content = UiText.Get(\"GoTo_GoTo2\")");
        source.Should().Contain("UiText.Get(\"GoTo_RecentReferencesAndDefinedNames\")");
        source.Should().Contain("Content = UiText.Get(\"GoTo_Reference\")");
        source.Should().Contain("Target = _addressBox");
        source.Should().Contain("Content = UiText.Get(\"GoTo_Special\")");
        source.Should().Contain("new GoToSpecialDialog");
        source.Should().Contain("SelectedSpecialKind");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });");
        source.Should().NotContain("Select a named or recently used reference");
    }

    [Fact]
    public void GoToSpecialParityCapture_SupportsFocusedProductionDialogRoute()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.GoToSpecial\", StringComparison.Ordinal)");
        source.Should().Contain("CaptureDialog(results, \"dialog.GoToSpecial\", outDir, () => new GoToSpecialDialog())");
        source.Should().Contain("dialog.GoToSpecial, dialog.Sparkline");
    }

    [Fact]
    public void GoToDialog_ExposesUIANamesAndHelpTextForReferenceSurfaces()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToDialog.cs");

        source.Should().Contain("AutomationProperties.SetName(_historyList, UiText.Get(\"GoTo_GoTo\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_historyList, UiText.Get(\"GoTo_ListsRecentReferencesAndDefinedNamesAvailableForNavigation\"));");
        source.Should().Contain("AutomationProperties.SetName(_addressBox, UiText.Get(\"GoTo_Reference2\"));");
        source.Should().Contain("AutomationProperties.SetHelpText(_addressBox, UiText.Get(\"GoTo_EnterACellReferenceRangeOrDefinedNameToNavigateTo\"));");
    }

    [Fact]
    public void GoToDialogOpenedFromKeyboard_FocusesReferenceBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_addressBox.Focus();");
        source.Should().Contain("_addressBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_addressBox);");
    }

    [Fact]
    public void GoToDialogReferenceList_DoubleClickAcceptsSelectedReference()
    {
        var sheetId = SheetId.New();
        StaTestRunner.Run(() =>
        {
            var dialog = new GoToDialog(sheetId, defaultAddress: "A1", recentReferences: ["D10"]);
            var historyList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_historyList");
            dialog.Dispatcher.BeginInvoke(() =>
            {
                historyList.SelectedItem = "D10";

                var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();
                historyList.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheetId, 10, 4),
                new CellAddress(sheetId, 10, 4)));
        });
    }

    [Fact]
    public void GoToDialogReferenceList_DoubleClickWithoutSelectionDoesNotHandleMouseEvent()
    {
        var sheetId = SheetId.New();
        StaTestRunner.Run(() =>
        {
            var dialog = new GoToDialog(sheetId, defaultAddress: "A1", recentReferences: ["D10"]);
            var historyList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_historyList");
            historyList.SelectedItem = null;

            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            historyList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
            dialog.SelectedRange.Should().BeNull();
        });
    }

    [Fact]
    public void GoToDialogInvalidReference_RefocusesAndSelectsReferenceBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToDialog.cs");

        source.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("private void FocusReferenceInput()");
        source.Should().Contain("_addressBox.Focus();");
        source.Should().Contain("_addressBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_addressBox);");
    }

    [Fact]
    public void MainWindow_GoToDialogRoutesSpecialSelectionThroughGoToSpecialService()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        source.Should().Contain("var goToDefinedNames = GoToDialogPlanner.BuildDefinedNamesForSheet(_workbook, _currentSheetId);");
        source.Should().Contain("resolveSheetId: ResolveSheetIdByName,");
        source.Should().Contain("resolveScopedName: (name, sheetId) => _workbook.TryGetNamedRange(name, sheetId, out var scoped) ? scoped : null");
        source.Should().Contain("dialog.SelectedSpecialKind is { } specialKind");
        source.Should().Contain("SelectGoToSpecialMatches(specialKind, dialog.SelectedSpecialOptions, showEmptyMessage: true)");
        source.Should().Contain("dialog.SelectedRange is { } selectedRange");
        source.Should().Contain("SheetGrid.SelectedRange = selectedRange");
        source.Should().Contain("CellAddressBox.Text = FormatNameBoxSelectionText(selectedRange)");
    }

    [Fact]
    public void MainWindow_NameBoxEnterRoutesTypedReferenceThroughGoToParser()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        xaml.Should().Contain("KeyDown=\"CellAddressBox_KeyDown\"");
        editingSource.Should().Contain("if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers != ModifierKeys.None)");
        editingSource.Should().Contain("_workbook.NamedRanges");
        editingSource.Should().Contain("SetSelectionRange(selectedRange, selectedRange.Start);");
        editingSource.Should().Contain("UpdateViewport();");
        editingSource.Should().Contain("RefreshValidationDropdown();");
    }

    [Fact]
    public void MainWindow_NameBoxEscapeCancelsTypedReference()
    {
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        editingSource.Should().Contain("if (e.Key == Key.Escape && e.KeyboardDevice.Modifiers == ModifierKeys.None)");
        editingSource.Should().Contain("RestoreCellAddressBoxText();");
        editingSource.Should().Contain("FocusSheetGridIfNeeded();");
        editingSource.Should().Contain("CellAddressBox.SelectAll();");
    }

    [Fact]
    public void GetChoices_ExposesExcelGoToSpecialCoreChoices()
    {
        var choices = GoToSpecialDialog.GetChoices();

        choices.Select(choice => choice.Kind).Should().Contain([
            GoToSpecialKind.Blanks,
            GoToSpecialKind.Constants,
            GoToSpecialKind.Formulas,
            GoToSpecialKind.Comments,
            GoToSpecialKind.CurrentRegion,
            GoToSpecialKind.RowDifferences,
            GoToSpecialKind.ColumnDifferences,
            GoToSpecialKind.LastCell,
            GoToSpecialKind.ConditionalFormats,
            GoToSpecialKind.Objects,
            GoToSpecialKind.Precedents,
            GoToSpecialKind.Dependents,
            GoToSpecialKind.DataValidation,
            GoToSpecialKind.VisibleCellsOnly]);
    }

    [Fact]
    public void GoToSpecialDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToSpecialDialog.cs");

        source.Should().Contain("GoToSpecialDialogPlanner.BuildChoices(CreateDialogText())");
        source.Should().Contain("private static GoToSpecialDialogText CreateDialogText()");

        foreach (var expectedResource in new[]
        {
            "GoToSpecial_Blanks",
            "GoToSpecial_Constants",
            "GoToSpecial_Formulas",
            "GoToSpecial_Comments",
            "GoToSpecial_CurrentRegion",
            "GoToSpecial_RowDifferences",
            "GoToSpecial_ColumnDifferences",
            "GoToSpecial_LastCell",
            "GoToSpecial_ConditionalFormats",
            "GoToSpecial_Objects",
            "GoToSpecial_Precedents",
            "GoToSpecial_Dependents",
            "GoToSpecial_DataValidation",
            "GoToSpecial_VisibleCellsOnly"
        })
            source.Should().Contain($"UiText.Get(\"{expectedResource}\")");

        source.Should().Contain("Header = UiText.Get(\"GoToSpecial_GoToSpecial\")");
        source.Should().NotContain("Header = \"Additional Excel options\"");
        source.Should().NotContain("IsEnabled = false");
        source.Should().NotContain("shown for parity");
        source.Should().NotContain("The selectable options match");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void GoToSpecialDialog_UsesUniqueChoiceAccessKeys()
    {
        var duplicateAccessKeys = GoToSpecialDialog.GetChoices()
            .Select(choice => new { choice.Label, AccessKey = GetAccessKey(choice.Label) })
            .Where(choice => choice.AccessKey is not null)
            .GroupBy(choice => choice.AccessKey)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(choice => choice.Label))}");

        duplicateAccessKeys.Should().BeEmpty();
    }

    [Fact]
    public void GoToSpecialDialog_ExposesExcelConstantsAndFormulasSuboptions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToSpecialDialog.cs");

        source.Should().Contain("Content = UiText.Get(\"GoToSpecial_Numbers\")");
        source.Should().Contain("Content = UiText.Get(\"GoToSpecial_Text\")");
        source.Should().Contain("Content = UiText.Get(\"GoToSpecial_Logicals\")");
        source.Should().Contain("Content = UiText.Get(\"GoToSpecial_Errors\")");
        source.Should().Contain("RefreshValueTypeOptions");
        source.Should().Contain("GoToSpecialDialogPlanner.UsesValueTypeOptions(kind)");
        source.Should().Contain("GoToSpecialDialogPlanner.BuildOptions(SelectedKind, GetSelectedValueTypes())");
        source.Should().NotContain("valueTypes == GoToSpecialValueTypes.None ? GoToSpecialValueTypes.All : valueTypes");
    }

    [Fact]
    public void GoToSpecialDialogOpenedFromKeyboard_FocusesFirstChoice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("GoToSpecialDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("var firstButton = FirstButton();");
        source.Should().Contain("firstButton?.Focus();");
        source.Should().Contain("Keyboard.Focus(firstButton);");
        source.Should().Contain("private RadioButton? FirstButton()");
        source.Should().Contain("foreach (var button in _buttons)");
        source.Should().Contain("return button;");
    }

    [Fact]
    public void GoToSpecialDialog_ConstantsWithNoValueTypesReturnsNone()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new GoToSpecialDialog();
            dialog.Show();
            try
            {
                SelectGoToSpecialChoice(dialog, GoToSpecialKind.Constants);
                SetAllValueTypeBoxes(dialog, isChecked: false);

                InvokePrivateAllowingNonModalDialogResult(dialog, "Accept");

                dialog.SelectedKind.Should().Be(GoToSpecialKind.Constants);
                dialog.SelectedOptions.ValueTypes.Should().Be(GoToSpecialValueTypes.None);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void GoToSpecialDialog_DisabledValueTypeStateDoesNotLeakToOtherChoices()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new GoToSpecialDialog();
            dialog.Show();
            try
            {
                SelectGoToSpecialChoice(dialog, GoToSpecialKind.Constants);
                SetAllValueTypeBoxes(dialog, isChecked: false);
                SelectGoToSpecialChoice(dialog, GoToSpecialKind.Blanks);

                InvokePrivateAllowingNonModalDialogResult(dialog, "Accept");

                dialog.SelectedKind.Should().Be(GoToSpecialKind.Blanks);
                dialog.SelectedOptions.ValueTypes.Should().Be(GoToSpecialValueTypes.All);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_GoToSpecialPassesDialogValueTypeOptionsToService()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        source.Should().Contain("dialog.SelectedOptions");
        source.Should().Contain("SelectGoToSpecialMatches(specialKind, dialog.SelectedSpecialOptions, showEmptyMessage: true)");
        source.Should().Contain("GoToSpecialService.Find(_workbook, sheet, searchRange, kind, activeCell, options)");
    }

    [Fact]
    public void TryParseChoice_MapsDisplayTextThroughExistingParser()
    {
        GoToSpecialDialog.TryParseChoice("Data validation", out var kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.DataValidation);

        GoToSpecialDialog.TryParseChoice("conditional formats", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.ConditionalFormats);

        GoToSpecialDialog.TryParseChoice("objects", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Objects);

        GoToSpecialDialog.TryParseChoice("precedents", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Precedents);

        GoToSpecialDialog.TryParseChoice("dependents", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Dependents);
    }

    private static char? GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        if (index < 0 || index + 1 >= label.Length)
            return null;

        return char.ToUpperInvariant(label[index + 1]);
    }

    private static void SelectGoToSpecialChoice(GoToSpecialDialog dialog, GoToSpecialKind kind)
    {
        var buttons = DialogSourceTestSupport.GetPrivateField<List<RadioButton>>(dialog, "_buttons");
        buttons.Single(button => button.Tag is GoToSpecialKind buttonKind && buttonKind == kind).IsChecked = true;
    }

    private static void SetAllValueTypeBoxes(GoToSpecialDialog dialog, bool isChecked)
    {
        foreach (var fieldName in new[] { "_numbersBox", "_textBox", "_logicalsBox", "_errorsBox" })
            DialogSourceTestSupport.GetPrivateField<CheckBox>(dialog, fieldName).IsChecked = isChecked;
    }

    private static void InvokePrivateAllowingNonModalDialogResult(GoToSpecialDialog dialog, string methodName)
    {
        var method = typeof(GoToSpecialDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, []);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation &&
                                                   invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }
}
