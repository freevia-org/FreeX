using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ActivateSheetDialog_UsesExcelReferenceChromeAndSheetListLabel()
    {
        var source = ReadClassSource("ActivateSheetDialog.cs", "public sealed class ActivateSheetDialog", "public sealed record __NoNextActivateSheetDialog");

        source.Should().Contain("private const double DialogWidth = 352;");
        source.Should().Contain("private const double DialogHeight = 380;");
        source.Should().Contain("private const double ExcelButtonWidth = 90;");
        source.Should().Contain("ResizeMode = ResizeMode.NoResize;");
        source.Should().Contain("SourceInitialized += (_, _) => ApplyContextHelpButtonStyle();");
        source.Should().Contain("Content = UiText.Get(\"ActivateSheet_Title\") + \":\"");
        source.Should().Contain("Target = _sheetList");
        source.Should().Contain("_sheetList.ItemContainerStyle = CreateSheetListItemStyle();");
        source.Should().Contain("_sheetList.Height = 260;");
        source.Should().Contain("private static Style CreateSheetListItemStyle()");
        source.Should().Contain("SystemColors.InactiveSelectionHighlightBrushKey");
        source.Should().Contain("SystemColors.InactiveSelectionHighlightTextBrushKey");
        source.Should().Contain("new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0))");
        source.Should().Contain("new Setter(Control.FontSizeProperty, 10.5)");
        source.Should().Contain("new Setter(Control.BorderBrushProperty, System.Windows.Media.Brushes.Transparent)");
        source.Should().Contain("new Setter(Control.BorderThicknessProperty, new Thickness(1))");
        source.Should().Contain("new Setter(FrameworkElement.HeightProperty, 13.0)");
        source.Should().Contain("new Setter(Control.FocusVisualStyleProperty, null)");
        source.Should().Contain("new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }");
        source.Should().Contain("new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush)");
        source.Should().Contain("new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush)");
        source.Should().Contain("new Setter(Control.BorderBrushProperty, SystemColors.ControlTextBrush)");
        source.Should().Contain("new Thickness(10, 8, 10, 10)");
        source.Should().Contain("_sheetList.Margin = new Thickness(0, 0, 0, 16);");
        source.Should().Contain("DialogButtonRowFactory.Create(_okButton, _cancelButton)");
    }

    [Fact]
    public void ActivateSheetDialog_InitiallySelectsFirstVisibleSheetLikeExcelReference()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            var first = workbook.AddSheet("Sheet1");
            var second = workbook.AddSheet("Sheet2");
            var dialog = new ActivateSheetDialog(workbook, second.Id);

            try
            {
                var sheetList = GetField<ListBox>(dialog, "_sheetList");
                var selected = sheetList.SelectedItem.Should().BeOfType<SheetDialogTarget>().Subject;

                selected.DisplayName.Should().Be("Sheet1");
                selected.SheetId.Should().Be(first.Id);
                dialog.Result.Should().Be(new ActivateSheetDialogResult(first.Id));
                AutomationProperties.GetAutomationId(sheetList).Should().Be(FreeXAutomationIdCatalog.ActivateSheetList);
                AutomationProperties.GetName(sheetList).Should().Be(UiText.Get("ActivateSheet_ListAutomationName"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ActivateSheetDialog_OkButtonTracksSelectedSheetAndDoubleClickAccepts()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var second = workbook.AddSheet("Sheet2");
            var dialog = new ActivateSheetDialog(workbook, second.Id);
            var sheetList = GetField<ListBox>(dialog, "_sheetList");
            var okButton = GetField<Button>(dialog, "_okButton");

            okButton.IsDefault.Should().BeTrue();
            okButton.IsEnabled.Should().BeTrue();

            sheetList.SelectedItem = null;
            okButton.IsEnabled.Should().BeFalse();

            var secondTarget = sheetList.Items.Cast<SheetDialogTarget>().Single(target => target.SheetId == second.Id);
            dialog.Dispatcher.BeginInvoke(() =>
            {
                sheetList.SelectedItem = secondTarget;
                var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();
                sheetList.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new ActivateSheetDialogResult(second.Id));
        });
    }
}
