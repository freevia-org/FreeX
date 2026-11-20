using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>The last fill-handle drag, remembered so the Auto Fill Options button can redo it.</summary>
public sealed record AutofillOptionsState(GridRange Source, GridRange Fill, bool CtrlHeld, AutofillMode Mode);

/// <summary>One choice on the Auto Fill Options menu.</summary>
public sealed record AutofillOptionsMenuItem(string Header, AutofillMode Mode, bool IsChecked);

/// <summary>
/// Excel's Auto Fill Options button: the small button left at the bottom-right of a filled range,
/// whose menu redoes the fill as Copy Cells, Fill Series, Fill Formatting Only or Fill Without
/// Formatting. It goes away as soon as anything else is done.
/// </summary>
public static class AutofillOptionsPlanner
{
    public const string ButtonAutomationId = "AutoFillOptionsButton";
    public const string ButtonToolTip = "Auto Fill Options";
    public const double ButtonHeight = 20;

    public static IReadOnlyList<AutofillOptionsMenuItem> Items(AutofillMode current) =>
    [
        new("Copy Cells", AutofillMode.CopyCells, current == AutofillMode.CopyCells),
        new("Fill Series", AutofillMode.FillSeries, current == AutofillMode.FillSeries),
        new("Fill Formatting Only", AutofillMode.FormattingOnly, current == AutofillMode.FormattingOnly),
        new("Fill Without Formatting", AutofillMode.WithoutFormatting, current == AutofillMode.WithoutFormatting),
    ];
}
