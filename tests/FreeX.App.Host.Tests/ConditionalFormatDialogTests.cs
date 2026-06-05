using System;
using System.Collections.Generic;
using System.IO;
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
        string.Join(Environment.NewLine, new[]
        {
            "ConditionalFormatDialog.cs",
            "ConditionalFormatDialog.Catalog.cs",
            "ConditionalFormatDialog.ColorEditors.cs",
            "ConditionalFormatDialog.IconSets.cs",
            "ConditionalFormatDialog.Parsing.cs",
            "ConditionalFormatDialog.Result.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));

    private static T GetControl<T>(ConditionalFormatDialog dialog, string name)
        where T : class
    {
        var field = typeof(ConditionalFormatDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static TextBlock? FindText(object? root, string text) =>
        FindLogicalSelfAndDescendants<TextBlock>(root).FirstOrDefault(block => Equals(block.Text, text));

    private static Label? FindLabel(object? root, string content) =>
        FindLogicalSelfAndDescendants<Label>(root).FirstOrDefault(label => Equals(label.Content, content));

    private static T? FindControl<T>(object? root)
        where T : DependencyObject =>
        FindLogicalSelfAndDescendants<T>(root).FirstOrDefault();

    private static T? FindNamedControl<T>(object? root, string name)
        where T : FrameworkElement =>
        FindLogicalSelfAndDescendants<T>(root).FirstOrDefault(element => element.Name == name);

    private static Button? FindButton(object? root, string content) =>
        FindLogicalSelfAndDescendants<Button>(root).FirstOrDefault(button => Equals(button.Content, content));

    private static IEnumerable<T> FindLogicalSelfAndDescendants<T>(object? root)
        where T : DependencyObject
    {
        if (root is not DependencyObject dependencyObject)
            yield break;

        if (dependencyObject is T match)
            yield return match;

        foreach (var descendant in WpfTestTree.FindLogicalDescendants<T>(dependencyObject))
            yield return descendant;
    }

    private static void ClickOkForTest(ConditionalFormatDialog dialog)
    {
        var method = typeof(ConditionalFormatDialog).GetMethod("Ok_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("DialogResult"))
        {
            // The handler creates ResultRule before setting DialogResult. Direct modeless invocation in
            // tests reaches WPF's modal-only postcondition after the behavior under test runs.
        }
    }

    private static void RefreshRuleDescriptionForTest(ConditionalFormatDialog dialog, string ruleType)
    {
        var method = typeof(ConditionalFormatDialog).GetMethod("RefreshRuleDescription", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [ruleType]);
    }

    private static GridRange RangeFor(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));
}
