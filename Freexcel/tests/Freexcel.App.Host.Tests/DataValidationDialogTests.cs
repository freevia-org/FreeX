using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Freexcel.App.Host;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class DataValidationDialogTests
{
    [Fact]
    public void DataValidationDialog_ContainsRangePickerButtonsForBothFormulaFields()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        xaml.Should().Contain("x:Name=\"UseSelectionButton\"");
        xaml.Should().Contain("x:Name=\"UseSelection2Button\"");
        xaml.Should().Contain("Click=\"UseSelectionButton_Click\"");
        xaml.Should().Contain("Click=\"UseSelection2Button_Click\"");
    }

    [Fact]
    public void DataValidationDialog_ExposesKeyboardAccessKeysForOptionsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        foreach (var content in new[]
        {
            "_Use Selection",
            "Use _Selection",
            "_In-cell dropdown",
            "_Allow blank",
            "Show _input message when cell is selected",
            "Show error _alert after invalid data is entered",
            "C_lear All",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain($"Content=\"{content}\"");
    }

    [Fact]
    public void UseSelection2Button_PopulatesFormula2()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog { SelectionSource = "=Sheet1!$B$2:$B$8" };
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "UseSelection2Button_Click");

                GetControl<TextBox>(dialog, "Formula2Box").Text.Should().Be("=Sheet1!$B$2:$B$8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T GetControl<T>(DataValidationDialog dialog, string name)
        where T : class
    {
        var field = typeof(DataValidationDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void InvokePrivate(DataValidationDialog dialog, string methodName)
    {
        var method = typeof(DataValidationDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }
}
