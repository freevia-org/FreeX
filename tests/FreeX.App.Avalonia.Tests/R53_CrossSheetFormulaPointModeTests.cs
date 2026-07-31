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

    [Fact]
    public async Task ModifierSheetTabsAndCtrlPointing_KeepSourceAndAppendQualifiedReference()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sourceSheet = window.Session.ActiveSheet;
            var revenueSheet = window.Session.Workbook.AddSheet("Revenue Data");
            var summarySheet = window.Session.Workbook.AddSheet("Summary Data");
            var source = new CellAddress(sourceSheet.Id, 10, 10);
            var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

            window.BeginFormulaEditForTest(source, "=");
            window.RaiseSheetTabModifierClickForTest(revenueSheet.Id, KeyModifiers.Control);
            window.Session.ActiveSheet.Should().BeSameAs(revenueSheet);
            window.Session.FormulaEditAddress.Should().Be(source);
            window.Session.IsWorkbookGrouped.Should().BeTrue();

            window.SetFormulaBoxSelectionForTest(1, 0);
            Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(revenueSheet.Id, 2, 1))
                .Should().BeTrue();
            window.RaiseSheetTabModifierClickForTest(summarySheet.Id, KeyModifiers.Control);
            window.Session.FormulaEditAddress.Should().Be(source);

            Invoke<bool>(window, "TryAppendDisjointFormulaPointReference", new CellAddress(summarySheet.Id, 3, 2))
                .Should().BeTrue();
            formulaBox.Text.Should().Be("='Revenue Data'!A2,'Summary Data'!B3");
            window.Session.ActiveSheet.Should().BeSameAs(summarySheet);
            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(summarySheet.Id, 3, 2),
                new CellAddress(summarySheet.Id, 3, 2)));

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
            window.Session.ActiveSheet.Should().BeSameAs(sourceSheet);
            window.Session.ActiveCell.Should().Be(source);
            window.Session.FormulaEditAddress.Should().BeNull();
            sourceSheet.GetCell(source).Should().BeNull();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointAppend_RecoversMissingTrackedSpanAndCalculatesDisjointAreas()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var formulaCell = new CellAddress(sheet.Id, 5, 5);
                var firstArea = new CellAddress(sheet.Id, 5, 6);
                var secondArea = new CellAddress(sheet.Id, 7, 6);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");
                sheet.SetCell(firstArea, new NumberValue(10));
                sheet.SetCell(secondArea, new NumberValue(20));

                window.BeginFormulaEditForTest(formulaCell, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);
                Invoke<bool>(window, "TryInsertFormulaPointReference", firstArea)
                    .Should().BeTrue();

                // This is the transient state observed after the physical Linux point click:
                // the text still contains F5, but the tracked span has not survived focus return.
                typeof(MainWindow).GetField("_formulaReferenceStart", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, null);
                typeof(MainWindow).GetField("_formulaReferenceLength", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, null);

                Invoke<bool>(window, "TryAppendDisjointFormulaPointReference", secondArea)
                    .Should().BeTrue();
                formulaBox.Text.Should().Be("=SUM(F5,F7");

                formulaBox.Text += ")";
                window.SetFormulaBoxSelectionForTest(formulaBox.Text.Length, 0);
                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

                sheet.GetCell(formulaCell)!.FormulaText.Should().Be("SUM(F5,F7)");
                sheet.GetValue(formulaCell).Should().Be(new NumberValue(30));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointEdit_PreservesQuotedAreaSpanBeforeReplacingExistingArea()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var qualifiedSheet = window.Session.Workbook.AddSheet("Revenue Data");
                var formulaCell = new CellAddress(sourceSheet.Id, 5, 5);
                var firstArea = new CellAddress(qualifiedSheet.Id, 5, 6);
                var authoredSecondArea = new CellAddress(qualifiedSheet.Id, 7, 8);
                var replacementArea = new CellAddress(qualifiedSheet.Id, 7, 10);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");
                qualifiedSheet.SetCell(firstArea, new NumberValue(10));
                qualifiedSheet.SetCell(replacementArea, new NumberValue(20));

                window.BeginFormulaEditForTest(formulaCell, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest(formulaBox.Text.Length, 0);
                window.RaiseSheetTabModifierClickForTest(qualifiedSheet.Id, KeyModifiers.None);

                Invoke<bool>(window, "TryInsertFormulaPointReference", firstArea).Should().BeTrue();
                Invoke<bool>(window, "TryAppendDisjointFormulaPointReference", authoredSecondArea)
                    .Should().BeTrue();
                formulaBox.Text.Should().Be("=SUM('Revenue Data'!F5,'Revenue Data'!H7");

                // Replace H7 with an equal-length caret edit. The subsequent point must replace
                // that existing area rather than inserting a third reference at the caret.
                // A physical Shift+Left selection is reverse-ordered in Avalonia: the anchor
                // remains at the end of the reference while the active end moves left.
                formulaBox.SelectionStart = formulaBox.Text.Length;
                formulaBox.SelectionEnd = formulaBox.Text.Length - 2;
                formulaBox.CaretIndex = formulaBox.SelectionStart;
                formulaBox.Text = "=SUM('Revenue Data'!F5,'Revenue Data'!I7";
                window.SetFormulaBoxSelectionForTest(formulaBox.Text.Length, 0);
                Invoke<bool>(window, "TryInsertFormulaPointReference", replacementArea)
                    .Should().BeTrue();

                formulaBox.Text.Should().Be("=SUM('Revenue Data'!F5,'Revenue Data'!J7");
                window.Session.SelectedRange.Should().Be(new GridRange(replacementArea, replacementArea));

                formulaBox.Text += ")";
                window.SetFormulaBoxSelectionForTest(formulaBox.Text.Length, 0);
                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

                sourceSheet.GetCell(formulaCell)!.FormulaText
                    .Should().Be("SUM('Revenue Data'!F5,'Revenue Data'!J7)");
                sourceSheet.GetValue(formulaCell).Should().Be(new NumberValue(30));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftSheetTabsInFormulaPointMode_EmitThreeDSheetSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var startSheet = window.Session.Workbook.AddSheet("Sheet2");
                var endSheet = window.Session.Workbook.AddSheet("Sheet3");
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(source, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);
                window.RaiseSheetTabModifierClickForTest(startSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);

                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();
                formulaBox.Text.Should().Be("=SUM(Sheet2:Sheet3!B2");
                window.Session.FormulaEditAddress.Should().Be(source);

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
                window.Session.ActiveSheet.Should().BeSameAs(sourceSheet);
                window.Session.ActiveCell.Should().Be(source);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointingAfterTypingOperator_DropsAbandonedSheetSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var startSheet = window.Session.Workbook.AddSheet("Sheet2");
                var endSheet = window.Session.Workbook.AddSheet("Sheet3");
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(source, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);
                window.RaiseSheetTabModifierClickForTest(startSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();

                formulaBox.Text = "=SUM(Sheet2:Sheet3!B2+";
                window.SetFormulaBoxSelectionForTest("=SUM(Sheet2:Sheet3!B2+".Length, 0);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 3, 3))
                    .Should().BeTrue();

                formulaBox.Text.Should().Be("=SUM(Sheet2:Sheet3!B2+Sheet3!C3");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointingExtendsLiveReference_WithoutDroppingThreeDSheetSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var startSheet = window.Session.Workbook.AddSheet("Sheet2");
                var endSheet = window.Session.Workbook.AddSheet("Sheet3");
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(source, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);
                window.RaiseSheetTabModifierClickForTest(startSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();
                Invoke<bool>(window, "TryApplyFormulaRangeSelection", new CellAddress(endSheet.Id, 4, 3), true)
                    .Should().BeTrue();

                formulaBox.Text.Should().Be("=SUM(Sheet2:Sheet3!B2:C4");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointingReverseExtendsThreeDSheetRange_PreservesDirectionalAnchor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var startSheet = window.Session.Workbook.AddSheet("Sheet2");
                var endSheet = window.Session.Workbook.AddSheet("Sheet3");
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(source, "=");
                formulaBox.Text = "=SUM(";
                window.SetFormulaBoxSelectionForTest(formulaBox.Text.Length, 0);
                window.RaiseSheetTabModifierClickForTest(startSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();
                Invoke<bool>(window, "TryApplyFormulaRangeSelection", new CellAddress(endSheet.Id, 1, 1), true)
                    .Should().BeTrue();

                formulaBox.Text.Should().Be("=SUM(Sheet2:Sheet3!A1:B2");
                window.Session.ActiveCell.Should().Be(new CellAddress(endSheet.Id, 2, 2),
                    "reverse 3-D formula pointing must retain the original cell anchor");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineCaretMovedOutsideLiveReference_DropsThreeDSheetSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var startSheet = window.Session.Workbook.AddSheet("Sheet2");
                var endSheet = window.Session.Workbook.AddSheet("Sheet3");
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginInlineCellEditForTest(source, "=", 1);
                var inlineEditor = GetField<global::Avalonia.Controls.TextBox>(window, "_inlineCellEditor");
                inlineEditor.Text = "=SUM(";
                inlineEditor.CaretIndex = inlineEditor.Text.Length;
                inlineEditor.SelectionStart = inlineEditor.Text.Length;
                inlineEditor.SelectionEnd = inlineEditor.Text.Length;
                window.RaiseSheetTabModifierClickForTest(startSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();

                inlineEditor.SelectionStart = 0;
                inlineEditor.SelectionEnd = 0;
                inlineEditor.CaretIndex = 0;
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 3, 3))
                    .Should().BeTrue();

                formulaBox.Text.Should().Contain("Sheet3!C3");
                formulaBox.Text.Should().NotContain("Sheet2:Sheet3!C3");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlColumnHeaderPointing_AppendsWholeColumnAreaWithoutCommittingEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                var source = new CellAddress(sourceSheet.Id, 1, 1);
                var formulaBox = GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(source, "=");
                window.SetFormulaBoxSelectionForTest(1, 0);
                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(sourceSheet.Id, 2, 1))
                    .Should().BeTrue();

                typeof(MainWindow)
                    .GetMethod("AddAdditionalColumnSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [(uint)5]);

                formulaBox.Text.Should().Be("=A2,E:E");
                window.Session.FormulaEditAddress.Should().Be(source);
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sourceSheet.Id, 1, 5),
                    new CellAddress(sourceSheet.Id, CellAddress.MaxRow, 5)));
                sourceSheet.GetCell(source)!.HasFormula.Should().BeFalse(
                    "header pointing must not commit the formula edit");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) as T
        ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Invoke<T>(MainWindow window, string name, params object[] args)
    {
        var method = typeof(MainWindow)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
            {
                if (candidate.Name != name)
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == args.Length &&
                    parameters.Zip(args).All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second));
            });
        return (T)method.Invoke(window, args)!;
    }
}
