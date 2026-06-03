using System;
using FluentAssertions;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void SortOptionsDialog_ExposesExcelOptionsAsRealChoices()
    {
        var source = ReadSortDialogSource();
        var optionsSource = source[source.IndexOf("public sealed class SortOptionsDialog", StringComparison.Ordinal)..];

        optionsSource.Should().Contain("Title = UiText.Get(\"SortOptions_SortOptions\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_CaseSensitive\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_FirstKeySortOrderLabel\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_FirstKeySunToSatShort\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_FirstKeyJanuaryToDecember\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_SortTopToBottom\")");
        optionsSource.Should().Contain("UiText.Get(\"SortOptions_SortLeftToRight\")");
        optionsSource.Should().Contain("Result = new SortDialogOptions");
        optionsSource.Should().Contain("FirstKeySortOrder:");
        optionsSource.Should().NotContain("IsEnabled = false");
        optionsSource.Should().NotContain("Unsupported Excel options");
    }

    [Fact]
    public void SortOptionsDialog_PreservesFirstKeySortOrderChoice()
    {
        StaTestRunner.Run(() =>
        {
            var current = new SortDialogOptions(
                CaseSensitive: true,
                LeftToRight: true,
                FirstKeySortOrder: "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec");
            var dialog = new SortOptionsDialog(current);
            dialog.Loaded += (_, _) =>
            {
                var combo = GetControl<ComboBox>(dialog, "_firstKeySortOrderBox");
                combo.SelectedValue.Should().Be(current.FirstKeySortOrder);
                combo.SelectedValue = "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday";
                dialog.Dispatcher.BeginInvoke(() => ClickDefaultButton(dialog));
            };

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new SortDialogOptions(
                CaseSensitive: true,
                LeftToRight: true,
                FirstKeySortOrder: "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday"));
        });
    }

    [Fact]
    public void SortOptionsDialogOpenedFromKeyboard_FocusesCaseSensitiveChoice()
    {
        var source = ReadSortDialogSource();
        var optionsSource = source[source.IndexOf("public sealed class SortOptionsDialog", StringComparison.Ordinal)..];

        optionsSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        optionsSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        optionsSource.Should().Contain("_caseSensitiveBox.Focus();");
        optionsSource.Should().Contain("Keyboard.Focus(_caseSensitiveBox);");
    }

    [Fact]
    public void SortOptionsDialog_FirstKeySortOrderCaptionTargetsComboBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SortOptionsDialog();
            dialog.Show();
            try
            {
                var combo = GetControl<ComboBox>(dialog, "_firstKeySortOrderBox");
                var label = FindVisualChildren<Label>(dialog)
                    .Single(candidate => Equals(candidate.Content, "_First key sort order:"));

                label.Target.Should().BeSameAs(combo);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
