using System;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const int SisterAppClientFrameFirstRow = 1;

    private void ApplySisterAppClientFrameContractRows()
    {
        Grid.SetRow(TitleBarRoot, 0);
        RootGrid.RowDefinitions[0].Height = GridLength.Auto;

        var contract = SisterAppClientFrameContractPlanner.Plan(
            topPanelsBelowChrome: 2,
            bottomPanelsAboveStatus: 1);
        var slotRow = SisterAppClientFrameFirstRow;

        foreach (var slot in contract.Slots)
        {
            switch (slot)
            {
                case { Role: SisterAppClientFrameSlotRole.Chrome, Index: 0 }:
                    ApplyRootFrameSlot(RibbonTabs, slotRow, GridLength.Auto);
                    break;
                case { Role: SisterAppClientFrameSlotRole.TopPanelBelowChrome, Index: 0 }:
                    ApplyRootFrameSlot(BelowRibbonQatRoot, slotRow, GridLength.Auto);
                    break;
                case { Role: SisterAppClientFrameSlotRole.TopPanelBelowChrome, Index: 1 }:
                    ApplyRootFrameSlot(FormulaBarBorder, slotRow, GridLength.Auto);
                    break;
                case { Role: SisterAppClientFrameSlotRole.WorkArea, Index: 0 }:
                    ApplyRootFrameSlot(WorkbookWorkAreaRoot, slotRow, new GridLength(1, GridUnitType.Star));
                    break;
                case { Role: SisterAppClientFrameSlotRole.BottomPanelAboveStatus, Index: 0 }:
                    ApplyRootFrameSlot(SheetTabsPanelRoot, slotRow, GridLength.Auto);
                    break;
                case { Role: SisterAppClientFrameSlotRole.StatusBar, Index: 0 }:
                    ApplyRootFrameSlot(StatusBarRoot, slotRow, GridLength.Auto);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected FreeX sister-app frame slot {slot.Role} index {slot.Index}.");
            }

            slotRow++;
        }
    }

    private void ApplyRootFrameSlot(UIElement element, int row, GridLength height)
    {
        Grid.SetRow(element, row);
        RootGrid.RowDefinitions[row].Height = height;
    }

    private void UpdateMaximizedContentInset()
    {
        if (RootGrid is null)
            return;

        RootGrid.Margin = WindowState == WindowState.Maximized
            ? GetMaximizedSafeInset()
            : new Thickness(0);
    }

    private static Thickness GetMaximizedSafeInset()
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        var inset = Math.Ceiling(Math.Max(
            MaximizedSafeInsetDip,
            Math.Max(
                Math.Max(resize.Left, resize.Right),
                Math.Max(resize.Top, resize.Bottom))));

        return new Thickness(inset);
    }

}
