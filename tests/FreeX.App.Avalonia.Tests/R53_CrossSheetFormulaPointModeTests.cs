using System.Reflection;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R53_CrossSheetFormulaPointModeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormulaPointingAcrossSheetTabs_QualifiesReferenceAndRestoresSourceOnCancel()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sourceSheet = window.Session.ActiveSheet;
            var targetSheet = window.Session.Workbook.AddSheet("Revenue Data");
            var source = new CellAddress(sourceSheet.Id, 10, 10);
            var pointed = new CellAddress(targetSheet.Id, 2, 2);
            var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

            window.BeginFormulaEditForTest(source, "=");
            window.RaiseSheetTabModifierClickForTest(targetSheet.Id, KeyModifiers.None);
            window.Session.ActiveSheet.Should().BeSameAs(targetSheet);
            window.Session.FormulaEditAddress.Should().Be(source);

            window.SetFormulaBoxSelectionForTest(1, 0);
            Invoke<bool>(window, "TryInsertFormulaPointReference", pointed).Should().BeTrue();
            formulaBox.Text.Should().Be("='Revenue Data'!B2");
            window.Session.SelectedRange.Should().Be(new GridRange(pointed, pointed));

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

            window.Session.ActiveSheet.Should().BeSameAs(sourceSheet);
            window.Session.ActiveCell.Should().Be(source);
            window.Session.SelectedRange.Should().Be(new GridRange(source, source));
            window.Session.FormulaEditAddress.Should().BeNull();
            sourceSheet.GetCell(source).Should().BeNull();
            window.Close();
        }, CancellationToken.None);
    }

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) as T
        ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Invoke<T>(MainWindow window, string name, params object[] args) =>
        (T)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, args)!;
}
