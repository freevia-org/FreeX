using System.Windows.Controls;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DataValidationDialogTests
{
    private static T GetControl<T>(DataValidationDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static void InvokePrivate(DataValidationDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName);

    private static void InvokePrivateAllowingNonModalDialogResult(DataValidationDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, methodName);

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private static string? SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void AssertUniqueAccessKeys(params string[] resourceKeys)
    {
        var labels = resourceKeys.Select(UiText.Get).ToArray();
        labels.Select(ExtractAccessKey).Should().OnlyHaveUniqueItems();
    }

    private static char ExtractAccessKey(string label)
    {
        var index = label.IndexOf('_');
        index.Should().BeGreaterThanOrEqualTo(0, $"'{label}' should expose an access key");
        (index + 1).Should().BeLessThan(label.Length, $"'{label}' should not end with an access-key marker");

        return char.ToUpperInvariant(label[index + 1]);
    }
}
