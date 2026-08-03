using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R120 (MED): the Avalonia Manage Rules dialog's scope ComboBox was hardcoded to exactly two
/// literal strings ("This Worksheet" / "Current Selection"), unlike the WPF host's
/// <see cref="FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner.CreateDialogPlan"/>-
/// driven dropdown, which also offers "This Table" whenever the selection sits inside a structured
/// table. A user working in a table on Linux/macOS could never scope the rule list to just that
/// table, even though Excel and the Windows host both offer it. The fix reuses
/// <c>CreateDialogPlan</c> to populate the Avalonia scope ComboBox, matching the WPF host.
///
/// Note: assertions run AFTER the dialog task completes, not inside the launch-smoke-probe
/// callback -- a probe exception raised from the dialog's Opened handler does not propagate back
/// to the awaiting test (it is swallowed by the Avalonia dispatcher), so a probe-internal
/// <c>Should()</c> failure would silently pass. The probe below only captures state; every
/// assertion happens on the captured snapshots afterwards.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R120_ManageConditionalFormatsTableScopeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    // R120 fail-before/pass-after: selecting a cell inside a structured table must offer a "This
    // Table" scope option, and switching to it must filter the rule list to only the rules
    // overlapping the table's range -- not the rules overlapping the (narrower) cell selection and
    // not every rule on the sheet.
    [Fact]
    public async Task ActiveCellInTable_ScopeBoxOffersThisTable_AndFiltersToTableRange()
    {
        int? itemCount = null;
        List<string?>? itemLabels = null;
        bool tableScopeSelected = false;
        List<System.Guid>? shownAfterTableScope = null;
        System.Guid insideTableRuleId = default;
        System.Guid outsideTableRuleId = default;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.ActiveSheet;

                var table = new StructuredTableModel
                {
                    Id = 1,
                    Name = "ScopeTable",
                    Range = Range(sheet.Id, 1, 1, 5, 3), // A1:C5
                };
                sheet.StructuredTables.Add(table);

                // Rule inside the table but outside the single-cell selection (A1) -- only Table
                // scope should surface it.
                var insideTableRule = new ConditionalFormat
                {
                    RuleType = CfRuleType.Blanks,
                    AppliesTo = Range(sheet.Id, 4, 1, 4, 3), // A4:C4
                };
                sheet.ConditionalFormats.Add(insideTableRule);
                insideTableRuleId = insideTableRule.Id;

                // Rule entirely outside the table -- must never surface under Table scope.
                var outsideTableRule = new ConditionalFormat
                {
                    RuleType = CfRuleType.Blanks,
                    AppliesTo = Range(sheet.Id, 1, 5, 1, 5), // E1
                };
                sheet.ConditionalFormats.Add(outsideTableRule);
                outsideTableRuleId = outsideTableRule.Id;

                // Select a single cell (A1) that is inside the table's range but does not overlap
                // either rule directly -- this is what makes FindSelectionTableRange find the table.
                window.Session.SelectCell(table.Range.Start);

                await window.ShowManageConditionalFormatsDialogAsync(probe =>
                {
                    itemCount = probe.ScopeBox.ItemCount;
                    itemLabels = Enumerable.Range(0, probe.ScopeBox.ItemCount)
                        .Select(i => probe.ScopeBox.Items[i]?.ToString())
                        .ToList();

                    var tableItemIndex = itemLabels.FindIndex(label =>
                        string.Equals(label, UiText.Get("ManageConditionalFormats_ScopeThisTable"), System.StringComparison.Ordinal));

                    if (tableItemIndex >= 0)
                    {
                        // SelectionChanged fires synchronously in Avalonia headless, so Reload()
                        // has already run by the time this setter returns.
                        probe.ScopeBox.SelectedIndex = tableItemIndex;
                        tableScopeSelected = true;
                        shownAfterTableScope = (probe.ListBox.ItemsSource as IEnumerable<ConditionalFormatRuleListItem>)?
                            .Select(item => item.Id)
                            .ToList();
                    }
                });
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        itemCount.Should().Be(3,
            "Sheet, Table, and Selection scopes should all be offered when the selection sits " +
            "inside a structured table");
        itemLabels.Should().Contain(UiText.Get("ManageConditionalFormats_ScopeThisTable"));
        tableScopeSelected.Should().BeTrue("a \"This Table\" option must exist and be selectable");
        shownAfterTableScope.Should().NotBeNull();
        shownAfterTableScope!.Should().Contain(insideTableRuleId,
            "the rule inside the table's range must show under Table scope");
        shownAfterTableScope.Should().NotContain(outsideTableRuleId,
            "a rule entirely outside the table's range must not show under Table scope");
    }

    // No-regression sibling: when the selection is NOT inside any structured table, the scope
    // ComboBox must keep offering exactly the original two options (Worksheet, Selection) and
    // Selection-scope filtering must keep working exactly as before this change.
    [Fact]
    public async Task ActiveCellNotInTable_ScopeBoxOffersOnlyWorksheetAndSelection()
    {
        int? itemCount = null;
        bool selectionScopeSelected = false;
        List<System.Guid>? shownAfterSelectionScope = null;
        System.Guid ruleAtSelectionId = default;
        System.Guid ruleElsewhereId = default;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.ActiveSheet;

                var ruleAtSelection = new ConditionalFormat
                {
                    RuleType = CfRuleType.Blanks,
                    AppliesTo = Range(sheet.Id, 1, 1, 1, 1), // A1
                };
                sheet.ConditionalFormats.Add(ruleAtSelection);
                ruleAtSelectionId = ruleAtSelection.Id;

                var ruleElsewhere = new ConditionalFormat
                {
                    RuleType = CfRuleType.Blanks,
                    AppliesTo = Range(sheet.Id, 10, 10, 10, 10),
                };
                sheet.ConditionalFormats.Add(ruleElsewhere);
                ruleElsewhereId = ruleElsewhere.Id;

                window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

                await window.ShowManageConditionalFormatsDialogAsync(probe =>
                {
                    itemCount = probe.ScopeBox.ItemCount;

                    var labels = Enumerable.Range(0, probe.ScopeBox.ItemCount)
                        .Select(i => probe.ScopeBox.Items[i]?.ToString())
                        .ToList();
                    var selectionIndex = labels.FindIndex(label =>
                        string.Equals(label, UiText.Get("ManageConditionalFormats_ScopeCurrentSelection"), System.StringComparison.Ordinal));

                    if (selectionIndex >= 0)
                    {
                        probe.ScopeBox.SelectedIndex = selectionIndex;
                        selectionScopeSelected = true;
                        shownAfterSelectionScope = (probe.ListBox.ItemsSource as IEnumerable<ConditionalFormatRuleListItem>)?
                            .Select(item => item.Id)
                            .ToList();
                    }
                });
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        itemCount.Should().Be(2,
            "no structured table overlaps the selection, so only Worksheet and Selection scopes " +
            "should be offered");
        selectionScopeSelected.Should().BeTrue();
        shownAfterSelectionScope.Should().NotBeNull();
        shownAfterSelectionScope!.Should().Contain(ruleAtSelectionId);
        shownAfterSelectionScope.Should().NotContain(ruleElsewhereId);
    }
}
