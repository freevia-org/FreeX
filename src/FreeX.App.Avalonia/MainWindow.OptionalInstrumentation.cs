using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    partial void PrepareOptionalStartupState(IReadOnlyList<string> startupArguments);

    partial void CompleteOptionalStartupState(IReadOnlyList<string> startupArguments);

    partial void RecordOptionalNeutralCellSelection();

    partial void RecordOptionalNameBoxSelection(NameBoxNavigationItem item);

    partial void AttachOptionalTextBoxInlineObservation();

    partial void RequestOptionalTextBoxInlineLayoutObservation();

    partial void RecordOptionalTextBoxInlineObservation(string phase, Guid textBoxId);
}
