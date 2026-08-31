using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    private static ConditionalFormatDialog ShowDialogForTest(ConditionalFormatDialog dialog)
    {
        dialog.Show();
        return dialog;
    }

    private static string ReadConditionalFormatDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "ConditionalFormatDialog.cs",
            "ConditionalFormatDialog.Catalog.cs",
            "ConditionalFormatDialog.ColorEditors.cs",
            "ConditionalFormatDialog.IconSets.cs",
            "ConditionalFormatDialog.Parsing.cs",
            "ConditionalFormatDialog.Result.cs");

    private static T GetControl<T>(ConditionalFormatDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static TextBlock? FindText(object? root, string text) =>
        root is DependencyObject dependencyObject
            ? WpfTestTree.FindLogicalSelfAndDescendants<TextBlock>(dependencyObject)
                .FirstOrDefault(block => Equals(block.Text, text))
            : null;

    private static Label? FindLabel(object? root, string content) =>
        root is DependencyObject dependencyObject
            ? WpfTestTree.FindLogicalSelfAndDescendants<Label>(dependencyObject)
                .FirstOrDefault(label => Equals(label.Content, content))
            : null;

    private static T? FindControl<T>(object? root)
        where T : DependencyObject =>
        root is DependencyObject dependencyObject
            ? WpfTestTree.FindLogicalSelfAndDescendants<T>(dependencyObject).FirstOrDefault()
            : null;

    private static T? FindNamedControl<T>(object? root, string name)
        where T : FrameworkElement =>
        root is DependencyObject dependencyObject
            ? WpfTestTree.FindLogicalSelfAndDescendants<T>(dependencyObject)
                .FirstOrDefault(element => element.Name == name)
            : null;

    private static Button? FindButton(object? root, string content) =>
        root is DependencyObject dependencyObject
            ? WpfTestTree.FindLogicalSelfAndDescendants<Button>(dependencyObject)
                .FirstOrDefault(button => Equals(button.Content, content))
            : null;

    private static void ClickOkForTest(ConditionalFormatDialog dialog)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "Ok_Click");

    private static void RefreshRuleDescriptionForTest(ConditionalFormatDialog dialog, string ruleType)
    {
        dialog.RefreshRuleDescription(ruleType);
    }

    private static GridRange RangeFor(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));
}
