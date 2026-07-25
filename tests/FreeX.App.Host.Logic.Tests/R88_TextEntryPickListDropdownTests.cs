using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R88-app-autocomplete-picklist-5-1 (MainWindow.EditingDropdowns.cs's
/// <c>OpenActiveDropdown</c>): Alt+Down (and right-click &gt; "Pick From Drop-down List...") on a
/// plain text cell with no Data Validation rule and no AutoFilter header never opened anything --
/// Excel's classic "Pick From Drop-down List" pick list, built from the active cell's contiguous
/// column text block, was entirely unimplemented. <c>OpenActiveDropdown</c> only ever tried
/// <c>RefreshValidationDropdown</c> (a no-op with no DV rule) then
/// <c>OpenAutoFilterDropdownForActiveCell</c> (a no-op when the cell isn't a filter header), with no
/// third branch.
/// </summary>
public sealed class R88_TextEntryPickListDropdownTests
{
    [Fact]
    public void OpenActiveDropdown_ForPlainTextColumn_ShowsPickListOfUniqueEntries()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var a2 = new CellAddress(sheet.Id, 2, 1);
                var a3 = new CellAddress(sheet.Id, 3, 1);
                var a4 = new CellAddress(sheet.Id, 4, 1);
                sheet.SetCell(a1, new TextValue("Apple"));
                sheet.SetCell(a2, new TextValue("Banana"));
                sheet.SetCell(a3, new TextValue("Apple")); // duplicate -- must be de-duplicated

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a4);
                R49MainWindowTestHarness.Invoke(window, "OpenActiveDropdown");

                var dropdown = GetValidationDropdown(window);
                dropdown.Should().NotBeNull(
                    "a plain cell below existing text entries must offer Excel's classic pick list");
                dropdown!.Visibility.Should().Be(Visibility.Visible);
                dropdown.ItemsSource.Should().NotBeNull();
                var items = dropdown.ItemsSource!.Cast<string>().ToList();
                items.Should().Equal("Apple", "Banana");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an isolated cell with no adjacent text (and no DV/AutoFilter) must not
    // have Alt+Down pop open a spurious empty dropdown.
    [Fact]
    public void OpenActiveDropdown_ForIsolatedCellWithNoAdjacentText_OpensNothing()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var isolated = new CellAddress(sheet.Id, 1, 1);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", isolated);
                R49MainWindowTestHarness.Invoke(window, "OpenActiveDropdown");

                var dropdown = GetValidationDropdown(window);
                (dropdown is null || dropdown.Visibility != Visibility.Visible).Should().BeTrue(
                    "an isolated cell with no adjacent text and no Data Validation/AutoFilter must not open any dropdown");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static ComboBox? GetValidationDropdown(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_validationDropdown", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_validationDropdown");
        return (ComboBox?)field.GetValue(window);
    }
}
