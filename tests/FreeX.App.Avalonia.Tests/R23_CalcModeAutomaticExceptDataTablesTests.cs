using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-23 regression guard for finding R23-workbook-calc-settings-1: the Avalonia
/// "Automatic Except Data Tables" menu item (Formulas ▸ Calculation ▸ Calculation Options)
/// used to be wired to <c>SetCalculationModeAutomatic</c>, so choosing it silently set the
/// workbook to plain <see cref="WorkbookCalculationMode.Automatic"/> instead of
/// <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/> — a parity regression versus
/// the Windows host's dedicated <c>CalcAutoExceptDataTablesMenuItem_Click</c>. The fix adds a
/// dedicated <c>SetCalculationModeAutomaticExceptDataTables</c> handler and re-wires the menu
/// item to it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R23_CalcModeAutomaticExceptDataTablesTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SetCalculationModeAutomaticExceptDataTables_SetsDedicatedMode_NotPlainAutomatic()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);

            // Sanity: the workbook must not already be in the target mode, otherwise the
            // assertion below would be vacuously true.
            window.Session.Workbook.CalculationMode.Should().NotBe(WorkbookCalculationMode.AutomaticExceptDataTables);

            InvokePrivate(window, "SetCalculationModeAutomaticExceptDataTables");

            window.Session.Workbook.CalculationMode.Should().Be(
                WorkbookCalculationMode.AutomaticExceptDataTables,
                "the 'Automatic Except Data Tables' menu handler must set the dedicated " +
                "AutomaticExceptDataTables calculation mode, not fall back to plain Automatic");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SetCalculationModeAutomaticExceptDataTables_DiffersFromPlainAutomaticHandler()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);

            InvokePrivate(window, "SetCalculationModeAutomaticExceptDataTables");
            var modeAfterExceptDataTables = window.Session.Workbook.CalculationMode;

            InvokePrivate(window, "SetCalculationModeAutomatic");
            var modeAfterPlainAutomatic = window.Session.Workbook.CalculationMode;

            modeAfterExceptDataTables.Should().Be(WorkbookCalculationMode.AutomaticExceptDataTables);
            modeAfterPlainAutomatic.Should().Be(WorkbookCalculationMode.Automatic);
            modeAfterExceptDataTables.Should().NotBe(modeAfterPlainAutomatic,
                "the two menu choices are distinct Excel calculation modes and must not collapse to the same value");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
