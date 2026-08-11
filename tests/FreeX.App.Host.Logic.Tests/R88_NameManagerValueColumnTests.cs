using System.Linq;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R88-app-name-manager-ui-5-2 (NamedRangeDialog.xaml.cs): the Name
/// Manager's Value column always duplicated the Refers To text verbatim (<c>FormatValue</c> simply
/// delegated to the same <c>FormatRange</c> formatter used for Refers To, and the named-formula
/// listing loops passed <c>formulaText</c> for both constructor arguments) instead of showing the
/// name's actual live computed value/preview.
/// </summary>
public sealed class R88_NameManagerValueColumnTests
{
    [Fact]
    public void RefreshList_ForNamedConstantFormula_ShowsComputedValueNotFormulaTextAgain()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            // Deliberately a formula whose computed result's text (39) differs from the raw formula
            // source text (1+38) -- a bare numeric-literal formula would make Value == RefersTo
            // trivially true even without the fix, since the "computed value" of a literal number
            // renders identically to the literal itself.
            workbook.NamedFormulas["TaxRate"] = "1+38";

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var vm = GetListedNames(dialog).Single(v => v.Name == "TaxRate");

                vm.RefersTo.Should().Be("=1+38", "Refers To must show the canonical formula source text");
                vm.Value.Should().Be(
                    "39", "the Value column must show the name's live computed value, not just repeat Refers To");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RefreshList_ForSingleCellNamedRange_ShowsCellValueNotRangeReference()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
            workbook.DefineNamedRange(
                "MyNum", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var vm = GetListedNames(dialog).Single(v => v.Name == "MyNum");

                vm.RefersTo.Should().Be("Sheet1!A1:A1");
                vm.Value.Should().Be(
                    "42", "a single-cell named range's Value column must show the cell's own computed value");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: a multi-cell named range must still list Refers To as the plain range
    // reference text, unaffected by the new Value computation -- only the Value column changed, and
    // it must render a small array-literal preview rather than blowing up or duplicating Refers To.
    [Fact]
    public void RefreshList_ForMultiCellNamedRange_StillListsRefersToRangeReference_AndShowsArrayValuePreview()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(100));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(200));
            workbook.DefineNamedRange(
                "Sales", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var vm = GetListedNames(dialog).Single(v => v.Name == "Sales");

                vm.RefersTo.Should().Be("Sheet1!A1:A2");
                vm.Value.Should().Be("{100;200}");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static List<DefinedNameRow> GetListedNames(NamedRangeDialog dialog) =>
        DialogSourceTestSupport.GetPrivateField<ListView>(dialog, "NamesList")
            .ItemsSource!
            .Cast<DefinedNameRow>()
            .ToList();

    private static Func<IWorkbookCommand, CommandOutcome> CreateCommandBus(Workbook workbook)
    {
        var commandBus = new CommandBus(_ => new TestCommandContext(workbook));
        return command => commandBus.Execute(workbook.Id, command);
    }
}
