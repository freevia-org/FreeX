using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-50 finding R50-commands-name-manager-crud-3-2
/// (src/FreeX.App.Host/NamedRangeDialog.xaml.cs): the WPF Name Manager could not list, create, or
/// edit named FORMULAS/constants (Workbook.NamedFormulas / ScopedNamedFormulas) at all -- only
/// plain cell-range names (Workbook.NamedRanges / ScopedNamedRanges) were supported. RefreshList()
/// enumerated only the range dictionaries, so an existing named formula/constant never appeared in
/// the list (invisible/undeletable), and DefineOrUpdateName rejected any Refers To text that wasn't
/// a parseable range with "Invalid range format", with no fallback to
/// <see cref="DefineNamedFormulaCommand"/>.
/// </summary>
public sealed class R50_NameManagerNamedFormulaCrudTests
{
    // ── Listing ────────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshList_IncludesWorkbookNamedFormulasAndConstants()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            workbook.NamedFormulas["TaxRate"] = "0.0825";

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var viewModels = GetListedNames(dialog);

                viewModels.Should().Contain(
                    vm => vm.Name == "TaxRate" && vm.RefersTo == "0.0825",
                    "named formulas/constants must be visible in the Name Manager just like named ranges");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: a plain named range defined alongside a named formula must still be
    // listed correctly -- the fix must not disturb the pre-existing NamedRanges enumeration.
    [Fact]
    public void RefreshList_StillListsPlainNamedRangesAlongsideFormulas()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet = workbook.AddSheet("Sheet1");
            workbook.DefineNamedRange(
                "Sales",
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));
            workbook.NamedFormulas["TaxRate"] = "0.0825";

            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            try
            {
                var viewModels = GetListedNames(dialog);

                viewModels.Should().Contain(vm => vm.Name == "Sales" && vm.RefersTo == "Sheet1!A1:A2");
                viewModels.Should().Contain(vm => vm.Name == "TaxRate" && vm.RefersTo == "0.0825");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    // ── Create ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DefineOrUpdateName_CreatesNamedFormulaWhenRefersToIsNotAParseableRange()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                InvokeDefineOrUpdateName(
                    dialog,
                    new NameDefinitionDialogResult("VatRate", "Workbook", "", "0.20"),
                    originalName: null,
                    originalScope: null);

                workbook.NamedFormulas.Should().ContainKey("VatRate");
                workbook.NamedFormulas["VatRate"].Should().Be("0.20");
                workbook.NamedRanges.Should().NotContainKey("VatRate");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // Sibling no-regression: the original Refers-To-is-a-range creation path (DefineNamedRangeCommand)
    // must still work exactly as before for text that does parse as a range.
    [Fact]
    public void DefineOrUpdateName_StillCreatesPlainNamedRangeWhenRefersToIsAParseableRange()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book");
            var sheet = workbook.AddSheet("Sheet1");
            var dialog = new NamedRangeDialog(workbook, CreateCommandBus(workbook));
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                InvokeDefineOrUpdateName(
                    dialog,
                    new NameDefinitionDialogResult("Sales2", "Workbook", "", "Sheet1!A1:A2"),
                    originalName: null,
                    originalScope: null);

                workbook.NamedRanges.Should().ContainKey("Sales2");
                workbook.NamedRanges["Sales2"].Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));
                workbook.NamedFormulas.Should().NotContainKey("Sales2");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
                dialog.Close();
            }
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<NamedRangeViewModel> GetListedNames(NamedRangeDialog dialog) =>
        DialogSourceTestSupport.GetPrivateField<ListView>(dialog, "NamesList")
            .ItemsSource!
            .Cast<NamedRangeViewModel>()
            .ToList();

    private static void InvokeDefineOrUpdateName(
        NamedRangeDialog dialog,
        NameDefinitionDialogResult definition,
        string? originalName,
        string? originalScope,
        SheetId? originalScopeSheetId = null)
    {
        var method = typeof(NamedRangeDialog).GetMethod("DefineOrUpdateName", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(NamedRangeDialog), "DefineOrUpdateName");
        method.Invoke(dialog, [definition, originalName, originalScope, originalScopeSheetId]);
    }

    private static ICommandBus CreateCommandBus(Workbook workbook) =>
        new CommandBus(_ => new TestCommandContext(workbook));
}
