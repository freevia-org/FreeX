using FreeX.App.Presentation.Protection;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private ProtectionWorkflowSession ProtectionSession =>
        new(
            _workbook,
            (command, titleResourceKey) =>
            {
                var succeeded = TryExecuteCommand(
                    command,
                    UiText.Get(titleResourceKey),
                    out var outcome);
                return new ProtectionCommandExecutionResult(
                    succeeded,
                    outcome.ErrorMessage,
                    outcome.IsNoOp);
            });
}
