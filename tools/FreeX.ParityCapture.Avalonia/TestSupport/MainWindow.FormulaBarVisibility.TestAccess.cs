namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Test-only seam for View &gt; Show &gt; Formula Bar.</summary>
    internal void ToggleFormulaBarVisibilityForTest() => ToggleFormulaBarVisibility();

    /// <summary>Test-only seam applying File &gt; Options &gt; Show formula bar through the live-options path.</summary>
    internal void ApplyOptionsShowFormulaBarForTest(bool showFormulaBar)
    {
        var options = _optionsRuntimeSession.LiveOptions.Clone();
        options.ShowFormulaBar = showFormulaBar;
        ApplyFormulaBarOptions(options);
    }

    /// <summary>Test-only view of the formula bar row and its input: (row, input, name box) visibility.</summary>
    internal (bool Row, bool Input, bool NameBox) FormulaBarVisibilityForTest =>
        (_formulaBarHost.IsVisible, _formulaBox.IsVisible, _cellAddressText.IsVisible);

    /// <summary>Test-only seam for the formula bar expand chevron / Ctrl+Shift+U.</summary>
    internal void ToggleFormulaBarExpansionForTest() => ToggleFormulaBarExpansion();
}
