using System.Windows;
using System.Windows.Input;

namespace FreeX.App.Host;

public partial class MainWindow
{
    internal const double NameBoxDropdownParityCaptureWidth = 208;
    internal const double NameBoxDropdownParityCaptureHeight = 136;

    /// <summary>
    /// Opens the production Name Box popup using the same deterministic fixture as the WPF screenshot tour.
    /// Keeping the popup child as the capture target excludes the unrelated shell while preserving the real
    /// ComboBox template, typography, selection styling, and popup placement.
    /// </summary>
    internal FrameworkElement OpenNameBoxDropdownForParityCapture()
    {
        EnsureFormulaBarNameBoxTourContext();
        CellAddressBox.Focus();
        Keyboard.Focus(CellAddressBox);
        CellAddressBox.ApplyTemplate();
        CellAddressBox.IsDropDownOpen = true;
        CellAddressBox.UpdateLayout();
        UpdateLayout();

        return FindOpenPopupChild(CellAddressBox)
            ?? throw new InvalidOperationException("The WPF Name Box popup did not open for parity capture.");
    }

    internal void CloseNameBoxDropdownForParityCapture()
    {
        CellAddressBox.IsDropDownOpen = false;
    }
}
