using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Test-only seam for View &gt; Show &gt; Formula Bar.</summary>
    internal void ToggleFormulaBarVisibilityForTest() => ToggleFormulaBarVisibility();

    /// <summary>Test-only seam applying File &gt; Options &gt; Show formula bar through the live-options path.</summary>
    internal void ApplyOptionsShowFormulaBarForTest(bool showFormulaBar) =>
        ApplyLiveOptions(new OptionsDialogPlanner.OptionsDialogInput(
            DefaultFontName: "Calibri",
            DefaultFontSize: 11,
            DefaultSheetCount: 1,
            UserName: "",
            AutoCalculate: true,
            UseR1C1ReferenceStyle: UseR1C1ReferenceStyle,
            ErrorCheckingEnabled: true,
            ProofingIgnoreUppercase: true,
            ProofingIgnoreNumbers: true,
            ShowFormulaBar: showFormulaBar,
            ShowGridlines: _session.IsShowingGridlines,
            ShowHeadings: _session.IsShowingHeadings,
            DefaultFormat: "xlsx",
            ShowScreenTips: true,
            MoveSelectionAfterEnter: true,
            AfterEnterDirection: AppOptionsEnterDirection.Down));

    /// <summary>Test-only view of the formula bar row and its input: (row, input, name box) visibility.</summary>
    internal (bool Row, bool Input, bool NameBox) FormulaBarVisibilityForTest =>
        (_formulaBarHost.IsVisible, _formulaBox.IsVisible, _cellAddressText.IsVisible);

    /// <summary>Test-only seam for the formula bar expand chevron / Ctrl+Shift+U.</summary>
    internal void ToggleFormulaBarExpansionForTest()
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        ApplyFormulaBarExpansion();
    }
}
