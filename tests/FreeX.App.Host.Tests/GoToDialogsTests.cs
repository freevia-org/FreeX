using System.IO;
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToDialog.cs"));

        source.Should().Contain("Content = \"_Go to:\"");
        source.Should().Contain("Recent references and defined names");
        source.Should().Contain("Content = \"_Reference:\"");
        source.Should().Contain("Target = _addressBox");
        source.Should().Contain("Content = \"S_pecial...\"");
        source.Should().Contain("new GoToSpecialDialog");
        source.Should().Contain("SelectedSpecialKind");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
        source.Should().Contain("root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });");
        source.Should().NotContain("Select a named or recently used reference");
    }

    [Fact]
    public void GoToDialog_ExposesUIANamesAndHelpTextForReferenceSurfaces()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_historyList, \"Go to\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_historyList, \"Lists recent references and defined names available for navigation.\");");
        source.Should().Contain("AutomationProperties.SetName(_addressBox, \"Reference\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_addressBox, \"Enter a cell reference, range, or defined name to navigate to.\");");
    }

    [Fact]
    public void GoToDialogOpenedFromKeyboard_FocusesReferenceBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToDialog.cs"));

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
            var historyList = GetPrivateControl<ListBox>(dialog, "_historyList");
            dialog.Dispatcher.BeginInvoke(() =>
            {
                historyList.SelectedItem = "D10";

                historyList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheetId, 10, 4),
                new CellAddress(sheetId, 10, 4)));
        });
    }

    [Fact]
    public void GoToDialogInvalidReference_RefocusesAndSelectsReferenceBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToDialog.cs"));

        source.Should().Contain("FocusReferenceInput();");
        source.Should().Contain("private void FocusReferenceInput()");
        source.Should().Contain("_addressBox.Focus();");
        source.Should().Contain("_addressBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_addressBox);");
    }

    [Fact]
    public void MainWindow_GoToDialogRoutesSpecialSelectionThroughGoToSpecialService()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs"));

        source.Should().Contain("new GoToDialog(_currentSheetId, defaultAddress, _workbook.NamedRanges)");
        source.Should().Contain("dialog.SelectedSpecialKind is { } specialKind");
        source.Should().Contain("SelectGoToSpecialMatches(specialKind, dialog.SelectedSpecialOptions, showEmptyMessage: true)");
        source.Should().Contain("dialog.SelectedRange is { } selectedRange");
        source.Should().Contain("SheetGrid.SelectedRange = selectedRange");
        source.Should().Contain("CellAddressBox.Text = FormatRangeReference(selectedRange.Start, selectedRange.End)");
    }

    [Fact]
    public void MainWindow_NameBoxEnterRoutesTypedReferenceThroughGoToParser()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var editingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));

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
        var editingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToSpecialDialog.cs"));

        foreach (var expected in new[]
        {
            "new(GoToSpecialKind.Blanks, \"_Blanks\")",
            "new(GoToSpecialKind.Constants, \"_Constants\")",
            "new(GoToSpecialKind.Formulas, \"_Formulas\")",
            "new(GoToSpecialKind.Comments, \"Co_mments\")",
            "new(GoToSpecialKind.CurrentRegion, \"Current _region\")",
            "new(GoToSpecialKind.RowDifferences, \"Row _differences\")",
            "new(GoToSpecialKind.ColumnDifferences, \"Column diff_erences\")",
            "new(GoToSpecialKind.LastCell, \"_Last cell\")",
            "new(GoToSpecialKind.ConditionalFormats, \"Conditional forma_ts\")",
            "new(GoToSpecialKind.Objects, \"_Objects\")",
            "new(GoToSpecialKind.Precedents, \"_Precedents\")",
            "new(GoToSpecialKind.Dependents, \"Depe_ndents\")",
            "new(GoToSpecialKind.DataValidation, \"Data valid_ation\")",
            "new(GoToSpecialKind.VisibleCellsOnly, \"_Visible cells only\")"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Header = \"Go to special\"");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToSpecialDialog.cs"));

        source.Should().Contain("Content = \"_Numbers\"");
        source.Should().Contain("Content = \"_Text\"");
        source.Should().Contain("Content = \"_Logicals\"");
        source.Should().Contain("Content = \"_Errors\"");
        source.Should().Contain("RefreshValueTypeOptions");
        source.Should().Contain("UsesValueTypeOptions(SelectedKind)");
        source.Should().Contain("new GoToSpecialOptions(GetSelectedValueTypes())");
        source.Should().Contain("new GoToSpecialOptions()");
        source.Should().NotContain("valueTypes == GoToSpecialValueTypes.None ? GoToSpecialValueTypes.All : valueTypes");
    }

    [Fact]
    public void GoToSpecialDialogOpenedFromKeyboard_FocusesFirstChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoToSpecialDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_buttons.FirstOrDefault()?.Focus();");
        source.Should().Contain("Keyboard.Focus(firstButton);");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs"));

        source.Should().Contain("dialog.SelectedOptions");
        source.Should().Contain("SelectGoToSpecialMatches(specialKind, dialog.SelectedSpecialOptions, showEmptyMessage: true)");
        source.Should().Contain("GoToSpecialService.Find(_workbook, sheet, range, kind, range.Start, options)");
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

    private static T GetPrivateControl<T>(GoToDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(GoToDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static T GetPrivateGoToSpecialField<T>(GoToSpecialDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(GoToSpecialDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void SelectGoToSpecialChoice(GoToSpecialDialog dialog, GoToSpecialKind kind)
    {
        var buttons = GetPrivateGoToSpecialField<List<RadioButton>>(dialog, "_buttons");
        buttons.Single(button => button.Tag is GoToSpecialKind buttonKind && buttonKind == kind).IsChecked = true;
    }

    private static void SetAllValueTypeBoxes(GoToSpecialDialog dialog, bool isChecked)
    {
        foreach (var fieldName in new[] { "_numbersBox", "_textBox", "_logicalsBox", "_errorsBox" })
            GetPrivateGoToSpecialField<CheckBox>(dialog, fieldName).IsChecked = isChecked;
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
