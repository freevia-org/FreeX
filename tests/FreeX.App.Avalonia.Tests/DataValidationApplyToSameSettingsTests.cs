using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for H33 (HIGH): the Avalonia Data Validation dialog's "Apply these changes to
/// all other cells with the same settings" checkbox was rendered and fully interactive, but its
/// IsChecked value was never read by Accept() and never reached the session/command layer — so
/// checking it had zero effect: only the originally selected range was updated, silently leaving
/// every other same-settings cell with the old, now-inconsistent validation rule.
///
/// The fix threads the checkbox state through <c>DataValidationDialogResult.ApplyToSameSettings</c>
/// and, when set, sweeps every data-validation range on the sheet whose settings match the rule that
/// was being edited (mirroring the WPF host's data-validation sweep) via the shared session API
/// <see cref="WorkbookSession.ApplyDataValidationToSelectedRangeAndMatchingRanges"/> — exercised here
/// directly against <c>window.Session</c> without driving the full dialog UI.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class DataValidationApplyToSameSettingsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ApplyToSameSettings_UpdatesEveryRangeSharingTheOldRulesSettings_NotJustTheSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheetId = window.Session.ActiveSheet.Id;

                // Arrange: two disjoint ranges (A1 and B1) both carrying the SAME WholeNumber
                // Between 1-10 validation rule — simulating a rule that was applied broadly.
                var originalRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "10",
                };

                var a1 = new CellAddress(sheetId, 1, 1);
                window.Session.SelectCell(a1);
                window.Session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();

                var b1 = new CellAddress(sheetId, 1, 2);
                window.Session.SelectCell(b1);
                window.Session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();

                // Capture the rule as it exists on the sheet before editing (this is what Excel/WPF
                // compares candidates against), then edit it (Formula2 20 instead of 10).
                var existingRule = window.Session.ActiveSheet.DataValidations[0];
                var editedRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "20",
                };

                // Act: select ONLY A1 (as if the user opened the dialog for A1's rule, edited it, and
                // checked "Apply to all other cells with the same settings") and run the sweep.
                window.Session.SelectCell(a1);
                var outcome = window.Session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

                // Assert
                outcome.Success.Should().BeTrue(outcome.ErrorMessage);
                outcome.Mutated.Should().BeTrue();

                var rules = window.Session.ActiveSheet.DataValidations;
                rules.Should().Contain(r => r.AppliesTo.Contains(a1) && r.Formula2 == "20",
                    "the originally selected range (A1) must carry the edited rule");
                rules.Should().Contain(r => r.AppliesTo.Contains(b1) && r.Formula2 == "20",
                    "B1 shared the old rule's settings, so it must be swept to the edited rule too — " +
                    "this is exactly the behavior the checkbox promises and previously failed to deliver");
                rules.Should().NotContain(r => r.Formula2 == "10",
                    "no range should be left behind on the stale rule once the sweep runs");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyToSameSettings_LeavesUnrelatedRulesWithDifferentSettingsUntouched()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheetId = window.Session.ActiveSheet.Id;

                var wholeNumberRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "10",
                };
                var a1 = new CellAddress(sheetId, 1, 1);
                window.Session.SelectCell(a1);
                window.Session.ApplyDataValidationToSelectedRange(wholeNumberRule).Success.Should().BeTrue();

                // A DIFFERENT rule (List type) on C1 must never be touched by a WholeNumber sweep.
                var listRule = new DataValidation
                {
                    Type = DvType.List,
                    Formula1 = "X,Y,Z",
                };
                var c1 = new CellAddress(sheetId, 1, 3);
                window.Session.SelectCell(c1);
                window.Session.ApplyDataValidationToSelectedRange(listRule).Success.Should().BeTrue();

                var editedRule = new DataValidation
                {
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "99",
                };

                window.Session.SelectCell(a1);
                var outcome = window.Session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, wholeNumberRule);

                outcome.Success.Should().BeTrue(outcome.ErrorMessage);

                var rules = window.Session.ActiveSheet.DataValidations;
                rules.Should().Contain(r => r.AppliesTo.Contains(c1) && r.Type == DvType.List,
                    "the unrelated List rule on C1 must survive the sweep untouched");
                rules.Should().Contain(r => r.AppliesTo.Contains(a1) && r.Formula2 == "99");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
