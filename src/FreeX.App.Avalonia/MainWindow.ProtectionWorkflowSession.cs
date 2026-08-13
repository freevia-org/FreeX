using FreeX.App.Presentation.Protection;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private ProtectionWorkflowSession ProtectionSession =>
        new(
            _session.Workbook,
            (command, _) =>
            {
                var result = _session.ExecuteReviewCommand(command);
                return new ProtectionCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp);
            });
}
