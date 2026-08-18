using System.Reflection;
using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R143-freex-datavalidation-DV-1: <c>MainWindow.CreateDataValidationCommand</c> — the WPF host's
/// "Apply these changes to all other cells with the same settings" implementation — ignored the
/// user's current selection entirely once the checkbox was checked. It retargeted every matched
/// rule at that rule's OLD <see cref="DataValidation.AppliesTo"/> instead of the live selection
/// (`rule.AppliesTo`, set moments earlier from <c>GetCurrentSelectionRanges</c>), so widening the
/// selection past the rule being edited (e.g. editing A1:A10's rule while A1:A20 is selected) left
/// the newly-selected cells (A11:A20) with no validation at all. A second, compounding defect on
/// the same lines passed an explicit empty <see cref="DataValidation.AdditionalRanges"/> to every
/// retargeted clone, so a matched rule's own disjoint areas (e.g. an unrelated C1:C10 sharing the
/// same settings) lost their rule outright, even without any selection-widening involved.
///
/// <c>CreateDataValidationCommand</c> is private and <see cref="MainWindow"/> is a real STA
/// <see cref="Window"/> (no headless constructor), so these tests build a real window via
/// <see cref="StaTestRunner"/> (mirroring <c>FreeXCleanupB10Tests</c>' reflection pattern) and
/// invoke the method directly, applying the returned <see cref="IWorkbookCommand"/> against the
/// window's own session workbook via <c>TestCommandContext</c>.
/// </summary>
public sealed class R143_DataValidationApplyToSameSettingsSelectionLossTests
{
    [Fact]
    public void CreateDataValidationCommand_SelectionWidenedPastEditedRule_WidenedSelectionReceivesTheNewRule()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var sheet = window.Session.Workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Seed A1:A10 with a Whole Number 1-10 rule -- the rule the dialog was opened on.
                var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
                window.Session.SelectRange(a1a10);
                window.Session
                    .ApplyDataValidationToSelectedRange(new DataValidation
                    {
                        Type = DvType.WholeNumber,
                        Operator = DvOperator.Between,
                        Formula1 = "1",
                        Formula2 = "10",
                    })
                    .Success.Should().BeTrue();
                var existingRule = sheet.DataValidations.Should().ContainSingle().Which;
                existingRule.AppliesTo.Should().Be(a1a10);

                // User widens the selection to A1:A20 (past the rule's own old range) and edits the
                // rule's upper bound to 100, with "Apply to all cells with the same settings" on.
                var a1a20 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 1));
                window.Session.SelectRange(a1a20);
                var editedRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "100",
                    AppliesTo = a1a20,
                };

                var command = InvokeCreateDataValidationCommand(window, sheetId, editedRule, existingRule, applyToSameSettings: true);
                command.Apply(new TestCommandContext(window.Session.Workbook));

                var rules = sheet.DataValidations;
                rules.Should().Contain(r => r.AppliesTo == a1a20 && r.Formula2 == "100",
                    "the widened selection A1:A20 -- what the user actually selected -- must receive the edited rule");
                rules.Should().NotContain(r => r.Formula2 == "10",
                    "the stale rule must not survive under its old, narrower A1:A10 footprint once " +
                    "the wider selection has been given the new settings");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            }
        });
    }

    [Fact]
    public void CreateDataValidationCommand_MatchedRuleHasDisjointAdditionalRange_AdditionalRangeSurvivesWithNewSettings()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var sheet = window.Session.Workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // A single rule spanning two disjoint areas: A1:A10 (AppliesTo) plus C1:C10
                // (AdditionalRanges) -- the shape Excel produces for one validation rule applied to
                // a Ctrl+click multi-area selection.
                var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
                var c1c10 = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 10, 3));
                var existingRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "10",
                    AppliesTo = a1a10,
                };
                existingRule.AdditionalRanges.Add(c1c10);
                sheet.DataValidations.Add(existingRule);

                // User re-selects only A1:A20 (widening past A1:A10, but NOT touching C1:C10) and
                // edits the rule, with "Apply to all cells with the same settings" on.
                var a1a20 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 1));
                window.Session.SelectRange(a1a20);
                var editedRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "100",
                    AppliesTo = a1a20,
                };

                var command = InvokeCreateDataValidationCommand(window, sheetId, editedRule, existingRule, applyToSameSettings: true);
                command.Apply(new TestCommandContext(window.Session.Workbook));

                var rules = sheet.DataValidations;
                var c1 = new CellAddress(sheetId, 1, 3);
                rules.Should().Contain(r => RangeContains(r, c1) && r.Formula2 == "100",
                    "C1:C10 shared the edited rule's old settings via AdditionalRanges and was not " +
                    "reselected -- it must survive under the new settings instead of losing its " +
                    "validation outright");
                rules.Should().Contain(r => r.AppliesTo == a1a20 && r.Formula2 == "100",
                    "the widened selection A1:A20 must also receive the edited rule");
                rules.Should().NotContain(r => r.Formula2 == "10",
                    "no range should be left behind on the stale rule once the sweep runs");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            }
        });
    }

    [Fact]
    public void CreateDataValidationCommand_UnrelatedRuleWithDifferentSettings_IsLeftUntouched()
    {
        StaTestRunner.Run(() =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var sheet = window.Session.Workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
                window.Session.SelectRange(a1a10);
                window.Session
                    .ApplyDataValidationToSelectedRange(new DataValidation
                    {
                        Type = DvType.WholeNumber,
                        Operator = DvOperator.Between,
                        Formula1 = "1",
                        Formula2 = "10",
                    })
                    .Success.Should().BeTrue();
                var existingRule = sheet.DataValidations.Should().ContainSingle().Which;

                // A DIFFERENT rule (List type) elsewhere -- must never be touched by this sweep.
                var c1 = new CellAddress(sheetId, 1, 3);
                window.Session.SelectCell(c1);
                window.Session
                    .ApplyDataValidationToSelectedRange(new DataValidation
                    {
                        Type = DvType.List,
                        Formula1 = "X,Y,Z",
                    })
                    .Success.Should().BeTrue();

                window.Session.SelectRange(a1a10);
                var editedRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "99",
                    AppliesTo = a1a10,
                };

                var command = InvokeCreateDataValidationCommand(window, sheetId, editedRule, existingRule, applyToSameSettings: true);
                command.Apply(new TestCommandContext(window.Session.Workbook));

                var rules = sheet.DataValidations;
                rules.Should().Contain(r => RangeContains(r, c1) && r.Type == DvType.List,
                    "the unrelated List rule on C1 must survive the sweep untouched");
                rules.Should().Contain(r => r.AppliesTo == a1a10 && r.Formula2 == "99");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            }
        });
    }

    private static bool RangeContains(DataValidation rule, CellAddress address) =>
        rule.AppliesTo.Contains(address) || rule.AdditionalRanges.Any(r => r.Contains(address));

    private static IWorkbookCommand InvokeCreateDataValidationCommand(
        MainWindow window,
        SheetId sheetId,
        DataValidation rule,
        DataValidation existingRule,
        bool applyToSameSettings)
    {
        var method = typeof(MainWindow).GetMethod("CreateDataValidationCommand", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CreateDataValidationCommand");
        return (IWorkbookCommand)method.Invoke(window, [sheetId, rule, existingRule, applyToSameSettings])!;
    }

    private static MainWindow CreateWindow()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var viewportService = new ViewportService();
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            viewportService,
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbook,
            NullUserMessageService.Instance)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };
    }
}
