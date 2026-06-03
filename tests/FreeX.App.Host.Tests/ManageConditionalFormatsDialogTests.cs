using System.IO;
using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    private static ConditionalFormat CloneWithPriority(ConditionalFormat source, int priority, Guid? id = null)
    {
        var method = typeof(ManageConditionalFormatsDialog)
            .GetMethod("CloneWithPriority", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, [source, priority, id]).Should().BeOfType<ConditionalFormat>().Subject;
    }

    private static T GetControl<T>(ManageConditionalFormatsDialog dialog, string name)
        where T : class
    {
        var field = typeof(ManageConditionalFormatsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

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
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ManageConditionalFormatsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ManageConditionalFormatsDialog.Columns.cs")));

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
