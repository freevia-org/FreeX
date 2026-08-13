using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    private static ConditionalFormat CloneWithPriority(ConditionalFormat source, int priority, Guid? id = null) =>
        ManageConditionalFormatsPlanner.CloneWithPriority(source, priority, id);

    private static T GetControl<T>(ManageConditionalFormatsDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static IReadOnlyList<string> ScopeContents(ComboBox scope) =>
        scope.Items
            .Cast<ComboBoxItem>()
            .Select(item => item.Content?.ToString() ?? "")
            .ToList();

    private static ComboBoxItem ScopeItem(ComboBox scope, string content) =>
        scope.Items
            .Cast<ComboBoxItem>()
            .Single(item => Equals(item.Content, content));

    private static string ReadManageConditionalFormatsDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "ManageConditionalFormatsDialog.cs",
            "ManageConditionalFormatsDialog.Columns.cs");

    private static ConditionalFormat CreateRule(
        SheetId sheetId,
        uint row,
        uint col,
        int priority,
        Guid? id = null,
        bool stopIfTrue = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sheetId, row, col), new CellAddress(sheetId, row, col)),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            StopIfTrue = stopIfTrue
        };
}
